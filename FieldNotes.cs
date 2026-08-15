// Field Notes - a Green Hell BepInEx plugin.
//
// A map you have to earn. Nothing is on it until you have been there.
//
// TWO SURFACES, ONE NOTEBOOK
//   The full map gets what you have found, permanently, by category, with the stock of each larder
//   point as of the last time you stood close enough to see it.
//   The minimap is a HALO, not a map: threats are legible at exactly one distance - their own
//   detection radius - and then go quiet again. See Minimap.cs for why that is the whole idea.
//
// WHAT IT IS NOT
//   It does not track animals. It marks the place a jaguar comes from, never the jaguar.
//   It does not tell you about places you have not visited.
//   It does not tell you a coconut regrew across the island. Stock is as-of-last-seen, so the map
//   can be out of date, which is a feature: a notebook that is always right is just a HUD.
//   It does not write anything into your save. The notebook is a text file next to the DLL, keyed
//   to the save slot, that you can read, hand-edit, or delete.
//
// BUILT IN THIS ORDER, ON PURPOSE
//   The phase-0 probe proved the two hard questions first - can we draw on the map, and can we draw
//   our own surface - before any of the design was written. That order came from the mod before
//   this one, where four sessions went into behaviour that the substrate was never going to support.
//
// Language level is C# 5 (stock Framework csc.exe) - no ?., no $"", no ??=.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace FieldNotes
{
    [BepInPlugin(Guid, Name, Version)]
    public class FieldNotesPlugin : BaseUnityPlugin
    {
        public const string Guid    = "com.mohammadkoush.fieldnotes";
        public const string Name    = "Field Notes";
        public const string Version = "1.0.0";

        internal static FieldNotesPlugin s_Self;

        private readonly PoiStore _store = new PoiStore();
        private readonly SettingsWindow _settings = new SettingsWindow();

        // ---- config ------------------------------------------------------------------------------
        private ConfigEntry<KeyboardShortcut> _keyMinimap, _keySize, _keyPin, _keyUnpin,
                                              _keyReport, _keyLive, _keySpawns, _keySettings;

        private ConfigEntry<bool>  _minimapOn;
        private ConfigEntry<string> _minimapSize;
        private ConfigEntry<float> _minimapRange, _band, _pingHold;
        private ConfigEntry<bool>  _headingUp;

        private ConfigEntry<float> _rPredator, _rSavage, _rSnake, _rCritter;
        private ConfigEntry<bool>  _showPredator, _showSavage, _showSnake, _showCritter,
                                   _showResource, _showCamp, _showManual, _hideEmpty, _showFood;

        private ConfigEntry<float> _discoverRadius, _seeRadius, _scanEvery, _mergeRadius;
        private ConfigEntry<bool>  _liveOn, _liveAnimals, _livePlants, _liveHalo;
        private ConfigEntry<bool>  _spawnsOn, _showNorth;
        private ConfigEntry<float> _liveRange, _liveEvery, _iconScale;
        private ConfigEntry<bool>  _revealOn;
        private ConfigEntry<float> _revealFraction, _morphSeconds;

        // ---- runtime -----------------------------------------------------------------------------
        private float _nextScanAt;
        private string _boundSave = "";
        private readonly List<string> _screen = new List<string>();
        private float _screenUntil;

        private void Awake()
        {
            s_Self = this;

            // Keypad only. From the config sweep done for Pickup Doctor, F4/F6/F7/F9/F10/F11/P and
            // Keypad5 are taken by other mods, and Keypad7 by Pickup Doctor itself.
            _keyMinimap  = Config.Bind("Keys", "ToggleMinimap", new KeyboardShortcut(KeyCode.Keypad3),
                "Show or hide the halo minimap.");
            _keySize     = Config.Bind("Keys", "CycleMinimapSize", new KeyboardShortcut(KeyCode.Keypad8),
                "Small -> Medium -> Large. Sized as a share of screen height, so it looks the same " +
                "on any monitor.");
            _keyPin      = Config.Bind("Keys", "DropPin", new KeyboardShortcut(KeyCode.Keypad4),
                "Pin your own marker here.");
            _keyUnpin    = Config.Bind("Keys", "RemoveNearestPin", new KeyboardShortcut(KeyCode.Keypad0),
                "Remove your nearest own pin. Never touches anything you discovered.");
            _keySettings = Config.Bind("Keys", "OpenSettings", new KeyboardShortcut(KeyCode.Keypad1),
                "Open the settings menu. Keypad1 came free when the notepad map was cut.");
            _keyReport   = Config.Bind("Keys", "Report", new KeyboardShortcut(KeyCode.Keypad9),
                "What do I know? A quick count on screen.");
            // Keypad6, not 5 or 7: Keypad5 belongs to another mod and Keypad7 to Pickup Doctor.
            _keyLive     = Config.Bind("Keys", "ToggleLiveLayer", new KeyboardShortcut(KeyCode.Keypad6),
                "Show what is actually out there right now, on top of what you remember.");
            // SHIFT + Keypad6, because there is no free keypad key left: this mod already owns
            // 0,1,2,3,4,6,8,9, Keypad5 belongs to another mod and Keypad7 to Pickup Doctor. Pairing
            // the spawn layer with the live layer on the same key plus a modifier is also the right
            // shape - they are the two halves of one idea.
            _keySpawns   = Config.Bind("Keys", "ToggleSpawnLayer",
                new KeyboardShortcut(KeyCode.Keypad6, KeyCode.LeftShift),
                "Shift+Keypad6. Show or hide remembered creature spawn points. Your resources and " +
                "your own pins are not affected.");

            _minimapOn   = Config.Bind("Minimap", "Enabled", true, "Show the minimap at all.");
            _minimapSize = Config.Bind("Minimap", "Size", "Medium",
                new ConfigDescription("Share of screen height: Small 16%, Medium 22%, Large 30%. " +
                    "A fixed pixel size is a postage stamp on a 4K panel and half the screen on a " +
                    "laptop, so this scales instead.",
                    new AcceptableValueList<string>("Small", "Medium", "Large")));
            _minimapRange = Config.Bind("Minimap", "RangeMetres", 80f,
                new ConfigDescription("World distance across the whole box. Only affects where your " +
                    "larder dots sit; threat pings always sit on the ring.",
                    new AcceptableValueRange<float>(20f, 400f)));
            _band = Config.Bind("Minimap", "BandMetres", 14f,
                new ConfigDescription("How thick the detection band is. Too thin and a running " +
                    "player crosses it between frames and never sees the ping.",
                    new AcceptableValueRange<float>(2f, 60f)));
            _pingHold = Config.Bind("Minimap", "PingHoldSeconds", 2.5f,
                new ConfigDescription("How long a ping lingers and fades after you leave the band.",
                    new AcceptableValueRange<float>(0f, 15f)));
            _headingUp = Config.Bind("Minimap", "HeadingUp", true,
                "On: the top of the minimap is where you are looking. Off: north is up.");

            // These four ARE the difficulty of the mod. Big threats ping early because you need room
            // to react; small ones ping late, because the fright is the content.
            _rPredator = Config.Bind("Detection", "PredatorRadius", 55f,
                new ConfigDescription("Jaguars, pumas, panthers, caimans. Far - you want warning.",
                    new AcceptableValueRange<float>(5f, 300f)));
            _rSavage = Config.Bind("Detection", "SavageRadius", 60f,
                new ConfigDescription("Hostile humans. Far. (Nothing feeds this yet - see the README.)",
                    new AcceptableValueRange<float>(5f, 300f)));
            _rSnake = Config.Bind("Detection", "SnakeRadius", 22f,
                new ConfigDescription("Close, but not close enough to give the game away.",
                    new AcceptableValueRange<float>(3f, 200f)));
            _rCritter = Config.Bind("Detection", "CritterRadius", 16f,
                new ConfigDescription("Spiders, scorpions, centipedes. Closest of all.",
                    new AcceptableValueRange<float>(3f, 200f)));

            _showPredator = Config.Bind("Show", "Predators", true, "");
            _showSavage   = Config.Bind("Show", "Savages", true, "");
            _showSnake    = Config.Bind("Show", "Snakes", true, "");
            _showCritter  = Config.Bind("Show", "Critters", true, "");
            _showResource = Config.Bind("Show", "Resources", true, "Food and water worth walking to.");
            _showCamp     = Config.Bind("Show", "CampGear", true, "");
            _showFood     = Config.Bind("Show", "FoodAnimals", true,
                "Tapir, capybara, peccary, agouti, armadillo, tortoise, birds and the rest. Drawn " +
                "like your larder rather than pinged on the halo - they are not threats.");
            _showManual   = Config.Bind("Show", "YourOwnPins", true, "");

            // A tree with nothing on it is not a place worth walking to. The POI is kept in the
            // notebook either way - hidden, not forgotten - so it reappears by itself when the tree
            // fruits again instead of needing rediscovering. Turn this off and empties come back as
            // faded ghosts, which is the only thing on screen that says "you have been here and it
            // was bare".
            _hideEmpty = Config.Bind("Show", "HideEmptyResources", true,
                "Do not draw a resource that had nothing on it when you last looked.");

            _discoverRadius = Config.Bind("Discovery", "DiscoverRadius", 28f,
                new ConfigDescription("How close you must get before something is written down. " +
                    "This is the whole mod: nothing is known until you have been there.",
                    new AcceptableValueRange<float>(5f, 200f)));
            _seeRadius = Config.Bind("Discovery", "StockCheckRadius", 40f,
                new ConfigDescription("How close you must be for the notebook to update whether a " +
                    "known spot still has anything on it. Outside this it keeps telling you what " +
                    "was true last time - deliberately.",
                    new AcceptableValueRange<float>(5f, 300f)));
            // One tree, one icon. coconuts_on_tree_01 is an item PER COCONUT, so the first live run
            // wrote down 28 "Coconut" places and five of them were the same palm - stacked from
            // y105 to y117 up one trunk. That is exactly the clutter that got wood excluded, so
            // anything of the same kind within this distance on the ground counts as one place.
            _mergeRadius = Config.Bind("Discovery", "MergeRadius", 7f,
                new ConfigDescription("Things of the same kind closer together than this are one " +
                    "place. Measured on the ground only - height is what varies up a tree.",
                    new AcceptableValueRange<float>(0f, 40f)));

            _scanEvery = Config.Bind("Discovery", "ScanEverySeconds", 2f,
                new ConfigDescription("A full scene sweep is far too costly per frame and " +
                    "indistinguishable from continuous at walking pace.",
                    new AcceptableValueRange<float>(0.25f, 30f)));

            // The live layer. This is a genuine reversal of the original design - the mod was built
            // to mark the SPAWN POINT and never the animal, which is what made it a scouting tool
            // rather than a radar. Live tracking cannot be earned, only switched on. It is a
            // separate layer for exactly that reason: the notebook underneath is untouched, and
            // LiveUsesHalo puts the tension back if the plain version gives too much away.
            _liveOn = Config.Bind("Live", "Enabled", true,
                "Show what is actually out there right now, not just where it comes from.");
            _liveAnimals = Config.Bind("Live", "Animals", true, "Live predators, snakes, critters, humans.");
            _livePlants = Config.Bind("Live", "Plants", true, "Live fruit, plants and camp gear.");
            _liveRange = Config.Bind("Live", "RangeMetres", 120f,
                new ConfigDescription("How far out live things are gathered.",
                    new AcceptableValueRange<float>(20f, 600f)));
            _liveEvery = Config.Bind("Live", "RefreshSeconds", 0.25f,
                new ConfigDescription("Nothing here moves fast enough for 60Hz to look different.",
                    new AcceptableValueRange<float>(0.05f, 3f)));
            _liveHalo = Config.Bind("Live", "LiveUsesHalo", false,
                "On: live threats obey the same detection ring as remembered ones, appearing only " +
                "as they cross the band. Off: they are simply shown. Try both and keep the better " +
                "game.");

            // The spawn layer, as a whole, mirroring Live/Enabled. Until now the discovered layer
            // had only the seven per-category switches under [Show] - and those are shared by BOTH
            // layers, so hiding predators hid live jaguars too. Two conceptually separate layers and
            // only one of them could be switched off; this is the missing half.
            //
            // Creature spawns only. "Turn off spawn creatures" was the ask, and the larder is the
            // half worth keeping.
            _spawnsOn = Config.Bind("Spawns", "Enabled", true,
                "Show remembered creature spawn points. Off leaves your resources and your own pins " +
                "alone - it only hides where things come from. Turning this off is also the quickest " +
                "way to find out whether a mark on the minimap is a spawn point or part of the " +
                "compass.");

            _showNorth = Config.Bind("Minimap", "ShowNorth", true,
                "Draw the north tick on the minimap rim. Only ever drawn in heading-up mode - with " +
                "north-up it would always be straight up and would say nothing.");

            // 0.08, which is what actually halves the apparent SIZE. The first attempt halved the
            // number to 0.058, and because this is a linear scale that quartered the area and read
            // as too small in game.
            _iconScale = Config.Bind("Minimap", "IconScale", 0.08f,
                new ConfigDescription("Icon size as a share of the minimap box.",
                    new AcceptableValueRange<float>(0.02f, 0.3f)));

            // The reveal. A dangerous thing is drawn DIMMED while it is far out and morphs to full
            // colour when it crosses inside - so the grey is a reward for watching the minimap, and
            // the red is what you cannot miss. Replaces the halo ping for threats; the ping is still
            // there behind RevealOn=false for anyone who prefers it.
            _revealOn = Config.Bind("Reveal", "Enabled", true,
                "Dangerous things are dimmed at range and turn full colour close in. Off: the old " +
                "halo ping, where a threat is legible at exactly one distance and then goes quiet.");
            _revealFraction = Config.Bind("Reveal", "InnerFraction", 0.40f,
                new ConfigDescription(
                    "Where the colour turns on, as a share of that category's own detection radius. " +
                    "0.40 puts a jaguar at 22m and a snake at about 9m, so each keeps its character " +
                    "off one number.",
                    new AcceptableValueRange<float>(0.05f, 1f)));
            _morphSeconds = Config.Bind("Reveal", "MorphSeconds", 1f,
                new ConfigDescription(
                    "How long dimmed-to-full takes. The movement is what makes the change read as an " +
                    "event rather than a redraw you can miss.",
                    new AcceptableValueRange<float>(0f, 5f)));

            Palette.Bind(Config);

            Icons.LoadAll(PluginDir());
            Logger.LogInfo("icons: " + Icons.Loaded + " loaded from " + Icons.Dir +
                           (Icons.LastError.Length > 0 ? "  (" + Icons.LastError + ")" : ""));

            Logger.LogInfo(Name + " " + Version + " loaded. Keypad3 minimap, Keypad8 size, " +
                           "Keypad4 pin, Keypad0 unpin, Keypad9 report, Keypad6 live layer.");
        }

        // ---- what the settings window may touch --------------------------------------------------
        // Handles to the ConfigEntry objects rather than their values: the window writes into
        // BepInEx's own config, so saving, ranges and the .cfg file all keep working untouched.
        internal ConfigEntry<bool>   CfgMinimapOn   { get { return _minimapOn; } }
        internal ConfigEntry<string> CfgMinimapSize { get { return _minimapSize; } }
        internal ConfigEntry<float>  CfgIconScale   { get { return _iconScale; } }
        internal ConfigEntry<float>  CfgRange       { get { return _minimapRange; } }
        internal ConfigEntry<float>  CfgBand        { get { return _band; } }
        internal ConfigEntry<float>  CfgPingHold    { get { return _pingHold; } }
        internal ConfigEntry<bool>   CfgHeadingUp   { get { return _headingUp; } }
        internal ConfigEntry<bool>   CfgShowNorth   { get { return _showNorth; } }
        internal ConfigEntry<bool>   CfgShowPredator{ get { return _showPredator; } }
        internal ConfigEntry<bool>   CfgShowSavage  { get { return _showSavage; } }
        internal ConfigEntry<bool>   CfgShowSnake   { get { return _showSnake; } }
        internal ConfigEntry<bool>   CfgShowCritter { get { return _showCritter; } }
        internal ConfigEntry<bool>   CfgShowFood    { get { return _showFood; } }
        internal ConfigEntry<bool>   CfgShowResource{ get { return _showResource; } }
        internal ConfigEntry<bool>   CfgShowCamp    { get { return _showCamp; } }
        internal ConfigEntry<bool>   CfgShowManual  { get { return _showManual; } }
        internal ConfigEntry<bool>   CfgLiveOn      { get { return _liveOn; } }
        internal ConfigEntry<bool>   CfgLiveHalo    { get { return _liveHalo; } }
        internal ConfigEntry<bool>   CfgSpawnsOn    { get { return _spawnsOn; } }
        internal ConfigEntry<bool>   CfgHideEmpty   { get { return _hideEmpty; } }
        internal ConfigEntry<float>  CfgRPredator   { get { return _rPredator; } }
        internal ConfigEntry<float>  CfgRSavage     { get { return _rSavage; } }
        internal ConfigEntry<float>  CfgRSnake      { get { return _rSnake; } }
        internal ConfigEntry<float>  CfgRCritter    { get { return _rCritter; } }
        internal ConfigEntry<float>  CfgDiscoverRadius { get { return _discoverRadius; } }
        internal ConfigEntry<float>  CfgSeeRadius   { get { return _seeRadius; } }
        internal ConfigEntry<bool>   CfgRevealOn    { get { return _revealOn; } }
        internal ConfigEntry<float>  CfgRevealFraction { get { return _revealFraction; } }
        internal ConfigEntry<float>  CfgMorphSeconds{ get { return _morphSeconds; } }

        // ---- helpers ---------------------------------------------------------------------------

        private static string PluginDir()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        private MinimapSize Size()
        {
            if (_minimapSize.Value == "Small") return MinimapSize.Small;
            if (_minimapSize.Value == "Large") return MinimapSize.Large;
            return MinimapSize.Medium;
        }

        private float RadiusOf(PoiKind k)
        {
            switch (k)
            {
                case PoiKind.Predator: return _rPredator.Value;
                case PoiKind.Savage:   return _rSavage.Value;
                case PoiKind.Snake:    return _rSnake.Value;
                case PoiKind.Critter:  return _rCritter.Value;
                default:               return 0f;
            }
        }

        private bool Enabled(PoiKind k)
        {
            switch (k)
            {
                case PoiKind.Predator: return _showPredator.Value;
                case PoiKind.Savage:   return _showSavage.Value;
                case PoiKind.Snake:    return _showSnake.Value;
                case PoiKind.Critter:  return _showCritter.Value;
                case PoiKind.Resource: return _showResource.Value;
                case PoiKind.Camp:     return _showCamp.Value;
                case PoiKind.Food:     return _showFood.Value;
                default:               return _showManual.Value;
            }
        }

        private void Begin() { _screen.Clear(); _screenUntil = Time.realtimeSinceStartup + 25f; }

        private void Say(string line)
        {
            Logger.LogMessage(line);
            _screen.Add(line);
            _screenUntil = Time.realtimeSinceStartup + 25f;
        }

        private static float GameHours()
        {
            try { return MainLevel.s_GameTime; } catch { return 0f; }
        }

        /// <summary>Live things, merged so one palm is one icon, or null when the layer is off.</summary>
        private List<LiveThing> LiveList()
        {
            if (!_liveOn.Value) return null;
            return Live.Merged(_mergeRadius.Value);
        }

        // ---- loop --------------------------------------------------------------------------------

        private void Update()
        {
            try
            {
                Keys();

                Player p = Player.Get();
                if (p == null) return;

                // The notebook is per save slot. Rebinding when the slot name changes is what makes
                // a new game start with an empty map instead of inheriting the last one's.
                string save = "";
                try { save = SaveGame.s_MainSaveName; } catch { }
                if (save != _boundSave)
                {
                    _boundSave = save;
                    _store.Bind(PluginDir(), save);
                    _store.Load();

                    // Fold away anything an older notebook recorded before the merge rule existed,
                    // so it cleans itself up instead of needing a wipe.
                    int folded = _store.Compact(_mergeRadius.Value);
                    if (folded > 0) _store.Save();

                    Logger.LogInfo("notebook: " + _store.Count + " entries from " + _store.Path_ +
                                   (folded > 0 ? "  (" + folded + " duplicate(s) merged)" : ""));
                }

                if (Time.time >= _nextScanAt)
                {
                    _nextScanAt = Time.time + _scanEvery.Value;
                    Scan(p.transform.position);
                }

                if (_liveOn.Value)
                    Live.Refresh(p.transform.position, _liveRange.Value, _liveAnimals.Value,
                                 _livePlants.Value, _liveEvery.Value);


                _store.SaveIfDirty(5f);
            }
            catch (Exception ex)
            {
                // Update runs every frame; a throw here would repeat forever.
                Logger.LogWarning("update: " + ex.Message);
            }
        }

        private void Keys()
        {
            if (_keyMinimap.Value.IsDown())
            {
                _minimapOn.Value = !_minimapOn.Value;
                Begin(); Say("minimap " + (_minimapOn.Value ? "on" : "off"));
            }

            if (_keySize.Value.IsDown())
            {
                _minimapSize.Value = (_minimapSize.Value == "Small" ? "Medium"
                                    : _minimapSize.Value == "Medium" ? "Large" : "Small");
                Begin(); Say("minimap size: " + _minimapSize.Value + "  (" +
                             Mathf.RoundToInt(Minimap.PixelsFor(Size())) + "px of " +
                             Screen.height + "px tall screen)");
            }

            // Checked BEFORE the live key: Shift+Keypad6 also satisfies plain Keypad6, so without
            // this order one press would flip both layers at once.
            if (_keySpawns.Value.IsDown())
            {
                _spawnsOn.Value = !_spawnsOn.Value;
                Begin(); Say("spawn layer " + (_spawnsOn.Value ? "on" : "off") +
                             "  (creature spawn points; resources and pins unaffected)");
                return;
            }

            if (_keyLive.Value.IsDown())
            {
                _liveOn.Value = !_liveOn.Value;
                Begin(); Say("live layer " + (_liveOn.Value ? "on" : "off") +
                             (_liveOn.Value ? "  (" + Live.Count + " things nearby)" : ""));
            }

            if (_keySettings.Value.IsDown()) _settings.Toggle();
            if (_keyPin.Value.IsDown()) DropPin();
            if (_keyUnpin.Value.IsDown()) RemoveNearestPin();
            if (_keyReport.Value.IsDown()) Report();
        }

        private void Scan(Vector3 me)
        {
            int newSpawners, spawnersSeen, newItems, restocked, emptied;

            float merge = _mergeRadius.Value;
            spawnersSeen = Discovery.ScanSpawners(_store, me, _discoverRadius.Value, merge, out newSpawners);

            // The other spawner. Fish do not come from AISpawner at all, which is why the stingray
            // stayed invisible even after being classified.
            int newFish;
            Discovery.ScanFishTanks(_store, me, _discoverRadius.Value, merge, out newFish);
            newSpawners += newFish;

            Discovery.ScanItems(_store, me, _discoverRadius.Value, _seeRadius.Value, GameHours(), merge,
                                out newItems, out restocked, out emptied);

            if (newSpawners > 0 || newItems > 0)
            {
                Begin();
                if (newSpawners > 0) Say("noted " + newSpawners + " spawn point(s)");
                if (newItems > 0)    Say("noted " + newItems + " resource(s)");
                Say("notebook: " + _store.Count + " places");
                _store.Save();
            }
        }

        private void DropPin()
        {
            Begin();
            Player p = Player.Get();
            if (p == null) return;

            Poi pin = new Poi();
            pin.Kind = PoiKind.Manual;
            pin.Label = "Pin";
            pin.Pos = p.transform.position;

            // No merging on his own pins: if he deliberately drops two close together, he meant to.
            if (_store.Discover(pin)) { Say("pinned. " + _store.CountOf(PoiKind.Manual) + " of your own."); _store.Save(); }
            else Say("already pinned here.");
        }

        /// <summary>
        /// Only ever removes his own pins. Discovered places are a record of where he has been and
        /// deleting one by accident would be deleting a memory - if he really wants them gone, the
        /// notebook is a text file he can edit.
        /// </summary>
        private void RemoveNearestPin()
        {
            Begin();
            Player p = Player.Get();
            if (p == null) return;

            Poi best = null; float bestD = float.MaxValue;
            foreach (Poi poi in _store.All)
            {
                if (poi.Kind != PoiKind.Manual) continue;
                float d = (poi.Pos - p.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = poi; }
            }

            if (best == null) { Say("you have no pins."); return; }
            if (bestD > 40f * 40f) { Say("nearest pin is " + Mathf.RoundToInt(Mathf.Sqrt(bestD)) + "m away - go closer."); return; }

            List<Poi> keep = new List<Poi>();
            foreach (Poi poi in _store.All) if (poi != best) keep.Add(poi);
            _store.Clear();
            for (int i = 0; i < keep.Count; i++) _store.Discover(keep[i]);
            _store.Save();
            Say("pin removed.");
        }

        private void Report()
        {
            Begin();
            Say("Field Notes - " + _store.Count + " places known");
            Say("  food " + _store.CountOf(PoiKind.Food) +
                "   resources " + _store.CountOf(PoiKind.Resource) +
                "   camp gear " + _store.CountOf(PoiKind.Camp) +
                "   your pins " + _store.CountOf(PoiKind.Manual));
            Say("  predators " + _store.CountOf(PoiKind.Predator) +
                "   snakes " + _store.CountOf(PoiKind.Snake) +
                "   critters " + _store.CountOf(PoiKind.Critter) +
                "   savages " + _store.CountOf(PoiKind.Savage));
            Say("  live now: " + (_liveOn.Value ? Live.Count + " nearby" : "layer off") +
                "   icons: " + Icons.Loaded);
            Say("  notebook: " + _store.Path_);
        }

        private void OnDestroy()
        {
            try { _store.Save(); } catch { }
        }

        // ---- drawing -----------------------------------------------------------------------------

        private void OnGUI()
        {
            if (_minimapOn.Value)
            {
                Player p = Player.Get();
                if (p != null)
                {
                    float yaw = (Camera.main != null ? Camera.main.transform.eulerAngles.y
                                                     : p.transform.eulerAngles.y);
                    Minimap.Draw(_store, LiveList(), p.transform.position, yaw, Size(),
                                 _minimapRange.Value, _band.Value, _pingHold.Value,
                                 _headingUp.Value, _liveHalo.Value, _iconScale.Value,
                                 _hideEmpty.Value, _showNorth.Value, _spawnsOn.Value,
                                 _revealOn.Value, _revealFraction.Value, _morphSeconds.Value,
                                 RadiusOf, Enabled);
                }
            }

            _settings.Draw(this);

            if (_screen.Count > 0 && Time.realtimeSinceStartup <= _screenUntil)
            {
                float w = Mathf.Min(720f, Screen.width - 40f);
                GUILayout.BeginArea(new Rect(20f, 20f, w, 200f), GUI.skin.box);
                GUILayout.Label(Name);
                for (int i = 0; i < _screen.Count; i++) GUILayout.Label(_screen[i]);
                GUILayout.EndArea();
            }
        }
    }
}

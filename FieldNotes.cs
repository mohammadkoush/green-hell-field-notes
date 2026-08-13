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
        private readonly MapMarkers _markers = new MapMarkers();

        // ---- config ------------------------------------------------------------------------------
        private ConfigEntry<KeyboardShortcut> _keyMinimap, _keySize, _keyPin, _keyUnpin,
                                              _keyMapMarks, _keyDump, _keyReport;

        private ConfigEntry<bool>  _minimapOn;
        private ConfigEntry<string> _minimapSize;
        private ConfigEntry<float> _minimapRange, _band, _pingHold;
        private ConfigEntry<bool>  _headingUp;

        private ConfigEntry<float> _rPredator, _rSavage, _rSnake, _rCritter;
        private ConfigEntry<bool>  _showPredator, _showSavage, _showSnake, _showCritter,
                                   _showResource, _showCamp, _showManual;

        private ConfigEntry<float> _discoverRadius, _seeRadius, _scanEvery;
        private ConfigEntry<bool>  _mapMarksOn;
        private ConfigEntry<bool>  _flipU, _flipV, _swapUV;
        private ConfigEntry<float> _markerScale;

        // ---- runtime -----------------------------------------------------------------------------
        private float _nextScanAt;
        private float _nextMarkerRefreshAt;
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
            _keyMapMarks = Config.Bind("Keys", "ToggleMapMarkers", new KeyboardShortcut(KeyCode.Keypad2),
                "Draw your notebook onto the game's own map.");
            _keyDump     = Config.Bind("Keys", "DumpMapDiagnostics", new KeyboardShortcut(KeyCode.Keypad1),
                "Write map hierarchy and world->map maths to mapdump.txt. For when markers misbehave.");
            _keyReport   = Config.Bind("Keys", "Report", new KeyboardShortcut(KeyCode.Keypad9),
                "What do I know? A quick count on screen.");

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
            _showManual   = Config.Bind("Show", "YourOwnPins", true, "");

            _discoverRadius = Config.Bind("Discovery", "DiscoverRadius", 28f,
                new ConfigDescription("How close you must get before something is written down. " +
                    "This is the whole mod: nothing is known until you have been there.",
                    new AcceptableValueRange<float>(5f, 200f)));
            _seeRadius = Config.Bind("Discovery", "StockCheckRadius", 40f,
                new ConfigDescription("How close you must be for the notebook to update whether a " +
                    "known spot still has anything on it. Outside this it keeps telling you what " +
                    "was true last time - deliberately.",
                    new AcceptableValueRange<float>(5f, 300f)));
            _scanEvery = Config.Bind("Discovery", "ScanEverySeconds", 2f,
                new ConfigDescription("A full scene sweep is far too costly per frame and " +
                    "indistinguishable from continuous at walking pace.",
                    new AcceptableValueRange<float>(0.25f, 30f)));

            _mapMarksOn = Config.Bind("Map", "DrawOnGameMap", true,
                "Draw the notebook onto the game's own map pages.");
            _flipU = Config.Bind("Map", "FlipU", false,
                "If markers come out mirrored left-to-right, turn this on. The world->map maths is " +
                "derived from the game's own GPS code, but which way the page's axes run is not " +
                "written down anywhere, so it is a toggle rather than a guess.");
            _flipV = Config.Bind("Map", "FlipV", false, "As FlipU, top-to-bottom.");
            _swapUV = Config.Bind("Map", "SwapUV", false, "If markers come out rotated 90 degrees.");
            _markerScale = Config.Bind("Map", "MarkerScale", 0.022f,
                new ConfigDescription("Marker size as a share of the page.",
                    new AcceptableValueRange<float>(0.004f, 0.12f)));

            Logger.LogInfo(Name + " " + Version + " loaded. Keypad3 minimap, Keypad8 size, " +
                           "Keypad4 pin, Keypad2 map markers, Keypad9 report, Keypad1 diagnostics.");
        }

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
                    _markers.Clear();
                    Logger.LogInfo("notebook: " + _store.Count + " entries from " + _store.Path_);
                }

                if (Time.time >= _nextScanAt)
                {
                    _nextScanAt = Time.time + _scanEvery.Value;
                    Scan(p.transform.position);
                }

                if (_mapMarksOn.Value && Time.time >= _nextMarkerRefreshAt)
                {
                    _nextMarkerRefreshAt = Time.time + 1f;
                    _markers.Refresh(_store, Enabled, _flipU.Value, _flipV.Value, _swapUV.Value,
                                     _markerScale.Value);
                }

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

            if (_keyMapMarks.Value.IsDown())
            {
                _mapMarksOn.Value = !_mapMarksOn.Value;
                if (!_mapMarksOn.Value) _markers.Clear(); else _nextMarkerRefreshAt = 0f;
                Begin(); Say("map markers " + (_mapMarksOn.Value ? "on" : "off") +
                             (_markers.LastNote.Length > 0 ? "  -  " + _markers.LastNote : ""));
            }

            if (_keyPin.Value.IsDown()) DropPin();
            if (_keyUnpin.Value.IsDown()) RemoveNearestPin();
            if (_keyReport.Value.IsDown()) Report();

            if (_keyDump.Value.IsDown())
            {
                Begin();
                try
                {
                    string path = Path.Combine(PluginDir(), "mapdump.txt");
                    File.WriteAllText(path, MapMarkers.DumpHierarchy());
                    Say("wrote " + path);
                    if (_markers.LastNote.Length > 0) Say(_markers.LastNote);
                }
                catch (Exception ex) { Say("dump failed: " + ex.Message); }
            }
        }

        private void Scan(Vector3 me)
        {
            int newSpawners, spawnersSeen, newItems, restocked, emptied;

            spawnersSeen = Discovery.ScanSpawners(_store, me, _discoverRadius.Value, out newSpawners);
            Discovery.ScanItems(_store, me, _discoverRadius.Value, _seeRadius.Value, GameHours(),
                                out newItems, out restocked, out emptied);

            if (newSpawners > 0 || newItems > 0)
            {
                Begin();
                if (newSpawners > 0) Say("noted " + newSpawners + " spawn point(s)");
                if (newItems > 0)    Say("noted " + newItems + " resource(s)");
                Say("notebook: " + _store.Count + " places");
                _store.Save();
                _nextMarkerRefreshAt = 0f;
            }
            else if (restocked > 0 || emptied > 0)
            {
                _nextMarkerRefreshAt = 0f;
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

            if (_store.Discover(pin)) { Say("pinned. " + _store.CountOf(PoiKind.Manual) + " of your own."); _store.Save(); }
            else Say("already pinned here.");
            _nextMarkerRefreshAt = 0f;
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
            _nextMarkerRefreshAt = 0f;
            Say("pin removed.");
        }

        private void Report()
        {
            Begin();
            Say("Field Notes - " + _store.Count + " places known");
            Say("  resources " + _store.CountOf(PoiKind.Resource) +
                "   camp gear " + _store.CountOf(PoiKind.Camp) +
                "   your pins " + _store.CountOf(PoiKind.Manual));
            Say("  predators " + _store.CountOf(PoiKind.Predator) +
                "   snakes " + _store.CountOf(PoiKind.Snake) +
                "   critters " + _store.CountOf(PoiKind.Critter) +
                "   savages " + _store.CountOf(PoiKind.Savage));
            Say("  notebook: " + _store.Path_);
            if (_markers.LastNote.Length > 0) Say("  map: " + _markers.LastNote);
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
                    Minimap.Draw(_store, p.transform.position, yaw, Size(),
                                 _minimapRange.Value, _band.Value, _pingHold.Value,
                                 _headingUp.Value, RadiusOf, Enabled);
                }
            }

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

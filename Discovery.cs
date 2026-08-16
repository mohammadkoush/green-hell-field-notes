// Field Notes - finding things, and remembering them.
//
// Player-based discovery is the load-bearing idea of the whole mod. Without it this is a wallhack
// that hands him the island on a plate; with it, the map is a record of where he has actually been.
// Everything in this file exists to keep that true.
//
// WHERE THE DATA COMES FROM, all read out of Assembly-CSharp with Mono.Cecil first:
//
//   Threat spawn points  AIs.AISpawner (ReplicatedBehaviour) and AIs.AISpawnerLocal (MonoBehaviour)
//                        are real objects placed in the level, each carrying a public m_ID of type
//                        AI.AIID - the species - and a transform. They are the genuine article: a
//                        fixed world position that says "a jaguar comes from here".
//
//   Resources            Item.s_AllItems is a public static HashSet<Item> of every live item, and
//                        Item.m_Info.m_ID gives the ItemID. Cheaper and far more complete than a
//                        physics sweep, which cannot see an item whose collider is asleep - the
//                        exact trap that made trigger-based pickup mods unreliable.
//
// SAVAGES ARE ABSENT ON PURPOSE. The research flagged it and it held up: savages do not come from
// placed spawners at all. EnemyAISpawnManager.SpawnWave puts them around a firecamp group, or at the
// player's own position when there is no group, on a timer. There is no fixed point to mark, so
// nothing is invented here. When savage marking is wanted it has to mean something else - the camps
// they come from, or a history of where one attacked - and that is a decision, not a lookup.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FieldNotes
{
    internal static class Discovery
    {
        // ---- what counts as what ----------------------------------------------------------------

        internal static bool Classify(AIs.AI.AIID id, out PoiKind kind, out string label)
        {
            kind = PoiKind.Critter; label = id.ToString();

            switch (id)
            {
                case AIs.AI.AIID.Jaguar:
                case AIs.AI.AIID.Puma:
                case AIs.AI.AIID.BlackPanther:
                case AIs.AI.AIID.Quest_BlackPanther:
                case AIs.AI.AIID.Jaguar_Arena:
                case AIs.AI.AIID.Jaguar_Arena_Farmer:
                case AIs.AI.AIID.Puma_Arena_Farmer:
                    kind = PoiKind.Predator; return true;

                case AIs.AI.AIID.GreenAnaconda:
                case AIs.AI.AIID.BoaConstrictor:
                case AIs.AI.AIID.SouthAmericanRattlesnake:
                    kind = PoiKind.Snake; return true;

                case AIs.AI.AIID.GoliathBirdEater:
                case AIs.AI.AIID.BrasilianWanderingSpider:
                case AIs.AI.AIID.Scorpion:
                case AIs.AI.AIID.Centipede:
                    kind = PoiKind.Critter; return true;

                // Caimans hunt you in the water and deserve the predator ring.
                case AIs.AI.AIID.BlackCaiman:
                case AIs.AI.AIID.AlbinoCaiman:
                    kind = PoiKind.Predator; return true;

                // Water hazards. They were falling through to "scenery" and were never drawn at all -
                // not a missing icon, a missing classification. A stingray you did not know about is
                // the one that gets you.
                case AIs.AI.AIID.Stingray:
                case AIs.AI.AIID.Piranha:
                case AIs.AI.AIID.VampireFish:
                    kind = PoiKind.Critter; return true;

                // FOOD. The biggest hole in the whole set until now - the most useful thing a map in
                // this game could carry, and none of it was tracked at all. Drawn like the larder
                // rather than pinged on the halo: these are not threats, and a thing you want to
                // walk towards should not behave like a thing you want to avoid.
                // Taken from the actual AIID enum, all 64 of it, rather than from memory - the first
                // attempt listed ParrotMacaw, Toucan and Bat and none of them exist. BIRDS AND BATS
                // HAVE NO AIID: they live under Prefabs/AI/FlyingAIs and are outside this enum
                // entirely, so nothing here can classify them and no icon will ever be asked for.
                // That is a separate system and a separate day's work.
                case AIs.AI.AIID.Tapir:
                case AIs.AI.AIID.Tapir_baby:
                case AIs.AI.AIID.Capybara:
                case AIs.AI.AIID.Peccary:
                case AIs.AI.AIID.Agouti:
                case AIs.AI.AIID.Armadillo:
                case AIs.AI.AIID.ArmadilloThreeBanded:
                case AIs.AI.AIID.Mouse:
                case AIs.AI.AIID.GiantAnteater:
                case AIs.AI.AIID.GoldenLionTamarin:
                case AIs.AI.AIID.Atelinae:
                case AIs.AI.AIID.RedFootedTortoise:
                case AIs.AI.AIID.MudTurtle:
                case AIs.AI.AIID.GreenIguana:
                case AIs.AI.AIID.CaimanLizard:
                case AIs.AI.AIID.Crab:
                case AIs.AI.AIID.Prawn:
                case AIs.AI.AIID.CaneToad:
                case AIs.AI.AIID.Arowana:
                case AIs.AI.AIID.PeacockBass:
                case AIs.AI.AIID.AngelFish:
                case AIs.AI.AIID.DiscusFish:
                case AIs.AI.AIID.Caterpillar:
                case AIs.AI.AIID.Beetle:
                    kind = PoiKind.Food; return true;

                // The stalker is a predator in every way that matters to a player.
                case AIs.AI.AIID.Stalker:
                    kind = PoiKind.Predator; return true;

                // Its own category, not a critter. Touching one is the problem and it will never
                // come to you, so it gets no detection ring and no reveal - just a mark that says
                // "do not grab this one".
                case AIs.AI.AIID.PoisonDartFrog:
                    kind = PoiKind.Frog; return true;

                default:
                    return false;   // everything else is scenery as far as this mod is concerned
            }
        }

        /// <summary>
        /// Resource items worth an icon. Curated, not exhaustive, and WOOD IS DELIBERATELY ABSENT:
        /// sticks and logs are everywhere, so marking them marks nothing and buries the things that
        /// matter. The rule this list follows is "worth walking to".
        /// </summary>
        private static readonly Dictionary<Enums.ItemID, string> Resources =
            new Dictionary<Enums.ItemID, string>();

        private static readonly Dictionary<Enums.ItemID, string> CampGear =
            new Dictionary<Enums.ItemID, string>();

        private static bool s_Built;

        private static void BuildTables()
        {
            if (s_Built) return;
            s_Built = true;

            // EVERY NAME HERE WAS CHECKED against the game's 1296-entry ItemID enum. Before that
            // audit, 24 of 43 did not exist - they were parsed with a silent skip, so half this
            // table had never worked and never said so. That is why the map felt thin, and why
            // icons were sourced for cassava, papaya and mushroom that could never be drawn.
            //
            // Names removed rather than corrected, because the game has no such item:
            //   Papaya / Papaya_Fruit   - no papaya in this build at all
            //   Mushroom*               - only FakeStory_Mushroome, a quest prop
            //   Tarp, Tarp_Poncho, Duct_Tape, Fuel*, Flare, Flashlight, Metal_Bidon,
            //   Machete_Broken, Bandage, Antivenom, Antibiotics
            Add(Resources, "Coconut",  "coconuts_on_tree_01", "Coconut", "Coconut_Green",
                                       "Coconut_Green_Destroyable");
            Add(Resources, "Banana",   "Banana", "Banana_Leaf", "Banana_Seeds");
            Add(Resources, "Cassava",  "Cassava_bulb");
            Add(Resources, "Palm heart", "Palm_heart");
            Add(Resources, "Molineria", "Molineria_leaf", "Molineria_Seeds", "molineria_flowers");
            Add(Resources, "Honeycomb", "Honeycomb");
            Add(Resources, "Bird nest", "Bird_Nest", "Bird_Nest_ToHoldHarvest");

            // THE MAP-WORTHY THREE. These are the only things the notepad map draws, and they earn
            // it by being rare, fixed and worth planning a trip around - which is what a map is for.
            // A coconut palm is worth knowing about when you are standing near one; an iron deposit
            // is worth knowing about from the other side of the island.
            Add(Resources, "Iron",    "iron_ore_stone", "iron_ore_melted", "iron_vein");
            Add(Resources, "Anthill", "Anthill", "Anthill_powder");
            Add(Resources, "Honey",   "Beehive", "Honeycomb");

            // Camp gear, likewise pruned to what exists. Most of the old list was aspirational:
            // this game has no tarp, no duct tape, no fuel canister and no flashlight.
            Add(CampGear, "Rope",      "Rope");
            Add(CampGear, "Machete",   "Machete", "Rusted_Machete", "MacheteToPickUp");
            Add(CampGear, "Pot",       "Pot", "Bidon", "clay_bidon", "Coconut_Bidon");
            Add(CampGear, "First aid", "Painkillers", "Leaf_Bandage", "ash_dressing");
        }

        /// <summary>
        /// Map several spelling attempts onto one label. The ItemID enum has 700+ entries and its
        /// naming is inconsistent, so every candidate is looked up by name and quietly skipped if it
        /// does not exist - a table that half-matches is far better than a build that will not run
        /// because one identifier was guessed wrong.
        /// </summary>
        /// <summary>Names that did not resolve, reported once at startup instead of vanishing.</summary>
        private static readonly List<string> s_Unknown = new List<string>();

        internal static string UnknownNames
        {
            get { return s_Unknown.Count == 0 ? "" : string.Join(", ", s_Unknown.ToArray()); }
        }

        private static void Add(Dictionary<Enums.ItemID, string> table, string label, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    object v = Enum.Parse(typeof(Enums.ItemID), names[i], true);
                    Enums.ItemID id = (Enums.ItemID)v;
                    if (!table.ContainsKey(id)) table.Add(id, label);
                }
                catch
                {
                    // NOT FINE, and the old comment here said it was. A name that does not resolve
                    // is a resource this mod will never mark, silently, forever - and 24 of the 43
                    // names in these tables were in that state before anyone checked. The mistake
                    // costs nothing to make and nothing to notice, which is the worst combination.
                    s_Unknown.Add(names[i]);
                }
            }
        }

        internal static int ResourceTableSize { get { BuildTables(); return Resources.Count; } }
        internal static int CampTableSize     { get { BuildTables(); return CampGear.Count; } }

        /// <summary>One item, classified. Shared with the live layer so both surfaces agree on what
        /// a thing is and what it is called.</summary>
        internal static bool LookUpItem(Enums.ItemID id, out PoiKind kind, out string label)
        {
            BuildTables();
            if (Resources.TryGetValue(id, out label)) { kind = PoiKind.Resource; return true; }
            if (CampGear.TryGetValue(id, out label))  { kind = PoiKind.Camp; return true; }
            kind = PoiKind.Resource; label = null; return false;
        }

        /// <summary>
        /// The same lookup, but able to ASK THE GAME when the curated list has nothing.
        ///
        /// His question, and it is the right one: why keep a hand-typed list of names at all when
        /// the game already knows what everything is? It does - every ItemInfo carries an ItemType,
        /// the game's own classification: Food, Herb, Seed, Dressing, Bowl, LiquidContainer,
        /// ItemTool, Weapon and so on.
        ///
        /// So the curated table stays for the things worth NAMING - a coconut should say "Coconut"
        /// and get a coconut icon - and everything else falls through to what the game says it is.
        /// A list I typed can be wrong and go stale in silence, which is exactly what happened: 24
        /// of 43 names never existed. A category read from the item itself cannot.
        /// </summary>
        internal static bool LookUpItem(ItemInfo info, out PoiKind kind, out string label)
        {
            kind = PoiKind.Resource; label = null;
            if (info == null) return false;

            if (LookUpItem(info.m_ID, out kind, out label)) return true;

            try
            {
                switch (info.m_Type)
                {
                    // Worth walking to: things you eat, brew or plant.
                    case Enums.ItemType.Food:
                    case Enums.ItemType.Herb:
                    case Enums.ItemType.Seed:
                        kind = PoiKind.Resource;
                        label = Pretty(info.m_ID);
                        return true;

                    // Worth remembering where you left it.
                    case Enums.ItemType.Bowl:
                    case Enums.ItemType.LiquidContainer:
                    case Enums.ItemType.Dressing:
                    case Enums.ItemType.ItemTool:
                    case Enums.ItemType.Torch:
                        kind = PoiKind.Camp;
                        label = Pretty(info.m_ID);
                        return true;
                }
            }
            catch (Exception) { }

            return false;
        }

        /// <summary>"Cassava_bulb" reads badly on a map; "Cassava bulb" does not.</summary>
        private static string Pretty(Enums.ItemID id)
        {
            return id.ToString().Replace('_', ' ');
        }

        // ---- scanning ---------------------------------------------------------------------------

        /// <summary>
        /// Sweep for spawn points near the player. Runs on a timer, not per frame: FindObjectsOfType
        /// walks every object in the scene and is far too expensive to do 60 times a second, while
        /// once every few seconds is indistinguishable to a player on foot.
        /// </summary>
        internal static int ScanSpawners(PoiStore store, Vector3 me, float radius, float merge,
                                         out int newFound)
        {
            newFound = 0;
            int seen = 0;
            float r2 = radius * radius;

            AIs.AISpawner[] a = UnityEngine.Object.FindObjectsOfType<AIs.AISpawner>();
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == null) continue;
                seen++;
                if ((a[i].transform.position - me).sqrMagnitude > r2) continue;
                if (Consider(store, a[i].m_ID, a[i].transform.position, merge)) newFound++;
            }

            AIs.AISpawnerLocal[] b = UnityEngine.Object.FindObjectsOfType<AIs.AISpawnerLocal>();
            for (int i = 0; i < b.Length; i++)
            {
                if (b[i] == null) continue;
                seen++;
                if ((b[i].transform.position - me).sqrMagnitude > r2) continue;
                if (Consider(store, b[i].m_ID, b[i].transform.position, merge)) newFound++;
            }

            return seen;
        }

        /// <summary>
        /// Fish tanks, which are the OTHER spawner and the reason the stingray never appeared.
        ///
        /// Classifying Stingray/Piranha/VampireFish fixed a real bug and was still not enough: fish
        /// are not placed by AISpawner at all. They come from `AIs.FishTank`, a completely separate
        /// spawner with its own registry, so the scan above was looking somewhere a fish can never
        /// be. The species was correct and nothing was calling it.
        ///
        /// Verified from IL before writing: `FishTank.s_FishTanks` is a public static List (no scene
        /// sweep needed at all), `m_Prefabs` is a `List&lt;GameObject&gt;`, and CreateFishes reads the
        /// species straight off each prefab with `GetComponent&lt;AI&gt;().m_ID`. So a tank knows what it
        /// holds, which makes it a genuine POI rather than a nameless blob of water.
        /// </summary>
        internal static int ScanFishTanks(PoiStore store, Vector3 me, float radius, float merge,
                                          out int newFound)
        {
            newFound = 0;
            int seen = 0;
            float r2 = radius * radius;

            try
            {
                List<AIs.FishTank> tanks = AIs.FishTank.s_FishTanks;
                if (tanks == null) return 0;

                for (int i = 0; i < tanks.Count; i++)
                {
                    AIs.FishTank t = tanks[i];
                    if (t == null || t.m_Prefabs == null) continue;
                    seen++;

                    Vector3 pos = t.transform.position;
                    if ((pos - me).sqrMagnitude > r2) continue;

                    // A tank can hold more than one species, so every distinct one it can produce
                    // gets its own POI - "there are piranha here" and "there are stingray here" are
                    // different things to know.
                    for (int p = 0; p < t.m_Prefabs.Count; p++)
                    {
                        GameObject prefab = t.m_Prefabs[p];
                        if (prefab == null) continue;

                        AIs.AI ai = prefab.GetComponent<AIs.AI>();
                        if (ai == null) continue;

                        if (Consider(store, ai.m_ID, pos, merge)) newFound++;
                    }
                }
            }
            catch { }

            return seen;
        }

        private static bool Consider(PoiStore store, AIs.AI.AIID id, Vector3 pos, float merge)
        {
            PoiKind kind; string label;
            if (!Classify(id, out kind, out label)) return false;

            Poi p = new Poi();
            p.Kind = kind; p.Label = label; p.Pos = pos;
            return store.Discover(p, merge);
        }

        /// <summary>
        /// Sweep live items for resources and camp gear, and refresh stock for anything already
        /// known that he is currently close enough to see.
        ///
        /// Stock is only ever updated inside <paramref name="seeRadius"/>. That is the whole
        /// as-of-last-seen rule in one condition: walk away and the map keeps telling you what was
        /// there when you left, right or wrong.
        /// </summary>
        internal static void ScanItems(PoiStore store, Vector3 me, float discoverRadius,
                                       float seeRadius, float gameHours, float merge,
                                       out int newFound, out int restocked, out int emptied)
        {
            BuildTables();
            newFound = 0; restocked = 0; emptied = 0;

            float dr2 = discoverRadius * discoverRadius;
            float sr2 = seeRadius * seeRadius;

            // Everything alive right now, by label, so stock can be answered by lookup rather than
            // by a second sweep per POI.
            Dictionary<string, List<Vector3>> live = new Dictionary<string, List<Vector3>>();

            HashSet<Item> all = Item.s_AllItems;
            if (all == null) return;

            foreach (Item it in all)
            {
                if (it == null || it.m_Info == null) continue;

                // Curated first, then the game's own ItemType. Same rule as the live layer, so the
                // notebook and the live view can never disagree about what a thing is.
                string label; PoiKind kind;
                if (!LookUpItem(it.m_Info, out kind, out label)) continue;

                Vector3 pos = it.transform.position;
                float d2 = (pos - me).sqrMagnitude;

                if (d2 <= sr2)
                {
                    List<Vector3> list;
                    if (!live.TryGetValue(label, out list)) { list = new List<Vector3>(); live.Add(label, list); }
                    list.Add(pos);
                }

                if (d2 > dr2) continue;

                Poi p = new Poi();
                p.Kind = kind; p.Label = label; p.Pos = pos;
                p.InStock = true; p.StockSeenAt = gameHours;
                if (store.Discover(p, merge)) newFound++;
            }

            // Now correct the stock of everything already known and currently in view.
            foreach (Poi p in store.All)
            {
                if (p.Kind != PoiKind.Resource && p.Kind != PoiKind.Camp) continue;
                if ((p.Pos - me).sqrMagnitude > sr2) continue;   // cannot see it, do not touch it

                // "Anything of this kind still within the merge radius, on the ground plane." One POI
                // now stands for a whole tree, so a single coconut left on it means the tree is not
                // empty - and height must be ignored or a POI recorded at head height would call a
                // tree bare while its crown is still full.
                bool present = false;
                float m2 = merge * merge;
                List<Vector3> list;
                if (live.TryGetValue(p.Label, out list))
                    for (int i = 0; i < list.Count; i++)
                    {
                        float dx = list[i].x - p.Pos.x, dz = list[i].z - p.Pos.z;
                        if (dx * dx + dz * dz <= m2) { present = true; break; }
                    }

                if (present != p.InStock)
                {
                    if (present) restocked++; else emptied++;
                    p.InStock = present;
                    store.MarkDirty();
                }
                p.StockSeenAt = gameHours;
            }
        }
    }
}

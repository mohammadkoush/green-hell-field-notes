// Field Notes - the live layer.
//
// A REVERSAL, AND IT IS WORTH BEING HONEST ABOUT IT.
//
// The design settled early on marking the SPAWN POINT and never the animal: "you are tracking the
// POI and not the actual animal itself... just a spawn point for where the cat would spawn." That
// made the mod a scouting tool - you learn the island, you do not get a feed of it - and it is what
// kept a minimap from deleting the being-lost that Green Hell is built on.
//
// This layer does the opposite: it shows the actual jaguar, actually there, moving. That is what was
// asked for, so that is what this does. But it does change what the mod IS, and the change is worth
// naming: a live layer cannot be earned, only switched on. Nothing about it is discovered.
//
// So it is built as a SEPARATE LAYER rather than a replacement:
//   - `Live/Enabled` turns it on and off whole.
//   - The discovered notebook keeps working underneath it, untouched.
//   - `Live/UseHalo` runs live things through the same detection-radius ring as the static layer,
//     which puts the tension back if it turns out the plain version gives too much away.
// Try it both ways and keep whichever is the better game.
//
// PERFORMANCE. Object.FindObjectsOfType walks every object in the scene and is far too expensive to
// call per frame - which is exactly why the static scan runs on a two second timer. The live layer
// cannot do that, so it uses the game's own registers instead:
//     AIs.AIManager.Get().m_ActiveAIs   public List<AI>, the AIs the game itself is ticking
//     Item.s_AllItems                   public static HashSet<Item>, every live item
// Both are maintained by the game; reading them is a walk over a list that already exists. Even so
// the result is cached and rebuilt a few times a second, because nothing here changes fast enough
// for 60Hz to look different.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FieldNotes
{
    internal struct LiveThing
    {
        public PoiKind Kind;
        public string Label;
        public Vector3 Pos;

        // A STABLE identity for one creature across frames. Needed because the grey-to-red morph
        // takes a second, and a thing that moves cannot be recognised frame to frame by where it
        // is - which is the only other handle we would have. Unity's instance id is free and does
        // not change while the object lives.
        public int Id;
    }

    internal static class Live
    {
        private static readonly List<LiveThing> _cache = new List<LiveThing>();
        private static float _nextAt;

        internal static int Count { get { return _cache.Count; } }
        internal static List<LiveThing> Things { get { return _cache; } }

        internal static void Refresh(Vector3 me, float range, bool animals, bool plants,
                                     float everySeconds)
        {
            if (Time.time < _nextAt) return;
            _nextAt = Time.time + everySeconds;

            _cache.Clear();
            float r2 = range * range;

            if (animals) { GatherAnimals(me, r2); GatherFish(me, r2); }
            if (plants)  GatherItems(me, r2);
        }

        /// <summary>
        /// Live fish, straight from their tanks.
        ///
        /// GatherAnimals reads `AIManager.m_ActiveAIs`, and whether tank-managed fish are registered
        /// there at all was never established - so rather than find out the hard way a second time,
        /// this walks `FishTank.s_FishTanks` and asks each tank for its fish directly. Guaranteed
        /// correct regardless of what the AI manager does or does not know about them.
        ///
        /// If a fish DOES also turn up in m_ActiveAIs, nothing breaks: Merged() folds two entries of
        /// the same species at the same spot into one.
        /// </summary>
        private static void GatherFish(Vector3 me, float r2)
        {
            try
            {
                List<AIs.FishTank> tanks = AIs.FishTank.s_FishTanks;
                if (tanks == null) return;

                for (int i = 0; i < tanks.Count; i++)
                {
                    AIs.FishTank tank = tanks[i];
                    if (tank == null) continue;

                    // Cheap rejection on the tank before asking it for anything.
                    if ((tank.transform.position - me).sqrMagnitude > r2 * 4f) continue;

                    int n = 0;
                    try { n = tank.GetFishesCount(); } catch { continue; }

                    for (int f = 0; f < n; f++)
                    {
                        AIs.Fish fish = null;
                        try { fish = tank.GetFish(f); } catch { }
                        if (fish == null) continue;

                        Vector3 p = fish.transform.position;
                        if ((p - me).sqrMagnitude > r2) continue;

                        bool dead = false;
                        try { dead = fish.IsDead(); } catch { }
                        if (dead) continue;

                        PoiKind kind; string label;
                        if (!Discovery.Classify(fish.m_ID, out kind, out label)) continue;

                        LiveThing t = new LiveThing();
                        t.Kind = kind; t.Label = label; t.Pos = p;
                        try { t.Id = fish.GetInstanceID(); } catch { t.Id = 0; }
                        _cache.Add(t);
                    }
                }
            }
            catch { }
        }

        private static void GatherAnimals(Vector3 me, float r2)
        {
            try
            {
                AIs.AIManager mgr = AIs.AIManager.Get();
                if (mgr == null) return;

                List<AIs.AI> list = mgr.m_ActiveAIs;
                if (list == null) return;

                for (int i = 0; i < list.Count; i++)
                {
                    AIs.AI ai = list[i];
                    if (ai == null) continue;

                    // A corpse is not a threat and a corpse on the minimap is a lie about where the
                    // danger is.
                    bool dead = false;
                    try { dead = ai.IsDead(); } catch { }
                    if (dead) continue;

                    Vector3 p = ai.transform.position;
                    if ((p - me).sqrMagnitude > r2) continue;

                    PoiKind kind; string label;
                    if (!Discovery.Classify(ai.m_ID, out kind, out label))
                    {
                        // Everything the static layer files as scenery still gets shown live if it
                        // is a human - a tribal walking past matters even though there is no spawn
                        // point anywhere to mark him with.
                        bool human = false;
                        try { human = ai.IsHuman(); } catch { }
                        if (!human) continue;
                        kind = PoiKind.Savage;
                        label = ai.m_ID.ToString();
                    }

                    LiveThing t = new LiveThing();
                    t.Kind = kind; t.Label = label; t.Pos = p;
                    try { t.Id = ai.GetInstanceID(); } catch { t.Id = 0; }
                    _cache.Add(t);
                }
            }
            catch { }
        }

        private static void GatherItems(Vector3 me, float r2)
        {
            try
            {
                HashSet<Item> all = Item.s_AllItems;
                if (all == null) return;

                foreach (Item it in all)
                {
                    if (it == null || it.m_Info == null) continue;

                    // Anything in a backpack or a rack is not out there in the world, and drawing it
                    // would paint a cluster of icons on top of the player's own camp.
                    try { if (it.m_InInventory || it.m_InStorage) continue; } catch { }

                    PoiKind kind; string label;
                    if (!Discovery.LookUpItem(it.m_Info.m_ID, out kind, out label)) continue;

                    Vector3 p = it.transform.position;
                    if ((p - me).sqrMagnitude > r2) continue;

                    LiveThing t = new LiveThing();
                    t.Kind = kind; t.Label = label; t.Pos = p;
                    try { t.Id = it.GetInstanceID(); } catch { t.Id = 0; }
                    _cache.Add(t);
                }
            }
            catch { }
        }

        /// <summary>
        /// Live things of the same kind standing within <paramref name="merge"/> of each other are
        /// folded into one. Same reason as the notebook: coconuts_on_tree_01 is an item per coconut,
        /// so one palm is five icons unless something says otherwise - and on the live layer that
        /// pile redraws every quarter second, which reads as a smear rather than a tree.
        /// </summary>
        internal static List<LiveThing> Merged(float merge)
        {
            if (merge <= 0f) return _cache;

            List<LiveThing> outp = new List<LiveThing>();
            float m2 = merge * merge;

            for (int i = 0; i < _cache.Count; i++)
            {
                bool dup = false;
                for (int j = 0; j < outp.Count; j++)
                {
                    if (outp[j].Kind != _cache[i].Kind || outp[j].Label != _cache[i].Label) continue;
                    float dx = outp[j].Pos.x - _cache[i].Pos.x;
                    float dz = outp[j].Pos.z - _cache[i].Pos.z;
                    if (dx * dx + dz * dz <= m2) { dup = true; break; }
                }
                if (!dup) outp.Add(_cache[i]);
            }
            return outp;
        }
    }
}

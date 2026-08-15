// Grey outside, red inside, and one second to cross between them.
//
// HIS DIAGRAM, EXACTLY. The minimap has two zones. In the outer band a dangerous thing is drawn in a
// DIMMED version of its own colour - visible, but only to someone actually watching the minimap. The
// moment it crosses into the inner zone it morphs to full colour over a second.
//
// Why it works, and why it is not just decoration: the grey is a reward for paying attention. A
// player staring at the jungle sees nothing until the thing is close and goes red; a player watching
// the corner of the screen gets a quiet head start. And the morph is what makes the transition read
// as an EVENT rather than a redraw - an icon that simply changed colour between two frames is
// something you can miss, while a second of movement catches the eye.
//
// The grey is deliberately the SAME HUE, darkened and desaturated, not a neutral grey. At range it
// still hints at what it is without naming it, which is the same instinct as the halo itself.
//
// THE INNER RADIUS IS PER CATEGORY, which is what he asked for. It is a fraction of that category's
// own detection radius, so a snake stays intimate and a jaguar gives room - one number to tune
// instead of six, and each animal keeps its character.
//
// Language level is C# 5 (stock Framework csc.exe) - no ?., no $"", no ??=.

using System.Collections.Generic;
using UnityEngine;

namespace FieldNotes
{
    internal static class Reveal
    {
        // key -> how far through the morph, 0 = dimmed, 1 = full colour.
        private static readonly Dictionary<string, float> s_Progress = new Dictionary<string, float>();
        private static readonly List<string> s_Stale = new List<string>();
        private static float s_LastSeen;

        /// <summary>How dark and how washed-out the far-away version is.</summary>
        private const float DimValue = 0.42f;
        private const float DimSaturation = 0.28f;

        /// <summary>A stable handle for one thing across frames. Live creatures move; ids do not.</summary>
        internal static string KeyFor(int id, PoiKind kind, string label, Vector3 pos)
        {
            if (id != 0) return "l" + id;
            // Spawn points and pins never move, so their position IS a stable identity.
            return "s" + kind + "|" + label + "|" + Mathf.RoundToInt(pos.x) + "," + Mathf.RoundToInt(pos.z);
        }

        /// <summary>
        /// The colour to draw this thing in right now, moving it a little closer to where it should
        /// be each frame.
        /// </summary>
        internal static Color ColourFor(string key, Color full, bool inside, float morphSeconds)
        {
            float target = inside ? 1f : 0f;

            float now;
            if (!s_Progress.TryGetValue(key, out now))
            {
                // First sight. Something already inside the ring starts there rather than animating
                // in from grey - the morph is meant to mark a CROSSING, and a thing that was already
                // close never crossed anything.
                now = target;
            }
            else if (morphSeconds > 0.01f)
            {
                now = Mathf.MoveTowards(now, target, Time.deltaTime / morphSeconds);
            }
            else
            {
                now = target;
            }

            s_Progress[key] = now;
            return Blend(full, now);
        }

        /// <summary>Dimmed version of the same hue at 0, the colour itself at 1.</summary>
        internal static Color Blend(Color full, float t)
        {
            float h, s, v;
            Color.RGBToHSV(full, out h, out s, out v);
            Color dim = Color.HSVToRGB(h, s * DimSaturation, v * DimValue);
            return Color.Lerp(dim, full, Mathf.Clamp01(t));
        }

        /// <summary>
        /// Forget things that have not been drawn for a while.
        ///
        /// Without this the dictionary grows for every animal the player ever walks past in a
        /// session. It also means a creature that wanders off and comes back gets to cross the ring
        /// again, which is right - that IS a new approach.
        /// </summary>
        internal static void Sweep(HashSet<string> seenThisFrame)
        {
            // Only every couple of seconds; walking a dictionary every frame to save a few bytes
            // would cost more than it saves.
            if (Time.realtimeSinceStartup - s_LastSeen < 2f) return;
            s_LastSeen = Time.realtimeSinceStartup;

            s_Stale.Clear();
            foreach (KeyValuePair<string, float> kv in s_Progress)
                if (!seenThisFrame.Contains(kv.Key)) s_Stale.Add(kv.Key);
            for (int i = 0; i < s_Stale.Count; i++) s_Progress.Remove(s_Stale[i]);
        }

        internal static void Clear() { s_Progress.Clear(); }
    }
}

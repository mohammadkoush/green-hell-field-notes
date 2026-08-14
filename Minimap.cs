// Field Notes - the halo.
//
// This is the idea the mod is actually built around, so it is worth stating plainly before the code.
//
// A normal minimap gets MORE informative as you approach something. This one does the opposite. A
// threat is legible at exactly one distance - its own detection radius - and then goes quiet. Walk
// toward the snake and its icon slides onto the ring, sits there for a moment, and drops off the
// inside edge. You are left knowing something was detected, roughly which way, and nothing else.
//
// Three things fall out of that, and all three are the point:
//
//   It becomes a SENSOR, not a map. A sonar ping rather than a display.
//   It makes the PLAYER do the remembering. The information is real but perishable, so the value
//     sits in his attention rather than in the UI.
//   It rewards movement. Standing still surfaces nothing; the band only sweeps things up as he
//     walks. The opposite of a radar you can camp on.
//
// And it fixes the objection to having a minimap at all: Green Hell is built on being lost, and a
// minimap normally deletes that. A ring that forgets does not.
//
// The radius per category is the difficulty dial of the whole mod, not a readability setting. Big
// threats ping early because you need room to react. Small ones ping late, because the fright IS the
// content - "close, but not close enough to give away the game", as he put it.
//
// RESOURCES DO NOT PING. They are shown plainly, at true scaled position, because they are his own
// larder and there is nothing to spoil: he already went there, and knowing where his coconuts are is
// the reward for having found them.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FieldNotes
{
    public enum MinimapSize { Small, Medium, Large }

    internal static class Minimap
    {
        // Fractions of SCREEN HEIGHT, not pixel counts. A fixed 220px box is a postage stamp on a
        // 4K panel and half the screen on a laptop; a fraction is the same apparent size on both,
        // which is the actual thing he asked for when he said "different monitor size".
        private const float SmallFrac  = 0.16f;
        private const float MediumFrac = 0.22f;
        private const float LargeFrac  = 0.30f;

        internal static float PixelsFor(MinimapSize size)
        {
            float f = (size == MinimapSize.Small ? SmallFrac
                     : size == MinimapSize.Large ? LargeFrac : MediumFrac);
            // Never smaller than legible, never taller than the screen it sits on.
            return Mathf.Clamp(Screen.height * f, 120f, Screen.height - 40f);
        }

        private static Texture2D s_White;
        private static Texture2D White()
        {
            if (s_White == null)
            {
                s_White = new Texture2D(1, 1);
                s_White.SetPixel(0, 0, Color.white);
                s_White.Apply();
                s_White.hideFlags = HideFlags.HideAndDontSave;
            }
            return s_White;
        }

        private static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White());
            GUI.color = old;
        }

        private static void Dot(float x, float y, float size, Color c)
        {
            Fill(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), c);
        }

        /// <summary>
        /// An icon if there is one for this thing, a coloured dot if there is not. The icon is drawn
        /// at its own colours with alpha applied, so the silhouettes stay white and the plants stay
        /// green - the category colour is carried by the dot fallback and by the ring, not by
        /// tinting art that was cut to be read at a glance.
        /// </summary>
        private static void Mark(float x, float y, float size, PoiKind kind, string label, float alpha)
        {
            Texture2D icon = Icons.For(kind, label);
            if (icon == null)
            {
                Color c = ColorOf(kind);
                c.a = alpha;
                Dot(x, y, size, c);
                return;
            }

            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), icon,
                            ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        private static void Ring(Vector2 c, float radius, Color col, int segments, float dotSize)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                Dot(c.x + Mathf.Cos(a) * radius, c.y + Mathf.Sin(a) * radius, dotSize, col);
            }
        }

        internal static Color ColorOf(PoiKind k)
        {
            switch (k)
            {
                case PoiKind.Predator: return new Color(1.00f, 0.35f, 0.30f);
                case PoiKind.Savage:   return new Color(1.00f, 0.55f, 0.15f);
                case PoiKind.Snake:    return new Color(0.85f, 0.45f, 1.00f);
                case PoiKind.Critter:  return new Color(1.00f, 0.85f, 0.35f);
                case PoiKind.Resource: return new Color(0.45f, 0.95f, 0.55f);
                case PoiKind.Camp:     return new Color(0.55f, 0.80f, 1.00f);
                default:               return Color.white;
            }
        }

        internal static bool IsThreat(PoiKind k)
        {
            return k == PoiKind.Predator || k == PoiKind.Savage ||
                   k == PoiKind.Snake || k == PoiKind.Critter;
        }

        /// <summary>
        /// Draw the whole thing. <paramref name="radiusOf"/> hands back the detection radius in
        /// metres for a category, <paramref name="enabled"/> whether that category is switched on.
        /// </summary>
        internal static void Draw(PoiStore store, List<LiveThing> live, Vector3 me, float yawDegrees,
                                  MinimapSize size, float rangeMetres, float bandMetres,
                                  float pingHoldSeconds, bool headingUp, bool liveUsesHalo,
                                  float iconScale, bool hideEmpty,
                                  Func<PoiKind, float> radiusOf, Func<PoiKind, bool> enabled)
        {
            float px = PixelsFor(size);
            float pad = Mathf.Max(12f, px * 0.06f);
            Rect box = new Rect(Screen.width - px - pad, pad, px, px);

            Fill(box, new Color(0.03f, 0.05f, 0.04f, 0.55f));

            Color edge = new Color(0.86f, 0.82f, 0.68f, 0.85f);
            float b = Mathf.Max(1f, px * 0.008f);
            Fill(new Rect(box.x, box.y, box.width, b), edge);
            Fill(new Rect(box.x, box.yMax - b, box.width, b), edge);
            Fill(new Rect(box.x, box.y, b, box.height), edge);
            Fill(new Rect(box.xMax - b, box.y, b, box.height), edge);

            float half = px * 0.5f;
            Vector2 centre = new Vector2(box.x + half, box.y + half);

            // The halo itself, at 72% of the box so it reads as a ring rather than the border.
            float ringPx = half * 0.72f;
            Ring(centre, ringPx, new Color(0.55f, 0.85f, 1f, 0.22f), 64, Mathf.Max(2f, px * 0.012f));

            float pixelsPerMetre = half / (rangeMetres * 0.5f);
            float rot = headingUp ? -yawDegrees : 0f;

            float dotPx = Mathf.Max(4f, px * 0.035f);
            float iconPx = Mathf.Max(10f, px * iconScale);
            float now = Time.realtimeSinceStartup;

            // ---- the discovered notebook ----------------------------------------------------
            foreach (Poi p in store.All)
            {
                if (!enabled(p.Kind)) continue;

                Vector3 d = p.Pos - me;
                d.y = 0f;
                float dist = d.magnitude;

                // Bearing in screen space. Unity's +Z is north; screen Y grows downward, hence the
                // negated cosine.
                float rad = (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + rot) * Mathf.Deg2Rad;

                if (IsThreat(p.Kind))
                {
                    float detect = radiusOf(p.Kind);
                    bool inBand = Mathf.Abs(dist - detect) <= bandMetres * 0.5f;

                    if (inBand) p.PingedAt = now;

                    // A thin band and a fast walker would cross it between frames, so a ping is held
                    // briefly and faded out. Without this the whole mechanic is invisible at a jog.
                    float age = (p.PingedAt < 0f ? 999f : now - p.PingedAt);
                    if (age > pingHoldSeconds) continue;

                    float alpha = inBand ? 1f : Mathf.Clamp01(1f - (age / pingHoldSeconds));

                    // Drawn ON the ring, never at its true scaled distance: the ring IS the reading.
                    float x = centre.x + Mathf.Sin(rad) * ringPx;
                    float y = centre.y - Mathf.Cos(rad) * ringPx;
                    Mark(x, y, iconPx, p.Kind, p.Label, alpha);
                }
                else
                {
                    // A tree with nothing on it is not a place worth walking to, so it is not drawn.
                    // The POI stays in the notebook - hidden, not forgotten - so it comes back by
                    // itself when the tree fruits again rather than needing rediscovering.
                    if (hideEmpty && !p.InStock) continue;

                    // Larder: plotted honestly, and only inside the box.
                    if (dist * pixelsPerMetre > half - 4f) continue;
                    float x = centre.x + Mathf.Sin(rad) * dist * pixelsPerMetre;
                    float y = centre.y - Mathf.Cos(rad) * dist * pixelsPerMetre;
                    Mark(x, y, iconPx, p.Kind, p.Label, p.InStock ? 1f : 0.30f);
                }
            }

            // ---- the live layer -------------------------------------------------------------
            // Drawn on top, and slightly larger, because a thing that is actually THERE should read
            // ahead of a thing you merely remember.
            if (live != null)
            {
                for (int i = 0; i < live.Count; i++)
                {
                    LiveThing t = live[i];
                    if (!enabled(t.Kind)) continue;

                    Vector3 d = t.Pos - me;
                    d.y = 0f;
                    float dist = d.magnitude;
                    float rad = (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + rot) * Mathf.Deg2Rad;

                    if (liveUsesHalo && IsThreat(t.Kind))
                    {
                        // Same ring discipline as the notebook, for when the plain version turns out
                        // to give too much away.
                        float detect = radiusOf(t.Kind);
                        if (Mathf.Abs(dist - detect) > bandMetres * 0.5f) continue;
                        float x = centre.x + Mathf.Sin(rad) * ringPx;
                        float y = centre.y - Mathf.Cos(rad) * ringPx;
                        Mark(x, y, iconPx * 1.15f, t.Kind, t.Label, 1f);
                    }
                    else
                    {
                        if (dist * pixelsPerMetre > half - 4f) continue;
                        float x = centre.x + Mathf.Sin(rad) * dist * pixelsPerMetre;
                        float y = centre.y - Mathf.Cos(rad) * dist * pixelsPerMetre;
                        Mark(x, y, iconPx * 1.15f, t.Kind, t.Label, 1f);
                    }
                }
            }

            // The player, and which way north is.
            Dot(centre.x, centre.y, Mathf.Max(5f, px * 0.03f), Color.white);

            float northRad = rot * Mathf.Deg2Rad;
            Dot(centre.x + Mathf.Sin(northRad) * (half - 8f),
                centre.y - Mathf.Cos(northRad) * (half - 8f),
                Mathf.Max(4f, px * 0.022f), new Color(1f, 0.4f, 0.4f, 0.9f));

            GUI.color = edge;
            GUI.Label(new Rect(box.x + 6f, box.yMax - Mathf.Max(20f, px * 0.09f),
                               box.width - 12f, Mathf.Max(18f, px * 0.085f)),
                      (headingUp ? "^ " : "N ") + Mathf.RoundToInt(rangeMetres) + "m   " +
                      store.Count + " known" +
                      (live != null && live.Count > 0 ? "   " + live.Count + " live" : ""));
            GUI.color = Color.white;
        }
    }
}

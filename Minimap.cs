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

        private static Texture2D s_Disc;

        /// <summary>
        /// A filled circle, baked once.
        ///
        /// IMGUI has no circle fill - every other mark in this file is a stretched 1x1 texture, and
        /// the ring is sixty-four little squares walked round a radius. That works for a ring and is
        /// hopeless for a disc, so this is a real texture, cached like the player arrow.
        ///
        /// The edge is feathered over about two texels. A hard edge on a 128px texture stretched to
        /// 400 shows its stair-steps badly, and a circle with visible stairs looks like a mistake
        /// rather than a design.
        /// </summary>
        private static Texture2D DiscTex()
        {
            if (s_Disc != null) return s_Disc;

            const int N = 128;
            const float R = N * 0.5f;

            Texture2D t = new Texture2D(N, N, TextureFormat.ARGB32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp;

            Color32[] px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) - R;
                    float dy = (y + 0.5f) - R;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    // 1 inside, 0 outside, feathered across the last two texels.
                    float a = Mathf.Clamp01((R - d) / 2f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            t.SetPixels32(px);
            t.Apply();
            s_Disc = t;
            return s_Disc;
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
        /// The colour an icon is tinted - from the palette, which anyone can change.
        ///
        /// WHAT SHIPS: one red for predators, savages, snakes and critters, and white for everything
        /// harmless. That is deliberate and it is the fast reading. At twenty pixels a spread of
        /// warm hues is noise, while a single red is instant - and WHICH danger it is, the shape
        /// already says. Red only carries meaning while it is rare, which is also why the compass
        /// tick was taken off red: a permanent red mark on the ring would be indistinguishable from
        /// a predator.
        ///
        /// The settings menu can break that, per category, on purpose. It is his map. The default is
        /// the opinion; the picker is the escape hatch.
        /// </summary>
        private static Color TintFor(PoiKind kind)
        {
            return Palette.Of(kind);
        }

        /// <summary>
        /// An icon if there is one for this thing, a coloured dot if there is not.
        ///
        /// Alpha is applied to the tint WITHOUT washing the hue toward the background - a fading
        /// ping stays red and gets fainter, rather than turning into a dull brown-grey halfway
        /// through. That matters because the fade is how a threat leaves the ring, and a threat
        /// should look like a threat right up to the moment it goes.
        /// </summary>
        private static void Mark(float x, float y, float size, PoiKind kind, string label, float alpha)
        {
            Color tint = TintFor(kind);
            tint.a = alpha;
            MarkTinted(x, y, size, kind, label, tint);
        }

        /// <summary>As Mark, but the colour has already been decided - used by the reveal.</summary>
        private static void MarkTinted(float x, float y, float size, PoiKind kind, string label,
                                       Color tint)
        {

            Texture2D icon = Icons.For(kind, label);
            if (icon == null)
            {
                Dot(x, y, size, tint);
                return;
            }

            Color old = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), icon,
                            ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        // The player arrow.
        //
        // IMGUI cannot fill a polygon - everything on this minimap is a stretched 1x1 texture - so
        // the triangle is baked once into a small texture and then ROTATED by the GUI matrix. That
        // is also the only way to get a smoothly rotating arrow: drawing it from rectangles would
        // give sixteen visibly different arrows as it turned.
        // Everything drawn through the reveal this frame, so Reveal.Sweep can forget the rest.
        private static readonly HashSet<string> s_Seen = new HashSet<string>();

        /// <summary>
        /// Dimmed at range, full colour inside, and a second to cross between them.
        ///
        /// The inner radius is a FRACTION OF THAT CATEGORY'S OWN detection radius, which is what
        /// keeps a snake intimate and a jaguar roomy off a single number. It is deliberately not one
        /// shared distance: the categories already differ by a factor of three, and flattening them
        /// would throw away the difficulty dial the whole mod is tuned on.
        /// </summary>
        private static Color RevealColour(PoiKind kind, string label, Vector3 pos, int id, float dist,
                                          Func<PoiKind, float> radiusOf, float fraction,
                                          float morphSeconds)
        {
            float inner = radiusOf(kind) * Mathf.Clamp01(fraction);
            string key = Reveal.KeyFor(id, kind, label, pos);
            s_Seen.Add(key);
            Color c = Reveal.ColourFor(key, Palette.Of(kind), dist <= inner, morphSeconds);
            c.a = 1f;
            return c;
        }

        private static Texture2D s_Arrow;

        private static Texture2D ArrowTex()
        {
            if (s_Arrow != null) return s_Arrow;

            const int N = 64;
            s_Arrow = new Texture2D(N, N, TextureFormat.RGBA32, false);
            s_Arrow.hideFlags = HideFlags.HideAndDontSave;
            s_Arrow.wrapMode = TextureWrapMode.Clamp;
            s_Arrow.filterMode = FilterMode.Bilinear;

            // A kite rather than a plain triangle: the notch in the trailing edge is what makes it
            // read as pointing rather than as a wedge, at any size.
            //
            // Y IS UP HERE, NOT DOWN. SetPixels32 puts row 0 at the BOTTOM of the texture, unlike
            // every screen coordinate in this file. Authoring the tip near y=0 - the obvious thing -
            // bakes it at the bottom and the arrow points dead backwards.
            Vector2 tip = new Vector2(0.50f, 0.98f);
            Vector2 left = new Vector2(0.06f, 0.04f);
            Vector2 right = new Vector2(0.94f, 0.04f);
            Vector2 notch = new Vector2(0.50f, 0.30f);

            Color32[] px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    // Supersampled 2x2, because a hard-edged arrow at 12 pixels looks like a staircase.
                    int hits = 0;
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            Vector2 p = new Vector2((x + 0.25f + sx * 0.5f) / N,
                                                    (y + 0.25f + sy * 0.5f) / N);
                            if (InTri(p, tip, left, notch) || InTri(p, tip, notch, right)) hits++;
                        }
                    }
                    byte a = (byte)(hits * 255 / 4);
                    px[y * N + x] = new Color32(255, 255, 255, a);
                }
            }
            s_Arrow.SetPixels32(px);
            s_Arrow.Apply();
            return s_Arrow;
        }

        private static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
            bool pos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);
            return !(neg && pos);
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

        /// <summary>
        /// Things that can come AT you. Frogs are deliberately absent: a poison dart frog cannot
        /// approach and cannot hurt you unless you pick it up, so a detection ring and a
        /// grey-to-red reveal would both be answering a question it never asks. It is drawn plainly,
        /// where it actually is, in its own amber - "do not grab this one" rather than "something is
        /// hunting you".
        /// </summary>
        internal static bool IsThreat(PoiKind k)
        {
            return k == PoiKind.Predator || k == PoiKind.Savage ||
                   k == PoiKind.Snake || k == PoiKind.Critter;
        }

        /// <summary>
        /// Draw the whole thing. <paramref name="radiusOf"/> hands back the detection radius in
        /// metres for a category, <paramref name="enabled"/> whether that category is switched on.
        /// </summary>
        /// <summary>
        /// Label with a one-pixel drop shadow.
        ///
        /// Needed the moment the background panel went: pale text straight onto jungle is legible
        /// against dark leaves and gone against a sunlit clearing. The shadow is what the panel was
        /// really for.
        /// </summary>
        /// <summary>
        /// Text with a hard outline all the way round.
        ///
        /// "An outline that costs me nothing" - his words, and this is it: eight offset draws of the
        /// dark colour, then one bright draw on top. No readback, no shader, nothing to fail. It is
        /// not a true inverse and it does not need to be, because the point was never inversion, it
        /// was that the letter is always readable.
        ///
        /// Eight and not four: with only the axes the diagonals of a bold glyph poke through the
        /// outline and the letter still fades against a matching background.
        /// </summary>
        private static void Outlined(Rect r, string text, GUIStyle style, Color face, float w)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    GUI.Label(new Rect(r.x + dx * w, r.y + dy * w, r.width, r.height), text, style);
                }
            }
            GUI.color = face;
            GUI.Label(r, text, style);
        }

        private static void Shadowed(Rect r, string text, Color colour)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text);
            GUI.color = colour;
            GUI.Label(r, text);
        }

        /// <summary>
        /// Has he been away from this long enough to have lost it?
        ///
        /// StockSeenAt is game hours at the last scan that had it in sight, refreshed by Discovery
        /// for every Camp, Resource and Container POI in range. So "has not been near it recently" is
        /// a question the notebook could already answer - this only asks it.
        ///
        /// Never seen (-1) counts as forgotten: a container recorded but never since approached is
        /// precisely the one worth pointing at.
        /// </summary>
        private static bool Forgotten(Poi p, float forgetHours)
        {
            if (p.StockSeenAt < 0f) return true;

            // MainLevel.s_GameTime - the same accessor GameHours() in FieldNotes.cs already uses to
            // WRITE StockSeenAt. Reading the clock a different way from the one that set the value
            // would be comparing two numbers that only look like the same unit.
            float now;
            try { now = MainLevel.s_GameTime; } catch { return false; }

            // Not forgotten if the clock has gone backwards - a fresh save loaded into an older world
            // would otherwise make every container he owns reappear at once.
            float away = now - p.StockSeenAt;
            if (away < 0f) return false;
            return away >= forgetHours;
        }

        internal static void Draw(PoiStore store, List<LiveThing> live, Vector3 me, float yawDegrees,
                                  MinimapSize size, float rangeMetres, float bandMetres,
                                  float pingHoldSeconds, bool headingUp, bool liveUsesHalo,
                                  float iconScale, bool hideEmpty, bool showNorth, bool spawnsOn,
                                  bool revealOn, float revealFraction, float morphSeconds,
                                  float spawnFade, bool showCoords,
                                  float discAlpha, bool invertLetters, float forgetHours,
                                  Func<PoiKind, float> radiusOf, Func<PoiKind, bool> enabled)
        {
            s_Seen.Clear();
            float px = PixelsFor(size);
            float pad = Mathf.Max(12f, px * 0.06f);
            Rect box = new Rect(Screen.width - px - pad, pad, px, px);

            // NO PANEL AND NO BORDER. His instruction: "lose the rectangular in the minimap, keep
            // the circle, no background to it outside the circle." Things further out than the ring
            // now float over the jungle with nothing behind them, which is the point - the map is a
            // circle, so a square frame around it was only ever describing the texture it was drawn
            // on.
            //
            // The panel WAS doing one useful job: it made the footer text readable. That job moves to
            // a drop shadow at the bottom of this method rather than simply being dropped.
            Color edge = new Color(0.86f, 0.82f, 0.68f, 0.85f);

            float half = px * 0.5f;
            Vector2 centre = new Vector2(box.x + half, box.y + half);

            // The halo itself, at 72% of the box so it reads as a ring rather than the border.
            float ringPx = half * 0.72f;

            // THE BACKGROUND HE ASKED FOR, and only inside the ring.
            //
            // Drawn BEFORE the ring and before every marker, so it sits behind all of them - a disc
            // painted afterwards would grey out the icons it is supposed to be helping.
            //
            // Anything beyond the ring still floats over bare jungle, which is what he wanted when
            // the rectangle went. The disc fills the circle; it does not restore the panel.
            if (discAlpha > 0.001f)
            {
                Color old = GUI.color;
                GUI.color = new Color(0.03f, 0.05f, 0.04f, discAlpha);
                GUI.DrawTexture(new Rect(centre.x - ringPx, centre.y - ringPx, ringPx * 2f, ringPx * 2f),
                                DiscTex());
                GUI.color = old;
            }

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

                // A CONTAINER IS SHOWN ONLY ONCE HE HAS LOST TRACK OF IT.
                //
                // His rule, and it is about his knowledge rather than the object: a coconut shell he
                // set down at camp is not lost, so marking it is noise - but the same shell, after an
                // hour away, probably is lost, and that is exactly when a mark earns its place.
                //
                // One rule covers the case he did not mention too: a container he never placed and
                // walks past is one he has not been near, so it shows without any need to work out
                // who put it there.
                if (p.Kind == PoiKind.Container && !Forgotten(p, forgetHours)) continue;

                // The spawn layer, off as a whole. Creature spawn points only - the larder stays,
                // because "turn off spawn creatures" was the ask and the resources are the half
                // worth keeping. Symmetrical with the live layer, which has always had one switch.
                if (!spawnsOn && IsThreat(p.Kind)) continue;

                Vector3 d = p.Pos - me;
                d.y = 0f;
                float dist = d.magnitude;

                // Bearing in screen space. Unity's +Z is north; screen Y grows downward, hence the
                // negated cosine.
                float rad = (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + rot) * Mathf.Deg2Rad;

                if (IsThreat(p.Kind) && revealOn)
                {
                    if (dist * pixelsPerMetre > half - 4f) continue;
                    float x = centre.x + Mathf.Sin(rad) * dist * pixelsPerMetre;
                    float y = centre.y - Mathf.Cos(rad) * dist * pixelsPerMetre;
                    // REMEMBERED, not present. Spawn points used the same icon at the same weight
                    // as a live animal, so "a jaguar comes from here" and "a jaguar is here NOW"
                    // looked identical - which made the layer worse than useless and had him
                    // turning it off. A remembered mark is now faded and slightly smaller, so the
                    // living one always reads louder.
                    Color sc = RevealColour(p.Kind, p.Label, p.Pos, 0, dist,
                                            radiusOf, revealFraction, morphSeconds);
                    sc.a = spawnFade;
                    MarkTinted(x, y, iconPx * 0.8f, p.Kind, p.Label, sc);
                }
                else if (IsThreat(p.Kind))
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

                    if (revealOn && IsThreat(t.Kind))
                    {
                        if (dist * pixelsPerMetre > half - 4f) continue;
                        float x = centre.x + Mathf.Sin(rad) * dist * pixelsPerMetre;
                        float y = centre.y - Mathf.Cos(rad) * dist * pixelsPerMetre;
                        MarkTinted(x, y, iconPx * 1.15f, t.Kind, t.Label,
                                   RevealColour(t.Kind, t.Label, t.Pos, t.Id, dist,
                                                radiusOf, revealFraction, morphSeconds));
                    }
                    else if (liveUsesHalo && IsThreat(t.Kind))
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

            // The player, as an arrow pointing where he is facing.
            //
            // In HEADING-UP the whole map is already rotated so that forward is up, which means the
            // arrow is always up and the rotation is zero - it is the world that turns, not him. In
            // NORTH-UP nothing rotates, so the arrow itself has to carry the heading. Getting that
            // backwards would give an arrow that spins while standing still, or one that never
            // moves while turning on the spot.
            float arrowAngle = headingUp ? 0f : yawDegrees;
            float arrowSize = Mathf.Max(11f, px * 0.075f);

            Matrix4x4 savedMatrix = GUI.matrix;
            Color savedColour = GUI.color;
            GUIUtility.RotateAroundPivot(arrowAngle, centre);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(centre.x - arrowSize * 0.5f, centre.y - arrowSize * 0.5f,
                                     arrowSize, arrowSize), ArrowTex());
            GUI.color = savedColour;
            GUI.matrix = savedMatrix;

            Reveal.Sweep(s_Seen);

            // North.
            //
            // This used to be a RED DOT at the rim, and it was reported as an unexplained mark that
            // was always there. Two things were wrong with it and only one was documentation:
            //
            //   1. Red is the PREDATOR colour in this palette (1.00/0.35/0.30 against the marker's
            //      1.00/0.40/0.40 - indistinguishable at four pixels). On a minimap whose whole
            //      design is "a threat appears on the ring and then goes quiet", a permanent red
            //      mark on the ring read as a predator sitting in one direction forever.
            //   2. A DOT is the vocabulary of a point of interest. North is not a place.
            //
            // So it is now a pale TICK outside the ring, off the danger palette entirely, and only
            // drawn in heading-up mode - with north-up it is always straight up and says nothing.
            // ...and it is now four LETTERS, which is what he asked for. N, E, S, W around the
            // ring, turning with his heading so the letter that is up is the way he is facing.
            //
            // Drawn in north-up mode too, unlike the old tick. With the panel gone there is no frame
            // to imply which way up the map is, so even a fixed N is now carrying information the
            // border used to.
            if (showNorth)
            {
                Color letter = new Color(0.95f, 0.93f, 0.82f, 1.00f);
                Color minor  = new Color(0.88f, 0.85f, 0.72f, 0.85f);

                GUIStyle cs = new GUIStyle(GUI.skin.label);
                cs.alignment = TextAnchor.MiddleCenter;
                cs.fontSize = Mathf.Max(10, Mathf.RoundToInt(px * 0.075f));
                cs.fontStyle = FontStyle.Bold;

                // THE BOX COMES FROM THE FONT. It used to be a hard-coded 20x18 while the font scaled
                // with the map, so on his large minimap a 35px glyph was being drawn into an 18px box
                // and every letter was sliced. His screenshot showed fragments and no whole letter.
                float lw = cs.fontSize * 1.6f;
                float lh = cs.fontSize * 1.6f;

                float lr = half * 0.86f;
                string[] marks = { "N", "E", "S", "W" };
                for (int i = 0; i < 4; i++)
                {
                    float a = (rot + i * 90f) * Mathf.Deg2Rad;
                    float lx = centre.x + Mathf.Sin(a) * lr;
                    float ly = centre.y - Mathf.Cos(a) * lr;
                    Rect lrect = new Rect(lx - lw * 0.5f, ly - lh * 0.5f, lw, lh);

                    LetterInvert.SetPoint(i, lx, ly);

                    Color face = (i == 0) ? letter : minor;
                    if (invertLetters) face = LetterInvert.ColourFor(i, face);

                    Outlined(lrect, marks[i], cs, face,
                             Mathf.Max(1f, cs.fontSize * 0.09f));
                }
                GUI.color = Color.white;
            }

            string foot = (headingUp ? "^ " : "N ") + Mathf.RoundToInt(rangeMetres) + "m   " +
                          store.Count + " known" +
                          (live != null && live.Count > 0 ? "   " + live.Count + " live" : "");

            Rect footRect = new Rect(box.x + 6f, box.yMax - Mathf.Max(20f, px * 0.09f),
                                     box.width - 12f, Mathf.Max(18f, px * 0.085f));

            // THE GAME'S OWN COORDINATES, on their own line above the footer.
            //
            // Player.GetGPSCoordinates is what Watch.UpdateState calls and prints on the compass
            // face - first out param is the W figure, second is the other. Borrowing the same call
            // means the minimap and his watch cannot drift apart, and there is no coordinate scheme
            // of ours that would need explaining or would break when the game's did.
            if (showCoords)
            {
                string gps = null;
                try
                {
                    int w, s;
                    Player.Get().GetGPSCoordinates(out w, out s);
                    gps = w + "W  " + s + "S";
                }
                catch { gps = null; }

                if (gps != null)
                {
                    Rect g = new Rect(footRect.x, footRect.y - footRect.height,
                                      footRect.width, footRect.height);
                    Shadowed(g, gps, edge);
                }
            }

            Shadowed(footRect, foot, edge);
            GUI.color = Color.white;
        }
    }
}

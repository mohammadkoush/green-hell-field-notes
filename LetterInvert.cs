// Field Notes - compass letters in the reverse colour of whatever is behind them.
//
// HIS ASK, in two parts and in this order: "give the letters an outline that costs me nothing", and
// then "add in the option menu the low sample rate, and let me turn it on in the setting menu."
//
// So the free thing is the default and this is the opt-in. That shape matters, because the only way
// to get a TRUE inverse is to read the screen back, and a GPU->CPU readback is the single most
// reliable way to wreck a frame rate in Unity. Nobody pays for it here unless they ask for it, and
// the rate they pay at is theirs to set.
//
// WHY A COROUTINE AND NOT OnGUI. Texture2D.ReadPixels reads whatever is currently bound, and inside
// OnGUI that is only dependably the finished frame during Repaint - "only dependably" being the
// problem. WaitForEndOfFrame is the documented point at which the frame is definitely complete, so
// the read happens there and the colours are used on the NEXT frame. One frame of lag on a letter
// that moves at walking pace is not visible; a torn or empty read is.
//
// The sample is nine pixels per letter, four letters, at whatever rate he sets - so at the default
// 4Hz that is 36 pixels a second. The cost is not the pixel count, it is the pipeline stall, which is
// why the rate is exposed rather than the region size.
//
// Language level is C# 5 (stock Framework csc.exe) - no ?., no $"", no ??=.

using System.Collections;
using UnityEngine;

namespace FieldNotes
{
    internal static class LetterInvert
    {
        internal const int Count = 4;              // N, E, S, W

        // Where Minimap last drew each letter, in screen pixels. Written every frame by the draw
        // pass, read by the sampler. No locking: Unity is single-threaded here and a torn read would
        // at worst sample the wrong spot for one tick.
        private static readonly Vector2[] s_Points = new Vector2[Count];
        private static readonly Color[]   s_Colours = new Color[Count];
        private static bool s_Ready;

        private static Texture2D s_Patch;          // the 3x3 we read into, made once
        private static bool s_Running;

        internal static void SetPoint(int i, float x, float y)
        {
            if (i < 0 || i >= Count) return;
            s_Points[i] = new Vector2(x, y);
        }

        /// <summary>The colour to draw letter i in, or the fallback until the first sample lands.</summary>
        internal static Color ColourFor(int i, Color fallback)
        {
            if (!s_Ready || i < 0 || i >= Count) return fallback;
            return s_Colours[i];
        }

        internal static void Reset()
        {
            s_Ready = false;
        }

        /// <summary>Start the sampler if it is not already going. Safe to call every frame.</summary>
        internal static void Ensure(MonoBehaviour host)
        {
            if (s_Running || host == null) return;
            s_Running = true;
            host.StartCoroutine(Loop(host));
        }

        internal static void Stop()
        {
            s_Running = false;
            s_Ready = false;
        }

        private static IEnumerator Loop(MonoBehaviour host)
        {
            while (s_Running)
            {
                yield return new WaitForEndOfFrame();

                float rate = FieldNotesPlugin.InvertSampleRate;
                if (rate <= 0f) rate = 4f;

                if (!FieldNotesPlugin.InvertEnabled)
                {
                    s_Ready = false;
                    yield return new WaitForSeconds(0.5f);      // idle cheaply while it is off
                    continue;
                }

                Sample();
                yield return new WaitForSeconds(1f / Mathf.Clamp(rate, 0.5f, 30f));
            }
        }

        private static void Sample()
        {
            try
            {
                if (s_Patch == null)
                {
                    s_Patch = new Texture2D(3, 3, TextureFormat.RGB24, false);
                    s_Patch.hideFlags = HideFlags.HideAndDontSave;
                }

                for (int i = 0; i < Count; i++)
                {
                    Vector2 p = s_Points[i];
                    if (p == Vector2.zero) continue;

                    // GUI coordinates put y=0 at the TOP; ReadPixels puts it at the BOTTOM. Getting
                    // this backwards samples the mirror image of the map, which reads as "the colour
                    // is sort of right sometimes" rather than as an obvious bug - the same trap that
                    // made the player arrow point backwards.
                    int x = Mathf.Clamp(Mathf.RoundToInt(p.x) - 1, 0, Screen.width - 3);
                    int y = Mathf.Clamp(Screen.height - Mathf.RoundToInt(p.y) - 1, 0, Screen.height - 3);

                    s_Patch.ReadPixels(new Rect(x, y, 3, 3), 0, 0, false);
                    s_Patch.Apply(false);

                    Color[] px = s_Patch.GetPixels();
                    float r = 0f, g = 0f, b = 0f;
                    for (int k = 0; k < px.Length; k++) { r += px[k].r; g += px[k].g; b += px[k].b; }
                    float n = px.Length;
                    r /= n; g /= n; b /= n;

                    // INVERT, then push away from mid-grey. A straight 1-x inversion of a mid-grey
                    // background returns mid-grey, which is invisible against itself - the one case
                    // where "the reverse colour" is the worst possible answer. So the result is
                    // driven to whichever end it is already nearer.
                    Color inv = new Color(1f - r, 1f - g, 1f - b, 1f);
                    float lum = inv.r * 0.299f + inv.g * 0.587f + inv.b * 0.114f;
                    float push = (lum >= 0.5f) ? 1f : 0f;
                    float mix = 1f - Mathf.Abs(lum - 0.5f) * 2f;        // 1 at mid-grey, 0 at the ends
                    inv = Color.Lerp(inv, new Color(push, push, push, 1f), mix * 0.85f);

                    s_Colours[i] = inv;
                }
                s_Ready = true;
            }
            catch
            {
                // A readback can fail on some backends. Fall back to the outline rather than throwing
                // once a frame forever - the outline is legible on its own, which is the whole reason
                // it is the default.
                s_Ready = false;
                s_Running = false;
            }
        }
    }
}

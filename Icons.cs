// Field Notes - the icon set.
//
// Icons ship as loose 64px PNGs in an `icons` folder beside the DLL, NOT baked into the assembly and
// not in an AssetBundle. Three reasons, in order of how much they matter:
//
//   1. He can swap one. Drop a better snake in, restart, done - no rebuild, no Unity, no me.
//   2. An AssetBundle is locked to Unity 2021.2.20f1 and would have to be rebuilt for every game
//      update. A PNG is a PNG.
//   3. Texture2D.LoadImage reads a PNG straight off disk at runtime, so the whole pipeline is a
//      file copy.
//
// The artwork was cut from the sheets he supplied by icons-src\mkicons.ps1 - re-runnable, so the
// crops and the colour keying are recorded as code rather than as a one-off afternoon in an editor.
//
// MATCHING IS BY LABEL FIRST, THEN KIND. A POI labelled "Scorpion" gets the scorpion even though it
// is filed under Critter alongside the spiders, because the label is the more specific truth. Only
// when nothing matches does it fall back to the category, and then to a plain leaf - so a species we
// have no art for still draws something rather than vanishing.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FieldNotes
{
    internal static class Icons
    {
        private static readonly Dictionary<string, Texture2D> _tex =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        internal static string Dir;
        internal static int Loaded;
        internal static string LastError = "";

        internal static void LoadAll(string pluginDir)
        {
            _tex.Clear();
            Loaded = 0;
            Dir = Path.Combine(pluginDir, "icons");

            if (!Directory.Exists(Dir)) { LastError = "no icons folder at " + Dir; return; }

            string[] files = Directory.GetFiles(Dir, "*.png");
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(files[i]);
                    Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    // ImageConversion.LoadImage, not tex.LoadImage: it is an extension method living
                    // in UnityEngine.ImageConversionModule, and calling it the short way needs a
                    // using that the stock compiler will not resolve without the reference anyway.
                    if (!ImageConversion.LoadImage(t, bytes)) continue;

                    // Clamp, or the bilinear filter samples the opposite edge and leaves a faint
                    // seam around every icon on the minimap.
                    t.wrapMode = TextureWrapMode.Clamp;
                    t.filterMode = FilterMode.Bilinear;
                    t.hideFlags = HideFlags.HideAndDontSave;

                    _tex[Path.GetFileNameWithoutExtension(files[i])] = t;
                    Loaded++;
                }
                catch (Exception ex) { LastError = files[i] + ": " + ex.Message; }
            }
        }

        internal static Texture2D Get(string name)
        {
            Texture2D t;
            if (name != null && _tex.TryGetValue(name, out t)) return t;
            return null;
        }

        /// <summary>Letters only, lower case - so "Bird nest", "Bird_Nest" and "birdnest" all match.</summary>
        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            char[] buf = new char[s.Length];
            int n = 0;
            for (int i = 0; i < s.Length; i++)
                if (char.IsLetter(s[i])) buf[n++] = char.ToLowerInvariant(s[i]);
            return new string(buf, 0, n);
        }

        internal static Texture2D For(PoiKind kind, string label)
        {
            string k = Norm(label);

            // Species first. These are substring tests on purpose: the game's own names are things
            // like "BrasilianWanderingSpider" and "SouthAmericanRattlesnake", and matching the animal
            // inside the name is more robust than listing every AIID.
            if (k.Length > 0)
            {
                if (k.Contains("scorpion"))  return Get("scorpion");
                if (k.Contains("spider") || k.Contains("birdeater") || k.Contains("centipede"))
                                             return Get("spider");
                if (k.Contains("snake") || k.Contains("anaconda") || k.Contains("boa") ||
                    k.Contains("rattle"))    return Get("snake");
                if (k.Contains("jaguar") || k.Contains("puma") || k.Contains("panther") ||
                    k.Contains("caiman"))    return Get("predator");

                if (k.Contains("coconut"))   return Get("coconut");
                if (k.Contains("banana"))    return Get("banana");
                if (k.Contains("papaya"))    return Get("papaya");
                if (k.Contains("cassava") || k.Contains("manioc")) return Get("cassava");
                if (k.Contains("palmheart") || k.Contains("palm")) return Get("palmheart");
                if (k.Contains("molineria")) return Get("molineria");
                if (k.Contains("mushroom"))  return Get("mushroom");
                if (k.Contains("birdnest") || k.Contains("nest"))  return Get("birdnest");
                if (k.Contains("honey"))     return Get("molineria");

                // Exact filename, so a new icon dropped in the folder works with no code change at
                // all as long as it is named after the thing.
                Texture2D exact = Get(k);
                if (exact != null) return exact;
            }

            switch (kind)
            {
                case PoiKind.Predator: return Get("predator");
                case PoiKind.Snake:    return Get("snake");
                case PoiKind.Critter:  return Get("spider");
                case PoiKind.Camp:     return Get("camp");
                case PoiKind.Savage:   return Get("predator");
                case PoiKind.Resource: return Get("plant");
                default:               return null;    // his own pins stay a plain dot
            }
        }
    }
}

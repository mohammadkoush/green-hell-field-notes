// Where every colour on the minimap comes from.
//
// The default is RED FOR EVERYTHING DANGEROUS and that is not an accident: red means exactly one
// thing on this minimap, so you react to it rather than decode it. Anyone who wants a colour per
// species can have one, but they have to go and choose it - the shipped experience is the fast one.
//
// Stored as hex strings in the .cfg so a colour can be typed in by hand or shared with someone else,
// rather than as three floats that mean nothing to a person reading the file.
//
// Language level is C# 5 (stock Framework csc.exe) - no ?., no $"", no ??=.

using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace FieldNotes
{
    internal static class Palette
    {
        // One red, used by every dangerous category. Kept as a named constant so the "reset to
        // defaults" button and the shipped config cannot drift apart.
        internal const string DangerRed = "#FF4238";

        private static readonly Dictionary<PoiKind, ConfigEntry<string>> s_Entries =
            new Dictionary<PoiKind, ConfigEntry<string>>();
        private static readonly Dictionary<PoiKind, Color> s_Cache = new Dictionary<PoiKind, Color>();
        private static readonly Dictionary<PoiKind, string> s_Defaults = new Dictionary<PoiKind, string>();

        internal static readonly PoiKind[] Order = new PoiKind[]
        {
            PoiKind.Predator, PoiKind.Savage, PoiKind.Snake, PoiKind.Critter,
            PoiKind.Food, PoiKind.Resource, PoiKind.Camp, PoiKind.Manual,
        };

        internal static string LabelOf(PoiKind kind)
        {
            switch (kind)
            {
                case PoiKind.Predator: return "Big cats and caimans";
                case PoiKind.Savage:   return "Savages";
                case PoiKind.Snake:    return "Snakes";
                case PoiKind.Critter:  return "Spiders, scorpions, stingrays";
                case PoiKind.Food:     return "Food animals";
                case PoiKind.Resource: return "Plants and fruit";
                case PoiKind.Camp:     return "Camp gear";
                case PoiKind.Manual:   return "Your own pins";
                default:               return kind.ToString();
            }
        }

        internal static void Bind(ConfigFile config)
        {
            Add(config, PoiKind.Predator, DangerRed);
            Add(config, PoiKind.Savage,   DangerRed);
            Add(config, PoiKind.Snake,    DangerRed);
            Add(config, PoiKind.Critter,  DangerRed);
            Add(config, PoiKind.Food,     "#FFFFFF");
            Add(config, PoiKind.Resource, "#FFFFFF");
            Add(config, PoiKind.Camp,     "#FFFFFF");
            Add(config, PoiKind.Manual,   "#FFFFFF");
        }

        private static void Add(ConfigFile config, PoiKind kind, string hex)
        {
            s_Defaults[kind] = hex;
            ConfigEntry<string> entry = config.Bind(
                "Colours", kind.ToString(), hex,
                "Hex colour for " + LabelOf(kind).ToLower() + ". Default " + hex +
                (hex == DangerRed ? " - the one red shared by everything dangerous." : "."));
            s_Entries[kind] = entry;
            s_Cache[kind] = Parse(entry.Value, hex);
        }

        internal static Color Of(PoiKind kind)
        {
            Color c;
            if (s_Cache.TryGetValue(kind, out c)) return c;
            return Color.white;
        }

        internal static string HexOf(PoiKind kind)
        {
            ConfigEntry<string> e;
            if (s_Entries.TryGetValue(kind, out e)) return e.Value;
            return "#FFFFFF";
        }

        internal static void Set(PoiKind kind, Color c)
        {
            ConfigEntry<string> e;
            if (!s_Entries.TryGetValue(kind, out e)) return;
            e.Value = ToHex(c);
            s_Cache[kind] = c;
        }

        internal static void Reset(PoiKind kind)
        {
            string hex;
            if (!s_Defaults.TryGetValue(kind, out hex)) return;
            Set(kind, Parse(hex, "#FFFFFF"));
        }

        internal static void ResetAll()
        {
            foreach (PoiKind kind in Order) Reset(kind);
        }

        /// <summary>True when this category still ships red - used to warn before that stops being true.</summary>
        internal static bool IsDangerous(PoiKind kind)
        {
            return kind == PoiKind.Predator || kind == PoiKind.Savage
                || kind == PoiKind.Snake || kind == PoiKind.Critter;
        }

        internal static string ToHex(Color c)
        {
            return "#" + ((int)Mathf.Round(Mathf.Clamp01(c.r) * 255f)).ToString("X2")
                       + ((int)Mathf.Round(Mathf.Clamp01(c.g) * 255f)).ToString("X2")
                       + ((int)Mathf.Round(Mathf.Clamp01(c.b) * 255f)).ToString("X2");
        }

        /// <summary>Forgiving on purpose: this value can be hand-edited, and a typo must not black out the map.</summary>
        internal static Color Parse(string hex, string fallback)
        {
            Color c;
            if (TryParse(hex, out c)) return c;
            if (TryParse(fallback, out c)) return c;
            return Color.white;
        }

        private static bool TryParse(string hex, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrEmpty(hex)) return false;
            string s = hex.Trim();
            if (s.Length > 0 && s[0] == '#') s = s.Substring(1);
            if (s.Length != 6) return false;
            try
            {
                int r = Convert.ToInt32(s.Substring(0, 2), 16);
                int g = Convert.ToInt32(s.Substring(2, 2), 16);
                int b = Convert.ToInt32(s.Substring(4, 2), 16);
                c = new Color(r / 255f, g / 255f, b / 255f);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}

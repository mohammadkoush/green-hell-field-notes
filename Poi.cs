// Field Notes - the POI model and the store behind it.
//
// The design settled over eight passes, and this file is the part that does not change if the map,
// the minimap or the halo get rewritten: a set of remembered coordinates, and what is known about
// each one. Both surfaces read from here; neither owns it.
//
// TWO CLASSES OF POI, decided deliberately:
//
//   RESOURCE - a place worth walking to. Saved, kept, and it carries STOCK: picked up means the icon
//              goes; respawned means it comes back. Wood is deliberately excluded - abundance is the
//              disqualifier, not usefulness, because marking what is everywhere marks nothing.
//   THREAT   - a spawn point. Optional display, and the thing the halo pings. Never says whether
//              anything is home: we mark the place a jaguar comes from, not the jaguar.
//
// STOCK IS AS-OF-LAST-SEEN, not live. The map shows what was true when he was last there and only
// corrects itself when he goes back. Live stock would tell him a coconut refilled on the far side of
// the island without leaving camp, which is the exact failure he named himself about the halo: it
// gives away the game. A map that can be out of date is the more interesting object.
//
// Language level is C# 5 (stock Framework csc.exe) - no ?., no $"", no ??=.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace FieldNotes
{
    public enum PoiKind
    {
        Resource,   // food, water, useful pickups
        Predator,   // jaguar, puma, panther - the things that hunt you
        Savage,     // hostile humans
        Snake,
        Critter,    // spiders, scorpions, centipedes
        Camp,       // camp gear and abandoned camps
        Manual      // a pin he dropped himself
    }

    public class Poi
    {
        public PoiKind Kind;
        public string Label;        // "Coconut", "Jaguar", ...
        public Vector3 Pos;

        /// <summary>Resources only: was there anything here the last time he stood close enough to
        /// see? Threat POIs leave this true and never touch it.</summary>
        public bool InStock = true;

        /// <summary>Game time, in hours, when stock was last confirmed. -1 means never.</summary>
        public float StockSeenAt = -1f;

        /// <summary>Set while the halo is pinging it, so the minimap can fade it out rather than
        /// blinking it off the instant he crosses the band.</summary>
        [NonSerialized] public float PingedAt = -1f;

        public string Key()
        {
            // Rounded to the metre. Two scans of the same coconut must produce one POI, and the
            // world position of a harvestable wobbles slightly between loads.
            return ((int)Kind) + "|" + Label + "|" +
                   Mathf.RoundToInt(Pos.x) + "," + Mathf.RoundToInt(Pos.y) + "," + Mathf.RoundToInt(Pos.z);
        }
    }

    /// <summary>
    /// Everything he has found, and the file it lives in.
    ///
    /// The file sits next to the DLL and is keyed to the save slot - deliberately NOT written into
    /// Green Hell's own save. Same principle Pickup Doctor was built on: a mod that never touches the
    /// save file is a mod you can uninstall without consequences, and "turn it off and it is really
    /// off" is worth more than tidiness.
    /// </summary>
    public class PoiStore
    {
        private readonly Dictionary<string, Poi> _byKey = new Dictionary<string, Poi>();
        private string _path;
        private bool _dirty;
        private float _lastSaveAt;

        public int Count { get { return _byKey.Count; } }
        public IEnumerable<Poi> All { get { return _byKey.Values; } }

        public void Bind(string directory, string saveName)
        {
            if (string.IsNullOrEmpty(saveName)) saveName = "default";
            foreach (char c in Path.GetInvalidFileNameChars()) saveName = saveName.Replace(c, '_');
            _path = Path.Combine(directory, "fieldnotes-" + saveName + ".txt");
        }

        public string Path_ { get { return _path; } }

        /// <summary>Returns true if this was the first time he has seen it.</summary>
        public bool Discover(Poi p)
        {
            string k = p.Key();
            if (_byKey.ContainsKey(k)) return false;
            _byKey.Add(k, p);
            _dirty = true;
            return true;
        }

        public Poi Find(PoiKind kind, string label, Vector3 pos)
        {
            Poi probe = new Poi();
            probe.Kind = kind; probe.Label = label; probe.Pos = pos;
            Poi found;
            if (_byKey.TryGetValue(probe.Key(), out found)) return found;
            return null;
        }

        public void MarkDirty() { _dirty = true; }

        public int CountOf(PoiKind kind)
        {
            int n = 0;
            foreach (Poi p in _byKey.Values) if (p.Kind == kind) n++;
            return n;
        }

        public void Clear()
        {
            _byKey.Clear();
            _dirty = true;
        }

        // ---- persistence ----------------------------------------------------------------------
        // A flat text file, one POI per line, pipe separated. Not JSON, not binary: a format he can
        // open, read, hand-edit and delete a line from without any tooling. For a few hundred rows
        // that is worth more than efficiency.

        public void Load()
        {
            _byKey.Clear();
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return;
            try
            {
                string[] lines = File.ReadAllLines(_path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    string[] f = line.Split('|');
                    if (f.Length < 7) continue;

                    Poi p = new Poi();
                    try { p.Kind = (PoiKind)Enum.Parse(typeof(PoiKind), f[0]); } catch { continue; }
                    p.Label = f[1];
                    p.Pos = new Vector3(F(f[2]), F(f[3]), F(f[4]));
                    p.InStock = (f[5] == "1");
                    p.StockSeenAt = F(f[6]);

                    string k = p.Key();
                    if (!_byKey.ContainsKey(k)) _byKey.Add(k, p);
                }
            }
            catch { /* a corrupt notebook is not worth crashing his game over */ }
            _dirty = false;
        }

        private static float F(string s)
        {
            float v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return 0f;
        }

        private static string S(float v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>Writes at most every few seconds, and only when something changed.</summary>
        public bool SaveIfDirty(float minInterval)
        {
            if (!_dirty) return false;
            if (Time.realtimeSinceStartup - _lastSaveAt < minInterval) return false;
            return Save();
        }

        public bool Save()
        {
            if (string.IsNullOrEmpty(_path)) return false;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Field Notes - what you have found. Safe to hand-edit; delete a line to forget it.");
                sb.AppendLine("# kind|label|x|y|z|inStock|stockSeenAtHours");
                foreach (Poi p in _byKey.Values)
                {
                    sb.Append(p.Kind).Append('|').Append(p.Label).Append('|')
                      .Append(S(p.Pos.x)).Append('|').Append(S(p.Pos.y)).Append('|').Append(S(p.Pos.z))
                      .Append('|').Append(p.InStock ? "1" : "0").Append('|').Append(S(p.StockSeenAt));
                    sb.AppendLine();
                }

                // Write beside, then move into place, so a crash mid-write cannot leave him with
                // half a notebook.
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);

                _dirty = false;
                _lastSaveAt = Time.realtimeSinceStartup;
                return true;
            }
            catch { return false; }
        }
    }
}

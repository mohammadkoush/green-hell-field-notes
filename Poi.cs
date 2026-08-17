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
        // Harmless unless you HANDLE it. A poison dart frog cannot come at you and cannot touch you
        // unless you reach out and pick it up - which is a completely different kind of danger from
        // a spider or a stingray, and a proximity alarm for something that cannot approach is noise
        // pretending to be information. Its own kind so it can be its own colour and its own switch.
        Frog,
        Snake,
        Critter,    // spiders, scorpions, centipedes
        Camp,       // camp gear and abandoned camps
        Food,       // huntable animals - tapir, capybara, peccary, agouti, armadillo, turtle...
        Manual,     // a pin he dropped himself
        // APPENDED, NEVER INSERTED. The kind is serialised as its integer, so slotting a new value
        // into the middle would re-label every POI in every existing notebook - his coconuts would
        // come back as jaguars.
        //
        // Its own kind because it needs its own RULE, not just its own colour: a container is hidden
        // while he knows where it is and shown once he has lost track of it. See Minimap.Draw.
        Container   // bowls, pots, bidons - things he puts down and forgets
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

        /// <summary>
        /// Returns true if this was the first time he has seen it.
        ///
        /// <paramref name="mergeRadius"/> is horizontal and it matters more than it looks. The first
        /// live run came back with 28 "Coconut" entries, and five of them were the same palm tree:
        /// coconuts_on_tree_01 is an item PER COCONUT, so one tree at x471,z1392 produced five POIs
        /// stacked from y105 to y117. Which is precisely the clutter that got wood excluded - a map
        /// where one tree is five icons marks nothing. Merging on the horizontal only is deliberate:
        /// height is what varies within a tree, and distance on the ground is what he walks.
        /// </summary>
        public bool Discover(Poi p, float mergeRadius)
        {
            string k = p.Key();
            if (_byKey.ContainsKey(k)) return false;

            if (mergeRadius > 0f)
            {
                float r2 = mergeRadius * mergeRadius;
                foreach (Poi e in _byKey.Values)
                {
                    if (e.Kind != p.Kind || e.Label != p.Label) continue;
                    float dx = e.Pos.x - p.Pos.x, dz = e.Pos.z - p.Pos.z;
                    if (dx * dx + dz * dz <= r2) return false;   // same tree, same nest, same den
                }
            }

            _byKey.Add(k, p);
            _dirty = true;
            return true;
        }

        public bool Discover(Poi p) { return Discover(p, 0f); }

        /// <summary>
        /// Collapse anything already written down that the merge rule would now reject. Run once on
        /// load so a notebook made before the rule existed cleans itself up instead of needing a
        /// wipe. Returns how many entries were folded away.
        /// </summary>
        public int Compact(float mergeRadius)
        {
            if (mergeRadius <= 0f || _byKey.Count == 0) return 0;

            List<Poi> ordered = new List<Poi>(_byKey.Values);
            _byKey.Clear();
            int dropped = 0;
            for (int i = 0; i < ordered.Count; i++)
                if (!Discover(ordered[i], mergeRadius)) dropped++;

            if (dropped > 0) _dirty = true;
            return dropped;
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

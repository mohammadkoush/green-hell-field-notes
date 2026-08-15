// Field Notes - drawing on the game's own map.
//
// The map is not a UI canvas. MapController.CreateMapObject loads
// "Prefabs/TempPrefabs/Items/Item/map" and instantiates it INTO THE PLAYER'S RIGHT HAND, then points
// a dedicated notepad camera at it. So a marker is a small 3D quad parented to a page, not a sprite
// blitted onto a texture - and it must share the page's Unity LAYER or the notepad camera culls it
// and it is invisible while looking perfectly correct in the hierarchy.
//
// There are TWO maps and the first probe only knew about one:
//   MapController.m_Map   the physical map ITEM, and it exists only while that prop is out. Every
//                         probe attempt came back "open the map first" because of this.
//   MapTab                the NOTEPAD map. A component whose direct children are the pages, named
//                         Map00, Map01... Its m_MapDatas is a public Dictionary<string,MapPageData>
//                         and each MapPageData carries m_Object (the page) plus m_Elemets.
// The notepad page is the right target: it lives in the hierarchy permanently.
//
// WORLD -> MAP, derived from the game's own Player.GetGPSCoordinates rather than guessed:
//
//     zero  = MapTab.m_WorldZeroDummy          one = MapTab.m_WorldOneDummy
//     cellX = (one.x - zero.x) / 35            cellZ = (one.z - zero.z) / 27
//     local = zero.InverseTransformPoint(worldPos)
//     gridX = local.x / cellX + 20             gridZ = local.z / cellZ + 14
//
// The +20 and +14 are the game's own offsets, which tells us the grid runs -20..+15 and -14..+13
// rather than from a corner. Normalising by 35 and 27 gives 0..1 across the sheet.
//
// What CANNOT be derived from the assembly is which way round the page's own axes run, so U/V flip
// and swap are exposed as settings rather than assumed. If the markers land mirrored, that is three
// toggles and no rebuild.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace FieldNotes
{
    internal class MapMarkers
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private GameObject _pageUsed;
        private int _builtForCount = -1;
        private Material _sharedMat;

        internal string LastNote = "";

        /// <summary>The current notepad map page, plus a description of what was found.</summary>
        internal static GameObject GetNotepadPage(out string label)
        {
            label = "";
            MapTab tab = MapTab.Get();
            if (tab == null || tab.m_MapDatas == null || tab.m_MapDatas.Count == 0) return null;

            int current = 0;
            FieldInfo cp = typeof(NotepadTab).GetField("m_CurrentPage",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (cp != null) { try { current = (int)cp.GetValue(tab); } catch { current = 0; } }

            int i = 0;
            GameObject first = null; string firstLabel = "";
            foreach (KeyValuePair<string, MapPageData> kv in tab.m_MapDatas)
            {
                if (kv.Value != null && kv.Value.m_Object != null)
                {
                    string desc = kv.Key + " (" + i + "/" + tab.m_MapDatas.Count + ", " +
                                  (kv.Value.m_Elemets != null ? kv.Value.m_Elemets.Count : 0) + " elements)";
                    if (first == null) { first = kv.Value.m_Object; firstLabel = desc; }
                    if (i == current) { label = desc; return kv.Value.m_Object; }
                }
                i++;
            }
            label = firstLabel + " [fallback]";
            return first;
        }

        // THE PAGE HAS NO RENDERER. The first live run reported
        //     page 'Map00 (0/9, 37 elements)' has no renderer
        // and that is not a fault, it is what the page IS. MapTab.InitMapsData builds each page from
        // a child object and then walks ITS children, calling SetActive(false) on every one and
        // filing them in MapPageData.m_Elemets. So a page is not a sheet of paper with a picture on
        // it - it is a CONTAINER OF 37 MARKERS that the game reveals one at a time as you discover
        // the landmarks. The printed map itself lives elsewhere in the notepad model.
        //
        // Which is better news than a renderer would have been. Those 37 elements are already laid
        // out across the sheet in the page's own local space, so they ARE the calibration: the
        // bounding box of their local positions is the drawable area, measured from the game's own
        // data rather than assumed from a mesh. Self-calibrating, and it cannot drift if the art
        // changes.

        private struct LocalFrame
        {
            public bool Valid;
            public Vector3 Min, Max;      // in page-local space
            public int AxU, AxV, AxThin;  // 0=x 1=y 2=z
            public int Samples;
        }

        /// <summary>
        /// The page's drawable area, derived from where its own elements sit. Needs at least a few
        /// elements; below that there is nothing to measure and we say so rather than guess.
        /// </summary>
        private static LocalFrame MeasurePage(GameObject page)
        {
            LocalFrame f = new LocalFrame();
            f.Valid = false;

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            int n = 0;

            Transform t = page.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (c == null) continue;
                if (c.name.StartsWith("FieldNote")) continue;   // never measure ourselves
                Vector3 p = c.localPosition;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                n++;
            }
            if (n < 4) return f;

            Vector3 span = max - min;

            // The two axes that actually vary are the sheet; the one that barely moves is its
            // thickness.
            int thin = 0;
            if (span.y <= span.x && span.y <= span.z) thin = 1;
            else if (span.z <= span.x && span.z <= span.y) thin = 2;

            f.AxThin = thin;
            f.AxU = (thin == 0 ? 2 : 0);
            f.AxV = (thin == 1 ? 2 : 1);
            f.Min = min; f.Max = max; f.Samples = n; f.Valid = true;
            return f;
        }

        private static float Get(Vector3 v, int axis)
        {
            return axis == 0 ? v.x : (axis == 1 ? v.y : v.z);
        }

        private static Vector3 Set(Vector3 v, int axis, float value)
        {
            if (axis == 0) v.x = value; else if (axis == 1) v.y = value; else v.z = value;
            return v;
        }

        /// <summary>0..1 across the map sheet, or false if the reference dummies are missing.</summary>
        internal static bool WorldToMapUV(Vector3 world, out Vector2 uv)
        {
            uv = Vector2.zero;
            MapTab tab = MapTab.Get();
            if (tab == null || tab.m_WorldZeroDummy == null || tab.m_WorldOneDummy == null) return false;

            Vector3 zero = tab.m_WorldZeroDummy.position;
            Vector3 one = tab.m_WorldOneDummy.position;

            float cellX = (one.x - zero.x) / 35f;
            float cellZ = (one.z - zero.z) / 27f;
            if (Mathf.Abs(cellX) < 0.0001f || Mathf.Abs(cellZ) < 0.0001f) return false;

            Vector3 local = tab.m_WorldZeroDummy.InverseTransformPoint(world);
            float gx = local.x / cellX + 20f;
            float gz = local.z / cellZ + 14f;

            uv = new Vector2(gx / 35f, gz / 27f);
            return true;
        }

        internal void Clear()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) UnityEngine.Object.Destroy(_spawned[i]);
            _spawned.Clear();
            _pageUsed = null;
            _builtForCount = -1;
        }

        /// <summary>
        /// Rebuild only when something actually changed - the page swapped, or the notebook grew.
        /// Called on a timer, so it must be cheap to say "nothing to do".
        /// </summary>
        internal void Refresh(PoiStore store, List<LiveThing> live, Func<PoiKind, bool> enabled,
                              bool flipU, bool flipV, bool swapUV, float markerScale, bool hideEmpty,
                              bool spawnsOn)
        {
            // SAY WHY, EVERY TIME.
            //
            // This subsystem has never drawn a single visible thing and has taken four blind fixes,
            // because "nothing appeared" was all anyone ever had to go on. Every exit below now
            // records a reason, and the reasons are distinct - page not found, page unmeasurable,
            // everything off-sheet, nothing map-worthy - because they need different fixes and
            // guessing between them is what produced the last four rounds.
            string label;
            GameObject page = GetNotepadPage(out label);
            if (page == null)
            {
                MapTab tab = MapTab.Get();
                LastNote = (tab == null)
                    ? "no MapTab in the scene - the notepad has never been opened this session"
                    : "MapTab exists but has no usable page (m_MapDatas has " +
                      (tab.m_MapDatas == null ? "no dictionary" : tab.m_MapDatas.Count + " entries") + ")";
                if (_spawned.Count > 0) Clear();
                return;
            }

            // Live things move, so the map has to be rebuilt whenever their count changes as well as
            // when the notebook grows. The refresh runs on a one second timer, which is as live as a
            // sheet of paper in your hands needs to be.
            int liveCount = (live != null ? live.Count : 0);
            if (page == _pageUsed && _builtForCount == store.Count + liveCount * 1000) return;

            Clear();
            _pageUsed = page;
            _builtForCount = store.Count + liveCount * 1000;

            LocalFrame f = MeasurePage(page);
            if (!f.Valid)
            {
                LastNote = "page '" + label + "' has too few elements to measure";
                return;
            }

            float extU = Get(f.Max, f.AxU) - Get(f.Min, f.AxU);
            float extV = Get(f.Max, f.AxV) - Get(f.Min, f.AxV);
            float midThin = (Get(f.Max, f.AxThin) + Get(f.Min, f.AxThin)) * 0.5f;
            float dot = Mathf.Max(extU, extV) * markerScale;

            int placed = 0, offSheet = 0;
            foreach (Poi p in store.All)
            {
                if (!enabled(p.Kind)) continue;

                // THE MAP IS NOT THE MINIMAP. It carries only the handful of things worth walking
                // across the island for - iron, anthills, beehives, and his own pins. Everything
                // else lives on the minimap, where "what is near me right now" is the question.
                if (!Discovery.IsMapWorthy(p)) continue;

                // Same rule as the minimap: an empty tree is not drawn. Hidden, not forgotten.
                if (hideEmpty && !p.InStock &&
                    (p.Kind == PoiKind.Resource || p.Kind == PoiKind.Camp)) continue;

                // And the same spawn-layer switch, so turning it off clears BOTH surfaces. It is a
                // layer, not a widget.
                if (!spawnsOn && Minimap.IsThreat(p.Kind)) continue;

                Vector2 uv;
                if (!WorldToMapUV(p.Pos, out uv)) { LastNote = "no world dummies on MapTab"; return; }

                float u = uv.x, v = uv.y;
                if (swapUV) { float t = u; u = v; v = t; }
                if (flipU) u = 1f - u;
                if (flipV) v = 1f - v;

                // Outside the sheet is normal - the notebook covers the whole island and one page of
                // nine does not. Counted rather than clamped: clamping would pile every distant
                // marker onto the edges and read as a bug.
                if (u < 0f || u > 1f || v < 0f || v > 1f) { offSheet++; continue; }

                Vector3 local = Vector3.zero;
                local = Set(local, f.AxU, Mathf.Lerp(Get(f.Min, f.AxU), Get(f.Max, f.AxU), u));
                local = Set(local, f.AxV, Mathf.Lerp(Get(f.Min, f.AxV), Get(f.Max, f.AxV), v));
                local = Set(local, f.AxThin, midThin);

                Color c = Minimap.ColorOf(p.Kind);
                if ((p.Kind == PoiKind.Resource || p.Kind == PoiKind.Camp) && !p.InStock) c.a = 0.35f;

                GameObject q = MakeMarker(local, f, dot, c, Icons.For(p.Kind, p.Label), page);
                if (q != null) { _spawned.Add(q); placed++; }
            }

            // The live layer is deliberately NOT drawn on the map. A map is for places, and a
            // wandering jaguar is not a place - it is exactly the thing the minimap exists for.
            int liveOn = 0;
            if (false && live != null)
            {
                for (int i = 0; i < live.Count; i++)
                {
                    LiveThing t = live[i];
                    if (!enabled(t.Kind)) continue;

                    Vector2 uv;
                    if (!WorldToMapUV(t.Pos, out uv)) break;

                    float u = uv.x, v = uv.y;
                    if (swapUV) { float sw = u; u = v; v = sw; }
                    if (flipU) u = 1f - u;
                    if (flipV) v = 1f - v;
                    if (u < 0f || u > 1f || v < 0f || v > 1f) { offSheet++; continue; }

                    Vector3 local = Vector3.zero;
                    local = Set(local, f.AxU, Mathf.Lerp(Get(f.Min, f.AxU), Get(f.Max, f.AxU), u));
                    local = Set(local, f.AxV, Mathf.Lerp(Get(f.Min, f.AxV), Get(f.Max, f.AxV), v));
                    local = Set(local, f.AxThin, midThin);

                    // A shade bigger than a remembered place, for the same reason as on the minimap:
                    // something actually standing there should read first.
                    GameObject q = MakeMarker(local, f, dot * 1.2f, Color.white,
                                              Icons.For(t.Kind, t.Label), page);
                    if (q != null) { _spawned.Add(q); liveOn++; }
                }
            }

            // The distinct outcomes, each pointing at its own fix.
            if (placed == 0 && offSheet == 0)
                LastNote = "page " + label + ": nothing map-worthy known yet. The map only carries " +
                           "iron, anthills, beehives and your own pins - find one, or drop a pin.";
            else if (placed == 0 && offSheet > 0)
                LastNote = "page " + label + ": ALL " + offSheet + " markers landed OFF-SHEET. The " +
                           "world-to-map maths is wrong for this page - Keypad1 dumps the frame.";
            else
                LastNote = "page " + label + ": " + placed + " placed, " + offSheet + " off-sheet (" +
                           f.Samples + " elements measured, axes " + f.AxU + "/" + f.AxV +
                           ", parent active=" + page.activeInHierarchy + ")";
        }

        /// <summary>
        /// A flattened CUBE, not a quad. A quad is single-sided, and which way the page's thin axis
        /// points is not knowable from the data - so half the time a correct marker would be an
        /// invisible one, and we would be debugging maths that was already right. A squashed cube is
        /// visible from either side and removes the question.
        /// </summary>
        private GameObject MakeMarker(Vector3 localPos, LocalFrame f, float size, Color color,
                                      Texture2D icon, GameObject parent)
        {
            try
            {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Cube);
                q.name = "FieldNote";

                // A collider on something held in front of the player's face is asking for trouble.
                Collider col = q.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);

                // The single most important line in this file. The notepad camera renders one layer;
                // a fresh primitive is on layer 0. Get this wrong and everything else is correct and
                // invisible.
                q.layer = parent.layer;

                q.transform.SetParent(parent.transform, false);
                q.transform.localPosition = localPos;
                q.transform.localRotation = Quaternion.identity;

                Vector3 scale = new Vector3(size, size, size);
                scale = Set(scale, f.AxThin, size * 0.06f);   // thin in the page's own thin axis
                q.transform.localScale = scale;

                Renderer r = q.GetComponent<Renderer>();
                if (r != null)
                {
                    if (_sharedMat == null)
                    {
                        // Sprites/Default first: it is unlit and respects alpha, so a cut-out icon
                        // stays a cut-out icon under whatever lighting the notepad camera has.
                        Shader sh = Shader.Find("Sprites/Default");
                        if (sh == null) sh = Shader.Find("Unlit/Transparent");
                        if (sh == null) sh = Shader.Find("Unlit/Color");
                        if (sh == null) sh = Shader.Find("Standard");
                        if (sh != null) _sharedMat = new Material(sh);
                    }
                    if (_sharedMat != null) r.material = new Material(_sharedMat);

                    if (icon != null)
                    {
                        r.material.mainTexture = icon;
                        // White, so the artwork shows its own colours rather than being dyed by the
                        // category. Alpha still carries the out-of-stock fade.
                        r.material.color = new Color(1f, 1f, 1f, color.a);
                    }
                    else r.material.color = color;

                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                return q;
            }
            catch { return null; }
        }

        // ---- diagnostics, kept from the phase-0 probe -------------------------------------------

        internal static string DumpHierarchy()
        {
            StringBuilder sb = new StringBuilder();

            string label;
            GameObject page = GetNotepadPage(out label);
            sb.AppendLine("# Field Notes - map hierarchy dump");
            sb.AppendLine();
            sb.AppendLine("## notepad page: " + (page != null ? page.name + "  [" + label + "]" : "NOT FOUND"));
            if (page != null)
            {
                sb.AppendLine("root layer " + page.layer + " (" + LayerMask.LayerToName(page.layer) + ")");

                // The measured drawable area, and the element positions it came from. If markers land
                // in the wrong place, this is the first thing to read: the frame says which local
                // axes the sheet runs along, and the samples say whether they are plausible.
                LocalFrame f = MeasurePage(page);
                sb.AppendLine();
                sb.AppendLine("## measured frame: " + (f.Valid ? "OK" : "TOO FEW ELEMENTS"));
                if (f.Valid)
                {
                    sb.AppendLine("  local min " + f.Min.ToString("0.0000") + "  max " + f.Max.ToString("0.0000"));
                    sb.AppendLine("  U axis " + f.AxU + "   V axis " + f.AxV + "   thin axis " + f.AxThin);
                    sb.AppendLine("  from " + f.Samples + " elements");
                }
                sb.AppendLine();
                sb.AppendLine("## first elements, local positions");
                int shown = 0;
                for (int i = 0; i < page.transform.childCount && shown < 12; i++)
                {
                    Transform c = page.transform.GetChild(i);
                    if (c == null || c.name.StartsWith("FieldNote")) continue;
                    sb.AppendLine("  " + c.name + "  " + c.localPosition.ToString("0.0000") +
                                  (c.gameObject.activeSelf ? "  [shown]" : "  [hidden]"));
                    shown++;
                }
                sb.AppendLine();
                Walk(page.transform, 0, sb);
            }

            MapTab tab = MapTab.Get();
            sb.AppendLine();
            sb.AppendLine("## MapTab: " + (tab != null ? "present" : "null"));
            if (tab != null)
            {
                sb.AppendLine("zeroDummy: " + (tab.m_WorldZeroDummy != null
                    ? tab.m_WorldZeroDummy.position.ToString("0.00") : "null"));
                sb.AppendLine("oneDummy:  " + (tab.m_WorldOneDummy != null
                    ? tab.m_WorldOneDummy.position.ToString("0.00") : "null"));
                if (tab.m_MapDatas != null)
                    foreach (KeyValuePair<string, MapPageData> kv in tab.m_MapDatas)
                        sb.AppendLine("  page '" + kv.Key + "' obj=" +
                            (kv.Value != null && kv.Value.m_Object != null ? kv.Value.m_Object.name : "null") +
                            " elements=" + (kv.Value != null && kv.Value.m_Elemets != null
                                            ? kv.Value.m_Elemets.Count : 0) +
                            " unlocked=" + (kv.Value != null ? kv.Value.m_Unlocked.ToString() : "?"));
            }

            Player pl = Player.Get();
            if (pl != null)
            {
                Vector2 uv;
                bool ok = WorldToMapUV(pl.transform.position, out uv);
                sb.AppendLine();
                sb.AppendLine("## you are at " + pl.transform.position.ToString("0.0") +
                              "  ->  uv " + (ok ? uv.ToString("0.000") : "UNAVAILABLE"));
                sb.AppendLine("(uv should be inside 0..1 while you are on the island; if it is not,");
                sb.AppendLine(" the world->map derivation needs revisiting before markers mean anything.)");
            }

            return sb.ToString();
        }

        private static void Walk(Transform t, int depth, StringBuilder sb)
        {
            if (t == null || depth > 6) return;
            Renderer r = t.GetComponent<Renderer>();
            sb.AppendLine(new string(' ', depth * 2) + t.name +
                          " | " + (t.gameObject.activeSelf ? "on" : "OFF") +
                          " | L" + t.gameObject.layer +
                          " | " + (r != null ? r.bounds.size.ToString("0.000") : "-"));
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb);
        }
    }
}

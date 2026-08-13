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

        /// <summary>The biggest enabled renderer under the page - in practice the sheet itself.</summary>
        private static Renderer FindSheet(GameObject page)
        {
            Renderer best = null; float bestArea = 0f;
            Renderer[] rs = page.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                Vector3 s = rs[i].bounds.size;
                float area = Mathf.Max(s.x * s.y, Mathf.Max(s.x * s.z, s.y * s.z));
                if (area > bestArea) { bestArea = area; best = rs[i]; }
            }
            return best;
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
        internal void Refresh(PoiStore store, Func<PoiKind, bool> enabled,
                              bool flipU, bool flipV, bool swapUV, float markerScale)
        {
            string label;
            GameObject page = GetNotepadPage(out label);
            if (page == null) { if (_spawned.Count > 0) Clear(); return; }

            if (page == _pageUsed && _builtForCount == store.Count) return;

            Clear();
            _pageUsed = page;
            _builtForCount = store.Count;

            Renderer sheet = FindSheet(page);
            if (sheet == null) { LastNote = "page '" + label + "' has no renderer"; return; }

            Bounds b = sheet.bounds;
            Vector3 size = b.size;

            // The sheet is flat, so its thinnest axis is the normal. Work that out rather than
            // assuming "up" - the map is held in a hand and its orientation follows the animation.
            Vector3 normal, axU, axV;
            float extU, extV;
            if (size.y <= size.x && size.y <= size.z)
            { normal = sheet.transform.up;      axU = sheet.transform.right; axV = sheet.transform.forward; extU = size.x; extV = size.z; }
            else if (size.z <= size.x && size.z <= size.y)
            { normal = sheet.transform.forward; axU = sheet.transform.right; axV = sheet.transform.up;      extU = size.x; extV = size.y; }
            else
            { normal = sheet.transform.right;   axU = sheet.transform.forward; axV = sheet.transform.up;    extU = size.z; extV = size.y; }

            float lift = Mathf.Max(0.0008f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.55f);
            float dot = Mathf.Max(extU, extV) * markerScale;

            int placed = 0, offSheet = 0;
            foreach (Poi p in store.All)
            {
                if (!enabled(p.Kind)) continue;

                Vector2 uv;
                if (!WorldToMapUV(p.Pos, out uv)) { LastNote = "no world dummies on MapTab"; return; }

                float u = uv.x, v = uv.y;
                if (swapUV) { float t = u; u = v; v = t; }
                if (flipU) u = 1f - u;
                if (flipV) v = 1f - v;

                // Outside the sheet is normal - the notebook covers the whole island and one page
                // does not. Counted rather than clamped, because clamping would pile markers up on
                // the edges and read as a bug.
                if (u < 0f || u > 1f || v < 0f || v > 1f) { offSheet++; continue; }

                Vector3 pos = b.center
                            + axU * ((u - 0.5f) * extU)
                            + axV * ((v - 0.5f) * extV)
                            + normal * lift;

                Color c = Minimap.ColorOf(p.Kind);
                if ((p.Kind == PoiKind.Resource || p.Kind == PoiKind.Camp) && !p.InStock) c.a = 0.35f;

                GameObject q = MakeQuad(pos, normal, dot, c, page);
                if (q != null) { _spawned.Add(q); placed++; }
            }

            LastNote = "page " + label + ": " + placed + " marker(s), " + offSheet + " off-sheet";
        }

        private GameObject MakeQuad(Vector3 pos, Vector3 normal, float size, Color color, GameObject parent)
        {
            try
            {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "FieldNote";

                // A collider on something held in front of the player's face is asking for trouble.
                Collider col = q.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);

                // The single most important line in this file. The notepad camera renders one layer;
                // a fresh primitive is on layer 0. Get this wrong and everything else is correct and
                // invisible.
                q.layer = parent.layer;

                q.transform.position = pos;
                q.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);
                q.transform.localScale = new Vector3(size, size, size);
                q.transform.SetParent(parent.transform, true);

                Renderer r = q.GetComponent<Renderer>();
                if (r != null)
                {
                    if (_sharedMat == null)
                    {
                        Shader sh = Shader.Find("Sprites/Default");
                        if (sh == null) sh = Shader.Find("Unlit/Color");
                        if (sh == null) sh = Shader.Find("Standard");
                        if (sh != null) _sharedMat = new Material(sh);
                    }
                    if (_sharedMat != null) r.material = new Material(_sharedMat);
                    r.material.color = color;
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

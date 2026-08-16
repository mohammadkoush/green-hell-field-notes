// The in-game settings menu. Keypad1.
//
// TABS, IN A FIXED ORDER THAT NEVER MOVES. He navigates by remembered position rather than by
// reading labels, so the order below is a contract: nothing is inserted in the middle, nothing moves
// between tabs once it has a home, and anything added later goes on the END. A setting that wanders
// is a setting he has to hunt for every time.
//
// BepInEx has no in-game settings UI of its own unless ConfigurationManager is installed, so this is
// hand-built IMGUI - which is consistent, since the minimap is IMGUI too. Everything it writes goes
// straight into the ConfigEntry objects, so it is saved by BepInEx and survives a restart without
// any file handling here.
//
// Language level is C# 5 (stock Framework csc.exe) - no ?., no $"", no ??=.

using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace FieldNotes
{
    internal class SettingsWindow
    {
        internal bool Visible;

        /// <summary>Set by the close button. The plugin owns closing, because closing means releasing.</summary>
        internal bool WantsClose;

        // THE ORDER IS THE CONTRACT. Append only.
        private static readonly string[] Tabs = { "MINIMAP", "COLOURS", "REVEAL", "SHOW", "DETECTION", "KEYS" };
        private int _tab;

        private Rect _rect = new Rect(0f, 0f, 560f, 520f);
        private bool _placed;
        private Vector2 _scroll;
        private PoiKind _editing = PoiKind.Predator;
        private bool _picking;

        private static Texture2D s_Px;
        private GUIStyle _tabOn, _tabOff, _head, _hint, _swatch;
        private bool _styled;

        // The swatches offered in the picker. Reds first, because red is what most of this map
        // should stay, then the rest of the wheel for anyone who wants a palette.
        private static readonly string[] Swatches =
        {
            "#FF4238", "#FF7A2F", "#FFC53D", "#FFFFFF",
            "#8CE86B", "#3DD9C0", "#5FB4E8", "#B07CFF",
            "#FF7BC1", "#C9A227", "#9AA3B0", "#6B7280",
        };

        // The window never flips its own flag any more. Opening and closing has to take and hand back
        // the cursor, the input block and the pause, and a bool that could be set without doing that
        // is exactly how the game ends up paused with no way out.
        internal void SetVisible(bool on) { Visible = on; _picking = false; }

        private static Texture2D Px()
        {
            if (s_Px == null)
            {
                s_Px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                s_Px.hideFlags = HideFlags.HideAndDontSave;
                s_Px.SetPixel(0, 0, Color.white);
                s_Px.Apply();
            }
            return s_Px;
        }

        private static void Fill(Rect r, Color c)
        {
            Color was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Px());
            GUI.color = was;
        }

        private void BuildStyles()
        {
            if (_styled) return;
            _styled = true;

            _tabOn = new GUIStyle(GUI.skin.label);
            _tabOn.alignment = TextAnchor.MiddleCenter;
            _tabOn.fontStyle = FontStyle.Bold;
            _tabOn.normal.textColor = Color.white;

            _tabOff = new GUIStyle(_tabOn);
            _tabOff.fontStyle = FontStyle.Normal;
            _tabOff.normal.textColor = new Color(0.62f, 0.66f, 0.73f);

            _head = new GUIStyle(GUI.skin.label);
            _head.fontStyle = FontStyle.Bold;
            _head.normal.textColor = new Color(0.85f, 0.88f, 0.93f);

            _hint = new GUIStyle(GUI.skin.label);
            _hint.wordWrap = true;
            _hint.fontSize = 11;
            _hint.normal.textColor = new Color(0.60f, 0.65f, 0.72f);

            _swatch = new GUIStyle(GUI.skin.box);
        }

        internal void Draw(FieldNotesPlugin plugin)
        {
            if (!Visible) return;
            BuildStyles();

            if (!_placed)
            {
                _rect.x = (Screen.width - _rect.width) * 0.5f;
                _rect.y = (Screen.height - _rect.height) * 0.5f;
                _placed = true;
            }

            // Own dark panel rather than the game's box skin, which is a mottled parchment and makes
            // every colour swatch on this window a lie.
            Fill(new Rect(_rect.x - 2f, _rect.y - 2f, _rect.width + 4f, _rect.height + 4f),
                 new Color(0.36f, 0.72f, 0.91f, 0.55f));
            Fill(_rect, new Color(0.055f, 0.065f, 0.08f, 0.97f));

            GUILayout.BeginArea(new Rect(_rect.x + 14f, _rect.y + 12f,
                                         _rect.width - 28f, _rect.height - 24f));

            GUILayout.BeginHorizontal();
            GUILayout.Label("FIELD NOTES", _head);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("close", GUILayout.Width(58f))) WantsClose = true;
            GUILayout.EndHorizontal();

            DrawTabs();
            GUILayout.Space(8f);

            _scroll = GUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case 0: Minimap_(plugin); break;
                case 1: Colours(plugin); break;
                case 2: RevealTab(plugin); break;
                case 3: Show(plugin); break;
                case 4: Detection(plugin); break;
                default: Keys(plugin); break;
            }
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            float w = (_rect.width - 28f) / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
            {
                Rect r = GUILayoutUtility.GetRect(w, 26f);
                bool on = i == _tab;
                Fill(r, on ? new Color(0.36f, 0.72f, 0.91f, 0.22f)
                           : new Color(1f, 1f, 1f, 0.03f));
                if (on) Fill(new Rect(r.x, r.yMax - 2f, r.width, 2f),
                             new Color(0.36f, 0.72f, 0.91f, 0.95f));
                GUI.Label(r, Tabs[i], on ? _tabOn : _tabOff);
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    _tab = i;
                    _picking = false;
                    Event.current.Use();
                }
            }
            GUILayout.EndHorizontal();
        }

        // ---- rows --------------------------------------------------------------------------------

        private static void Toggle(string label, ConfigEntry<bool> entry, string hint, GUIStyle hintStyle)
        {
            GUILayout.BeginHorizontal();
            bool now = GUILayout.Toggle(entry.Value, "  " + label);
            if (now != entry.Value) entry.Value = now;
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(hint)) GUILayout.Label("      " + hint, hintStyle);
        }

        private static void Slider(string label, ConfigEntry<float> entry, float lo, float hi,
                                   string suffix, GUIStyle hintStyle)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(190f));
            float now = GUILayout.HorizontalSlider(entry.Value, lo, hi);
            GUILayout.Label(entry.Value.ToString(entry.Value < 1f ? "0.000" : "0") + suffix,
                            GUILayout.Width(64f));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(now - entry.Value) > 0.0001f) entry.Value = now;
        }

        // ---- tabs --------------------------------------------------------------------------------

        private void Minimap_(FieldNotesPlugin p)
        {
            Toggle("Show the minimap", p.CfgMinimapOn,
                   "Everything else on this tab needs this on.", _hint);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Size", GUILayout.Width(190f));
            string[] sizes = { "Small", "Medium", "Large" };
            for (int i = 0; i < sizes.Length; i++)
            {
                bool on = p.CfgMinimapSize.Value == sizes[i];
                if (GUILayout.Toggle(on, sizes[i], GUI.skin.button) && !on)
                    p.CfgMinimapSize.Value = sizes[i];
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("      A share of screen height - 16% / 22% / 30% - so it looks the same "
                            + "on any monitor.", _hint);
            GUILayout.Space(6f);

            Slider("Silhouette size", p.CfgIconScale, 0.02f, 0.30f, "", _hint);
            GUILayout.Label("      Share of the minimap box. This is a LINEAR scale, so halving the "
                            + "number makes each icon a quarter of the area.", _hint);
            Slider("Range", p.CfgRange, 20f, 400f, "m", _hint);
            Slider("Ring thickness", p.CfgBand, 2f, 60f, "m", _hint);
            Slider("Ping hold", p.CfgPingHold, 0f, 15f, "s", _hint);
            GUILayout.Space(6f);
            Toggle("Heading up", p.CfgHeadingUp,
                   "On: the top of the minimap is where you are looking. Off: north is up.", _hint);
            Toggle("North tick", p.CfgShowNorth,
                   "Only ever drawn heading-up - with north-up it would always point straight up.",
                   _hint);
        }

        private void Colours(FieldNotesPlugin p)
        {
            GUILayout.Label("Everything dangerous ships in one red on purpose: red means exactly one "
                            + "thing, so you react to it instead of decoding it. Change what you "
                            + "like - it is your map.", _hint);
            GUILayout.Space(8f);

            for (int i = 0; i < Palette.Order.Length; i++)
            {
                PoiKind kind = Palette.Order[i];
                GUILayout.BeginHorizontal();

                Rect chip = GUILayoutUtility.GetRect(26f, 18f, GUILayout.Width(26f));
                Fill(chip, new Color(1f, 1f, 1f, 0.25f));
                Fill(new Rect(chip.x + 1f, chip.y + 1f, chip.width - 2f, chip.height - 2f),
                     Palette.Of(kind));

                GUILayout.Label(" " + Palette.LabelOf(kind), GUILayout.Width(230f));
                GUILayout.Label(Palette.HexOf(kind), GUILayout.Width(74f));

                bool open = _picking && _editing == kind;
                if (GUILayout.Button(open ? "done" : "change", GUILayout.Width(66f)))
                {
                    _picking = !open;
                    _editing = kind;
                }
                if (GUILayout.Button("reset", GUILayout.Width(58f))) Palette.Reset(kind);
                GUILayout.EndHorizontal();

                if (open) DrawPicker(kind);
            }

            GUILayout.Space(10f);
            if (GUILayout.Button("Put every colour back to shipped", GUILayout.Width(250f)))
            {
                Palette.ResetAll();
                _picking = false;
            }
        }

        private void DrawPicker(PoiKind kind)
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Space(30f);
            GUILayout.BeginVertical();

            int perRow = 6;
            for (int i = 0; i < Swatches.Length; i++)
            {
                if (i % perRow == 0) GUILayout.BeginHorizontal();
                Rect r = GUILayoutUtility.GetRect(30f, 22f, GUILayout.Width(30f));
                Color c = Palette.Parse(Swatches[i], "#FFFFFF");
                Fill(r, new Color(1f, 1f, 1f, 0.22f));
                Fill(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), c);
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    Palette.Set(kind, c);
                    Event.current.Use();
                }
                if (i % perRow == perRow - 1 || i == Swatches.Length - 1) GUILayout.EndHorizontal();
            }

            Color cur = Palette.Of(kind);
            float r0 = cur.r, g0 = cur.g, b0 = cur.b;
            GUILayout.BeginHorizontal();
            GUILayout.Label("R", GUILayout.Width(14f));
            r0 = GUILayout.HorizontalSlider(r0, 0f, 1f, GUILayout.Width(150f));
            GUILayout.Label("G", GUILayout.Width(14f));
            g0 = GUILayout.HorizontalSlider(g0, 0f, 1f, GUILayout.Width(150f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("B", GUILayout.Width(14f));
            b0 = GUILayout.HorizontalSlider(b0, 0f, 1f, GUILayout.Width(150f));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(r0 - cur.r) > 0.002f || Mathf.Abs(g0 - cur.g) > 0.002f
                || Mathf.Abs(b0 - cur.b) > 0.002f)
                Palette.Set(kind, new Color(r0, g0, b0));

            if (Palette.IsDangerous(kind) && Palette.HexOf(kind) != Palette.DangerRed)
                GUILayout.Label("This one is off the shared red. Fine - just know that red stops "
                                + "meaning one single thing once several colours are in play.", _hint);

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        private void RevealTab(FieldNotesPlugin p)
        {
            GUILayout.Label("A dangerous thing is drawn DIMMED while it is far out, and turns full "
                            + "colour when it crosses inside. The dim version is the same colour "
                            + "darkened, not a neutral grey, so at range it still hints at what it "
                            + "is without naming it.", _hint);
            GUILayout.Space(4f);
            GUILayout.Label("The point is that the dim one is a reward for watching the minimap. "
                            + "Stare at the jungle and you get nothing until it goes red.", _hint);
            GUILayout.Space(8f);
            Toggle("Use the reveal", p.CfgRevealOn,
                   "Off: the old halo ping - legible at exactly one distance, then quiet.", _hint);
            GUILayout.Space(6f);
            Slider("Colour turns on at", p.CfgRevealFraction, 0.05f, 1f, " x", _hint);
            GUILayout.Label("      A share of each category's OWN detection radius, so they keep "
                            + "their character. At 0.40 a jaguar reveals around 22m and a snake "
                            + "around 9m.", _hint);
            Slider("Morph time", p.CfgMorphSeconds, 0f, 5f, "s", _hint);
            GUILayout.Label("      One second to start with. Two if it goes by too fast to notice.",
                            _hint);
        }

        private void Show(FieldNotesPlugin p)
        {
            GUILayout.Label("What is allowed on the minimap at all.", _hint);
            GUILayout.Space(6f);
            Toggle("Big cats and caimans", p.CfgShowPredator, "", _hint);
            Toggle("Savages", p.CfgShowSavage, "", _hint);
            Toggle("Snakes", p.CfgShowSnake, "", _hint);
            Toggle("Spiders, scorpions, stingrays", p.CfgShowCritter, "", _hint);
            Toggle("Food animals", p.CfgShowFood, "", _hint);
            Toggle("Plants and fruit", p.CfgShowResource, "", _hint);
            Toggle("Camp gear", p.CfgShowCamp, "", _hint);
            Toggle("Your own pins", p.CfgShowManual, "", _hint);
            GUILayout.Space(10f);
            GUILayout.Label("LAYERS", _head);
            Toggle("Live layer", p.CfgLiveOn,
                   "What is actually out there right now, on top of what you remember.", _hint);
            Toggle("Live things use the halo", p.CfgLiveHalo,
                   "Off: live things sit where they really are instead of on the ring.", _hint);
            Toggle("Remembered spawn points", p.CfgSpawnsOn,
                   "Creature spawn points only. Your resources and pins are not affected.", _hint);
            Toggle("Hide emptied resources", p.CfgHideEmpty,
                   "A picked tree stops being drawn until it grows back.", _hint);
        }

        private void Detection(FieldNotesPlugin p)
        {
            GUILayout.Label("THESE ARE THE DIFFICULTY OF THE MOD. A threat is legible at exactly one "
                            + "distance - its own detection radius - and then goes quiet. Big things "
                            + "ping early because you need room to react; small ones ping late, "
                            + "because the fright is the content.", _hint);
            GUILayout.Space(8f);
            Slider("Big cats and caimans", p.CfgRPredator, 5f, 300f, "m", _hint);
            Slider("Savages", p.CfgRSavage, 5f, 300f, "m", _hint);
            Slider("Snakes", p.CfgRSnake, 5f, 300f, "m", _hint);
            Slider("Critters", p.CfgRCritter, 5f, 300f, "m", _hint);
            GUILayout.Space(10f);
            GUILayout.Label("DISCOVERY", _head);
            Slider("Notice things within", p.CfgDiscoverRadius, 5f, 200f, "m", _hint);
            Slider("Read stock within", p.CfgSeeRadius, 2f, 100f, "m", _hint);
        }

        private void Keys(FieldNotesPlugin p)
        {
            GUILayout.Label("Keypad only. F4/F6/F7/F9/F10/F11/P and Keypad5 belong to other mods, "
                            + "and Keypad7 to Pickup Doctor.", _hint);
            GUILayout.Space(8f);
            Row("Keypad 1", "This menu");
            Row("Keypad 3", "Minimap on / off");
            Row("Keypad 8", "Size - Small / Medium / Large");
            Row("Keypad 4", "Drop your own pin here");
            Row("Keypad 0", "Remove your nearest pin");
            Row("Keypad 9", "What do I know? A quick count");
            Row("Keypad 6", "Live layer on / off");
            Row("Shift + Keypad 6", "Remembered spawn points on / off");
            GUILayout.Space(10f);
            GUILayout.Label("Keys are rebound in the .cfg, not here - a rebinding UI that can trap "
                            + "you inside itself is worse than editing a file once.", _hint);
        }

        private void Row(string key, string what)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(key, GUILayout.Width(140f));
            GUILayout.Label(what);
            GUILayout.EndHorizontal();
        }
    }
}

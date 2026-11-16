using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    /// <summary>
    /// Shared visual primitives for the sandbox and career HUDs: warm paper panels and rounded IMGUI controls
    /// that scale with the screen (fonts are sized up, not stretched, so text stays sharp), and the parts of the
    /// screen the HUD covers, so clicks and scrolls over it don't reach the world tools (PointerOverHud).
    ///
    ///   DevGui.Begin();                          // first thing in OnGUI
    ///   DevGui.Panel(rect);                      // background; also marks the rect as HUD
    ///   if (DevGui.Button(r, "Runway", "B", on: tool == Runway)) ...
    /// </summary>
    public static class DevGui
    {
        // written as sRGB; GUI() converts them for the project's colour space
        public static readonly Color Ink = new Color(.14f, .23f, .28f);
        public static readonly Color Paper = new Color(.98f, .975f, .94f);
        public static readonly Color Surface = new Color(.89f, .93f, .91f);
        public static readonly Color Primary = new Color(.06f, .43f, .44f);
        public static readonly Color Success = new Color(.13f, .52f, .36f);
        static readonly Color text = Ink, muted = new Color(.39f, .47f, .49f);
        static readonly Color panelFill = Paper, panelEdge = new Color(1f, 1f, 1f, .9f);
        static readonly Color buttonFill = Surface, buttonHover = new Color(.79f, .87f, .84f);
        static readonly Color onText = Ink, shadow = new Color(.07f, .17f, .19f, .24f);

        /// <summary>On / selected: the aircraft selection outline's amber.</summary>
        public static readonly Color Accent = new Color(1f, .75f, .22f);
        /// <summary>The delete tool and delete buttons.</summary>
        public static readonly Color Danger = new Color(.77f, .23f, .23f);

        /// <summary>IMGUI takes colours as they're stored in a linear-space project: convert sRGB ones for it.</summary>
        public static Color GUI(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        /// <summary>A colour as a rich-text tag ("&lt;color=#...&gt;"), converted like GUI().</summary>
        public static string Tag(Color c) => "<color=#" + ColorUtility.ToHtmlStringRGB(GUI(c)) + ">";

        public static Color Muted => muted;

        /// <summary>H hides the whole HUD (screenshots).</summary>
        public static bool Visible = true;
        public static bool GateDesignerOpen { get; set; }
        public static bool WorldHandleCaptured { get; set; }
        static bool keyboardCaptured;
        /// <summary>Runtime text fields own the keyboard until focus leaves them.</summary>
        public static bool KeyboardCaptured { get => Visible && keyboardCaptured; set => keyboardCaptured = value; }

        /// <summary>Pixels per HUD unit: 1 up to 1000 px tall, more on bigger screens.</summary>
        public static float Scale { get; private set; } = 1f;

        /// <summary>HUD units to whole pixels.</summary>
        public static float U(float units) => Mathf.Round(units * Scale);

        public static GUIStyle Label, Bold, Small, Caption, Title, Hint;
        static GUIStyle buttonText, buttonOnText, dangerText, dangerOnText, keyHint, keyHintOn, cardSub, cardSubOn, tooltipText, primaryText;
        static Color Text, OnText, PanelFill, PanelEdge, ButtonFill, ButtonHover, Shadow, AccentFill, DangerFill, DangerHover, Well;
        static float builtScale;
        static GUISkin hudSkin;
        static Texture2D scrollbarThumb;

        static readonly List<Rect> covered = new List<Rect>();
        static int coveredFrame = -10;

        /// <summary>Call first in OnGUI: (re)builds the styles for the screen size.</summary>
        public static void Begin()
        {
            Scale = Mathf.Round(Mathf.Clamp(Screen.height / 1000f, 1f, 2.5f) * 4f) / 4f;
            if (Label != null && Mathf.Approximately(builtScale, Scale))
            { UnityEngine.GUI.skin = hudSkin; return; }
            builtScale = Scale;
            Text = GUI(text);
            PanelFill = GUI(panelFill);
            PanelEdge = GUI(panelEdge);
            ButtonFill = GUI(buttonFill);
            ButtonHover = GUI(buttonHover);
            Shadow = GUI(shadow);
            AccentFill = GUI(Accent);
            DangerFill = GUI(Danger);
            DangerHover = GUI(new Color(Danger.r, Danger.g, Danger.b, 0.28f));
            Well = GUI(new Color(.82f, .88f, .85f));
            var dim = GUI(muted);
            var onDark = OnText = GUI(onText);
            GUIStyle Style(float size, Color color, FontStyle font = FontStyle.Normal, TextAnchor align = TextAnchor.MiddleLeft) =>
                new GUIStyle
                {
                    fontSize = Mathf.RoundToInt(size * Scale), fontStyle = font, alignment = align, richText = true, wordWrap = false,
                    normal = { textColor = color }, hover = { textColor = color }, active = { textColor = color },
                    clipping = TextClipping.Clip,
                };
            Label = Style(14f, Text);
            tooltipText = new GUIStyle(Label) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            Bold = Style(14f, Text, FontStyle.Bold);
            Small = Style(12f, dim);
            Caption = Style(10f, dim, FontStyle.Bold);
            Title = Style(19f, Text, FontStyle.Bold);
            Hint = Style(12f, dim);
            buttonText = Style(13f, Text, FontStyle.Bold, TextAnchor.MiddleCenter);
            buttonOnText = Style(13f, onDark, FontStyle.Bold, TextAnchor.MiddleCenter);
            primaryText = Style(13f, GUI(Color.white), FontStyle.Bold, TextAnchor.MiddleCenter);
            dangerText = Style(12f, DangerFill, FontStyle.Bold, TextAnchor.MiddleCenter);
            dangerOnText = Style(12f, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            keyHint = Style(10f, dim);
            keyHintOn = Style(10f, new Color(onDark.r, onDark.g, onDark.b, 0.6f));
            cardSub = Style(11f, dim, FontStyle.Normal, TextAnchor.MiddleCenter);
            cardSubOn = Style(11f, new Color(onDark.r, onDark.g, onDark.b, 0.8f), FontStyle.Normal, TextAnchor.MiddleCenter);
            if (hudSkin != null) UnityEngine.Object.Destroy(hudSkin);
            if (scrollbarThumb != null) UnityEngine.Object.Destroy(scrollbarThumb);
            hudSkin = UnityEngine.Object.Instantiate(UnityEngine.GUI.skin); hudSkin.hideFlags = HideFlags.HideAndDontSave;
            scrollbarThumb = new Texture2D(4, 4) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[16]; for (int i = 0; i < pixels.Length; i++) pixels[i] = GUI(new Color(.53f, .66f, .60f));
            scrollbarThumb.SetPixels(pixels); scrollbarThumb.Apply();
            hudSkin.verticalScrollbar = new GUIStyle { fixedWidth = U(8), margin = new RectOffset((int)U(4), 0, 0, 0) };
            hudSkin.verticalScrollbarThumb = new GUIStyle { normal = { background = scrollbarThumb }, border = new RectOffset(1, 1, 1, 1), fixedWidth = U(5) };
            hudSkin.verticalScrollbarUpButton = new GUIStyle { fixedHeight = 0 };
            hudSkin.verticalScrollbarDownButton = new GUIStyle { fixedHeight = 0 };
            UnityEngine.GUI.skin = hudSkin;
        }

        // ------------------------------------------------------------------ what the HUD covers

        /// <summary>Mark a GUI rect as HUD for this frame (done by Panel).</summary>
        public static void Cover(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (coveredFrame != Time.frameCount)
            {
                covered.Clear();
                coveredFrame = Time.frameCount;
            }
            covered.Add(r);
        }

        /// <summary>Is the mouse over the HUD drawn last frame? World tools ignore clicks and scrolls there.</summary>
        public static bool PointerOverHud
        {
            get
            {
                var mouse = Mouse.current;
                if (mouse == null || Time.frameCount - coveredFrame > 1) return false;
                var p = mouse.position.ReadValue();
                var gui = new Vector2(p.x, Screen.height - p.y);
                foreach (var r in covered)
                    if (r.Contains(gui)) return true;
                return false;
            }
        }

        // ------------------------------------------------------------------ drawing

        public static void Fill(Rect r, Color color, float radius = 0f)
        {
            if (Event.current.type != EventType.Repaint) return;
            UnityEngine.GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius);
        }

        public static void Outline(Rect r, Color color, float radius = 0f, float width = 1f)
        {
            if (Event.current.type != EventType.Repaint) return;
            UnityEngine.GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, width, radius);
        }

        /// <summary>A rounded, shadowed panel; the rect becomes HUD.</summary>
        public static void Panel(Rect r)
        {
            Cover(r);
            float radius = U(16f);
            Fill(new Rect(r.x, r.y + U(5f), r.width, r.height), Shadow, radius);
            Fill(r, PanelFill, radius);
            Outline(r, PanelEdge, radius);
        }

        /// <summary>A thin vertical rule between groups.</summary>
        public static void Divider(float x, float y, float height) => Fill(new Rect(x, y, U(1f), height), GUI(new Color(.72f, .80f, .77f)));

        public static void SurfaceCard(Rect r) => Fill(r, GUI(Surface), U(12));

        public static void StatusPill(Rect r, string label, Color color)
        {
            Fill(r, GUI(Color.Lerp(Paper, color, .16f)), U(12));
            var style = new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            style.normal.textColor = GUI(color.grayscale > .55f ? Color.Lerp(color, Ink, .65f) : color);
            float available = r.width - U(14);
            string shown = label ?? "";
            while (shown.Length > 1 && Width(shown, style) > available) shown = shown.Substring(0, shown.Length - 1);
            if (shown != label && shown.Length > 1) shown = shown.Substring(0, shown.Length - 1) + "…";
            UnityEngine.GUI.Label(r, new GUIContent(shown, label), style);
        }

        public static void ProgressBar(Rect r, float amount, Color color)
        {
            Fill(r, GUI(Color.Lerp(Surface, Ink, .12f)), U(6));
            if (amount > 0) Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(amount), r.height), GUI(color), U(6));
        }

        public static bool PrimaryButton(Rect r, string label, string tip = null, bool enabled = true)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            Fill(r, GUI(enabled ? (hover ? Color.Lerp(Primary, Color.white, .12f) : Primary) : Color.Lerp(Paper, Primary, .35f)), U(9));
            bool was = UnityEngine.GUI.enabled;
            UnityEngine.GUI.enabled = was && enabled;
            bool clicked = UnityEngine.GUI.Button(r, new GUIContent("", tip), GUIStyle.none);
            UnityEngine.GUI.Label(r, label, primaryText);
            UnityEngine.GUI.enabled = was;
            return clicked;
        }

        /// <summary>
        /// A rounded button: filled amber while on (red for danger), else dark with a hover; danger buttons that
        /// aren't on have red text. A hint (its key) shows small in the bottom corner, the tip on hover.
        /// </summary>
        public static bool Button(Rect r, string text, string tip = null, bool on = false, bool danger = false,
            bool enabled = true, string hint = null)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            var fill = on ? (danger ? DangerFill : AccentFill) : hover ? (danger ? DangerHover : ButtonHover) : ButtonFill;
            if (on && hover) fill = Color.Lerp(fill, Color.white, 0.15f);
            if (!enabled) fill = Color.Lerp(PanelFill, ButtonFill, .5f);
            Fill(r, fill, U(9f));
            var style = on ? (danger ? dangerOnText : buttonOnText) : danger ? dangerText : buttonText;
            var hintStyle = on ? keyHintOn : keyHint;
            // tall buttons: the key in the bottom corner; short ones: beside the label
            bool tall = r.height >= U(36f);
            float hintWidth = hint != null && !tall ? Width(hint, hintStyle) + U(4f) : 0f;
            bool was = UnityEngine.GUI.enabled;
            UnityEngine.GUI.enabled = was && enabled;
            bool clicked = UnityEngine.GUI.Button(r, new GUIContent("", tip), GUIStyle.none);
            if (Event.current.type == EventType.Repaint)
            {
                UnityEngine.GUI.Label(new Rect(r.x, r.y, r.width - hintWidth, r.height), text, style);
                if (hint != null)
                {
                    hintStyle.alignment = tall ? TextAnchor.LowerRight : TextAnchor.MiddleRight;
                    UnityEngine.GUI.Label(new Rect(r.x, r.y, r.width - U(tall ? 5f : 7f), r.height - (tall ? U(3f) : 0f)), hint, hintStyle);
                }
            }
            UnityEngine.GUI.enabled = was;
            return clicked;
        }

        /// <summary>
        /// A square tool button showing its icon (white art, drawn light, dark while on, red for danger), or
        /// `fallback` text if there's no icon; its key small in the corner and its tip on hover.
        /// </summary>
        public static bool IconButton(Rect r, Texture icon, string fallback, string tip = null, bool on = false, bool danger = false,
            string hint = null)
        {
            bool clicked = Button(r, icon != null ? "" : fallback, tip, on, danger, true, hint);
            if (icon != null && Event.current.type == EventType.Repaint)
            {
                float s = Mathf.Round(Mathf.Min(r.width, r.height) * 0.54f);
                var ir = new Rect(Mathf.Round(r.center.x - s * 0.5f), Mathf.Round(r.center.y - s * 0.5f - (hint != null ? U(2f) : 0f)), s, s);
                var tint = on ? (danger ? Color.white : OnText) : danger ? DangerFill : Text;
                UnityEngine.GUI.DrawTexture(ir, icon, ScaleMode.ScaleToFit, true, 0f, tint, 0f, 0f);
            }
            return clicked;
        }

        /// <summary>
        /// A choice card (the tools' tray): a picture in a dark well at the top (`picture` draws it into the rect it
        /// gets, at repaint), its name under it and an optional small line under that; amber while chosen, faded
        /// when it can't be had.
        /// </summary>
        public static bool Card(Rect r, Action<Rect> picture, string label, string sub, string tip, bool on, bool enabled = true)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            var fill = on ? AccentFill : hover ? ButtonHover : ButtonFill;
            if (on && hover) fill = Color.Lerp(fill, Color.white, 0.15f);
            Fill(r, fill, U(12f));
            if (on) Outline(r, GUI(Primary), U(12), U(2));
            bool was = UnityEngine.GUI.enabled;
            UnityEngine.GUI.enabled = was && enabled;
            bool clicked = UnityEngine.GUI.Button(r, new GUIContent("", tip), GUIStyle.none);
            UnityEngine.GUI.enabled = was;
            if (Event.current.type != EventType.Repaint) return clicked;
            float inset = U(4f), text = sub != null ? U(30f) : U(20f);
            var well = new Rect(r.x + inset, r.y + inset, r.width - inset * 2f, r.height - inset - text);
            Fill(well, Well, U(8f));
            picture?.Invoke(well);
            UnityEngine.GUI.Label(new Rect(r.x, well.yMax + U(2f), r.width, U(16f)), label, on ? buttonOnText : buttonText);
            if (sub != null) UnityEngine.GUI.Label(new Rect(r.x, well.yMax + U(16f), r.width, U(12f)), sub, on ? cardSubOn : cardSub);
            if (!enabled) Fill(r, new Color(PanelFill.r, PanelFill.g, PanelFill.b, 0.6f), U(6f));
            return clicked;
        }

        /// <summary>A white icon tinted, scaled to fit the rect (repaint only).</summary>
        public static void DrawIcon(Rect r, Texture icon, Color tint)
        {
            if (icon == null || Event.current.type != EventType.Repaint) return;
            UnityEngine.GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit, true, 0f, GUI(tint), 0f, 0f);
        }

        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

        /// <summary>A HUD icon (ArtSource/UI/icons.py, in Resources/AirCityIcons), or null if there's none.</summary>
        public static Texture2D Icon(string name)
        {
            if (!icons.TryGetValue(name, out var tex) || tex == null) icons[name] = tex = Resources.Load<Texture2D>("AirCityIcons/" + name);
            return tex;
        }

        public static float Width(string text, GUIStyle style) => style.CalcSize(new GUIContent(text)).x;

        /// <summary>A wrapped tip kept inside the viewport. Call last in OnGUI; tips don't capture world input.</summary>
        public static void Tooltip()
        {
            if (string.IsNullOrEmpty(UnityEngine.GUI.tooltip) || Event.current.type != EventType.Repaint) return;
            var content = new GUIContent(UnityEngine.GUI.tooltip);
            float pad = U(7f), margin = U(8f);
            float maxWidth = Mathf.Max(1f, Mathf.Min(U(420f), Screen.width - margin * 2f));
            float width = Mathf.Min(maxWidth, Mathf.Ceil(Label.CalcSize(content).x) + pad * 2f);
            float textWidth = Mathf.Max(1f, width - pad * 2f);
            float height = Mathf.Min(Mathf.Max(1f, Screen.height - margin * 2f), Mathf.Ceil(tooltipText.CalcHeight(content, textWidth)) + pad * 2f);
            var m = Event.current.mousePosition;
            var r = new Rect(m.x + U(14f), m.y - height - U(8f), width, height);
            r.x = Mathf.Clamp(r.x, margin, Mathf.Max(margin, Screen.width - r.width - margin));
            if (r.y < margin) r.y = m.y + U(22f);
            r.y = Mathf.Clamp(r.y, margin, Mathf.Max(margin, Screen.height - r.height - margin));
            Fill(r, PanelFill, U(9f));
            Outline(r, PanelEdge, U(6f));
            UnityEngine.GUI.Label(new Rect(r.x + pad, r.y + pad, textWidth, Mathf.Max(1f, r.height - pad * 2f)), content, tooltipText);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    /// <summary>
    /// The sandbox HUD's look and plumbing (development only, not game UI): dark rounded IMGUI panels and buttons
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
        static readonly Color text = new Color(0.91f, 0.93f, 0.96f), muted = new Color(0.56f, 0.61f, 0.68f);
        static readonly Color panelFill = new Color(0.07f, 0.085f, 0.11f, 0.92f), panelEdge = new Color(1f, 1f, 1f, 0.09f);
        static readonly Color buttonFill = new Color(1f, 1f, 1f, 0.06f), buttonHover = new Color(1f, 1f, 1f, 0.14f);
        static readonly Color onText = new Color(0.1f, 0.08f, 0.04f), shadow = new Color(0f, 0f, 0f, 0.28f);

        /// <summary>On / selected: the aircraft selection outline's amber.</summary>
        public static readonly Color Accent = new Color(1f, 0.74f, 0.1f);
        /// <summary>The delete tool and delete buttons.</summary>
        public static readonly Color Danger = new Color(0.93f, 0.3f, 0.3f);

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
        static GUIStyle buttonText, buttonOnText, dangerText, dangerOnText, keyHint, keyHintOn, cardSub, cardSubOn;
        static Color Text, OnText, PanelFill, PanelEdge, ButtonFill, ButtonHover, Shadow, AccentFill, DangerFill, DangerHover, Well;
        static float builtScale;

        static readonly List<Rect> covered = new List<Rect>();
        static int coveredFrame = -10;

        /// <summary>Call first in OnGUI: (re)builds the styles for the screen size.</summary>
        public static void Begin()
        {
            Scale = Mathf.Round(Mathf.Clamp(Screen.height / 1000f, 1f, 2.5f) * 4f) / 4f;
            if (Label != null && Mathf.Approximately(builtScale, Scale)) return;
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
            Well = GUI(new Color(0f, 0f, 0f, 0.32f));
            var dim = GUI(muted);
            var onDark = OnText = GUI(onText);
            GUIStyle Style(float size, Color color, FontStyle font = FontStyle.Normal, TextAnchor align = TextAnchor.MiddleLeft) =>
                new GUIStyle
                {
                    fontSize = Mathf.RoundToInt(size * Scale), fontStyle = font, alignment = align, richText = true, wordWrap = false,
                    normal = { textColor = color }, hover = { textColor = color }, active = { textColor = color },
                    clipping = TextClipping.Overflow,
                };
            Label = Style(13f, Text);
            Bold = Style(13f, Text, FontStyle.Bold);
            Small = Style(11f, dim);
            Caption = Style(10f, dim, FontStyle.Bold);
            Title = Style(14f, Text, FontStyle.Bold);
            Hint = Style(12f, dim);
            buttonText = Style(12f, Text, FontStyle.Normal, TextAnchor.MiddleCenter);
            buttonOnText = Style(12f, onDark, FontStyle.Bold, TextAnchor.MiddleCenter);
            dangerText = Style(12f, DangerFill, FontStyle.Bold, TextAnchor.MiddleCenter);
            dangerOnText = Style(12f, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            keyHint = Style(10f, dim);
            keyHintOn = Style(10f, new Color(onDark.r, onDark.g, onDark.b, 0.6f));
            cardSub = Style(10f, dim, FontStyle.Normal, TextAnchor.MiddleCenter);
            cardSubOn = Style(10f, new Color(onDark.r, onDark.g, onDark.b, 0.7f), FontStyle.Normal, TextAnchor.MiddleCenter);
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
            float radius = U(10f);
            Fill(new Rect(r.x, r.y + U(3f), r.width, r.height), Shadow, radius);
            Fill(r, PanelFill, radius);
            Outline(r, PanelEdge, radius);
        }

        /// <summary>A thin vertical rule between groups.</summary>
        public static void Divider(float x, float y, float height) => Fill(new Rect(x, y, 1f, height), PanelEdge);

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
            Fill(r, fill, U(6f));
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
            Fill(r, fill, U(6f));
            bool was = UnityEngine.GUI.enabled;
            UnityEngine.GUI.enabled = was && enabled;
            bool clicked = UnityEngine.GUI.Button(r, new GUIContent("", tip), GUIStyle.none);
            UnityEngine.GUI.enabled = was;
            if (Event.current.type != EventType.Repaint) return clicked;
            float inset = U(4f), text = sub != null ? U(30f) : U(20f);
            var well = new Rect(r.x + inset, r.y + inset, r.width - inset * 2f, r.height - inset - text);
            Fill(well, Well, U(4f));
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

        /// <summary>The hovered control's tip, in a small box above the cursor. Call last in OnGUI.</summary>
        public static void Tooltip()
        {
            if (string.IsNullOrEmpty(UnityEngine.GUI.tooltip) || Event.current.type != EventType.Repaint) return;
            var content = new GUIContent(UnityEngine.GUI.tooltip);
            var size = Label.CalcSize(content);
            float pad = U(7f);
            var m = Event.current.mousePosition;
            var r = new Rect(m.x + U(14f), m.y - size.y - pad * 2f - U(8f), size.x + pad * 2f, size.y + pad);
            r.x = Mathf.Clamp(r.x, 4f, Screen.width - r.width - 4f);
            if (r.y < 4f) r.y = m.y + U(22f);
            Fill(r, GUI(new Color(0.03f, 0.035f, 0.05f, 0.96f)), U(6f));
            Outline(r, PanelEdge, U(6f));
            UnityEngine.GUI.Label(new Rect(r.x + pad, r.y, r.width - pad * 2f, r.height), content, Label);
        }
    }
}

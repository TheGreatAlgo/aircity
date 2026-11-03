using AirCity.Airfield;
using UnityEngine;

namespace AirCity.Dev
{
    /// <summary>The gate build palette: opaque paper surfaces, blue actions, generous spacing and quiet controls.</summary>
    static class GateDesignerGui
    {
        public static readonly Color Ink = new Color(0.13f, 0.21f, 0.29f);
        public static readonly Color Muted = new Color(0.40f, 0.48f, 0.56f);
        public static readonly Color Blue = new Color(0.08f, 0.46f, 0.72f);
        public static readonly Color Teal = new Color(0.12f, 0.64f, 0.65f);
        public static readonly Color Paper = new Color(0.97f, 0.98f, 0.99f);
        public static readonly Color Surface = new Color(0.91f, 0.94f, 0.96f);
        public static readonly Color Border = new Color(0.83f, 0.88f, 0.92f);
        public static readonly Color Red = new Color(0.76f, 0.24f, 0.25f);
        public static GUIStyle Title, Label, Bold, Small, Caption, Hint, Input, NumberInput, Centre, ButtonText, ButtonOn, DangerText, ErrorCentre, Tab;
        static GUIStyle sliderThumb;
        public static GUISkin Skin;
        public static GUIStyle Card;
        static float builtScale;
        static Texture2D scrollbarThumb;
        public static float U(float value) => DevGui.U(value);
        static Color C(Color color) => DevGui.GUI(color);

        public static void Begin()
        {
            if (Label != null && Skin != null && Mathf.Approximately(builtScale, DevGui.Scale)) return;
            builtScale = DevGui.Scale;
            GUIStyle Text(float size, Color color, FontStyle weight = FontStyle.Normal, TextAnchor align = TextAnchor.MiddleLeft) =>
                new GUIStyle { fontSize = Mathf.RoundToInt(U(size)), fontStyle = weight, alignment = align,
                    wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = C(color) } };
            Title = Text(23f, Ink, FontStyle.Bold);
            Label = Text(14f, Ink);
            Bold = Text(14f, Ink, FontStyle.Bold);
            Small = Text(12f, Muted);
            Caption = Text(10f, Muted, FontStyle.Bold);
            Hint = Text(12f, Muted); Hint.wordWrap = true;
            Centre = Text(14f, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            Tab = Text(12f, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            ButtonText = Text(13f, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            ButtonOn = Text(13f, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            Input = new GUIStyle { font = GUI.skin.font, fontSize = Mathf.RoundToInt(U(14)), alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset((int)U(12), (int)U(12), 0, 0), border = new RectOffset() };
            foreach (var state in new[] { Input.normal, Input.focused, Input.hover, Input.active, Input.onNormal, Input.onFocused, Input.onHover, Input.onActive })
            { state.background = null; state.textColor = C(Ink); }
            NumberInput = new GUIStyle(Input) { alignment = TextAnchor.MiddleCenter };
            DangerText = new GUIStyle(ButtonText) { normal = { textColor = C(Red) } };
            ErrorCentre = new GUIStyle(Centre) { normal = { textColor = C(Red) } };
            sliderThumb = new GUIStyle { fixedWidth = U(14), fixedHeight = U(20) };
            Card = new GUIStyle { padding = new RectOffset((int)U(16), (int)U(16), (int)U(16), (int)U(16)),
                margin = new RectOffset(0, 0, 0, (int)U(14)) };
            if (Skin != null) Object.Destroy(Skin);
            Skin = Object.Instantiate(GUI.skin); Skin.hideFlags = HideFlags.HideAndDontSave;
            Skin.verticalScrollbar = new GUIStyle { fixedWidth = U(8), margin = new RectOffset((int)U(6), 0, 0, 0) };
            if (scrollbarThumb != null) Object.Destroy(scrollbarThumb);
            scrollbarThumb = new Texture2D(4, 4) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[16]; for (int i = 0; i < pixels.Length; i++) pixels[i] = C(Border);
            scrollbarThumb.SetPixels(pixels); scrollbarThumb.Apply();
            Skin.verticalScrollbarThumb = new GUIStyle { normal = { background = scrollbarThumb }, border = new RectOffset(1, 1, 1, 1), fixedWidth = U(4) };
            Skin.verticalScrollbarUpButton = new GUIStyle { fixedHeight = 0 };
            Skin.verticalScrollbarDownButton = new GUIStyle { fixedHeight = 0 };
            Skin.settings.selectionColor = C(new Color(0.7f, 0.85f, 0.95f)); Skin.settings.cursorColor = C(Ink);
        }

        public static void Panel(Rect rect)
        {
            DevGui.Cover(rect);
            DevGui.Fill(new Rect(rect.x, rect.y + U(6), rect.width, rect.height), C(new Color(0.03f, 0.09f, 0.14f, 0.22f)), U(18));
            DevGui.Fill(rect, C(Paper), U(16));
            DevGui.Outline(rect, C(Color.white), U(16));
        }
        public static void Fill(Rect rect, Color color, float radius = 8) => DevGui.Fill(rect, C(color), U(radius));
        public static void Outline(Rect rect, Color color, float radius = 8, float width = 1) => DevGui.Outline(rect, C(color), U(radius), U(width));

        public static bool Button(Rect rect, string text, bool on = false, bool primary = false, bool danger = false, string tip = null)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            var color = danger ? new Color(0.99f, 0.93f, 0.93f) : on || primary ? Blue : hover ? new Color(0.85f, 0.91f, 0.96f) : Surface;
            if (hover && (on || primary)) color = Color.Lerp(color, Color.white, 0.10f);
            Fill(rect, color);
            var style = danger ? DangerText : on || primary ? ButtonOn : ButtonText;
            GUI.Label(rect, text, style);
            return GUI.Button(rect, new GUIContent("", tip), GUIStyle.none);
        }

        public static bool Toggle(Rect rect, string label, bool on)
        {
            GUI.Label(new Rect(rect.x, rect.y, rect.width - U(51), rect.height), label, Label);
            var track = new Rect(rect.xMax - U(42), rect.center.y - U(11), U(42), U(22));
            Fill(track, on ? Blue : Border, 12);
            Fill(new Rect(track.x + U(on ? 23 : 3), track.y + U(3), U(16), U(16)), Color.white, 9);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        public static float Slider(Rect rect, float value, float min, float max)
        {
            var track = new Rect(rect.x + U(7), rect.center.y - U(2), rect.width - U(14), U(4));
            Fill(track, Border, 3);
            float t = Mathf.InverseLerp(min, max, value);
            Fill(new Rect(track.x, track.y, track.width * t, track.height), Blue, 3);
            var thumb = new Rect(track.x + track.width * t - U(7), rect.center.y - U(7), U(14), U(14));
            Fill(thumb, Blue, 8);
            return GUI.HorizontalSlider(rect, value, min, max, GUIStyle.none, sliderThumb);
        }

        public static void Line(Vector2 a, Vector2 b, Color color, float width = 2)
        {
            if (Event.current.type != EventType.Repaint) return;
            var previous = GUI.matrix;
            var delta = b - a;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, a);
            Fill(new Rect(a.x, a.y - U(width) * 0.5f, delta.magnitude, U(width)), color, 1);
            GUI.matrix = previous;
        }

        public static void BridgeGlyph(Rect rect, Color color, int sections = 3)
        {
            float size = Mathf.Min(rect.width, rect.height);
            var centre = rect.center;
            Fill(new Rect(centre.x - size * 0.42f, centre.y - size * 0.11f, size * 0.22f, size * 0.22f), color, 20);
            for (int i = 0; i < sections; i++)
                Fill(new Rect(centre.x - size * 0.25f + size * 0.19f * i, centre.y - size * (0.09f - i * 0.008f), size * 0.22f, size * (0.18f - i * 0.016f)), color, 2);
        }
        public static void BuildingGlyph(Rect rect, Color color, bool tall = false)
        {
            float s = Mathf.Min(rect.width, rect.height), h = s * (tall ? 0.65f : 0.42f);
            var body = new Rect(rect.center.x - s * 0.33f, rect.center.y + s * 0.24f - h, s * 0.66f, h);
            Fill(body, color, 3);
            for (int y = 0; y < (tall ? 3 : 2); y++)
                for (int x = 0; x < 3; x++)
                    Fill(new Rect(body.x + s * (0.10f + x * 0.17f), body.y + s * (0.08f + y * 0.18f), s * 0.08f, s * 0.07f), Paper, 1);
        }

        public static void ParkingGlyph(Rect rect, Color color)
        {
            Outline(new Rect(rect.x + rect.width * .12f, rect.y + rect.height * .08f, rect.width * .76f, rect.height * .84f), color, 3);
            Line(new Vector2(rect.center.x, rect.y + rect.height * .25f), new Vector2(rect.center.x, rect.yMax), color);
            Line(new Vector2(rect.x + rect.width * .3f, rect.y + rect.height * .3f), new Vector2(rect.x + rect.width * .7f, rect.y + rect.height * .3f), color);
            Line(new Vector2(rect.center.x, rect.y + rect.height * .45f), new Vector2(rect.x + rect.width * .25f, rect.y + rect.height * .65f), color);
            Line(new Vector2(rect.center.x, rect.y + rect.height * .45f), new Vector2(rect.x + rect.width * .75f, rect.y + rect.height * .65f), color);
        }

        public static void FacadeGlyph(Rect rect, FacadeStyle style)
        {
            var body = new Rect(rect.x + U(4), rect.y + U(6), rect.width - U(8), rect.height - U(8));
            Fill(body, new Color(0.64f, 0.80f, 0.86f), 1);
            var frame = style == FacadeStyle.Minimal ? Muted : new Color(0.77f, 0.80f, 0.80f);
            float thickness = style == FacadeStyle.Minimal ? 1f : style == FacadeStyle.Framed ? 3f : 2f;
            for (int i = 0; i <= 6; i++)
                Line(new Vector2(body.x + body.width * i / 6f, body.y), new Vector2(body.x + body.width * i / 6f, body.yMax), frame, style == FacadeStyle.Minimal ? 1 : 1.5f);
            Line(new Vector2(body.x, body.center.y), new Vector2(body.xMax, body.center.y), frame, 1);
            Fill(new Rect(rect.x, rect.y + U(2), rect.width, U(style == FacadeStyle.Minimal ? 2 : style == FacadeStyle.Framed ? 7 : 5)), frame, 1);
            if (style != FacadeStyle.Minimal)
                for (int i = 0; i < 3; i++)
                {
                    float x = body.x + body.width * (i + 0.5f) / 3f;
                    if (style == FacadeStyle.Colonnade)
                    {
                        Line(new Vector2(x, body.yMax), new Vector2(x - U(10), body.y), Paper, 2);
                        Line(new Vector2(x, body.yMax), new Vector2(x + U(10), body.y), Paper, 2);
                    }
                    else Line(new Vector2(x, body.yMax), new Vector2(x, body.y), style == FacadeStyle.Framed ? Ink : frame, thickness);
                }
        }

        public static void AngleGlyph(Rect rect, float angle)
        {
            float bottom = rect.yMax - U(15), top = rect.y + U(12), x = rect.center.x - U(18);
            float lean = Mathf.Tan(angle * Mathf.Deg2Rad) * (bottom - top);
            Fill(rect, Surface, 9);
            Line(new Vector2(rect.x + U(22), bottom), new Vector2(rect.xMax - U(22), bottom), Border);
            Line(new Vector2(x, bottom), new Vector2(x, top), Border, 1);
            Line(new Vector2(x, bottom), new Vector2(x + lean, top), Blue, 4);
            Line(new Vector2(x + lean, top), new Vector2(x + lean - U(50), top), Muted);
            GUI.Label(new Rect(rect.center.x + U(40), rect.center.y - U(12), U(60), U(24)), angle.ToString("0.#") + "°", Bold);
        }
        public static void RoofGlyph(Rect rect, RoofForm form)
        {
            Vector2 previous = default;
            for (int i = 0; i <= 48; i++)
            {
                float u = i / 48f;
                float height = form == RoofForm.Wave ? (1f - Mathf.Cos(u * Mathf.PI * 4f)) * 0.5f :
                    form == RoofForm.Scalloped ? Mathf.Pow(Mathf.Repeat(u * 3f, 1f) * 2f - 1f, 2f) : 0.5f;
                var p = new Vector2(rect.x + rect.width * u, rect.yMax - U(4) - height * (rect.height - U(8)));
                if (i > 0) Line(previous, p, Blue, 3);
                previous = p;
            }
        }
    }
}

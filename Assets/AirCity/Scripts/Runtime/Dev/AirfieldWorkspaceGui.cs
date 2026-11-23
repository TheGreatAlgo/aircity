using System;
using UnityEngine;

namespace AirCity.Dev
{
    /// <summary>The shared frame for editing buildings while keeping the airport visible.</summary>
    static class AirfieldWorkspaceGui
    {
        static readonly Color HeaderFill = new Color(.09f, .17f, .19f), RailFill = new Color(.13f, .21f, .22f);
        static readonly Color RailHover = new Color(.19f, .28f, .29f), LightText = new Color(.94f, .97f, .95f);
        static readonly Color LightMuted = new Color(.71f, .79f, .78f);
        static GUIStyle titleStyle, captionStyle, tabStyle, selectedStyle, compactTabStyle, compactSelectedStyle, controlStyle;
        static float builtScale;
        static float U(float n) => DevGui.U(n);
        public static float Margin => U(12);
        public static float Scrollbar => U(18);

        public readonly struct Tab
        {
            public readonly string Label, Icon;
            public Tab(string label, string icon) { Label = label; Icon = icon; }
        }

        public readonly struct Layout
        {
            public readonly Rect Panel, Header, Rail, Body, Footer;
            public readonly bool Compact;
            public Layout(Rect panel)
            {
                Panel = panel;
                Compact = panel.width < U(430);
                Header = new Rect(panel.x + 1, panel.y + 1, panel.width - 2, U(58));
                Footer = new Rect(panel.x + 1, panel.yMax - U(50), panel.width - 2, U(49));
                Rail = new Rect(Header.x, Header.yMax, Compact ? Header.width : U(126),
                    Compact ? U(48) : Footer.y - Header.yMax);
                float x = Compact ? Header.x : Rail.xMax;
                float y = Compact ? Rail.yMax : Header.yMax;
                Body = new Rect(x, y, Header.xMax - x, Footer.y - y);
            }
        }

        public static Rect PanelRect(float top, float height, float bottomSpace = 92)
        {
            float width = Mathf.Min(U(494), Screen.width - 2 * Margin);
            return new Rect(Screen.width - Margin - width, top, width,
                Mathf.Min(U(height), Screen.height - top - U(bottomSpace)));
        }

        static void Styles()
        {
            if (titleStyle != null && Mathf.Approximately(DevGui.Scale, builtScale)) return;
            builtScale = DevGui.Scale;
            GUIStyle Tint(GUIStyle source, Color color)
            {
                return new GUIStyle(source) { normal = { textColor = DevGui.GUI(color) } };
            }
            titleStyle = Tint(DevGui.Title, LightText);
            captionStyle = Tint(DevGui.Small, LightMuted);
            tabStyle = Tint(DevGui.Bold, LightText);
            tabStyle.fontSize = Mathf.RoundToInt(U(13));
            selectedStyle = Tint(tabStyle, DevGui.Ink);
            compactTabStyle = new GUIStyle(tabStyle) { alignment = TextAnchor.MiddleCenter };
            compactSelectedStyle = new GUIStyle(selectedStyle) { alignment = TextAnchor.MiddleCenter };
            controlStyle = Tint(tabStyle, LightText);
            controlStyle.alignment = TextAnchor.MiddleCenter;
        }

        public static void Frame(Layout layout, string caption, string title, string icon, Action close,
            string toggleLabel = null, Action toggle = null)
        {
            Styles();
            DevGui.Panel(layout.Panel);
            var header = layout.Header;
            DevGui.Fill(header, DevGui.GUI(HeaderFill), U(12));
            DevGui.Fill(new Rect(header.x, header.center.y, header.width, header.height * .5f + 1), DevGui.GUI(HeaderFill));
            DevGui.DrawIcon(new Rect(header.x + U(13), header.center.y - U(12), U(24), U(24)), DevGui.Icon(icon), DevGui.Accent);
            float controls = toggle != null ? U(118) : U(48);
            float start = header.x + U(47), textWidth = Mathf.Max(0, header.width - U(47) - controls);
            GUI.Label(new Rect(start, header.y + U(8), textWidth, U(17)), new GUIContent(caption, caption), captionStyle);
            GUI.Label(new Rect(start, header.y + U(24), textWidth, U(24)), new GUIContent(title, title), titleStyle);
            var closeRect = new Rect(header.xMax - U(41), header.center.y - U(15), U(30), U(30));
            if (HeaderButton(closeRect, "×", "Close workspace (Esc)")) close();
            if (toggle != null && HeaderButton(new Rect(closeRect.x - U(72), closeRect.y, U(65), closeRect.height),
                toggleLabel, "Pause or resume the airport while editing")) toggle();
            DevGui.Fill(new Rect(layout.Footer.x, layout.Footer.y, layout.Footer.width, 1), DevGui.GUI(RailHover));
        }

        static bool HeaderButton(Rect rect, string label, string tip)
        {
            bool hover = GUI.enabled && rect.Contains(Event.current.mousePosition);
            DevGui.Fill(rect, DevGui.GUI(hover ? RailHover : HeaderFill), U(5));
            DevGui.Outline(rect, DevGui.GUI(hover ? DevGui.Accent : RailHover), U(5));
            GUI.Label(rect, label, controlStyle);
            return GUI.Button(rect, new GUIContent("", tip), GUIStyle.none);
        }

        public static void Navigation(Layout layout, Tab[] tabs, int active, Action<int> change)
        {
            var rail = layout.Rail;
            DevGui.Fill(rail, DevGui.GUI(RailFill));
            float compactWidth = (rail.width - U(12)) / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                const float rowUnits = 34;
                var rect = layout.Compact
                    ? new Rect(rail.x + U(6) + i * compactWidth, rail.y + U(5), compactWidth - U(3), U(38))
                    : new Rect(rail.x + U(5), rail.y + U(8) + i * U(rowUnits + 2), rail.width - U(10), U(rowUnits));
                bool selected = i == active;
                bool hover = GUI.enabled && rect.Contains(Event.current.mousePosition);
                if (selected || hover) DevGui.Fill(rect, DevGui.GUI(selected ? DevGui.Accent : RailHover), U(3));
                float iconSize = U(17);
                var texture = DevGui.Icon(tabs[i].Icon);
                float iconLeft = layout.Compact ? rect.center.x - iconSize * .5f : rect.x + U(7);
                float iconTop = layout.Compact ? rect.y + U(2) : rect.center.y - iconSize * .5f;
                if (texture != null) DevGui.DrawIcon(new Rect(iconLeft, iconTop, iconSize, iconSize), texture, selected ? DevGui.Ink : LightText);
                var label = layout.Compact
                    ? new Rect(rect.x + U(3), rect.y + U(19), rect.width - U(6), U(17))
                    : new Rect(rect.x + U(30), rect.y, rect.width - U(33), rect.height);
                var style = layout.Compact ? (selected ? compactSelectedStyle : compactTabStyle) : (selected ? selectedStyle : tabStyle);
                GUI.Label(label, tabs[i].Label, style);
                if (GUI.Button(rect, new GUIContent("", tabs[i].Label), GUIStyle.none) && !selected) change(i);
            }
        }

        public static void FeatureIcon(Rect rect, string icon)
        {
            DevGui.Fill(rect, DevGui.GUI(DevGui.Surface), U(4));
            rect.xMin += U(5); rect.yMin += U(5); rect.xMax -= U(5); rect.yMax -= U(5);
            DevGui.DrawIcon(rect, DevGui.Icon(icon), DevGui.Primary);
        }
    }
}

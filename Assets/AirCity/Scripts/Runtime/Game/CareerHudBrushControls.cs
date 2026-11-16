using System.Globalization;
using AirCity.Dev;
using AirCity.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Game
{
    public partial class CareerHud
    {
        const string BrushHeightControl = "AirCity.BrushHeight";
        GUIStyle brushValue, brushField, brushTrack, brushThumb, quotePrice, quoteReason;
        string heightText;
        int heldHeightDirection, heightStepFrame = -1;
        float heightHoldStart, heightHoldLast;
        Rect brushTrayRect, worldQuoteRect, heightDownRect, heightUpRect, heightValueRect, radiusSliderRect;
        bool BrushKeyboardCaptured => builder != null && builder.Tool == CareerTool.Level &&
            GUI.GetNameOfFocusedControl() == BrushHeightControl;

        void BrushStyles()
        {
            if (brushValue != null && brushValue.fontSize == DevGui.Bold.fontSize) return;
            brushValue = new GUIStyle(DevGui.Bold) { alignment = TextAnchor.MiddleCenter };
            brushField = new GUIStyle(brushValue) { clipping = TextClipping.Clip,
                padding = new RectOffset((int)U(5), (int)U(24), 0, 0) };
            brushTrack = new GUIStyle { fixedHeight = U(30) };
            brushThumb = new GUIStyle { fixedWidth = U(16), fixedHeight = U(24) };
            quotePrice = new GUIStyle(DevGui.Bold) { alignment = TextAnchor.MiddleLeft };
            quotePrice.normal.textColor = DevGui.GUI(new Color(.98f, .99f, .96f));
            quoteReason = new GUIStyle(DevGui.Small) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            quoteReason.normal.textColor = DevGui.GUI(new Color(.91f, .95f, .94f));
        }

        void BrushTray(Rect active, Rect bar)
        {
            BrushStyles();
            bool level = builder.Tool == CareerTool.Level;
            bool radius = level || !builder.SingleTree;
            float width = Mathf.Min(U(level ? 380 : radius ? 448 : 156), Screen.width - U(24));
            bool stackedRadius = !level && radius && width < U(400);
            float height = U(level || stackedRadius ? 88 : 52);
            var r = brushTrayRect = new Rect(Mathf.Clamp(active.center.x - width * .5f, U(12), Screen.width - width - U(12)),
                bar.y - U(8) - height, width, height);
            Panel(r);
            float x = r.x + U(10), y = r.y + U(10), inner = r.width - U(20);
            if (Event.current.type == EventType.MouseDown && !r.Contains(Event.current.mousePosition) && BrushKeyboardCaptured)
            { CommitHeightText(); GUI.FocusControl(null); }
            if (level)
            {
                float spinnerWidth = Mathf.Min(U(212), inner - U(72));
                GUI.Label(new Rect(x + U(2), y, inner - spinnerWidth - U(8), U(30)), inner < U(300) ? "Height" : "Target height", DevGui.Small);
                float spinner = x + inner - spinnerWidth;
                heightDownRect = new Rect(spinner, y, U(32), U(30));
                heightUpRect = new Rect(x + inner - U(32), y, U(32), U(30));
                heightValueRect = new Rect(heightDownRect.xMax + U(4), y,
                    heightUpRect.x - heightDownRect.xMax - U(8), U(30));
                bool down = HeightButton(heightDownRect, "−", "Lower target height. Hold to move faster.");
                HeightField(heightValueRect);
                bool up = HeightButton(heightUpRect, "+", "Raise target height. Hold to move faster.");
                RepeatHeight(down ? -1 : up ? 1 : 0);
                y += U(38);
            }
            else
            {
                ResetHeightHold();
                if (Button(new Rect(x, y, U(64), U(30)), "Brush", !builder.SingleTree,
                    tip: "Clear trees, rocks and fallen timber inside the brush."))
                { builder.SingleTree = false; GUI.FocusControl(null); }
                if (Button(new Rect(x + U(68), y, U(58), U(30)), "Tree", builder.SingleTree,
                    tip: "Remove one selected tree."))
                { builder.SingleTree = true; GUI.FocusControl(null); }
                if (stackedRadius) y += U(38);
                else if (radius) { DevGui.Divider(x + U(137), y + U(4), U(22)); x += U(150); inner -= U(150); }
            }
            if (radius) BrushRadius(new Rect(x, y, inner, U(30)));
            if (BrushKeyboardCaptured) game.view.KeyboardCaptured = DevGui.KeyboardCaptured = true;
        }

        void BrushRadius(Rect r)
        {
            float caption = U(46), value = U(54), gap = U(8);
            GUI.Label(new Rect(r.x + U(2), r.y, caption - U(2), r.height), "Radius", DevGui.Small);
            radiusSliderRect = new Rect(r.x + caption, r.y, r.width - caption - value - gap, r.height);
            builder.Radius = Mathf.Round(BrushSlider(radiusSliderRect, builder.Radius, 15f, 90f));
            var number = new Rect(r.xMax - value, r.y, value, r.height);
            DevGui.Fill(number, DevGui.GUI(DevGui.Surface), U(7));
            GUI.Label(number, $"{builder.Radius:0} m", brushValue);
        }

        float BrushSlider(Rect r, float value, float minimum, float maximum)
        {
            value = GUI.HorizontalSlider(r, value, minimum, maximum, brushTrack, brushThumb);
            if (Event.current.type == EventType.ScrollWheel && r.Contains(Event.current.mousePosition))
            { value -= Event.current.delta.y; Event.current.Use(); }
            value = Mathf.Clamp(value, minimum, maximum);
            float t = Mathf.InverseLerp(minimum, maximum, value), knob = U(14);
            var track = new Rect(r.x + knob * .5f, r.center.y - U(3), r.width - knob, U(6));
            DevGui.Fill(track, DevGui.GUI(new Color(.77f, .84f, .80f)), U(3));
            DevGui.Fill(new Rect(track.x, track.y, track.width * t, track.height), DevGui.GUI(DevGui.Primary), U(3));
            var handle = new Rect(track.x + t * track.width - knob * .5f, r.center.y - knob * .5f, knob, knob);
            DevGui.Fill(handle, DevGui.GUI(DevGui.Paper), U(7));
            DevGui.Outline(handle, DevGui.GUI(DevGui.Primary), U(7), U(2));
            return value;
        }

        bool HeightButton(Rect r, string text, string tip)
        {
            int control = GUIUtility.GetControlID(text.GetHashCode(), FocusType.Passive, r);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && GUI.enabled && r.Contains(e.mousePosition))
            { GUIUtility.hotControl = control; e.Use(); }
            if (e.type == EventType.MouseUp && e.button == 0 && GUIUtility.hotControl == control)
            { GUIUtility.hotControl = 0; e.Use(); }
            bool held = GUI.enabled && GUIUtility.hotControl == control && Mouse.current != null && Mouse.current.leftButton.isPressed;
            bool hover = r.Contains(Event.current.mousePosition);
            DevGui.Fill(r, DevGui.GUI(held ? DevGui.Accent : hover ? new Color(.79f, .87f, .84f) : DevGui.Surface), U(7));
            GUI.Label(r, new GUIContent(text, tip), brushValue);
            return held;
        }

        void HeightField(Rect r)
        {
            bool focused = GUI.GetNameOfFocusedControl() == BrushHeightControl;
            if (!focused) heightText = builder.TargetHeight.ToString("0.0", CultureInfo.InvariantCulture);
            DevGui.Fill(r, DevGui.GUI(DevGui.Surface), U(7));
            if (focused) DevGui.Outline(r, DevGui.GUI(DevGui.Primary), U(7));
            GUI.SetNextControlName(BrushHeightControl);
            string next = GUI.TextField(r, heightText ?? "", 12, brushField);
            if (heightText != next) { heightText = next; CommitHeightText(false); }
            GUI.Label(new Rect(r.xMax - U(24), r.y, U(19), r.height), "m", DevGui.Small);
            if (focused && Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            { CommitHeightText(); GUI.FocusControl(null); Event.current.Use(); }
        }

        void CommitHeightText(bool round = true)
        {
            if (float.TryParse((heightText ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float target) &&
                ValleyWorld.Finite(target))
                builder.TargetHeight = Mathf.Clamp(round ? Mathf.Round(target * 10f) / 10f : target, 10f, CareerConstruction.MaxBuildHeight);
            if (round) heightText = builder.TargetHeight.ToString("0.0", CultureInfo.InvariantCulture);
        }

        void RepeatHeight(int direction)
        {
            if (direction == 0) { ResetHeightHold(); return; }
            float now = Time.unscaledTime;
            if (heldHeightDirection != direction)
            {
                heldHeightDirection = direction; heightHoldStart = heightHoldLast = now; heightStepFrame = Time.frameCount;
                CommitHeightText(); GUI.FocusControl(null);
                builder.TargetHeight = Mathf.Clamp(builder.TargetHeight + direction * .5f, 10f, CareerConstruction.MaxBuildHeight);
            }
            else if (heightStepFrame != Time.frameCount)
            {
                float elapsed = now - heightHoldStart, delta = Mathf.Min(.1f, now - heightHoldLast);
                float rate = BrushHeightRate(elapsed);
                builder.TargetHeight = Mathf.Clamp(builder.TargetHeight + direction * rate * delta, 10f, CareerConstruction.MaxBuildHeight);
                heightStepFrame = Time.frameCount; heightHoldLast = now;
            }
            heightText = builder.TargetHeight.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>A short press is precise; holding accelerates without following the simulation speed.</summary>
        public static float BrushHeightRate(float heldSeconds) => heldSeconds < .35f ? 0f : heldSeconds < 1f ? 4f : heldSeconds < 2.5f ? 12f : 40f;
        void ResetHeightHold() { heldHeightDirection = 0; heightStepFrame = -1; }

        void BuildWorldQuote()
        {
            if (gameMenu || newWorldDialog || regionBrowser.IsOpen || game.view.PointerOverHud) return;
            var runwayTool = game.SharedTools ? game.BuildTools : null;
            var runway = runwayTool != null ? runwayTool.PlacingRunway : null;
            if (runway != null)
            {
                string why = runwayTool.RunwayProblem;
                bool valid = string.IsNullOrEmpty(why) && (game.Sandbox || runwayTool.RunwayPrice <= game.State.funds);
                if (!game.Sandbox && runwayTool.RunwayPrice > game.State.funds) why = "Not enough funds.";
                MapQuote(runway.Airport.transform.TransformPoint(runway.end), runwayTool.RunwayPrice, valid, why);
                return;
            }
            if(runwayTool!=null && (runwayTool.AirsideDrawing || runwayTool.StreetDrawing))
            {
                bool street=runwayTool.StreetDrawing;
                var end=street?runwayTool.StreetEndpoint:runwayTool.AirsideEndpoint;
                string why=street?runwayTool.StreetProblem:runwayTool.RoadProblem;
                int price=street?runwayTool.StreetPrice:runwayTool.AirsidePrice;
                MapQuote(end,price,string.IsNullOrEmpty(why),why);
                return;
            }
            if(runwayTool!=null && runwayTool.StandDrawing)
            {
                string why=runwayTool.GhostProblem;
                MapQuote(runwayTool.StandEndpoint,runwayTool.StandPrice,string.IsNullOrEmpty(why),why);
                return;
            }
            if (builder == null || (builder.Tool != CareerTool.ClearTrees && builder.Tool != CareerTool.Level) || !builder.TryPoint(out var point)) return;
            string reason = builder.Valid ? null : !game.Sandbox && builder.Price > game.State.funds ? "Not enough funds." : builder.Status;
            MapQuote(new Vector3(point.x, game.valley.DisplayHeight(point), point.y), builder.Price, builder.Valid, reason);
        }

        void MapQuote(Vector3 point, int cost, bool valid, string reason)
        {
            BrushStyles();
            var camera = game.view.GetComponent<Camera>();
            var position = camera.WorldToScreenPoint(point);
            if (position.z <= 0f) return;
            string price = !valid && cost<=0 ? "Cannot build here" : game.Sandbox ? "Free" : Money(cost);
            float maxWidth = Mathf.Min(U(270), Screen.width - U(24)), pad = U(10);
            float width = Mathf.Min(maxWidth, Mathf.Max(U(82), quotePrice.CalcSize(new GUIContent(price)).x + pad * 2f));
            float reasonHeight = 0f;
            if (!string.IsNullOrEmpty(reason))
            {
                width = Mathf.Min(maxWidth, Mathf.Max(U(220), width, quoteReason.CalcSize(new GUIContent(reason)).x + pad * 2f));
                int lines=1;foreach(char c in reason)if(c=='\n')lines++;
                reasonHeight = Mathf.Max(quoteReason.CalcHeight(new GUIContent(reason), width - pad * 2f) + U(8),lines*U(17)+U(6));
            }
            float height = U(28) + reasonHeight, x = position.x + U(22), y = Screen.height - position.y - height - U(12);
            if (x + width > Screen.width - U(12)) x = position.x - width - U(22);
            var r = worldQuoteRect = new Rect(Mathf.Clamp(x, U(12), Screen.width - width - U(12)),
                Mathf.Clamp(y, HeaderHeight + U(6), Screen.height - height - U(12)), width, height);
            // This read-only world label deliberately does not capture map input.
            DevGui.Fill(new Rect(r.x, r.y + U(3), r.width, r.height), DevGui.GUI(new Color(0, 0, 0, .22f)), U(8));
            DevGui.Fill(r, DevGui.GUI(Rail), U(8));
            DevGui.Fill(new Rect(r.x, r.y + U(7), U(3), U(14)), DevGui.GUI(valid ? DevGui.Accent : DevGui.Danger), U(1));
            GUI.Label(new Rect(r.x + pad, r.y + U(2), r.width - pad * 2f, U(24)), price, quotePrice);
            if (reasonHeight > 0f) GUI.Label(new Rect(r.x + pad, r.y + U(27), r.width - pad * 2f, reasonHeight), reason, quoteReason);
        }
    }
}

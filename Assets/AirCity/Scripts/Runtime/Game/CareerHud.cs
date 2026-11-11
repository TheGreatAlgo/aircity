using System.Collections.Generic;
using AirCity.Dev;
using AirCity.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Game
{
    /// <summary>Career objectives and earned tools, using the same visual controls as the sandbox.</summary>
    [RequireComponent(typeof(CareerGame))]
    public class CareerHud : MonoBehaviour
    {
        public Material guideMaterial;
        CareerGame game;
        CareerBuildTool builder;
        GUIStyle body, field;
        float styleScale;
        readonly List<Rect> hitAreas = new List<Rect>();
        readonly List<CareerTool> tools = new List<CareerTool>();
        string seedText = "19770616", unlockedMessage, observedMessage;
        bool newWorldDialog, gameMenu, clearFocus, visible = true;
        int previousUnlocks;
        float unlockUntil, messageUntil;
        Vector2 scroll;
        LineRenderer boundary;
        ValleyWorld drawnWorld;
        int drawnSite = -2;
        static readonly string[] Icons = { "select", "clear-trees", "level-ground", "runway", "taxiway", "stand", "road", "terminal" };
        static readonly string[] Labels = { "Select", "Clear trees", "Level ground", "Runway", "Taxiway", "Parking", "Public road", "Reception" };
        static readonly int[] Groups = { 0, 1, 1, 2, 2, 2, 3, 3 };
        static float U(float units) => DevGui.U(units);

        void Awake() { game = GetComponent<CareerGame>(); builder = GetComponent<CareerBuildTool>(); }
        void Update()
        {
            if (game.State == null) return;
            if (builder == null) builder = GetComponent<CareerBuildTool>();
            var keys = Keyboard.current;
            if (keys != null)
            {
                if (keys.escapeKey.wasPressedThisFrame)
                { gameMenu = newWorldDialog = false; game.view.KeyboardCaptured = false; clearFocus = true; }
                if (keys.hKey.wasPressedThisFrame && !game.view.KeyboardCaptured && !newWorldDialog)
                { visible = !visible; hitAreas.Clear(); }
            }
            var mouse = Mouse.current;
            var p = mouse == null ? Vector2.zero : new Vector2(mouse.position.x.ReadValue(), Screen.height - mouse.position.y.ReadValue());
            game.view.PointerOverHud = visible && (newWorldDialog || hitAreas.Exists(r => r.Contains(p)));
            if (!visible) game.view.KeyboardCaptured = false;
            int unlocks = game.State.layout != null ? game.State.layout.unlocked : 0;
            if (drawnWorld != game.State.world)
            {
                previousUnlocks = unlocks; unlockedMessage = observedMessage = null; unlockUntil = messageUntil = 0f;
                if (game.State.Site == null) seedText = game.State.world.seed.ToString();
            }
            if (previousUnlocks != 0 && unlocks != previousUnlocks)
            {
                for (int i = 0; i < Labels.Length; i++)
                    if ((unlocks & (1 << i)) != 0 && (previousUnlocks & (1 << i)) == 0) unlockedMessage = Labels[i] + " unlocked";
                unlockUntil = Time.unscaledTime + 5f;
            }
            previousUnlocks = unlocks;
            if (observedMessage != game.Message) { observedMessage = game.Message; messageUntil = Time.unscaledTime + 6f; }
            int site = game.State.Site != null ? game.State.selectedSite : game.SurveyedSite;
            if (drawnWorld != game.State.world || drawnSite != site) { drawnWorld = game.State.world; drawnSite = site; DrawBoundary(); }
        }

        void OnGUI()
        {
            if (game == null || game.State == null || !visible) return;
            DevGui.Begin(); Styles();
            if (clearFocus) { GUI.FocusControl(null); clearFocus = false; }
            if (Event.current.type == EventType.Repaint) hitAreas.Clear();
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !newWorldDialog;
            SessionControls();
            GUI.enabled = enabled && !newWorldDialog;
            if (game.State.Site == null) SiteSelection();
            else { Objective(); Toolbar(); Notice(); }
            GUI.enabled = enabled;
            if (newWorldDialog) NewWorldDialog();
            game.view.KeyboardCaptured = newWorldDialog || GUI.GetNameOfFocusedControl() == "WorldSeed";
            DevGui.Tooltip();
        }

        void SessionControls()
        {
            var r = new Rect(Screen.width - U(234), U(12), U(222), U(40)); Panel(r);
            GUI.Label(new Rect(r.x + U(12), r.y + U(7), U(93), U(26)), Money(game.State.funds), DevGui.Bold);
            if (Button(new Rect(r.x + U(110), r.y + U(7), U(48), U(26)), "Save", enabled: game.State.Site != null)) game.Save();
            if (Button(new Rect(r.x + U(162), r.y + U(7), U(53), U(26)), game.Paused ? "Paused" : "Game",
                gameMenu || game.Paused, tip: "Game menu · Focus, time controls and new game")) gameMenu = !gameMenu;
            if (!gameMenu) return;
            var menu = new Rect(r.x, r.yMax + U(8), r.width, U(game.State.Site == null ? 111 : 143)); Panel(menu);
            float x = menu.x + U(10), y = menu.y + U(10), width = menu.width - U(20);
            if (Button(new Rect(x, y, width, U(26)), game.State.Site == null ? "Region view" : "Focus airport"))
            { if (game.State.Site == null) game.view.Region(); else game.Focus(); gameMenu = false; }
            y += U(32);
            if (Button(new Rect(x, y, U(70), U(26)), game.Paused ? "Resume" : "Pause", game.Paused)) game.Paused = !game.Paused;
            for (int i = 0; i < 3; i++)
            {
                int speed = i == 0 ? 1 : i == 1 ? 5 : 20;
                if (Button(new Rect(x + U(76 + i * 42), y, U(38), U(26)), speed + "×", game.Speed == speed)) game.Speed = speed;
            }
            y += U(32);
            if (game.State.Site != null)
            {
                if (Button(new Rect(x, y, width, U(26)), "New game")) { newWorldDialog = true; gameMenu = false; }
                y += U(32);
            }
            GUI.Label(new Rect(x, y, width, U(18)), "Mouse orbit/zoom · H hide UI", DevGui.Small);
        }

        void SiteSelection()
        {
            float width = Mathf.Min(U(300), Screen.width - U(24));
            float description = body.CalcHeight(new GUIContent(game.State.world.sites[game.SurveyedSite].description), width - U(28));
            float height = U(390) + (game.HasSave ? U(33) : 0f) + Mathf.Max(0f, description - U(28));
            var r = new Rect(U(12), U(12), width, Mathf.Min(height, Screen.height - U(24))); Panel(r);
            GUILayout.BeginArea(new Rect(r.x + U(14), r.y + U(12), r.width - U(28), r.height - U(24)));
            scroll = GUILayout.BeginScrollView(scroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.Label("Choose a site", DevGui.Title); GUILayout.Space(U(6));
            GUILayout.Label("Buy land, then choose where to clear and build your airport.", body);
            GUILayout.Space(U(12)); GUILayout.Label("WORLD SEED", DevGui.Caption);
            GUI.SetNextControlName("WorldSeed"); seedText = GUILayout.TextField(seedText, 11, field, GUILayout.Height(U(28)));
            bool valid = int.TryParse(seedText, out int seed);
            if (LayoutButton("Generate region", enabled: valid)) { game.NewWorld(seed); GUI.FocusControl(null); }
            if (game.HasSave && LayoutButton("Continue saved airport")) { game.Continue(); GUI.FocusControl(null); }
            GUILayout.Space(U(10));
            for (int i = 0; i < game.State.world.sites.Count; i++)
            {
                var site = game.State.world.sites[i];
                if (LayoutButton(site.name, game.SurveyedSite == i)) game.Survey(i);
                if (game.SurveyedSite == i)
                {
                    GUILayout.Label(site.description, body);
                    GUILayout.Label($"{site.elevation:0} m elevation · {(ValleyWorld.RoadX - site.centre.x):N0} m to road", DevGui.Small);
                    GUILayout.Space(U(8));
                }
            }
            GUILayout.Space(U(10)); GUILayout.Label(Money(game.State.world.sites[game.SurveyedSite].landPrice), DevGui.Bold);
            if (LayoutButton("Buy this land", true))
            { GUI.FocusControl(null); if (game.HasSave) newWorldDialog = true; else game.Construct(); }
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        void Objective()
        {
            var step = game.State.Step;
            bool flights = step == CareerStep.FirstCharter || step == CareerStep.Operating || step == CareerStep.Expansion;
            var r = new Rect(U(12), U(12), U(286), U(flights ? 88 : 54)); Panel(r);
            GUI.Label(new Rect(r.x + U(12), r.y + U(7), r.width - U(24), U(14)), "NEXT TASK", DevGui.Caption);
            GUI.Label(new Rect(r.x + U(12), r.y + U(24), r.width - U(24), U(22)),
                new GUIContent(TaskTitle(step), TaskHint(step)), DevGui.Title);
            if (game.State.charterActive)
                GUI.Label(new Rect(r.x + U(12), r.y + U(52), r.width - U(24), U(27)), game.FlightStatus, body);
            else if (flights && Button(new Rect(r.x + U(10), r.y + U(52), r.width - U(20), U(26)),
                "Schedule local charter", enabled: game.State.funds >= CareerState.CharterCost)) game.AcceptCharter();
        }

        void Toolbar()
        {
            if (builder == null || game.State.layout == null) return;
            tools.Clear(); int groups = 0;
            for (int i = 0; i < Labels.Length; i++)
                if (game.State.layout.Unlocked((CareerTool)i))
                {
                    if (tools.Count > 0 && Groups[(int)tools[tools.Count - 1]] != Groups[i]) groups++;
                    tools.Add((CareerTool)i);
                }
            if (tools.Count == 0) return;
            float gap = U(4), pad = U(7), split = U(14), tile = U(48);
            float fixedWidth = pad * 2f + (tools.Count - 1 - groups) * gap + groups * split;
            tile = Mathf.Min(tile, Mathf.Floor((Screen.width - U(24) - fixedWidth) / tools.Count));
            float width = fixedWidth + tools.Count * tile;
            var bar = new Rect(Mathf.Round((Screen.width - width) * .5f), Screen.height - tile - pad * 2f - U(14), width, tile + pad * 2f);
            Panel(bar);
            float x = bar.x + pad, y = bar.y + pad; Rect active = default;
            for (int index = 0; index < tools.Count; index++)
            {
                int i = (int)tools[index];
                if (index > 0 && Groups[(int)tools[index - 1]] != Groups[i])
                { x -= gap; DevGui.Divider(x + split * .5f, y + U(8), tile - U(16)); x += split; }
                var b = new Rect(x, y, tile, tile);
                bool selected = builder.Tool == tools[index];
                if (selected) active = b;
                if (DevGui.IconButton(b, DevGui.Icon(Icons[i]), Labels[i], $"<b>{Labels[i]}</b>   {CareerBuildTool.Hint(tools[index])}",
                    selected, hint: tools[index] == CareerTool.Select ? "Esc" : null))
                    builder.Choose(selected ? CareerTool.Select : tools[index]);
                x += tile + gap;
            }
            if (builder.Tool != CareerTool.Select && active.width > 0f) ToolTray(active, bar);
        }

        void ToolTray(Rect active, Rect bar)
        {
            bool brush = builder.Tool == CareerTool.ClearTrees || builder.Tool == CareerTool.Level;
            bool finish = builder.Tool == CareerTool.Taxiway && builder.Draft.Count >= 2;
            string status = builder.Tool == CareerTool.Road ? game.GetComponent<CareerRoadTool>().Status : builder.Status;
            status ??= CareerBuildTool.Hint(builder.Tool);
            float width = Mathf.Min(U(520), Screen.width - U(24)), inner = width - U(24);
            float textHeight = Mathf.Max(U(28), body.CalcHeight(new GUIContent(status), inner));
            float height = U(40) + textHeight + (brush || finish ? U(34) : 0f);
            var r = new Rect(Mathf.Clamp(active.center.x - width * .5f, U(12), Screen.width - width - U(12)), bar.y - U(8) - height, width, height);
            Panel(r);
            GUI.Label(new Rect(r.x + U(12), r.y + U(9), inner, U(18)), Labels[(int)builder.Tool], DevGui.Title);
            GUI.Label(new Rect(r.x + U(12), r.y + U(31), inner, textHeight), status, body);
            float x = r.x + U(12), y = r.yMax - U(34);
            if (brush)
            {
                if (builder.Tool == CareerTool.ClearTrees)
                {
                    if (Button(new Rect(x, y, U(66), U(26)), "Brush", !builder.SingleTree)) builder.SingleTree = false;
                    if (Button(new Rect(x + U(70), y, U(90), U(26)), "Single tree", builder.SingleTree)) builder.SingleTree = true;
                }
                else
                {
                    if (Button(new Rect(x, y, U(28), U(26)), "−")) builder.TargetHeight -= .5f;
                    GUI.Label(new Rect(x + U(36), y, U(82), U(26)), $"{builder.TargetHeight:0.0} m height", DevGui.Small);
                    if (Button(new Rect(x + U(122), y, U(28), U(26)), "+")) builder.TargetHeight += .5f;
                }
                if (builder.Tool == CareerTool.Level || !builder.SingleTree)
                {
                    GUI.Label(new Rect(x + U(174), y, U(80), U(26)), $"Brush {builder.Radius:0} m", DevGui.Small);
                    builder.Radius = GUI.HorizontalSlider(new Rect(x + U(256), y + U(9), inner - U(256), U(18)), builder.Radius, 15f, 90f);
                }
            }
            else if (finish && Button(new Rect(x, y, inner, U(26)), "Finish taxiway", tip: "Build the drawn taxiway · Enter")) builder.FinishTaxiway();
        }

        void Notice()
        {
            string text = Time.unscaledTime < unlockUntil ? unlockedMessage : Time.unscaledTime < messageUntil ? observedMessage : null;
            if (string.IsNullOrEmpty(text)) return;
            float width = U(286), height = body.CalcHeight(new GUIContent(text), width - U(24)) + U(16);
            float y = U(game.State.IsOpen ? 108 : 74);
            var r = new Rect(U(12), y, width, height); Panel(r);
            GUI.Label(new Rect(r.x + U(12), r.y + U(8), width - U(24), height - U(16)), text, body);
        }

        void NewWorldDialog()
        {
            var full = new Rect(0, 0, Screen.width, Screen.height); Cover(full);
            DevGui.Fill(full, DevGui.GUI(new Color(0f, 0f, 0f, .6f)));
            float width = Mathf.Min(U(420), Screen.width - U(24));
            var r = new Rect((Screen.width - width) * .5f, (Screen.height - U(210)) * .5f, width, U(210)); Panel(r);
            GUI.Label(new Rect(r.x + U(18), r.y + U(18), r.width - U(36), U(24)), "Start a new airport?", DevGui.Title);
            GUI.Label(new Rect(r.x + U(18), r.y + U(57), r.width - U(36), U(77)),
                game.State.Site == null ? "Buying this land replaces the current saved airport. The previous save stays as a backup." :
                "Your airport will be saved before you return to site selection.", body);
            float buttonWidth = (r.width - U(44)) * .5f;
            if (Button(new Rect(r.x + U(18), r.yMax - U(48), buttonWidth, U(30)), "Keep playing")) newWorldDialog = false;
            if (Button(new Rect(r.x + U(26) + buttonWidth, r.yMax - U(48), buttonWidth, U(30)),
                game.State.Site == null ? "Buy & begin" : "Choose new site"))
            {
                if (game.State.Site == null) game.Construct(); else if (game.Save()) game.NewWorld(game.State.world.seed);
                newWorldDialog = false;
            }
        }

        void DrawBoundary()
        {
            if (boundary == null)
            {
                var go = new GameObject("Airport land boundary"); go.transform.SetParent(transform, false);
                boundary = go.AddComponent<LineRenderer>(); boundary.sharedMaterial = guideMaterial; boundary.useWorldSpace = true;
                boundary.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var area = game.State.world.sites[drawnSite].Parcel;
            var corners = new[] { new Vector2(area.xMin, area.yMin), new Vector2(area.xMin, area.yMax), new Vector2(area.xMax, area.yMax), new Vector2(area.xMax, area.yMin) };
            var points = new List<Vector3>();
            for (int side = 0; side < 4; side++) for (int i = 0; i < 48; i++)
            { var p = Vector2.Lerp(corners[side], corners[(side + 1) % 4], i / 48f); points.Add(new Vector3(p.x, game.State.world.Height(p) + .7f, p.y)); }
            points.Add(points[0]); boundary.positionCount = points.Count; boundary.SetPositions(points.ToArray()); boundary.widthMultiplier = 1.1f;
        }
        static string TaskTitle(CareerStep step) => step switch {
            CareerStep.ClearTrees => "Clear some space", CareerStep.GradeLand => "Prepare your ground", CareerStep.Runway => "Build a runway",
            CareerStep.Parking => "Connect aircraft parking", CareerStep.AccessRoad => "Connect to the valley", CareerStep.Reception => "Build passenger reception",
            CareerStep.FirstCharter => "Welcome your first flight", CareerStep.Expansion => "Grow your airfield", _ => "Open for business" };
        static string TaskHint(CareerStep step) => step switch {
            CareerStep.ClearTrees => "Choose where to begin. Select trees or brush away a patch of woodland.",
            CareerStep.GradeLand => "Level a patch with the terrain brush. Clear only the land your layout needs.",
            CareerStep.Runway => "Drag a grass runway of at least 700 m. Its position and direction are yours.",
            CareerStep.Parking => "Connect a taxiway near a runway end, then place parking beside the taxiway.",
            CareerStep.AccessRoad => "Clear a route and draw your public road from the airport to the valley road.",
            CareerStep.Reception => "Place a small reception beside the airport end of your road.",
            CareerStep.FirstCharter => "Your airport is ready. Host a local charter and earn its service fee.",
            CareerStep.Expansion => "Keep earning from charters. Clear and level new land, then drag a threshold to extend.",
            _ => "Serve local charters and keep shaping your airport with the tools below." };
        void Styles()
        {
            if (body != null && Mathf.Approximately(styleScale, DevGui.Scale)) return;
            styleScale = DevGui.Scale;
            body = new GUIStyle(DevGui.Label) { wordWrap = true };
            field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(U(13)),
                padding = new RectOffset((int)U(7), (int)U(7), (int)U(4), (int)U(4)) };
        }
        void Cover(Rect r) { if (Event.current.type == EventType.Repaint) hitAreas.Add(r); DevGui.Cover(r); }
        void Panel(Rect r) { Cover(r); DevGui.Panel(r); }
        bool LayoutButton(string text, bool selected = false, bool enabled = true)
        {
            var r = GUILayoutUtility.GetRect(U(100), U(28), GUILayout.ExpandWidth(true));
            // These rects are local to BeginArea; the enclosing site panel already covers their screen area.
            bool result = DevGui.Button(r, text, on: selected, enabled: enabled); GUILayout.Space(U(5)); return result;
        }
        bool Button(Rect r, string text, bool selected = false, bool enabled = true, string tip = null)
        { Cover(r); return DevGui.Button(r, text, tip, selected, enabled: enabled); }
        static string Money(int value) => "$" + value.ToString("N0");
        void OnDisable()
        {
            hitAreas.Clear();
            if (game != null && game.view != null) game.view.PointerOverHud = game.view.KeyboardCaptured = false;
        }
    }
}

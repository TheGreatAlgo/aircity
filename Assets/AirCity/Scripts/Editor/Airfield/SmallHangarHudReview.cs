using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirCity.Airfield;
using AirCity.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Airfield
{
    /// <summary>Inspect the shared hangar placement palette and lease inspector in the real Game view.</summary>
    [InitializeOnLoad]
    public static class SmallHangarHudReview
    {
        const string Key = "AirCity.SmallHangarHudReview.", Output = "Captures/SmallHangars/Hud/";
        static double next;
        static EditorWindow window;
        static Keyboard reviewKeyboard;
        static Vector3 focusPivot;
        static SmallHangar focusedHangar;
        static int focusCount;
        static string beforeTyping, leaseTenant;
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        [Serializable] sealed class ViewState { public string id; public int index; }
        [Serializable] sealed class Views { public List<ViewState> items = new List<ViewState>(); }
        static SmallHangarHudReview()
        { if (SessionState.GetBool(Key + "Running", false)) EditorApplication.update += Update; }

        public static void Run()
        {
            EditorSceneManager.OpenScene(SmallHangarReview.ScenePath);
            var camera = Camera.main; var airport = Object.FindAnyObjectByType<Airport>();
            var tool = camera.gameObject.AddComponent<DevAirfieldTool>(); tool.airport = airport;
            camera.gameObject.AddComponent<DevToolbar>();
            Directory.CreateDirectory(Output);
            foreach (var name in new[] { "palette", "vacant", "leased", "closed" }) File.Delete(Output + name + ".png");
            window = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
            window.position = new Rect(20, 20, 1440, 930); window.Show(); window.Focus();
            var saved = new Views();
            foreach (var view in GameViews()) saved.items.Add(new ViewState { id = view.GetEntityId().ToString(), index = (int)view.GetType().GetProperty("selectedSizeIndex", Instance).GetValue(view) });
            SessionState.SetString(Key + "Views", JsonUtility.ToJson(saved));
            SessionState.SetInt(Key + "EditorInput", (int)InputSystem.settings.editorInputBehaviorInPlayMode);
            SessionState.SetBool(Key + "Background", Application.runInBackground);
            SessionState.SetBool(Key + "AsyncShaders", ShaderUtil.allowAsyncCompilation);
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground = true;
            ShaderUtil.allowAsyncCompilation = false;
            SetReviewSize();
            SessionState.SetBool(Key + "Running", true); SessionState.SetInt(Key + "Stage", 0);
            SessionState.SetFloat(Key + "Started", (float)EditorApplication.timeSinceStartup);
            EditorApplication.update -= Update; EditorApplication.update += Update; EditorApplication.EnterPlaymode();
        }

        static void Update()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "Started", 0) > 100)
                    throw new TimeoutException("Hangar Game view review timed out.");
                int stage = SessionState.GetInt(Key + "Stage", 0);
                if (stage == 5 && !EditorApplication.isPlaying)
                { Finish(0); return; }
                if (!EditorApplication.isPlaying) return;
                var tool = Object.FindAnyObjectByType<DevAirfieldTool>(); if (tool == null) return;
                if (stage == 0)
                {
                    var orbit = tool.GetComponent<DevOrbitCamera>();
                    orbit.pivot = new Vector3(47, 1, -5); orbit.distance = 72; orbit.pitch = 35; orbit.yaw = 195;
                    tool.GetComponent<Camera>().nearClipPlane = .3f;
                    tool.GetComponent<Camera>().aspect = 1440f / 900f;
                    tool.airport.timeScale = 0; DevGui.Visible = true;
                    tool.GetComponent<DevToolbar>().ShowMore = false; tool.SetTool(DevTool.Hangar);
                    next = EditorApplication.timeSinceStartup + 2; SessionState.SetInt(Key + "Stage", 1); return;
                }
                if (stage == 1 && Mouse.current != null)
                {
                    var target = Target();
                    var p = tool.GetComponent<Camera>().WorldToViewportPoint(new Vector3(55, 0, 0));
                    if (target != null) InputState.Change(Mouse.current.position, new Vector2(p.x * target.width, p.y * target.height));
                }
                if (stage >= 20)
                {
                    if (EditorApplication.timeSinceStartup < next) return;
                    var toolbar = tool.GetComponent<DevToolbar>();
                    var field = typeof(DevToolbar).GetField("hangarTenant", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (stage == 23)
                    {
                        if (!focusedHangar.Occupied || focusedHangar.TenantName != leaseTenant || DevGui.KeyboardCaptured)
                        {
                            var problem = typeof(DevToolbar).GetField("hangarMessage", Instance).GetValue(toolbar);
                            throw new InvalidOperationException($"Start lease did not create the resident and release keyboard capture. " +
                                $"Expected tenant '{leaseTenant}', got '{focusedHangar.TenantName}', keyboard captured: {DevGui.KeyboardCaptured}. {problem}");
                        }
                        SessionState.SetInt(Key + "Stage", 3); next = EditorApplication.timeSinceStartup + 1; return;
                    }
                    if (stage == 24)
                    {
                        var page = typeof(DevToolbar).GetField("hangarPage", Instance).GetValue(toolbar);
                        if (page.ToString() != "Building")
                            throw new InvalidOperationException("The Building tab did not open.");
                        ClickFooter(toolbar);
                        SessionState.SetInt(Key + "Stage", 25); next = EditorApplication.timeSinceStartup + 1; return;
                    }
                    if (stage == 25)
                    {
                        if (tool.SelectedHangar.DoorsOpen)
                            throw new InvalidOperationException("The Building footer did not close the hangar doors.");
                        SessionState.SetInt(Key + "Stage", 4); next = EditorApplication.timeSinceStartup + 1; return;
                    }
                    if (stage == 20)
                    {
                        if (!DevGui.KeyboardCaptured) { FocusTenant(toolbar); next = EditorApplication.timeSinceStartup + .2f; return; }
                        focusedHangar = tool.SelectedHangar; focusCount = tool.airport.Hangars.Count();
                        focusPivot = tool.GetComponent<DevOrbitCamera>().pivot; beforeTyping = (string)field.GetValue(toolbar);
                        reviewKeyboard = InputSystem.AddDevice<Keyboard>(); reviewKeyboard.MakeCurrent();
                        InputSystem.QueueStateEvent(reviewKeyboard, new KeyboardState(UnityEngine.InputSystem.Key.W));
                        SendKey(new Event { type = EventType.KeyDown, keyCode = KeyCode.W, character = 'w' });
                        SendKey(new Event { type = EventType.KeyDown, keyCode = KeyCode.S, character = 's' });
                        SessionState.SetInt(Key + "Stage", 21); next = EditorApplication.timeSinceStartup + .4f; return;
                    }
                    if (stage == 21)
                    {
                        if ((string)field.GetValue(toolbar) == beforeTyping) throw new InvalidOperationException("Typing did not reach the tenant field.");
                        InputSystem.QueueStateEvent(reviewKeyboard, new KeyboardState(UnityEngine.InputSystem.Key.Backspace));
                        SendKey(new Event { type = EventType.KeyDown, keyCode = KeyCode.Backspace, character = '\b' });
                        SessionState.SetInt(Key + "Stage", 22); next = EditorApplication.timeSinceStartup + .4f; return;
                    }
                    InputSystem.QueueStateEvent(reviewKeyboard, new KeyboardState());
                    if (tool.SelectedHangar != focusedHangar || tool.airport.Hangars.Count() != focusCount ||
                        (tool.GetComponent<DevOrbitCamera>().pivot - focusPivot).sqrMagnitude > .00001f || !DevGui.KeyboardCaptured)
                        throw new InvalidOperationException("Typing/backspace escaped the tenant field into camera movement or demolition.");
                    InputSystem.RemoveDevice(reviewKeyboard); reviewKeyboard = null;
                    Debug.Log("HANGAR HUD keyboard passed: tenant input accepted typing/backspace without camera movement or demolition.");
                    leaseTenant = ((string)field.GetValue(toolbar)).Trim();
                    ClickFooter(toolbar);
                    SessionState.SetInt(Key + "Stage", 23); next = EditorApplication.timeSinceStartup + 1; return;
                }
                if (stage > 4 || EditorApplication.timeSinceStartup < next) return;
                string name = stage == 1 ? "palette" : stage == 2 ? "vacant" : stage == 3 ? "leased" : "closed";
                if (!File.Exists(Output + name + ".png"))
                {
                    if (!Capture(Output + name + ".png")) { next = EditorApplication.timeSinceStartup + .2f; return; }
                    next = EditorApplication.timeSinceStartup + .5f; return;
                }
                if (stage == 1 && !tool.PlaceHangar(new Vector3(55, 0, 0))) throw new InvalidOperationException(tool.HangarProblem);
                if (stage == 2)
                {
                    var hangar = tool.SelectedHangar;
                    if (hangar == null || hangar.Occupied) throw new InvalidOperationException("The placed hangar was not selected and vacant.");
                    focusedHangar = hangar;
                    FocusTenant(tool.GetComponent<DevToolbar>());
                    SessionState.SetInt(Key + "Stage", 20); next = EditorApplication.timeSinceStartup + .5f; return;
                }
                if (stage == 3)
                {
                    var body = HangarBody(tool.GetComponent<DevToolbar>());
                    ClickHud(new Vector2(body.x - DevGui.U(60), body.y + DevGui.U(97)));
                    SessionState.SetInt(Key + "Stage", 24); next = EditorApplication.timeSinceStartup + 1; return;
                }
                SessionState.SetInt(Key + "Stage", stage + 1); next = EditorApplication.timeSinceStartup + 1;
                if (stage == 4)
                { Debug.Log("HANGAR HUD passed: shared placement, vacant inspector, resident lease and closed doors."); EditorApplication.ExitPlaymode(); }
            }
            catch (Exception exception)
            {
                if (reviewKeyboard != null && reviewKeyboard.added) InputSystem.RemoveDevice(reviewKeyboard);
                Debug.LogException(exception); Finish(1);
            }
        }

        static void SendKey(Event key)
        {
            if (window == null) window = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
            window.Focus();
            var queue = typeof(EditorGUIUtility).GetMethod("QueueGameViewInputEvent", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(Event) }, null);
            if (queue != null) queue.Invoke(null, new object[] { key }); else window.SendEvent(key);
            window.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
        }

        static void FocusTenant(DevToolbar toolbar)
        {
            var rows = (List<(float h, Action<Rect> draw)>)typeof(DevToolbar).GetField("main", Instance).GetValue(toolbar);
            if (rows == null || rows.Count <= 2) return;
            // In the single-bay fixture, the resident heading and aircraft picker precede the tenant.
            var body = HangarBody(toolbar);
            float y = body.y + DevGui.U(14 + 12) + rows[0].h + rows[1].h + rows[2].h * .5f;
            ClickHud(new Vector2(body.x + DevGui.U(14 + 78 + 60), y));
        }

        static Rect HangarBody(DevToolbar toolbar) => (Rect)typeof(DevToolbar).GetField("hangarBody", Instance).GetValue(toolbar);

        static void ClickFooter(DevToolbar toolbar)
        {
            var body = HangarBody(toolbar);
            ClickHud(new Vector2(body.xMax - DevGui.U(86), body.yMax + DevGui.U(25)));
        }

        static void ClickHud(Vector2 point)
        {
            var target = Target(); if (target == null) return;
            if (Mouse.current != null) InputState.Change(Mouse.current.position, new Vector2(point.x, target.height - point.y));
            SendKey(new Event { type = EventType.MouseDown, button = 0, mousePosition = point });
            SendKey(new Event { type = EventType.MouseUp, button = 0, mousePosition = point });
        }

        static EditorWindow[] GameViews() => Resources.FindObjectsOfTypeAll<EditorWindow>().Where(v => v.GetType().FullName == "UnityEditor.GameView").ToArray();

        static object SizeGroup()
        {
            var type = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes", true);
            var instance = type.GetProperty("instance", Static).GetValue(null);
            var kind = Enum.ToObject(typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeGroupType", true), 0);
            return type.GetMethod("GetGroup", Instance).Invoke(instance, new[] { kind });
        }

        static void SetReviewSize()
        {
            var group = SizeGroup(); var assembly = typeof(Editor).Assembly;
            var kind = Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeType", true), "FixedResolution");
            var size = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize", true), Instance, null,
                new[] { kind, (object)1440, 900, "Small hangar review temporary" }, null);
            int index = (int)group.GetType().GetMethod("GetTotalCount", Instance).Invoke(group, null);
            group.GetType().GetMethod("AddCustomSize", Instance).Invoke(group, new[] { size });
            SessionState.SetInt(Key + "CustomSize", index);
            foreach (var view in GameViews()) { view.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(view, index); view.Repaint(); }
        }

        static RenderTexture Target()
        {
            if (window == null) window = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
            return window.GetType().GetField("m_RenderTexture", Instance)?.GetValue(window) as RenderTexture;
        }

        static bool Capture(string path)
        {
            var target = Target();
            if (ShaderUtil.anythingCompiling || target == null || !target.IsCreated() || target.width != 1440 || target.height != 900)
            { window.Repaint(); return false; }
            var previous = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                var pixels = image.GetPixels32();
                bool Toolbar(int row)
                {
                    int paper = 0;
                    for (int x = target.width / 2 - 120; x < target.width / 2 + 120; x += 37)
                    {
                        var p = pixels[row * target.width + x];
                        if (p.r > 200 && p.g > 200 && p.b > 190) paper++;
                    }
                    return paper >= 4;
                }
                int toolbarInset = Mathf.RoundToInt(DevGui.U(17));
                if (Toolbar(target.height - 1 - toolbarInset) && !Toolbar(toolbarInset))
                {
                    var flipped = new Color32[pixels.Length];
                    for (int row = 0; row < target.height; row++) Array.Copy(pixels, row * target.width, flipped, (target.height - 1 - row) * target.width, target.width);
                    image.SetPixels32(flipped); image.Apply();
                }
                File.WriteAllBytes(path, image.EncodeToPNG());
                Debug.Log("HANGAR HUD captured " + path + " " + target.width + "x" + target.height);
                return true;
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }

        static void Finish(int result)
        {
            SessionState.SetBool(Key + "Running", false); EditorApplication.update -= Update;
            InputSystem.settings.editorInputBehaviorInPlayMode = (InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt(Key + "EditorInput", 0);
            Application.runInBackground = SessionState.GetBool(Key + "Background", false);
            ShaderUtil.allowAsyncCompilation = SessionState.GetBool(Key + "AsyncShaders", true);
            try
            {
                var saved = JsonUtility.FromJson<Views>(SessionState.GetString(Key + "Views", "{\"items\":[]}"));
                foreach (var state in saved.items)
                {
                    var view = GameViews().FirstOrDefault(v => v.GetEntityId().ToString() == state.id);
                    if (view != null) view.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(view, state.index);
                }
                int index = SessionState.GetInt(Key + "CustomSize", -1);
                if (index >= 0) { var group = SizeGroup(); group.GetType().GetMethod("RemoveCustomSize", Instance).Invoke(group, new object[] { index }); }
                SessionState.SetInt(Key + "CustomSize", -1);
            }
            catch (Exception cleanup) { Debug.LogException(cleanup); result = 1; }
            EditorApplication.Exit(result);
        }
    }
}

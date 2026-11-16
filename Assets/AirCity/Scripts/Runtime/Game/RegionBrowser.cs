using System;
using System.Globalization;
using System.Threading;
using AirCity.Dev;
using AirCity.World;
using UnityEngine;

namespace AirCity.Game
{
    /// <summary>Globe selection shares the normal region preview and never replaces it until loading succeeds.</summary>
    public sealed class RegionBrowser : IDisposable
    {
        readonly CareerGame game;
        readonly RegionGlobe globe = new RegionGlobe();
        CancellationTokenSource request;
        GUIStyle title, text, small, caption, button, selectedButton, field;
        float builtScale, viewLatitude = 28f, viewLongitude = -115f, zoom = 1f;
        double latitude, longitude;
        string latitudeText, longitudeText, error, status;
        int curated;
        float progress;
        bool dragging, moved, cached;
        Vector2 dragOrigin, lastPointer;
        Rect globeRect, sideRect;
        public bool IsOpen { get; private set; }
        public bool Busy => request != null;
        public RegionBrowser(CareerGame game) { this.game = game; }

        public void Open()
        {
            IsOpen = true; error = status = null; zoom = 1f;
            var world = game.State.world;
            if (world.hasCoordinates)
            {
                curated = -1; latitude = world.latitude; longitude = world.longitude;
                for (int i = 0; i < RegionLocations.Curated.Length; i++)
                    if (Math.Abs(latitude - RegionLocations.Curated[i].latitude) < .00001 &&
                        Math.Abs(longitude - RegionLocations.Curated[i].longitude) < .00001) curated = i;
                SetCoordinateText();
            }
            else SelectCurated(Math.Min(3, Math.Max(0, (int)world.preset)));
            viewLatitude = (float)latitude; viewLongitude = (float)longitude;
            CheckCache();
        }
        public void Close() { Cancel(); IsOpen = false; dragging = false; }
        public void Dispose() { Close(); globe.Dispose(); }
        public void Cancel() { request?.Cancel(); request?.Dispose(); request = null; status = null; }
        static float U(float value) => DevGui.U(value);
        static Color Ink => new Color(.88f, .94f, .94f);
        static Color Muted => new Color(.56f, .68f, .72f);
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        void Styles()
        {
            if (text != null && Mathf.Approximately(builtScale, DevGui.Scale)) return;
            builtScale = DevGui.Scale;
            GUIStyle Style(GUIStyle source, float size, Color colour) => new GUIStyle(source) {
                fontSize = Mathf.RoundToInt(U(size)), normal = { textColor = DevGui.GUI(colour) },
                hover = { textColor = DevGui.GUI(colour) }, active = { textColor = DevGui.GUI(colour) } };
            title = Style(DevGui.Bold, 24f, Ink); text = Style(DevGui.Label, 13f, Ink); text.wordWrap = true;
            small = Style(DevGui.Small, 11f, Muted); small.wordWrap = true;
            caption = Style(DevGui.Caption, 10f, Muted);
            button = Style(DevGui.Bold, 12f, Ink); button.alignment = TextAnchor.MiddleCenter;
            selectedButton = Style(button, 12f, new Color(.06f, .10f, .13f));
            field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(U(13)),
                padding = new RectOffset((int)U(7), (int)U(7), (int)U(5), (int)U(5)) };
            field.normal.textColor = field.focused.textColor = DevGui.GUI(Ink);
        }

        public void Draw()
        {
            if (!IsOpen) return;
            Styles();
            var full = new Rect(0, 0, Screen.width, Screen.height); DevGui.Cover(full);
            DevGui.Fill(full, DevGui.GUI(new Color(.015f, .035f, .05f, .93f)));
            bool narrow = Screen.width < U(850);
            float width = Mathf.Min(Screen.width - U(24), U(1120));
            float height = Mathf.Min(Screen.height - U(68), U(narrow ? 900 : 760));
            var panel = new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f + U(12), width, height);
            DevGui.Fill(panel, DevGui.GUI(new Color(.045f, .075f, .10f)), U(12));
            DevGui.Outline(panel, DevGui.GUI(new Color(.19f, .29f, .34f)), U(12));
            GUI.Label(new Rect(panel.x + U(24), panel.y + U(16), panel.width - U(86), U(34)), "Choose a place on Earth", title);
            GUI.Label(new Rect(panel.x + U(24), panel.y + U(52), panel.width - U(48), U(32)),
                "Four places ready offline. Choose another location to download its terrain.", small);
            if (Button(new Rect(panel.xMax - U(48), panel.y + U(20), U(28), U(28)), "×")) { Close(); return; }
            Rect sphere, side;
            if (narrow)
            {
                float sideHeight = U(250), globeSize = Mathf.Max(U(120), Mathf.Min(panel.width - U(48), panel.height - sideHeight - U(112)));
                sphere = new Rect(panel.center.x - globeSize * .5f, panel.y + U(86), globeSize, globeSize);
                side = new Rect(panel.x + U(24), sphere.yMax + U(8), panel.width - U(48), panel.yMax - sphere.yMax - U(26));
            }
            else
            {
                side = new Rect(panel.xMax - U(336), panel.y + U(92), U(312), panel.height - U(116));
                float globeSize = Mathf.Min(side.x - panel.x - U(52), panel.height - U(174));
                sphere = new Rect(panel.x + U(24) + (side.x - panel.x - U(48) - globeSize) * .5f, panel.y + U(94), globeSize, globeSize);
            }
            globeRect = sphere; sideRect = side;
            globe.Draw(sphere, viewLatitude, viewLongitude, zoom);
            DrawMarkers(sphere);
            HandleGlobe(sphere);
            if (!narrow) GUI.Label(new Rect(panel.x + U(24), sphere.yMax + U(12), side.x - panel.x - U(48), U(38)),
                "Drag to rotate · Scroll to zoom · Click to choose\nGlobe imagery: NASA Blue Marble", small);
            DrawSide(side, narrow);
        }

        void DrawSide(Rect r, bool narrow)
        {
            float y = r.y;
            GUI.Label(new Rect(r.x, y, r.width, U(18)), "CURATED LOCATIONS · OFFLINE", caption); y += U(24);
            bool enabled = GUI.enabled; GUI.enabled &= !Busy;
            float cardHeight = U(narrow ? 34 : 52), cardWidth = narrow ? (r.width - U(8)) * .5f : r.width;
            for (int i = 0; i < RegionLocations.Curated.Length; i++)
            {
                var location = RegionLocations.Curated[i];
                int column = narrow ? i % 2 : 0, rowIndex = narrow ? i / 2 : i;
                var row = new Rect(r.x + column * (cardWidth + U(8)), y + rowIndex * (cardHeight + U(6)), cardWidth, cardHeight);
                if (Button(row, narrow ? location.name + "  ·  " + location.preset : "", curated == i))
                { SelectCurated(i); viewLatitude = (float)latitude; viewLongitude = (float)longitude; zoom = 1f; }
                if (!narrow)
                {
                    GUI.Label(new Rect(row.x + U(12), row.y + U(5), row.width - U(24), U(22)), location.name, curated == i ? selectedButton : text);
                    GUI.Label(new Rect(row.x + U(12), row.y + U(27), row.width - U(24), U(19)), location.description, curated == i ? selectedButton : small);
                }
            }
            y += (narrow ? 2 : 4) * (cardHeight + U(6));
            y += U(narrow ? 2 : 12);
            GUI.Label(new Rect(r.x, y, r.width, U(18)), "SELECTED AREA", caption); y += U(22);
            float coordinateWidth = (r.width - U(12)) * .5f;
            GUI.Label(new Rect(r.x, y, coordinateWidth, U(16)), "Latitude", small);
            GUI.Label(new Rect(r.x + coordinateWidth + U(12), y, coordinateWidth, U(16)), "Longitude", small); y += U(18);
            string oldLat = latitudeText, oldLon = longitudeText;
            GUI.SetNextControlName("GlobeLatitude"); latitudeText = GUI.TextField(new Rect(r.x, y, coordinateWidth, U(30)), latitudeText, 14, field);
            GUI.SetNextControlName("GlobeLongitude"); longitudeText = GUI.TextField(new Rect(r.x + coordinateWidth + U(12), y, coordinateWidth, U(30)), longitudeText, 14, field);
            bool validLat = double.TryParse(latitudeText, NumberStyles.Float, Invariant, out double lat);
            bool validLon = double.TryParse(longitudeText, NumberStyles.Float, Invariant, out double lon);
            bool valid = validLat && validLon;
            if (oldLat != latitudeText || oldLon != longitudeText)
            {
                curated = -1; error = null;
                if (valid)
                {
                    latitude = lat; longitude = lon;
                    if (latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180)
                    { viewLatitude = (float)latitude; viewLongitude = (float)longitude; }
                }
                if (valid) CheckCache(); else cached = false;
            }
            // Latitude limits follow the providers' common coverage; unsupported choices stay visible and explicit.
            const double cropRadius = 8000d / (2d * 111320d);
            bool covered = valid && latitude >= -60 + cropRadius && latitude <= 83 - cropRadius && longitude >= -180 && longitude <= 180;
            y += U(36);
            string availability = !valid ? "Enter decimal coordinates or click the globe." : Busy ? status : curated >= 0 ? "Ready offline" : cached ? "Cached on this device" : "Downloads on first use";
            float extent = curated >= 0 ? RegionElevation.SourceExtent(RegionLocations.Curated[curated].preset) : 8000f;
            string narrowMessage = error ?? (!covered && valid ? "Terrain downloads cover 60°S to 83°N." : availability);
            GUI.enabled = enabled;
            float actionY = r.yMax - U(38);
            GUI.Label(new Rect(r.x, y, r.width, narrow ? (Busy ? U(14) : Mathf.Max(U(18), actionY - y - U(6))) : U(38)),
                new GUIContent(narrow ? narrowMessage : availability + $"\n{extent / 1000f:0.#} km source area · 6.4 km game region", error), small);
            if (!narrow)
            {
                string detail = !valid ? "Enter decimal coordinates or click the globe." : !covered ? "Terrain downloads cover 60°S to 83°N." :
                    error ?? (Busy ? status : "Elevation and landcover: Mapzen Terrain Tiles / ESA WorldCover.");
                GUI.Label(new Rect(r.x, y + U(44), r.width, Mathf.Max(U(36), actionY - y - U(52))), detail, small);
            }
            if (Busy)
            {
                var bar = new Rect(r.x, actionY - U(14), r.width, U(4));
                DevGui.Fill(bar, DevGui.GUI(new Color(.11f, .20f, .25f)), U(2));
                DevGui.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(progress), bar.height), DevGui.GUI(DevGui.Accent), U(2));
                if (Button(new Rect(r.x, actionY, r.width, U(34)), "Cancel download")) Cancel();
            }
            else
            {
                GUI.enabled = enabled && covered;
                if (Button(new Rect(r.x, actionY, r.width, U(34)), curated >= 0 ? "Preview offline region  →" :
                    cached ? "Preview cached region  →" : "Download & preview →", true)) Preview();
                GUI.enabled = enabled;
            }
        }

        void SelectCurated(int index)
        { curated = index; var p = RegionLocations.Curated[index]; latitude = p.latitude; longitude = p.longitude; SetCoordinateText(); error = null; cached = false; }
        void CheckCache()
        { cached = curated < 0 && latitude >= -60 && latitude <= 83 && longitude >= -180 && longitude <= 180 && LocationTerrainLoader.HasCached(latitude, longitude); }
        void SetCoordinateText()
        { latitudeText = latitude.ToString("0.0000", Invariant); longitudeText = longitude.ToString("0.0000", Invariant); }
        void DrawMarkers(Rect rect)
        {
            for (int i = 0; i < RegionLocations.Curated.Length; i++)
            {
                var p = RegionLocations.Curated[i];
                var point = GlobeProjection.Project(p.latitude, p.longitude, viewLatitude, viewLongitude);
                if (point.z <= 0f) continue;
                var pixel = Pixel(rect, point);
                if (!rect.Contains(pixel)) continue;
                float size = U(curated == i ? 13 : 9);
                DevGui.Fill(new Rect(pixel.x - size * .5f, pixel.y - size * .5f, size, size), DevGui.GUI(curated == i ? DevGui.Accent : new Color(.86f, .97f, .97f)), size);
            }
            var chosen = GlobeProjection.Project(latitude, longitude, viewLatitude, viewLongitude);
            if (chosen.z <= 0f) return;
            var selected = Pixel(rect, chosen);
            if (!rect.Contains(selected)) return;
            float half = U(12);
            DevGui.Outline(new Rect(selected.x - half, selected.y - half, half * 2f, half * 2f), DevGui.GUI(DevGui.Accent), U(12), U(2));
            DevGui.Fill(new Rect(selected.x - U(18), selected.y, U(8), U(1)), DevGui.GUI(DevGui.Accent));
            DevGui.Fill(new Rect(selected.x + U(10), selected.y, U(8), U(1)), DevGui.GUI(DevGui.Accent));
        }
        Vector2 Pixel(Rect r, Vector3 p) => new Vector2(r.center.x + p.x * r.width * .5f * zoom, r.center.y - p.y * r.height * .5f * zoom);
        void HandleGlobe(Rect rect)
        {
            if (Busy) return;
            var e = Event.current; var mouse = e.mousePosition;
            if (e.type == EventType.ScrollWheel && rect.Contains(mouse))
            { zoom = Mathf.Clamp(zoom * Mathf.Pow(1.2f, -e.delta.y), 1f, 64f); e.Use(); }
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(mouse))
            {
                for (int i = 0; i < RegionLocations.Curated.Length; i++)
                {
                    var p = RegionLocations.Curated[i]; var point = GlobeProjection.Project(p.latitude, p.longitude, viewLatitude, viewLongitude);
                    if (point.z > 0f && Vector2.Distance(Pixel(rect, point), mouse) < U(13)) { SelectCurated(i); e.Use(); return; }
                }
                dragging = true; moved = false; dragOrigin = lastPointer = mouse; GUI.FocusControl(null); e.Use();
            }
            if (dragging && e.type == EventType.MouseDrag)
            {
                Vector2 delta = mouse - lastPointer; lastPointer = mouse;
                moved |= Vector2.Distance(mouse, dragOrigin) > U(4);
                if (moved)
                {
                    viewLongitude = Mathf.Repeat(viewLongitude - delta.x / rect.width * 180f / zoom + 180f, 360f) - 180f;
                    viewLatitude = Mathf.Clamp(viewLatitude + delta.y / rect.height * 150f / zoom, -85f, 85f);
                }
                e.Use();
            }
            if (dragging && e.type == EventType.MouseUp && e.button == 0)
            {
                dragging = false;
                var unit = new Vector2((mouse.x - rect.center.x) * 2f / rect.width / zoom, (rect.center.y - mouse.y) * 2f / rect.height / zoom);
                if (!moved && GlobeProjection.TryPick(unit, viewLatitude, viewLongitude, out var coordinate))
                { curated = -1; latitude = coordinate.x; longitude = coordinate.y; SetCoordinateText(); CheckCache(); error = null; }
                e.Use();
            }
        }
        bool Button(Rect r, string label, bool selected = false)
        {
            bool clicked = GUI.Button(r, label, GUIStyle.none);
            Color fill = selected ? DevGui.Accent : r.Contains(Event.current.mousePosition) && GUI.enabled ? new Color(.15f, .25f, .30f) : new Color(.09f, .16f, .20f);
            DevGui.Fill(r, DevGui.GUI(fill), U(6));
            DevGui.Outline(r, DevGui.GUI(selected ? DevGui.Accent : new Color(.22f, .33f, .38f)), U(6));
            GUI.Label(r, label, selected ? selectedButton : button); return clicked;
        }
        async void Preview()
        {
            GUI.FocusControl(null); error = null;
            if (curated >= 0)
            {
                var location = RegionLocations.Curated[curated];
                try { game.NewWorld(location.seed, location.preset); Close(); }
                catch (Exception exception) { error = exception.Message; }
                return;
            }
            var active = new CancellationTokenSource(); request = active; progress = 0f; status = "Loading terrain…";
            try
            {
                var report = new Progress<RegionTerrainProgress>(p => { if (request == active) { progress = p.fraction; status = p.message; } });
                var data = await LocationTerrainLoader.LoadAsync(latitude, longitude, cancellationToken: active.Token, progress: report);
                if (active.IsCancellationRequested || request != active || !IsOpen) return;
                status = "Building region…"; game.NewLocation(data); request = null; active.Dispose(); IsOpen = false;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (request == active) error = exception.Message; }
            finally { if (request == active) { request = null; active.Dispose(); } }
        }
    }
}

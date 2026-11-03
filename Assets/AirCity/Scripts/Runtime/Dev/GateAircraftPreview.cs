using System;
using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Dev
{
    /// <summary>A visual-only copy. It is never enabled as an aircraft, picked, routed or used for occupancy.</summary>
    public sealed class GateAircraftPreview : IDisposable
    {
        GameObject root, source;
        BridgeRig rig;
        readonly Dictionary<Renderer, bool> hidden = new Dictionary<Renderer, bool>();
        readonly HashSet<Renderer> covered = new HashSet<Renderer>();
        public GameObject PreviewObject => root;
        public static readonly Color Tint = new Color(.3f, .72f, 1f);

        public bool Show(Airport airport, Stand stand, AircraftType type, GameObject template, float progress)
        {
            if (airport == null || stand == null || type == null || template == null) { Dispose(); return false; }
            var layout = airport.Taxiways.Layout(stand);
            if (layout == null) { Dispose(); return false; }
            if (root == null || source != template || root.transform.parent != airport.transform)
            {
                Dispose();
                root = new GameObject("Gate aircraft preview") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
                root.SetActive(false);
                root.transform.SetParent(airport.transform, false);
                // Instantiate under an inactive parent so no OnEnable callback registers a second aircraft.
                var model = Object.Instantiate(template, root.transform);
                model.SetActive(true);
                model.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                var original = template.GetComponentsInChildren<Renderer>(true);
                var copies = model.GetComponentsInChildren<Renderer>(true);
                var block = new MaterialPropertyBlock();
                for (int i = 0; i < Mathf.Min(original.Length, copies.Length); i++)
                {
                    copies[i].sharedMaterials = original[i].sharedMaterials;
                    original[i].GetPropertyBlock(block);
                    block.SetColor("_BaseColor", Tint);
                    copies[i].SetPropertyBlock(block);
                    copies[i].forceRenderingOff = false;
                }
                model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                source = template;
                root.SetActive(true);
            }
            root.transform.SetLocalPositionAndRotation(Geo.X0Z(layout.ParkPosition(type)), Quaternion.LookRotation(Geo.X0Z(layout.nose)));
            HideParkedAircraft(airport, layout);
            rig = airport.GetComponent<BridgeRig>();
            if (rig != null) rig.Preview(stand, type, progress);
            return true;
        }

        public void DrawLabel(Camera camera)
        {
            if (root == null || camera == null) return;
            var p = camera.WorldToScreenPoint(root.transform.position + root.transform.up * 7f);
            if (p.z <= 0f) return;
            GateDesignerGui.Begin();
            var rect = new Rect(p.x - GateDesignerGui.U(79), Screen.height - p.y, GateDesignerGui.U(158), GateDesignerGui.U(28));
            GateDesignerGui.Fill(rect, new Color(.86f, .95f, 1f), 7);
            GUI.Label(rect, "PREVIEW AIRCRAFT", GateDesignerGui.Centre);
        }

        void HideParkedAircraft(Airport airport, StandLayout layout)
        {
            covered.Clear();
            foreach (var aircraft in AirCity.Aircraft.Aircraft.All)
            {
                if (aircraft == null || aircraft.Moving) continue;
                var p = airport.transform.InverseTransformPoint(aircraft.transform.position);
                if (p.y > 1f || !layout.Contains(Geo.XZ(p))) continue;
                foreach (var renderer in aircraft.GetComponentsInChildren<Renderer>(true))
                {
                    covered.Add(renderer);
                    if (!hidden.ContainsKey(renderer)) hidden.Add(renderer, renderer.forceRenderingOff);
                    renderer.forceRenderingOff = true;
                }
            }
            var restore = new List<Renderer>();
            foreach (var pair in hidden)
                if (pair.Key == null || !covered.Contains(pair.Key))
                { if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value; restore.Add(pair.Key); }
            foreach (var renderer in restore) hidden.Remove(renderer);
        }

        public void Dispose()
        {
            foreach (var pair in hidden) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            hidden.Clear(); covered.Clear();
            if (rig != null) rig.Preview(null, null);
            if (root != null)
            {
                root.SetActive(false);
                if (Application.isPlaying) Object.Destroy(root); else Object.DestroyImmediate(root);
            }
            root = null; source = null; rig = null;
        }
    }
}

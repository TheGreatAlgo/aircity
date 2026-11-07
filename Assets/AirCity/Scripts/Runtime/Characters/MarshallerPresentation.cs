using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Traffic;
using UnityEngine;

namespace AirCity.Characters
{
    [DisallowMultipleComponent, RequireComponent(typeof(Airport))]
    public sealed class MarshallerPresentation : MonoBehaviour
    {
        public GameObject crewPrefab, wandPrefab;
        sealed class Visual { public RampCrewRig rig; public Transform left,right; }
        readonly Dictionary<StandMarshaller,Visual> visuals = new Dictionary<StandMarshaller,Visual>();
        readonly List<StandMarshaller> removed = new List<StandMarshaller>();
        Airport Airport => GetComponent<Airport>();
        void LateUpdate() { if (Application.isPlaying) Present(); }
        public void Present()
        {
            if (crewPrefab == null || wandPrefab == null) return;
            var active = Airport.Marshalling.Crew;
            removed.Clear();
            foreach (var pair in visuals)
            {
                if (!Contains(active,pair.Key)) { removed.Add(pair.Key); continue; }
            }
            foreach (var key in removed) { DestroyVisual(visuals[key]); visuals.Remove(key); }
            foreach (var worker in active)
            {
                if (!visuals.TryGetValue(worker,out var visual))
                {
                    var rig = Instantiate(crewPrefab,transform).GetComponent<RampCrewRig>(); rig.Initialize(2);
                    rig.name = "Marshaller · " + worker.Flight.Name; rig.gameObject.hideFlags = HideFlags.DontSave;
                    visual = new Visual { rig = rig,left = Instantiate(wandPrefab,rig.transform).transform,right = Instantiate(wandPrefab,rig.transform).transform };
                    visuals.Add(worker,visual);
                }
                var camera = Camera.main;
                bool visible = !Application.isPlaying || camera == null ||
                    (camera.transform.position-transform.TransformPoint(worker.Position)).sqrMagnitude < 250f*250f;
                visual.rig.gameObject.SetActive(visible); if (!visible) continue;
                visual.rig.Apply(worker.Pose);
                bool guiding = worker.Phase != MarshallerPhase.WalkingIn && worker.Phase != MarshallerPhase.WalkingOut;
                Vector3 Direction(bool left) => guiding ? Airport.transform.TransformDirection(worker.WandDirection(left)) : Vector3.down;
                visual.left.SetPositionAndRotation(visual.rig.LeftPalm,Quaternion.FromToRotation(Vector3.up,Direction(true)));
                visual.right.SetPositionAndRotation(visual.rig.RightPalm,Quaternion.FromToRotation(Vector3.up,Direction(false)));
            }
        }
        static bool Contains(IReadOnlyList<StandMarshaller> list,StandMarshaller worker)
        { for (int i = 0; i < list.Count; i++) if (list[i] == worker) return true; return false; }
        static void DestroyVisual(Visual visual)
        { if (visual.rig == null) return; if (Application.isPlaying) Destroy(visual.rig.gameObject); else DestroyImmediate(visual.rig.gameObject); }
        void OnDisable() { foreach (var visual in visuals.Values) DestroyVisual(visual); visuals.Clear(); }
    }
}

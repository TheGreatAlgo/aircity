using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Aircraft
{
    /// <summary>
    /// Deflects the movable surfaces. Each surface's pivot sits on its hinge line and its local X axis
    /// (Y for rudders) runs along the hinge, so a deflection is a rotation about that axis on top of the
    /// part's rest rotation. Parts are found by name prefix (Elevator, Elevator_L, Flap_L_In, ...), so the
    /// same component drives every aircraft type. Angles in degrees.
    /// </summary>
    [ExecuteAlways]
    public class AircraftControlSurfaces : MonoBehaviour
    {
        [Range(-1f, 1f)] public float elevator;   // + = trailing edge up (pull)
        [Range(-1f, 1f)] public float aileron;    // + = roll right (right aileron up)
        [Range(-1f, 1f)] public float rudder;     // + = yaw right (trailing edge to starboard)
        [Range(0f, 1f)] public float flaps;       // 0 = up, 1 = full

        public float elevatorUp = 28f, elevatorDown = 23f, aileronUp = 20f, aileronDown = 15f, rudderMax = 17f, flapsMax = 30f;

        [Serializable]
        public struct Part
        {
            public Transform transform;
            public Quaternion rest;
        }

        public Part[] elevators = Array.Empty<Part>(), aileronsLeft = Array.Empty<Part>(), aileronsRight = Array.Empty<Part>(),
            rudders = Array.Empty<Part>(), flapsLeft = Array.Empty<Part>(), flapsRight = Array.Empty<Part>();

        void Reset() => FindParts();

        /// <summary>Find the surfaces by name and record their rest rotations (call with everything neutral).</summary>
        public void FindParts()
        {
            elevators = Find("Elevator");
            aileronsLeft = Find("Aileron_L");
            aileronsRight = Find("Aileron_R");
            rudders = Find("Rudder");
            flapsLeft = Find("Flap_L");
            flapsRight = Find("Flap_R");
        }

        Part[] Find(string prefix)
        {
            var list = new List<Part>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(prefix) && !t.name.Contains("_LOD"))
                    list.Add(new Part { transform = t, rest = t.localRotation });
            return list.ToArray();
        }

        void LateUpdate()
        {
            // positive rotation about the hinge (local +X, pointing to starboard) raises a trailing edge
            Set(elevators, Vector3.right, elevator >= 0 ? elevator * elevatorUp : elevator * elevatorDown);
            Set(aileronsRight, Vector3.right, aileron >= 0 ? aileron * aileronUp : aileron * aileronDown);   // right aileron up for +roll
            Set(aileronsLeft, Vector3.right, aileron >= 0 ? -aileron * aileronDown : -aileron * aileronUp);
            Set(rudders, Vector3.up, -rudder * rudderMax);
            Set(flapsLeft, Vector3.right, -flaps * flapsMax);
            Set(flapsRight, Vector3.right, -flaps * flapsMax);
        }

        static void Set(Part[] parts, Vector3 axis, float deg)
        {
            foreach (var p in parts)
                if (p.transform != null) p.transform.localRotation = p.rest * Quaternion.AngleAxis(deg, axis);
        }
    }
}

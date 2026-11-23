using System;
using UnityEngine;

namespace AirCity.Aircraft
{
    /// <summary>The lower-hinged passenger door carries the aircraft's boarding steps.</summary>
    [DisallowMultipleComponent]
    public sealed class AircraftAirstair : MonoBehaviour
    {
        Aircraft aircraft;
        Transform hinge;
        Quaternion shutRotation;
        float progress;
        int simulationFrame = -1;

        public bool TargetOpen { get; private set; }
        public bool Stowed => progress == 0f && !TargetOpen;

        bool FindHinge()
        {
            if (aircraft != null) return hinge != null;
            var owner = GetComponent<Aircraft>();
            if (owner == null || owner.type == null) return false;
            aircraft = owner;
            var definition = aircraft.type.airstair;
            hinge = Array.Find(GetComponentsInChildren<Transform>(true), part => part.name == definition.transform);
            if (hinge != null) shutRotation = hinge.localRotation;
            return hinge != null;
        }

        public void SetOpen(bool open)
        {
            if (!FindHinge() || (open && aircraft.Moving)) return;
            TargetOpen = open;
        }

        internal bool StowForDeparture(float seconds)
        {
            SetOpen(false);
            if (Stowed) return true;
            simulationFrame = Time.frameCount;
            Animate(seconds);
            return Stowed;
        }

        void LateUpdate()
        {
            if (simulationFrame != Time.frameCount) Animate(Time.deltaTime);
        }

        internal void Animate(float seconds)
        {
            if (!FindHinge()) return;
            if (aircraft.Moving) TargetOpen = false;
            var definition = aircraft.type.airstair;
            float next = Mathf.MoveTowards(progress, TargetOpen ? 1f : 0f,
                Mathf.Max(0f, seconds) / definition.seconds);
            if (next == progress) return;
            progress = next;
            float angle = definition.openAngle * Mathf.SmoothStep(0f, 1f, progress);
            // Positive rotation about forward lowers the port door toward the apron.
            hinge.localRotation = shutRotation * Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }
}

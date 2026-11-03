using System;
using UnityEngine;

namespace AirCity.Aircraft
{
    /// <summary>Independent cargo hatches, driven in game seconds by baggage service or manually for review.</summary>
    [DisallowMultipleComponent]
    public sealed class AircraftCargoDoors : MonoBehaviour
    {
        sealed class Door
        {
            public AircraftType.CargoDoor definition;
            public Transform part;
            public Quaternion rest;
            public float progress;
            public bool target, servicing;
        }

        AircraftType cachedType;
        Door[] doors = Array.Empty<Door>();

        public bool AllClosed
        {
            get
            {
                FindParts();
                foreach (var door in doors) if (door.progress > 0f || door.target) return false;
                return true;
            }
        }

        public void FindParts()
        {
            var aircraft = GetComponent<Aircraft>();
            var type = aircraft != null ? aircraft.type : null;
            if (type == cachedType) return;
            foreach (var door in doors) if (door.part != null) door.part.localRotation = door.rest;
            cachedType = type;
            if (type == null) { doors = Array.Empty<Door>(); return; }
            var parts = GetComponentsInChildren<Transform>(true);
            doors = new Door[type.cargoDoors.Length];
            for (int i = 0; i < doors.Length; i++)
            {
                var definition = type.cargoDoors[i];
                var part = Array.Find(parts, p => p.name == definition.transform);
                doors[i] = new Door { definition = definition, part = part,
                    rest = part != null ? part.localRotation : Quaternion.identity };
            }
        }

        Door Find(string point)
        {
            FindParts();
            return Array.Find(doors, d => d.definition.point == point);
        }

        public float Progress(string point) => Find(point)?.progress ?? 0f;
        public bool IsOpen(string point) => Find(point)?.progress >= 1f;

        /// <summary>Manual review; baggage retains control of any hatch it is using.</summary>
        public void SetOpen(bool open)
        {
            FindParts();
            var aircraft = GetComponent<Aircraft>();
            if (open && aircraft != null && aircraft.Moving) return;
            foreach (var door in doors) if (!door.servicing) door.target = open;
        }

        /// <summary>Advance just this hold's hatch. A missing rig blocks service instead of loading through the skin.</summary>
        public bool Operate(string point, bool open, float seconds, out string reason)
        {
            var door = Find(point);
            reason = null;
            // An aircraft removed during a cancelled service still needs to release its equipment.
            if (!open && (door == null || door.part == null))
            {
                if (door != null) { door.progress = 0f; door.target = door.servicing = false; }
                return true;
            }
            if (door == null || door.part == null || door.definition.seconds <= 0f || door.definition.axis.sqrMagnitude < 0.5f)
            { reason = "cargo door rig is missing: " + point; return false; }
            door.servicing = true;
            door.target = open;
            Advance(door, seconds);
            bool complete = open ? door.progress == 1f : door.progress == 0f;
            if (!complete) reason = (open ? "opening cargo door: " : "closing cargo door: ") + point;
            if (complete && !open) door.servicing = false;
            return complete;
        }

        static void Advance(Door door, float seconds)
        {
            door.progress = Mathf.MoveTowards(door.progress, door.target ? 1f : 0f,
                Mathf.Max(0f, seconds) / Mathf.Max(0.1f, door.definition.seconds));
            if (door.part == null) return;
            float eased = Mathf.SmoothStep(0f, 1f, door.progress);
            door.part.localRotation = door.rest * Quaternion.AngleAxis(door.definition.openAngle * eased, door.definition.axis);
        }

        void Update()
        {
            FindParts();
            var aircraft = GetComponent<Aircraft>();
            foreach (var door in doors)
            {
                if (door.servicing) continue;
                if (aircraft != null && aircraft.Moving) door.target = false;
                Advance(door, Time.deltaTime);
            }
        }
    }
}

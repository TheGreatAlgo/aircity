using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>A facade's geometry and finish. Stored independently of floor elevations and bridge equipment.</summary>
    [Serializable]
    public sealed class BuildingFacade
    {
        [Tooltip("Degrees from vertical. Positive leans outwards as the wall rises.")]
        [Range(-30f, 30f)] public float lean;
        public Color cladding = new Color(0.80f, 0.81f, 0.80f);
        public Color glassTint = Color.white;
        public Color frames = new Color(0.42f, 0.44f, 0.46f);
        [Range(0f, 1f)] public float glazing = 1f;
        [Min(0.5f)] public float mullionSpacing = 3f;
        [Min(0.1f)] public float spandrel = 1.1f;
        public float Outset(float height) => Mathf.Tan(lean * Mathf.Deg2Rad) * height;
        public BuildingFacade Clone() => (BuildingFacade)MemberwiseClone();
        internal bool Valid => Finite(lean) && Mathf.Abs(lean) <= 30f && Finite(glazing) && glazing >= 0f && glazing <= 1f &&
            Finite(mullionSpacing) && mullionSpacing >= 0.5f && Finite(spandrel) && spandrel >= 0.1f &&
            ValidColor(cladding) && ValidColor(glassTint) && ValidColor(frames);
        internal static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        internal static bool ValidColor(Color c) => Finite(c.r) && Finite(c.g) && Finite(c.b) && Finite(c.a);
    }

    /// <summary>A building-wide facade with optional overrides for the original footprint edges, including arcs.
    /// Sampling an arc into mesh pieces never changes the identity of its facade.</summary>
    [Serializable]
    public sealed class BuildingExterior
    {
        [Serializable] public sealed class Face
        {
            [Min(0)] public int edge;
            public BuildingFacade facade = new BuildingFacade();
        }
        public BuildingFacade facade = new BuildingFacade();
        public List<Face> faces = new List<Face>();
        public Color roof = new Color(0.58f, 0.58f, 0.56f);
        [Min(0f)] public float parapet = Terminal.Parapet;
        public BuildingRoof roofShape = new BuildingRoof();
        [Range(0f, 6f)] public float columnGap;
        public float ColumnDepth => Mathf.Max(0.35f, FacadeStyles.Geometry(this).pierWidth * 1.15f);
        public float ColumnReach => FacadeStyles.Geometry(this).pierWidth > 0f ? columnGap + ColumnDepth : 0f;
        public float RoofOverhang => Mathf.Max(roofShape.overhang, ColumnReach > 0f ? ColumnReach + 0.15f : 0f);
        // Old scenes keep their authored finishes until the player chooses a coordinated style.
        [HideInInspector] public bool usesStyle;
        [HideInInspector] public FacadeStyle style;
        public bool HasLean
        {
            get
            {
                if (facade.lean != 0f) return true;
                foreach (var face in faces) if (face.facade.lean != 0f) return true;
                return false;
            }
        }

        public BuildingFacade ForFace(int edge)
        {
            foreach (var face in faces)
                if (face != null && face.edge == edge && face.facade != null) return face.facade;
            return facade;
        }
        public BuildingFacade Override(int edge)
        {
            foreach (var face in faces) if (face.edge == edge) return face.facade;
            var value = facade.Clone(); faces.Add(new Face { edge = edge, facade = value }); return value;
        }
        public BuildingExterior Clone()
        {
            var result = new BuildingExterior { facade = facade.Clone(), roof = roof, parapet = parapet, roofShape = roofShape.Clone(), columnGap = columnGap, usesStyle = usesStyle, style = style };
            foreach (var face in faces) result.faces.Add(new Face { edge = face.edge, facade = face.facade.Clone() });
            return result;
        }
        internal bool Valid(int edges)
        {
            if (facade == null || !facade.Valid || faces == null || !BuildingFacade.Finite(parapet) || parapet < 0f ||
                parapet > 3f || !BuildingFacade.ValidColor(roof) || roofShape == null || !roofShape.Valid ||
                !BuildingFacade.Finite(columnGap) || columnGap < 0f || columnGap > 6f ||
                (usesStyle && !Enum.IsDefined(typeof(FacadeStyle), style))) return false;
            var seen = new HashSet<int>();
            foreach (var face in faces)
                if (face == null || face.edge < 0 || face.edge >= edges || !seen.Add(face.edge) || face.facade == null || !face.facade.Valid) return false;
            return true;
        }
        public void MirrorHead()
        {
            // A head's sides exchange under reflection; its near and far ends retain their identity.
            foreach (var face in faces) if (face.edge == 0 || face.edge == 2) face.edge = 2 - face.edge;
        }
    }
}

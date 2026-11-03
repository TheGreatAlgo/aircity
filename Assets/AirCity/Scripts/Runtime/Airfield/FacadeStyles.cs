using System;
using UnityEngine;

namespace AirCity.Airfield
{
    public enum FacadeStyle { Classic, Minimal, Framed, Colonnade }
    public enum GlassTint { Clear, Blue, Green, Smoke }

    /// <summary>Coordinated glass facades. Shape belongs to the building; finishes belong to the recipe.</summary>
    public static class FacadeStyles
    {
        public readonly struct Details
        {
            public readonly float frameWidth, frameDepth, pierWidth, pierSpacing;
            public readonly bool forked;
            public Details(float width, float depth, float pier = 0f, float spacing = 9f, bool forked = false)
            { frameWidth = width; frameDepth = depth; pierWidth = pier; pierSpacing = spacing; this.forked = forked; }
        }

        public static Details Geometry(BuildingExterior exterior)
        {
            if (!exterior.usesStyle) return new Details(0.14f, 0.22f);
            return exterior.style switch {
                FacadeStyle.Minimal => new Details(0.065f, 0.12f),
                FacadeStyle.Framed => new Details(0.22f, 0.22f, 0.45f, 7.2f),
                FacadeStyle.Colonnade => new Details(0.10f, 0.18f, 0.22f, 9f, true),
                _ => new Details(0.14f, 0.22f, 0.28f),
            };
        }

        public static Color Tint(GlassTint tint) => tint switch {
            GlassTint.Blue => new Color(0.78f, 0.92f, 1f),
            GlassTint.Green => new Color(0.82f, 1f, 0.91f),
            GlassTint.Smoke => new Color(0.70f, 0.76f, 0.82f),
            _ => Color.white,
        };

        public static void Apply(BuildingExterior exterior, FacadeStyle style)
        {
            if (!Enum.IsDefined(typeof(FacadeStyle), style)) throw new ArgumentOutOfRangeException(nameof(style));
            var previous = exterior.facade;
            var facade = new BuildingFacade { lean = previous.lean, glassTint = previous.glassTint };
            switch (style)
            {
                case FacadeStyle.Minimal:
                    facade.cladding = new Color(0.86f, 0.87f, 0.87f);
                    facade.frames = new Color(0.30f, 0.34f, 0.36f);
                    facade.mullionSpacing = 4f; facade.spandrel = 0.22f; exterior.parapet = 0.2f;
                    break;
                case FacadeStyle.Framed:
                    facade.cladding = new Color(0.73f, 0.75f, 0.76f);
                    facade.frames = new Color(0.38f, 0.41f, 0.43f);
                    facade.mullionSpacing = 3.6f; facade.spandrel = 0.7f; exterior.parapet = 1f;
                    break;
                case FacadeStyle.Colonnade:
                    facade.cladding = new Color(0.89f, 0.88f, 0.85f);
                    facade.frames = new Color(0.73f, 0.75f, 0.75f);
                    facade.mullionSpacing = 3f; facade.spandrel = 0.35f; exterior.parapet = 0.75f;
                    break;
                default:
                    facade.cladding = new Color(0.80f, 0.82f, 0.83f);
                    facade.frames = new Color(0.56f, 0.60f, 0.63f);
                    facade.mullionSpacing = 3f; facade.spandrel = 0.8f; exterior.parapet = 0.9f;
                    break;
            }
            // Keep per-face lean, but replace per-face finishes with the coordinated recipe.
            foreach (var face in exterior.faces)
            {
                float lean = face.facade.lean;
                face.facade = facade.Clone(); face.facade.lean = lean;
            }
            exterior.faces.RemoveAll(f => Mathf.Approximately(f.facade.lean, facade.lean));
            exterior.facade = facade;
            exterior.roof = new Color(0.61f, 0.63f, 0.64f);
            exterior.style = style; exterior.usesStyle = true;
        }

        public static void SetTint(BuildingExterior exterior, GlassTint tint)
        {
            if (!exterior.usesStyle) Apply(exterior, FacadeStyle.Classic);
            exterior.facade.glassTint = Tint(tint);
            foreach (var face in exterior.faces) face.facade.glassTint = Tint(tint);
        }
    }
}

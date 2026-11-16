using System;
using UnityEngine;

namespace AirCity.World
{
    public enum GroundCoverKind { Boulder, Stone, Grass, Fern, Shrub, Log }

    [Serializable]
    public sealed class GroundCoverModel
    {
        public string name;
        public GroundCoverKind kind;
        public Mesh nearMesh, farMesh;
        public Material material;
        public Bounds bounds;
    }

    /// <summary>Reusable metre-scale rocks and understorey meshes, shared by the entire valley.</summary>
    public sealed class GroundCoverLibrary : ScriptableObject
    {
        public int generatorVersion;
        public GroundCoverModel[] models;
    }
}

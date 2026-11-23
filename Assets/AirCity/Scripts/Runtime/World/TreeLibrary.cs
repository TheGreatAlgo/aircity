using System;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>A tree's foliage as a two-ended pyramid in the tree's own space: its widest ring and both tips.</summary>
    [Serializable]
    public struct TreeCrown { public Vector2 centre; public float ring, radius, bottom, top; }

    [Serializable]
    public class TreeVariant
    {
        public string name;
        public float height = 24f;
        /// <summary>Nearest first. Submesh 0 is opaque (trunk and crown volumes), submesh 1 alpha-tested sprays.</summary>
        public Mesh[] levels = new Mesh[3];
        public Material bark, foliage, distantFoliage;
        /// <summary>Set by the generator; a zero radius leaves the forest renderer to measure the mesh.</summary>
        public TreeCrown crown;
    }

    /// <summary>Generated, reusable tree meshes. Each level has bark and alpha-tested foliage submeshes.</summary>
    public sealed class TreeLibrary : ScriptableObject
    {
        public Material bark, foliage;
        public Material distantFoliage;
        public int generatorVersion;
        public TreeVariant[] variants;
    }
}

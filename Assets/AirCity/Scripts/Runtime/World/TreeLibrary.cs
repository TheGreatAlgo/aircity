using System;
using UnityEngine;

namespace AirCity.World
{
    [Serializable]
    public class TreeVariant
    {
        public string name;
        public float height = 24f;
        public Mesh[] levels = new Mesh[3];
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

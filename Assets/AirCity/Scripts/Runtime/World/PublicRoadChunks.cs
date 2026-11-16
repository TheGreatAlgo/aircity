using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Persistent section/junction geometry. Edits replace only dependent chunks, preserving distant mesh
    /// and renderer identities. The composed view serves terrain ownership and grading without merging render meshes.</summary>
    public sealed class PublicRoadChunks : IDisposable
    {
        public sealed class Part
        {
            public readonly int id;
            public readonly bool junction;
            public readonly PublicRoadGeometry geometry;
            public GameObject root;
            internal ulong terrainSignature;
            internal Part(int id,bool junction,PublicRoadGeometry geometry){this.id=id;this.junction=junction;this.geometry=geometry;}
        }
        readonly Dictionary<(bool junction,int id),Part> parts=new Dictionary<(bool,int),Part>();
        readonly PublicRoadProfile rural,airport;
        PublicRoadNetwork previous;
        public PublicRoadGeometry Geometry {get;private set;}
        public int RebuiltParts {get;private set;}
        public IReadOnlyCollection<Part> Parts=>parts.Values;
        public PublicRoadChunks(PublicRoadProfile rural,PublicRoadProfile airport){this.rural=rural;this.airport=airport;}
        public void Update(PublicRoadNetwork graph,Func<Vector2,float> height,Action<Part> render,bool force=false,Rect? terrainChange=null)
        {
            var changed=previous!=null&&!force?new PublicRoadPreviewPartition(previous,graph):null;
            var empty=new HashSet<int>();var edges=new HashSet<int>(graph.segments.Select(e=>e.id));
            var nodes=new HashSet<int>(graph.nodes.Where(n=>graph.At(n.id).Count()>1).Select(n=>n.id));
            foreach(var pair in parts.ToArray())
                if(pair.Key.junction?!nodes.Contains(pair.Key.id):!edges.Contains(pair.Key.id))Remove(pair.Key);
            var terrainEdges = new HashSet<int>();
            var terrainNodes = new HashSet<int>();
            if (terrainChange.HasValue) foreach (var edge in graph.segments)
                if (edge.structure != RoadStructure.Ground && graph.Path(edge).Bounds(edge.Type.FullWidth).Overlaps(terrainChange.Value))
                {
                    if(parts.TryGetValue((false,edge.id),out var previousPart)&&previousPart.terrainSignature==TerrainSignature(graph.Path(edge),edge.Type.FullWidth*.5f,height))continue;
                    terrainEdges.Add(edge.id); terrainNodes.Add(edge.a); terrainNodes.Add(edge.b);
                }
            RebuiltParts=0;
            if(changed!=null&&changed.changedSegments.Count==0&&changed.changedJunctions.Count==0&&terrainEdges.Count==0)return;
            foreach(var edge in graph.segments)
            {
                var key=(false,edge.id);
                if(changed!=null&&!changed.changedSegments.Contains(edge.id)&&!terrainEdges.Contains(edge.id)&&parts.ContainsKey(key))continue;
                Remove(key);var geometry=new PublicRoadGeometry(graph,rural,airport,height,new HashSet<int>{edge.id},empty);
                var part=new Part(edge.id,false,geometry){terrainSignature=TerrainSignature(graph.Path(edge),edge.Type.FullWidth*.5f,height)};parts.Add(key,part);render?.Invoke(part);RebuiltParts++;
            }
            var roadParts=graph.segments.Select(e=>parts[(false,e.id)].geometry).ToArray();
            var context=PublicRoadGeometry.Compose(graph,rural,airport,height,roadParts);
            foreach(var node in graph.nodes)if(nodes.Contains(node.id))
            {
                var key=(true,node.id);
                if(changed!=null&&!changed.changedJunctions.Contains(node.id)&&!terrainNodes.Contains(node.id)&&parts.ContainsKey(key))continue;
                Remove(key);var geometry=new PublicRoadGeometry(graph,rural,airport,height,empty,new HashSet<int>{node.id},context);
                var part=new Part(node.id,true,geometry);parts.Add(key,part);render?.Invoke(part);RebuiltParts++;
            }
            context.DestroyMeshes();Geometry?.DestroyMeshes();
            var ordered=roadParts.Concat(graph.nodes.Where(n=>nodes.Contains(n.id)).Select(n=>parts[(true,n.id)].geometry)).ToArray();
            Geometry=PublicRoadGeometry.Compose(graph,rural,airport,height,ordered);previous=graph.Clone();
        }
        static ulong TerrainSignature(Alignment3D path,float half,Func<Vector2,float> height)
        {
            ulong hash=1469598103934665603UL;int count=Mathf.Max(1,Mathf.CeilToInt(path.Length/4f));
            for(int i=0;i<=count;i++)for(int side=-1;side<=1;side++)
                unchecked{hash^=(uint)BitConverter.SingleToInt32Bits(height(Alignment3D.XZ(path.Offset(path.Length*i/count,side*half))));hash*=1099511628211UL;}
            return hash;
        }
        void Remove((bool junction,int id) key)
        {
            if(!parts.TryGetValue(key,out var part))return;part.geometry.DestroyMeshes();
            if(part.root!=null){if(Application.isPlaying)UnityEngine.Object.Destroy(part.root);else UnityEngine.Object.DestroyImmediate(part.root);}
            parts.Remove(key);
        }
        public void Dispose(){foreach(var key in parts.Keys.ToArray())Remove(key);Geometry?.DestroyMeshes();Geometry=null;previous=null;}
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Separate an immutable street draft into changed approaches and reusable roads. The full graph still
    /// supplies junction layout. Two rings include the opposite-end junctions whose available reach may change.</summary>
    public sealed class PublicRoadPreviewPartition
    {
        public readonly HashSet<int> changedSegments=new HashSet<int>(),changedJunctions=new HashSet<int>();
        public readonly HashSet<int> unchangedSegments=new HashSet<int>(),unchangedJunctions=new HashSet<int>();
        public PublicRoadPreviewPartition(PublicRoadNetwork source,PublicRoadNetwork draft)
        {
            var touched=new HashSet<int>();
            var oldSegments=new Dictionary<int,PublicRoadSegment>();var newSegments=new Dictionary<int,PublicRoadSegment>();
            var oldNodes=new Dictionary<int,PublicRoadNode>();var newNodes=new Dictionary<int,PublicRoadNode>();
            foreach(var edge in source.segments)oldSegments.Add(edge.id,edge);
            foreach(var edge in draft.segments)newSegments.Add(edge.id,edge);
            foreach(var node in source.nodes)oldNodes.Add(node.id,node);
            foreach(var node in draft.nodes)newNodes.Add(node.id,node);
            foreach(var edge in source.segments)
            {
                newSegments.TryGetValue(edge.id,out var next);
                if(next==null || !Same(oldNodes,edge,newNodes,next)){touched.Add(edge.a);touched.Add(edge.b);}
            }
            foreach(var edge in draft.segments)
            {
                oldSegments.TryGetValue(edge.id,out var previous);
                if(previous==null || !Same(oldNodes,previous,newNodes,edge)){touched.Add(edge.a);touched.Add(edge.b);}
            }
            for(int ring=0;ring<2;ring++)
            {
                var expanded=new HashSet<int>(touched);
                foreach(var edge in draft.segments)if(touched.Contains(edge.a)||touched.Contains(edge.b))
                {expanded.Add(edge.a);expanded.Add(edge.b);}
                touched=expanded;
            }
            // A bridge's piers are spaced along its whole run (RoadStructureBuilder.Runs): a change anywhere along
            // it re-lays them all, so the run's other sections and bends are redrawn with it.
            var arms=new Dictionary<int,List<PublicRoadSegment>>();
            foreach(var edge in draft.segments)if(edge.structure==RoadStructure.Elevated)
                foreach(int node in new[]{edge.a,edge.b})
                {if(!arms.TryGetValue(node,out var list))arms.Add(node,list=new List<PublicRoadSegment>());list.Add(edge);}
            if(arms.Count>0)
            {
                var degree=new Dictionary<int,int>();
                foreach(var edge in draft.segments){degree[edge.a]=degree.TryGetValue(edge.a,out int a)?a+1:1;degree[edge.b]=degree.TryGetValue(edge.b,out int b)?b+1:1;}
                var open=new Stack<int>();foreach(int node in touched)if(arms.ContainsKey(node))open.Push(node);
                var walked=new HashSet<int>();
                while(open.Count>0)
                {
                    int node=open.Pop();
                    foreach(var edge in arms[node])
                    {
                        if(!walked.Add(edge.id))continue;
                        int far=edge.a==node?edge.b:edge.a;touched.Add(far);
                        if(degree[far]==2&&arms[far].Count==2)open.Push(far);
                    }
                }
            }
            foreach(var edge in draft.segments)
                if(touched.Contains(edge.a)||touched.Contains(edge.b))changedSegments.Add(edge.id);else unchangedSegments.Add(edge.id);
            foreach(var edge in draft.segments)if(changedSegments.Contains(edge.id))
            {changedJunctions.Add(edge.a);changedJunctions.Add(edge.b);}
            foreach(var node in draft.nodes)if(!changedJunctions.Contains(node.id))unchangedJunctions.Add(node.id);
        }
        static bool Same(Dictionary<int,PublicRoadNode> a,PublicRoadSegment x,Dictionary<int,PublicRoadNode> b,PublicRoadSegment y)=>
            x.a==y.a && x.b==y.b && x.style==y.style && x.structure==y.structure && x.bridgeDesign==y.bridgeDesign && x.tunnelPortalStyle==y.tunnelPortalStyle && x.alignment.Equals(y.alignment) &&
            a[x.a].position==b[y.a].position && a[x.b].position==b[y.b].position &&
            a[x.a].outsideConnection==b[y.a].outsideConnection && a[x.b].outsideConnection==b[y.b].outsideConnection &&
            a[x.a].control==b[y.a].control && a[x.b].control==b[y.b].control;
    }
}

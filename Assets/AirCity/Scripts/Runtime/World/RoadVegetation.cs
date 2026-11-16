using System.Collections.Generic;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Bridge clearance is a volume: keep low vegetation underneath, remove canopies that enter a deck.
    /// Ground footprints and tunnel entrances are cleared separately by their exact terrain-ownership map.</summary>
    internal static class RoadVegetation
    {
        public static int ClearCanopies(ValleyWorld world,Alignment3D path,float half,Vector3 origin)
        {
            if(world==null)return 0;var min=Alignment3D.XZ(path.start)+Alignment3D.XZ(origin);var max=min;
            for(int i=1;i<=16;i++){var p=Alignment3D.XZ(path.Position(i/16f))+Alignment3D.XZ(origin);min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
            float radius=half+4f;var bounds=Rect.MinMaxRect(min.x-radius,min.y-radius,max.x+radius,max.y+radius);int cleared=0;
            foreach(int index in ForestQuery.Candidates(world,bounds))
            {
                var tree=world.trees[index];if(tree.removed)continue;var local=tree.position-Alignment3D.XZ(origin);
                float t=path.ClosestParameter(local);if(path.DistanceTo(local)>half+3f)continue;
                float deck=path.Height(t)+origin.y-RoadStructureBuilder.DeckDepth;
                float ground=world.Height(tree.position);
                if(ground<deck && ground+tree.height<deck-.25f)continue;
                tree.removed=true;cleared++;
            }
            return cleared;
        }
    }
}

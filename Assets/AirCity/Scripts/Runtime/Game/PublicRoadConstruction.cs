using System;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.World;
using UnityEngine;

namespace AirCity.Game
{
    /// <summary>One road transaction for both modes. Preview/refusal cannot split or mutate the live graph.</summary>
    public static class PublicRoadConstruction
    {
        public const float MaximumGroundGrade = .12f;
        public static bool Build(CareerState state, PublicRoadNetwork network, int from, Vector3 end,
            int endNode, int endSegment, PublicRoadStyle style, bool commit,
            out PublicRoadNetwork result, out int price, out string problem,Action<PublicRoadNetwork> preview=null)
        {
            result=network;price=0;
            var settings=new RoadBuildSettings {mode=RoadBuildMode.Ground,legacyPrice=true};
            if(!RoadConstructionPlan.TryCreate(state,network,from,end,endNode,endSegment,style,settings,out var plan,out problem))
            {if(plan!=null)price=plan.Price;return false;}
            price=plan.Price;preview?.Invoke(plan.Draft);
            return !commit || plan.TryCommit(state,network,out result,out problem);
        }

        static bool Finite(Vector3 p) => !(float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.z));
    }
}

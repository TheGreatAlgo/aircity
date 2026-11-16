using System.Collections.Generic;
using AirCity.Characters;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>
    /// Pooled bag art. Cart piles, the belt and the hold come from <see cref="BaggageBagFlow"/>, which trails the ledger
    /// during an aircraft transfer and copies it otherwise; bags in a handler's hands come from the
    /// <see cref="BaggageCrewPerformance"/>, at the aircraft and in depot handovers alike.
    /// </summary>
    public sealed class BaggageLoadVisuals
    {
        readonly BaggageArtPool art;
        BaggageBagFlow own;
        /// <summary>Largest distance this frame between a palm and the grip on the side of the bag drawn in that hand (m).</summary>
        public float PalmGap { get; private set; }
        BaggageFleet ownFleet;
        public BaggageLoadVisuals(Transform root,GameObject[] prefabs) { art=new BaggageArtPool(root,prefabs); }

        /// <summary>Drawn size of each bag variant (x across, y along, z height), for the performers' grips.</summary>
        public Vector3[] Sizes()
        {
            var sizes=new Vector3[3];
            for (int i=0;i<sizes.Length;i++) sizes[i]=art.Size(i);
            return sizes;
        }

        static RampCrewRig Rig(BaggageFleet fleet,IReadOnlyList<RampCrewRig> workers,bool ground,int worker)
        {
            if (ground && worker>=2) return fleet.Station.CrewRig(true,2);
            return workers!=null && worker<workers.Count ? workers[worker] : null;
        }
        /// <summary>A bag in someone's hands is drawn between the palms the rig actually shows (the rig eases at screen rate,
        /// so at a fast clock its hands can trail the performer's targets by a little): moved to the middle of the shown
        /// palms and turned to the line between them.</summary>
        void InHands(BaggageCrewPerformance performance,bool ground,int worker,ref Vector3 bottom,ref Quaternion rotation,bool held,RampCrewRig rig)
        {
            if (!held || rig==null || !rig.isActiveAndEnabled || !performance.HandTargets(ground,worker,out var l,out var r)) return;
            var frame=art.Root;
            var palmL=frame.InverseTransformPoint(rig.LeftPalm); var palmR=frame.InverseTransformPoint(rig.RightPalm);
            var centre=(l+r)*.5f; var shown=(palmL+palmR)*.5f;
            var turn=Quaternion.FromToRotation(r-l,palmR-palmL);
            bottom=shown+turn*(bottom-centre); rotation=turn*rotation;
            float gap=Mathf.Max(Vector3.Distance(shown+turn*(l-centre),palmL),Vector3.Distance(shown+turn*(r-centre),palmR));
            if (gap>PalmGap) PalmGap=gap;
        }

        /// <summary>Draw the fleet's bags. Without a performance the piles copy the ledger (tests, editor previews).</summary>
        public void Present(BaggageFleet fleet,BaggageCrewPerformance performance=null,
            IReadOnlyList<RampCrewRig> groundWorkers=null,IReadOnlyList<RampCrewRig> holdWorkers=null)
        {
            BaggageBagFlow flow;
            if (performance!=null) flow=performance.Flow;
            else
            {
                if (own==null || ownFleet!=fleet) { own=new BaggageBagFlow(fleet,true); ownFleet=fleet; }
                own.Step(0f); flow=own;
            }
            art.Begin();
            for (int c=0;c<flow.Carts.Count;c++)
                foreach (var e in flow.Carts[c].entries)
                    art.Show(flow.CartSlot(c,e.slot)+Vector3.up*BaggageBagFlow.Offset(e.motion,e.anim),flow.CartRotation(c,e.variant),e.variant);
            foreach (var t in flow.Tokens) art.Show(flow.TokenBottom(t),flow.BeltRotation(t.s),t.variant);
            foreach (var t in flow.Ghosts) art.Show(flow.GhostBottom(t),flow.GhostRotation,t.variant);
            var job=flow.Job;
            if (job?.Plan!=null && job.Aircraft!=null && job.Aircraft.CargoDoors.IsOpen(job.HoldConfiguration.cargoPoint))
            {
                foreach (var e in flow.HoldBags) art.Show(flow.HoldBottom(e),flow.HoldRotation(e),e.variant);
                foreach (var e in flow.HoldLeaving) art.Show(flow.HoldBottom(e),flow.HoldRotation(e),e.variant);
            }
            PalmGap=0f;
            if (performance!=null)
                for (int w=0;w<3;w++)
                {
                    if (performance.Bag(true,w,out var b,out var r,out var v,out bool held)) { InHands(performance,true,w,ref b,ref r,held,Rig(fleet,groundWorkers,true,w)); art.Show(b,r,v); }
                    if (w<2 && performance.Bag(false,w,out b,out r,out v,out held)) { InHands(performance,false,w,ref b,ref r,held,Rig(fleet,holdWorkers,false,w)); art.Show(b,r,v); }
                }
            art.End();
        }
    }
}

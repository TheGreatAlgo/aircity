using System.Collections.Generic;
using AirCity.Characters;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Pooled bag art follows game-time handovers. The baggage ledger remains the owner of every bag.</summary>
    public sealed class BaggageLoadVisuals
    {
        readonly BaggageArtPool art;
        readonly Dictionary<BaggageStore,BaggageStorageLayout> cartLayouts=new Dictionary<BaggageStore,BaggageStorageLayout>();
        readonly List<BaggageTurnaround> transfers=new List<BaggageTurnaround>(1);
        BaggageStore[] previousCarts;
        BaggageTurnaround lastJob;
        public BaggageLoadVisuals(Transform root,GameObject[] prefabs) { art=new BaggageArtPool(root,prefabs); }

        static int Variant(BaggageBatch batch,int bag) => BaggageHoldPacking.Variant(batch,bag);

        public void Present(BaggageFleet fleet, RampCrewRig ground = null, RampCrewRig hold = null,
            IReadOnlyList<RampCrewRig> groundWorkers=null,IReadOnlyList<RampCrewRig> holdWorkers=null)
        {
            art.Begin();
            if (!ReferenceEquals(previousCarts,fleet.Carts)) { cartLayouts.Clear(); previousCarts=fleet.Carts; }
            if (fleet.Job != null) lastJob = fleet.Job;
            var job = fleet.Job ?? lastJob;
            transfers.Clear(); if (job?.TransferBatch!=null) transfers.Add(job);
            if (job?.TransferBatch != null)
            {
                bool loading = job.TransferTo == job.Hold;
                float travel = job.BagTravelSeconds, interval = job.TransferInterval;
                for (int i = 0; i < job.TransferBatch.Count; i++)
                {
                    float elapsed = job.TransferSeconds - i * interval;
                    if (elapsed < 0f) break;
                    if (elapsed >= travel) continue;
                    var pose = job.DepotTransfer ? fleet.Station.depot.BagAt(fleet,elapsed,i) : fleet.Crew.BagAt(elapsed,loading,i);
                    var workers=pose.holder==0 ? groundWorkers : holdWorkers;
                    var holder=pose.holder<0 ? null : workers!=null && workers.Count>pose.worker ? workers[pose.worker] : pose.holder==0 ? ground : hold;
                    art.Show(pose.bottom,pose.rotation,Variant(job.TransferBatch,i),holder);
                }
            }
            var packing=fleet.CartPacking;
            for (int cartIndex = 0; cartIndex < fleet.Carts.Length; cartIndex++)
            {
                var pose=fleet.Train.CartAt(cartIndex); var store=fleet.Carts[cartIndex];
                if (!cartLayouts.TryGetValue(store,out var layout)) cartLayouts.Add(store,layout=new BaggageStorageLayout(store.Capacity));
                layout.Update(store,transfers);
                foreach (var bag in layout.Bags)
                {
                    var local=packing.Position(bag.slot);
                    var point=pose.Point(local);
                    art.Show(new Vector3(point.x,local.y,point.y),Quaternion.LookRotation(new Vector3(pose.facing.x,0,pose.facing.y))*packing.Rotation(bag.variant),bag.variant);
                }
            }

            if (job?.Plan != null && job.Aircraft != null && job.Aircraft.CargoDoors.IsOpen(job.HoldConfiguration.cargoPoint))
            {
                foreach (var allocation in job.Packing.Allocations)
                {
                    var batch = allocation.Key;
                    for (int i = 0; i < allocation.Value.Length; i++)
                    {
                        int slot = allocation.Value[i]; if (slot < 0) continue;
                        bool show = batch.Location == job.Hold;
                        if (batch == job.TransferBatch)
                        {
                            float elapsed = job.TransferSeconds-i*job.TransferInterval;
                            if (job.TransferFrom == job.Hold && elapsed >= 0) show = false;
                            if (job.TransferTo == job.Hold && elapsed >= job.BagTravelSeconds) show = true;
                        }
                        if (show) art.Show(job.Packing.WorldSlot(slot),job.Packing.BagRotation(batch,i),Variant(batch,i));
                    }
                }
            }
            art.End();
        }
    }
}

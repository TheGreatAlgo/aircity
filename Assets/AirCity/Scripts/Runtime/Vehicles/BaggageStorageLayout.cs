using System.Collections.Generic;

namespace AirCity.Vehicles
{
    /// <summary>Visible bags retain their identity, appearance and slot while other bags move.</summary>
    public sealed class BaggageStorageLayout
    {
        public readonly struct Bag
        {
            public readonly BaggageBatch batch;
            public readonly int index,slot,variant;
            public Bag(BaggageBatch batch,int index,int slot)
            { this.batch=batch; this.index=index; this.slot=slot; variant=BaggageHoldPacking.Variant(batch,index); }
        }
        readonly List<Bag> bags=new List<Bag>();
        readonly HashSet<(BaggageBatch,int)> assigned=new HashSet<(BaggageBatch,int)>();
        readonly bool[] occupied;
        public IReadOnlyList<Bag> Bags => bags;
        public BaggageStorageLayout(int capacity) { occupied=new bool[capacity]; }

        public void Update(BaggageStore store,IReadOnlyList<BaggageTurnaround> transfers=null)
        {
            bool Present(BaggageBatch batch,int index)
            {
                bool present=batch.Location==store;
                if (transfers!=null)
                    foreach (var job in transfers)
                    {
                        if (job.TransferBatch!=batch) continue;
                        float elapsed=job.TransferSeconds-index*job.TransferInterval;
                        if (job.TransferFrom==store && elapsed>=0) present=false;
                        if (job.TransferTo==store && elapsed>=job.BagTravelSeconds) present=true;
                    }
                return present;
            }
            for (int i=bags.Count-1;i>=0;i--)
            {
                var bag=bags[i]; if (Present(bag.batch,bag.index)) continue;
                occupied[bag.slot]=false; assigned.Remove((bag.batch,bag.index)); bags.RemoveAt(i);
            }
            void Fill(BaggageBatch batch)
            {
                int slot=0;
                for (int i=0;i<batch.Count && bags.Count<occupied.Length;i++)
                {
                    if (assigned.Contains((batch,i)) || !Present(batch,i)) continue;
                    while (slot<occupied.Length && occupied[slot]) slot++;
                    if (slot==occupied.Length) break;
                    bags.Add(new Bag(batch,i,slot)); occupied[slot]=true; assigned.Add((batch,i));
                }
            }
            foreach (var batch in store.Batches) { if (bags.Count==occupied.Length) break; Fill(batch); }
            if (transfers!=null)
                foreach (var job in transfers)
                    if (job.TransferTo==store && job.TransferBatch!=null && job.TransferBatch.Location!=store) Fill(job.TransferBatch);
        }
    }
}

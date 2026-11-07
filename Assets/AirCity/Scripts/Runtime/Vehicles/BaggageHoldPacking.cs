using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Stable storage slots for the modelled bay. Ledger capacity may include additional unmodelled bays.</summary>
    public sealed class BaggageHoldPacking
    {
        public static readonly Vector3 BagSpacing = new Vector3(.50f,.31f,.80f);
        readonly BaggageTurnaround job;
        readonly List<Vector3> slots = new List<Vector3>();
        readonly Dictionary<BaggageBatch,int[]> allocations = new Dictionary<BaggageBatch,int[]>();
        readonly bool[] occupied;
        readonly float[] heights, bottoms;
        static readonly string[] Shapes = { "Bag_HardSmall","Bag_Soft","Bag_Duffel" };
        public static int Variant(BaggageBatch batch,int bag)
        { unchecked { int hash = 17; foreach (char c in batch.Id) hash = hash*31+c; return (hash+bag*7)&int.MaxValue; } }
        float BagHeight(int variant)
        {
            var profile = job.Fleet.Station.bagPropType;
            if (profile != null)
                foreach (var shape in profile.props) if (shape.name == Shapes[variant%Shapes.Length]) return shape.size.z;
            return BagSpacing.y;
        }
        public int SlotCount => slots.Count;
        public IEnumerable<KeyValuePair<BaggageBatch,int[]>> Allocations => allocations;
        public Vector3 LocalSlot(int index) => slots[index];
        public Vector3 WorldSlot(int index) => job.Plan.door + Vector3.Cross(Vector3.up,job.Plan.inward)*slots[index].x +
            Vector3.up*bottoms[index] + job.Plan.inward*slots[index].z;
        public float Depth { get; }
        public float SideMin { get; }
        public float SideMax { get; }
        public BaggageHoldPacking(BaggageTurnaround job)
        {
            this.job = job;
            job.HoldConfiguration.aircraft.TryGetPoint(job.HoldConfiguration.cargoPoint,out var point);
            var door = System.Array.Find(job.HoldConfiguration.aircraft.cargoDoors,d => d.point == point.name);
            Depth = door.holdDepth > 0 ? door.holdDepth : .65f;
            SideMin = door.holdSideMin < door.holdSideMax ? door.holdSideMin : -point.size.x*.5f;
            SideMax = door.holdSideMin < door.holdSideMax ? door.holdSideMax : point.size.x*.5f;
            float height = door.holdHeight > 0 ? door.holdHeight : point.size.y;
            int across = Mathf.Max(1,Mathf.FloorToInt((SideMax-SideMin-.08f)/BagSpacing.x));
            int rows = Mathf.Max(1,Mathf.FloorToInt((Depth-door.holdFloorStart-.04f)/BagSpacing.z));
            float pitch = Mathf.Max(BagHeight(0),BagHeight(1),BagHeight(2))+.003f;
            int layers = Mathf.Max(1,Mathf.FloorToInt((height+.001f)/pitch));
            float width = across*BagSpacing.x;
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < across; column++)
                {
                    float side = (SideMin+SideMax-width)*.5f+(column+.5f)*BagSpacing.x;
                    float depth = door.holdFloorStart+(row+.5f)*BagSpacing.z+.02f;
                    // Keep a knee-width aisle and the complete doorway working bay clear.
                    bool aisle = SideMax-SideMin > 1.6f && side > -.20f && side < .85f;
                    bool entrance = Mathf.Abs(side) < point.size.x*.5f+.25f && depth < 1.45f;
                    if ((aisle || entrance) && rows > 1) continue;
                    for (int layer = 0; layer < layers; layer++) slots.Add(new Vector3(side,layer*pitch,depth));
                }
            // Deep rows and the far ends of each row first; stacks build from floor to ceiling.
            slots.Sort((a,b) => { int depth = b.z.CompareTo(a.z); if (depth != 0) return depth;
                int side = Mathf.Abs(b.x-.3f).CompareTo(Mathf.Abs(a.x-.3f)); return side != 0 ? side : a.y.CompareTo(b.y); });
            occupied = new bool[slots.Count];
            heights = new float[slots.Count]; bottoms = new float[slots.Count];
        }
        public void Seed(IReadOnlyList<BaggageBatch> incoming)
        {
            int total = 0; foreach (var batch in incoming) total += batch.Count;
            int next = Mathf.Min(total,slots.Count)-1;
            foreach (var batch in incoming)
            {
                var indices = new int[batch.Count];
                for (int i = 0; i < indices.Length; i++)
                { indices[i] = next >= 0 ? next-- : -1; if (indices[i] >= 0) occupied[indices[i]] = true; }
                allocations[batch] = indices;
            }
            RecalculateStacks();
        }
        public void Reserve(BaggageBatch batch)
        {
            if (allocations.ContainsKey(batch)) return;
            var indices = new int[batch.Count]; int next = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                while (next < occupied.Length && occupied[next]) next++;
                indices[i] = next < occupied.Length ? next++ : -1;
                if (indices[i] >= 0) occupied[indices[i]] = true;
            }
            allocations.Add(batch,indices);
            RecalculateStacks();
        }
        public void Release(BaggageBatch batch)
        {
            if (!allocations.TryGetValue(batch,out var indices)) return;
            foreach (var index in indices) if (index >= 0) occupied[index] = false;
            allocations.Remove(batch);
            RecalculateStacks();
        }
        void RecalculateStacks()
        {
            System.Array.Clear(heights,0,heights.Length);
            foreach (var pair in allocations)
                for (int bag = 0; bag < pair.Value.Length; bag++)
                { int slot = pair.Value[bag]; if (slot >= 0) heights[slot] = BagHeight(Variant(pair.Key,bag))+.003f; }
            for (int i = 0; i < slots.Count; i++)
            {
                float y = 0;
                for (int j = 0; j < i; j++)
                    if (slots[i].x == slots[j].x && slots[i].z == slots[j].z && slots[j].y < slots[i].y) y += heights[j];
                bottoms[i] = y;
            }
        }
        public int Slot(BaggageBatch batch,int bag)
        { return batch != null && allocations.TryGetValue(batch,out var indices) && bag >= 0 && bag < indices.Length ? indices[bag] : -1; }
        public Vector3 BagPosition(BaggageBatch batch,int bag)
        {
            int slot = Slot(batch,bag);
            return slot >= 0 ? WorldSlot(slot) : job.Plan.door + job.Plan.inward*Mathf.Min(1.2f,Depth-.35f)-Vector3.Cross(Vector3.up,job.Plan.inward)*.25f;
        }
        public Quaternion BagRotation(BaggageBatch batch,int bag)
        {
            var rotation = Quaternion.LookRotation(job.Plan.inward);
            var profile = job.Fleet.Station.bagPropType;
            if (profile != null)
                foreach (var shape in profile.props)
                    if (shape.name == Shapes[Variant(batch,bag)%Shapes.Length] && shape.size.x > BagSpacing.x && shape.size.y < BagSpacing.x)
                        return rotation*Quaternion.Euler(0,90,0);
            return rotation;
        }

        public Vector3 WorkerPosition(BaggageBatch batch,int bag)
        {
            int index = Slot(batch,bag);
            if (index < 0)
            {
                var handoff = BagPosition(batch,bag); handoff.y = job.Plan.door.y;
                return handoff-job.Plan.inward*.43f+Vector3.Cross(Vector3.up,job.Plan.inward)*.27f;
            }
            var slot = slots[index];
            if (SideMax-SideMin < 1.6f)
                return job.Plan.door+job.Plan.inward*Mathf.Max(.6f,slot.z-.68f)+Vector3.Cross(Vector3.up,job.Plan.inward)*.08f;
            float side = slot.x < .3f ? slot.x+.52f : slot.x-.52f;
            return job.Plan.door + Vector3.Cross(Vector3.up,job.Plan.inward)*side + job.Plan.inward*Mathf.Clamp(slot.z-.1f,.5f,Depth-.45f);
        }
    }
}

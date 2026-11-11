using UnityEngine;

namespace AirCity.World
{
    [ExecuteAlways]
    public sealed class RoadSignalHead : MonoBehaviour
    {
        public Renderer red, amber, green;
        public int signalGroup;
        RoadSignal state = (RoadSignal)(-1);
        MaterialPropertyBlock block;
        void OnEnable() { state = (RoadSignal)(-1); if (red != null) SetState(PublicRoadSignals.Signal(signalGroup, 0f)); }
        public void SetState(RoadSignal next)
        {
            if (next == state) return;
            state = next; block ??= new MaterialPropertyBlock();
            Set(red, new Color(1f, .035f, .008f), next == RoadSignal.Red);
            Set(amber, new Color(1f, .55f, .025f), next == RoadSignal.Amber);
            Set(green, new Color(.02f, .9f, .22f), next == RoadSignal.Green);
        }
        void Set(Renderer renderer, Color color, bool on)
        {
            if (renderer == null) return;
            block.SetColor("_BaseColor", color * (on ? 1.5f : .035f)); renderer.SetPropertyBlock(block);
        }
    }
}

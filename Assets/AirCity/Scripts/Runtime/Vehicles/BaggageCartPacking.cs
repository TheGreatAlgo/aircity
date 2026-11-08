using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>The same physical slots limit cart manifests and position every visible suitcase.</summary>
    public sealed class BaggageCartPacking
    {
        readonly Vector3 minimum,spacing;
        readonly int columns,rows;
        readonly bool[] turned=new bool[3];
        public int Capacity { get; }
        public BaggageCartPacking(VehicleType cart,VehicleType props)
        {
            var size=new Vector3(BaggageHoldPacking.BagSpacing.x,BaggageHoldPacking.BagSpacing.z,BaggageHoldPacking.BagSpacing.y);
            var largest=Vector3.zero;
            for (int i=0;i<turned.Length;i++)
            {
                var shape=size; bool profiled=false;
                if (props!=null)
                    foreach (var prop in props.props)
                        if (prop.name==BaggageHoldPacking.Shapes[i]) { shape=prop.size; profiled=true; break; }
                turned[i]=profiled ? shape.x>shape.y : i==2;
                largest=Vector3.Max(largest,new Vector3(Mathf.Min(shape.x,shape.y),shape.z,Mathf.Max(shape.x,shape.y)));
            }
            spacing=largest+new Vector3(.016f,.003f,.016f);
            var available=cart.baggage.loadMax-cart.baggage.loadMin;
            columns=Mathf.Max(0,Mathf.FloorToInt((available.x+.0001f)/spacing.x));
            rows=Mathf.Max(0,Mathf.FloorToInt((available.z+.0001f)/spacing.z));
            int layers=Mathf.Max(0,Mathf.FloorToInt((available.y+.0001f)/spacing.y));
            Capacity=columns*rows*layers;
            minimum=cart.baggage.loadMin+new Vector3((available.x-columns*spacing.x)*.5f,0,(available.z-rows*spacing.z)*.5f);
        }
        public Vector3 Position(int slot) => minimum+new Vector3((slot%columns+.5f)*spacing.x,
            slot/(columns*rows)*spacing.y,(slot/columns%rows+.5f)*spacing.z);
        public Quaternion Rotation(int variant) => Quaternion.Euler(0,turned[variant%turned.Length] ? 90 : 0,0);
    }
}

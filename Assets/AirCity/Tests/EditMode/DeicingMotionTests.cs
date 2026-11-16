using AirCity.Aircraft;
using AirCity.Airfield;
using UnityEditor;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests
{
    public sealed class DeicingMotionTests
    {
        VehicleType truck;
        AircraftType aircraft;
        [SetUp] public void SetUp()
        {
            truck = ScriptableObject.CreateInstance<VehicleType>(); truck.wheelbase = 6f;
            truck.turnRadius = 7f; truck.transitSpeed = 5f;
            truck.bounds = new Bounds(new Vector3(0f, 2.6f, 3.9f), new Vector3(3.13f, 5.3f, 12f));
            aircraft = ScriptableObject.CreateInstance<AircraftType>();
            aircraft.bounds = new Bounds(new Vector3(0, 5, 0), new Vector3(34, 10, 38));
            aircraft.deicingWing = new[] {
                new AircraftType.DeicingWingSection { span=2f, leading=new Vector3(2,4,5), middle=new Vector3(2,4.4f,1), trailing=new Vector3(2,4.1f,-3) },
                new AircraftType.DeicingWingSection { span=17f, leading=new Vector3(17,5,-3), middle=new Vector3(17,5.2f,-4), trailing=new Vector3(17,5.1f,-5) }
            };
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(truck); Object.DestroyImmediate(aircraft); }

        [Test] public void Drive_TurnsByTravellingAndBrakesAtItsDestination()
        {
            var from = new BaggageVehiclePose(Vector2.zero, Vector2.up);
            var path = new VehiclePath(); path.Start(from.position, truck.transitSpeed);
            path.TurnTo(from.facing, new Vector2(22, 28), Vector2.right, truck.turnRadius, truck.transitSpeed);
            var drive = new DeicingDrive(from, truck); drive.Begin(path);
            bool turned = false;
            for (int i=0; i<3000 && !drive.Finished; i++)
            {
                var before = drive.Position; var facing = drive.Forward;
                drive.Step(.05f); var travelled = drive.Position-before;
                if (travelled.magnitude>.001f)
                {
                    Assert.Greater(Vector2.Dot(travelled.normalized,drive.Forward),.96f,"Truck slid sideways.");
                    Assert.Less(travelled.magnitude,.26f,"Truck teleported along its route.");
                    if (Vector2.Angle(facing,drive.Forward)>.01f) turned=true;
                }
                else Assert.Less(Vector2.Angle(facing,drive.Forward),.01f,"Truck turned in place.");
            }
            Assert.IsTrue(turned); Assert.IsTrue(drive.Finished);
            Assert.Less(Vector2.Distance(drive.Position,new Vector2(22,28)),.025f);
            Assert.AreEqual(0f,drive.Speed);
        }
        [Test] public void Drive_HoldAndZeroTimePreserveThePose()
        {
            var drive = new DeicingDrive(new BaggageVehiclePose(Vector2.zero,Vector2.up),truck);
            var path = new VehiclePath(); path.Start(Vector2.zero,4); path.LineTo(Vector2.up*30,4);
            drive.Begin(path); drive.Step(1f); var position=drive.Position; var forward=drive.Forward;
            drive.Hold(); drive.Step(0f);
            Assert.AreEqual(position,drive.Position); Assert.AreEqual(forward,drive.Forward); Assert.AreEqual(0f,drive.Speed);
            drive.Step(.05f); Assert.Less(Vector2.Distance(position,drive.Position),.01f);
        }
        [Test] public void Drive_StopsAndDwellsBeforeChangingGear()
        {
            var path=new VehiclePath(); path.Start(Vector2.zero,8f); path.LineTo(Vector2.up*45f,8f);
            path.LineTo(Vector2.up*30f,2f,reverse:true);
            var drive=new DeicingDrive(new BaggageVehiclePose(Vector2.zero,Vector2.up),truck); drive.Begin(path);
            int stopped=0; bool reversed=false; float previous=0f;
            for(int i=0;i<4000 && !drive.Finished;i++)
            {
                drive.Step(.05f);
                if(previous>0f && drive.Speed>=0f) Assert.GreaterOrEqual(drive.Speed,previous-.102f,"Truck exceeded physical braking before the gear change.");
                if(Mathf.Abs(drive.Along-45f)<.001f && drive.Speed==0f) stopped++;
                if(drive.Speed<0f)
                { Assert.GreaterOrEqual(stopped,10,"Reverse began without a stationary gear change."); Assert.LessOrEqual(previous,.12f); reversed=true; break; }
                previous=drive.Speed;
            }
            Assert.IsTrue(reversed,"Truck never reached the reverse leg.");
        }
        [TestCase("Narrowbody")][TestCase("DoubleDeck")]
        public void InitialApproach_HasAClearDrivenRoute(string model)
        {
            var type=AssetDatabase.LoadAssetAtPath<AircraftType>($"Assets/AirCity/Aircraft/{model}/{model}.aircraft");
            var art=Resources.Load<DeicingAssets>("DeicingAssets");
            var root=new GameObject("Deicing approach test"); var plane=new GameObject("Approach aircraft");
            try
            {
                root.SetActive(false); var airport=root.AddComponent<Airport>(); airport.groundSize=0; root.SetActive(true);
                airport.aprons.Add(ApronArea.Rect(new Vector2(0,-20),new Vector2(300,300),0,PavementSurface.Concrete));
                var net=airport.Taxiways; int a=net.AddNode(new Vector3(0,0,-200)),b=net.AddNode(new Vector3(0,0,200));
                net.Connect(a,b,RunwayRules.TaxiwayWidthFor(type.SizeCode),PavementSurface.Concrete);
                var stand=net.AddStand(StandKind.DeIcing,Vector3.zero,0,(SizeCode)(type.SizeCode-'A'),PavementSurface.Concrete); var layout=net.Layout(stand);
                var park=layout.ParkPosition(type); plane.transform.position=new Vector3(park.x,0,park.y);
                var aircraft=plane.AddComponent<AirCity.Aircraft.Aircraft>(); aircraft.type=type;
                foreach(int side in new[]{-1,1})
                {
                    var target=DeicingWingCoverage.Create(type,side)[0];
                    var to=DeicingWingCoverage.WorkingPose(airport,aircraft,art.truckType,target);
                    Assert.IsTrue(DeicingMotion.StaticClear(airport,art.truckType,to),DeicingMotion.DescribeAircraftBlockage(airport,art.truckType,to));
                    var from=DeicingMotion.ParkPosition(layout,side<0?0:1,art.truckType);
                    var path=DeicingMotion.Plan(airport,art.truckType,from,to,aircraft);
                    Assert.NotNull(path,$"No approach from {from.position}/{from.facing} to {to.position}/{to.facing}; target {target}.");
                    Assert.Less(path.Length,130f,"Local approach made an airport-wide detour.");
                }
            }
            finally { Object.DestroyImmediate(plane); Object.DestroyImmediate(root); }
        }
        [TestCase("Cessna172")][TestCase("Turboprop")][TestCase("Narrowbody")][TestCase("Trijet")]
        [TestCase("Widebody")][TestCase("Quadjet")][TestCase("DoubleDeck")][TestCase("Supersonic")]
        public void Coverage_AllTargetsHaveAReachableMechanicalWorkPose(string model)
        {
            var type=AssetDatabase.LoadAssetAtPath<AircraftType>($"Assets/AirCity/Aircraft/{model}/{model}.aircraft");
            var art=Resources.Load<DeicingAssets>("DeicingAssets"); Assert.NotNull(type); Assert.NotNull(art); Assert.NotNull(art.truckType);
            var root=new GameObject("Deicing reach test"); var plane=new GameObject("Coverage aircraft");
            try
            {
                var airport=root.AddComponent<Airport>(); var aircraft=plane.AddComponent<AirCity.Aircraft.Aircraft>(); aircraft.type=type;
                foreach(int side in new[]{-1,1}) foreach(var target in DeicingWingCoverage.Create(type,side))
                {
                    var pose=DeicingWingCoverage.WorkingPose(airport,aircraft,art.truckType,target);
                    var solution=DeicingBoomKinematics.Solve(pose,target,art);
                    Assert.IsTrue(solution.Reachable,$"{model} {target} needs a {solution.jetDistance:0.00}m spray jet from {pose.position}.");
                    Assert.Greater(solution.cabin.y,0f);
                    Assert.IsTrue(DeicingMotion.StaticClear(airport,art.truckType,pose),$"{model} body at {pose.position}, facing {pose.facing}, target {target} overlaps {DeicingMotion.DescribeAircraftBlockage(airport,art.truckType,pose)}.");
                }
            }
            finally { Object.DestroyImmediate(plane); Object.DestroyImmediate(root); }
        }
        [Test] public void Coverage_SweepsRootToTipAcrossTheWholeChordWithOverlappingBands()
        {
            var right = DeicingWingCoverage.Create(aircraft,1); var left = DeicingWingCoverage.Create(aircraft,-1);
            Assert.AreEqual(DeicingWingCoverage.SpanStations(aircraft)*3,right.Count);
            Assert.AreEqual(right.Count,left.Count);
            Assert.AreEqual(2f,right[0].x,.001f); Assert.AreEqual(17f,right[right.Count-1].x,.001f);
            for (int station=0;station<right.Count/3;station++)
            {
                int index=station*3; Assert.AreEqual(right[index].x,right[index+2].x,.001f);
                if (station>0) Assert.LessOrEqual(right[index].x-right[index-3].x,DeicingWingCoverage.SwathSpacing+.001f);
                float t=Mathf.InverseLerp(2,17,right[index].x);
                float expectedLeading=Mathf.Lerp(5,-3,t),expectedTrailing=Mathf.Lerp(-3,-5,t);
                Assert.AreEqual(expectedLeading,station%2==0?right[index].z:right[index+2].z,.001f);
                Assert.AreEqual(expectedTrailing,station%2==0?right[index+2].z:right[index].z,.001f);
                Assert.AreEqual(Mathf.Lerp(4.4f,5.2f,t)+.035f,right[index+1].y,.001f);
            }
            for(int i=0;i<right.Count;i++) Assert.Less(Vector3.Distance(new Vector3(-right[i].x,right[i].y,right[i].z),left[i]),.001f);
        }
    }
}

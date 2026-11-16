using System.Collections.Generic;
using System.Reflection;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Vehicles
{
    public sealed class DeicingRoadTests
    {
        readonly List<GameObject> spawned = new();
        [TearDown] public void Cleanup()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            spawned.Clear();
        }

        (Airport airport, DevAirfieldTool tool, DeicingWorkArea area) Create(float heading, bool smallApron = false)
        {
            var root = new GameObject("Deicing road test"); root.SetActive(false); spawned.Add(root);
            var airport = root.AddComponent<Airport>(); airport.groundSize = 0; root.SetActive(true);
            var draft = new DeicingWorkArea { heading = heading };
            airport.aprons.Add(smallApron
                ? ApronArea.Rect(Vector2.zero, new Vector2(20.04f, 24.04f), 0, PavementSurface.Concrete)
                : ApronArea.Rect(Vector2.zero, new Vector2(100, 100), 0, PavementSurface.Concrete));
            var area = airport.Deicing.AddWorkArea(draft.position, draft.heading);
            Assert.That(area, Is.Not.Null, airport.Deicing.WorkAreaProblem(draft.position, draft.heading));
            var view = new GameObject("Deicing road camera"); view.SetActive(false); spawned.Add(view);
            var camera = view.AddComponent<Camera>(); camera.transform.position = new Vector3(0, 60, -35);
            camera.transform.LookAt(Vector3.zero);
            var tool = view.AddComponent<DevAirfieldTool>(); tool.airport = airport; tool.enabled = false;
            Field(tool, "cam", camera); view.SetActive(true); tool.SetTool(DevTool.Road);
            return (airport, tool, area);
        }

        [TestCase(0f)] [TestCase(37f)]
        public void ActualRoadSnapAndCommitConnectAllFourStationPorts(float heading)
        {
            var (airport, tool, area) = Create(heading);
            var net = airport.Taxiways;
            for (int i = 0; i < area.RoadPorts.Length; i++)
            {
                var port = area.RoadPorts[i];
                var outward = Vector2.Dot(port - area.position, area.Axis) > 0 ? area.Axis : -area.Axis;
                int start = net.AddNode(Ground(port + outward * 18)); Field(tool, "chainNode", start);
                var cursor = port + area.Right * .8f + outward * .25f;
                var snap = Invoke(tool, "SnapLine", net, cursor, true, false, airport.transform.TransformPoint(Ground(cursor)));
                var snappedPoint = (Vector2)snap.GetType().GetField("p").GetValue(snap);
                Assert.That(Vector2.Distance(snappedPoint, port), Is.LessThan(.001f), DeicingWorkArea.RoadPortName(i));
                StringAssert.Contains(DeicingWorkArea.RoadPortName(i), (string)snap.GetType().GetField("what").GetValue(snap));
                int node = (int)Invoke(tool, "Commit", net, snap);
                Assert.That(Vector2.Distance(net.At(node), port), Is.LessThan(.001f));
                net.Connect(start, node, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
                int count = net.nodes.Count;
                var again = Invoke(tool, "SnapLine", net, port, true, false, airport.transform.TransformPoint(Ground(port)));
                Assert.That((int)Invoke(tool, "Commit", net, again), Is.EqualTo(node), "An existing port node should be reused.");
                Assert.That(net.nodes.Count, Is.EqualTo(count));
            }
            Assert.That(net.edges.Count, Is.EqualTo(4));
            Assert.That(airport.Deicing.ParkingBays, Is.Empty, "Service-road connections must not create truck parking.");
        }

        [Test] public void StationSnapChoosesAPavedEndpointInsteadOfACloserUnpavedSlide()
        {
            var (airport, tool, area) = Create(0, smallApron: true);
            var net = airport.Taxiways;
            Connect(net, area.FillExit, area.FillExit + area.Axis * 20);
            Connect(net, area.FillEntry, area.FillEntry - area.Axis * 20);
            var cursor = area.position + area.Right * .6f + area.Axis * .35f;
            Assert.That(tool.TrySnapDeicingStationToRoad(cursor, area.heading, 1, false,
                out var snapped, out float angle, out var label, area), Is.True);
            Assert.That(Vector2.Distance(snapped, area.position), Is.LessThan(.001f),
                "Sliding the north port onto the road interior would put part of the station beyond its small paved apron.");
            Assert.That(angle, Is.EqualTo(area.heading).Within(.001f));
            Assert.That(label, Is.Not.Empty);
            Assert.That(airport.Deicing.WorkAreaProblem(snapped, angle, area), Is.Null);
        }

        [Test] public void StationSnapRefusesWhenEveryNearbyCandidateLeavesTheApron()
        {
            var (airport, tool, area) = Create(0, smallApron: true);
            Connect(airport.Taxiways, area.FillExit, area.FillExit + area.Axis * 20);
            var cursor = area.position + area.Right * .6f + area.Axis * .35f;
            Assert.That(tool.TrySnapDeicingStationToRoad(cursor, area.heading, 1, false,
                out _, out _, out _, area), Is.False);
        }

        [TestCase(0f)] [TestCase(37f)]
        public void RoadThroughTankIslandIsRejectedButFillLaneStaysConnectable(float heading)
        {
            var (_, tool, area) = Create(heading);
            string blocked = (string)Invoke(tool, "DeicingRoadProblem", area.Point(0, -25), area.Point(0, 25), RunwayRules.RoadWidth);
            StringAssert.Contains("tank", blocked);
            Assert.That(Invoke(tool, "DeicingRoadProblem", area.FillEntry - area.Axis * 20, area.FillExit, RunwayRules.RoadWidth), Is.Null);
            Assert.That(Invoke(tool, "DeicingRoadProblem", area.UnloadEntry + area.Axis * 20, area.UnloadExit, RunwayRules.RoadWidth), Is.Null);
        }

        [TestCase(0f)] [TestCase(37f)]
        public void ActualVehicleBodiesFitBothMarkedServiceLanesAndTheVisibleApron(float heading)
        {
            var assets = Resources.Load<DeicingAssets>("DeicingAssets");
            Assert.That(assets, Is.Not.Null); Assert.That(assets.truckType, Is.Not.Null); Assert.That(assets.tankerType, Is.Not.Null);
            var area = new DeicingWorkArea { position = new Vector2(8, -13), heading = heading };
            CheckBody(area, area.RefillPose(assets.truckType), assets.truckType, area.FillCentre, area.Axis);
            CheckBody(area, area.UnloadPose(assets.tankerType), assets.tankerType, area.UnloadCentre, -area.Axis);
            CheckInside(area, new BaggageVehiclePose(area.RecyclerPosition, area.RecyclerFacing), assets.recyclerType);
            CheckInside(area, new BaggageVehiclePose(area.TankPosition + area.Axis * 4.2f, -area.Axis), assets.tankType);
        }

        static void CheckBody(DeicingWorkArea area, BaggageVehiclePose pose, VehicleType type, Vector2 centre, Vector2 facing)
        {
            var bodyCentre = pose.Point(type.bounds.center);
            Assert.That(Vector2.Distance(bodyCentre, centre), Is.LessThan(.001f));
            CheckInside(area, pose, type);
            var right = new Vector2(facing.y, -facing.x);
            foreach (var point in Corners(pose, type))
            {
                Assert.That(Mathf.Abs(Vector2.Dot(point - centre, right)), Is.LessThan(2.25f));
                Assert.That(Mathf.Abs(Vector2.Dot(point - centre, facing)), Is.LessThan(6.5f));
            }
        }
        static void CheckInside(DeicingWorkArea area, BaggageVehiclePose pose, VehicleType type)
        {
            Assert.That(type, Is.Not.Null);
            foreach (var point in Corners(pose, type)) Assert.That(area.Contains(point), Is.True, type.name + " extends beyond the visible station apron.");
        }
        static IEnumerable<Vector2> Corners(BaggageVehiclePose pose, VehicleType type)
        {
            foreach (float x in new[] { type.bounds.min.x, type.bounds.max.x })
                foreach (float z in new[] { type.bounds.min.z, type.bounds.max.z }) yield return pose.Point(new Vector3(x, 0, z));
        }
        static void Connect(TaxiwayNetwork net, Vector2 a, Vector2 b)
        {
            int from = net.AddNode(Ground(a)), to = net.AddNode(Ground(b));
            net.Connect(from, to, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
        }
        static Vector3 Ground(Vector2 point) => new Vector3(point.x, 0, point.y);
        static void Field(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
}

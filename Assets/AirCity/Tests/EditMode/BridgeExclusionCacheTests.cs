using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AirCity.Tests.Airfield
{
    public sealed class BridgeExclusionCacheTests
    {
        readonly List<Object> spawned = new();
        [TearDown] public void Cleanup()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            spawned.Clear();
        }

        (Airport airport, Stand stand, JetBridge bridge, JetBridgeDefinition family, AircraftType type) Create()
        {
            var type = AssetDatabase.LoadAssetAtPath<AircraftType>("Assets/AirCity/Aircraft/Narrowbody/Narrowbody.aircraft");
            Assert.That(type, Is.Not.Null);
            var family = ScriptableObject.CreateInstance<JetBridgeDefinition>(); spawned.Add(family);
            var root = new GameObject("Bridge exclusion cache test"); root.SetActive(false); spawned.Add(root);
            var airport = root.AddComponent<Airport>(); airport.groundSize = 0;
            airport.aircraftTypes.Add(type); root.SetActive(true);
            var net = airport.Taxiways;
            int west = net.AddNode(new Vector3(-100, 0, 0)), east = net.AddNode(new Vector3(100, 0, 0));
            net.Connect(west, east, RunwayRules.TaxiwayWidthFor('C'), PavementSurface.Asphalt);
            var stand = net.AddStand(StandKind.Parking, Vector3.zero, 180, SizeCode.C, PavementSurface.Concrete);
            Assert.That(stand, Is.Not.Null); stand.noSmallAircraft = true;
            var pad = net.Layout(stand).Pad;
            float south = pad.Min(p => p.y), left = pad.Min(p => p.x), right = pad.Max(p => p.x);
            Assert.That(airport.AddTerminal(new[] { new Vector2(left - 40, south - 42), new Vector2(right + 40, south - 42),
                new Vector2(right + 40, south - 12), new Vector2(left - 40, south - 12) }), Is.Not.Null);
            Assert.That(airport.Bridges.SetBridges(stand, 1), Is.EqualTo(1));
            var bridge = stand.bridges.Single();
            // Preserve the suggested family's actual short/long geometry; this fixture owns its mutable asset.
            family.specification = JsonUtility.FromJson<BridgeSpecification>(JsonUtility.ToJson(bridge.Spec));
            bridge.definition = family; BridgeAssetRevision.Touch();
            airport.Bridges.Simulate(0);
            return (airport, stand, bridge, family, type);
        }

        [Test] public void UnchangedQueriesReuseTheirSnapshotWhileGameTimeAdvances()
        {
            var (airport, _, _, _, _) = Create();
            var snapshot = airport.Bridges.BaggageExclusions();
            Assert.That(snapshot, Is.Not.Empty);
            var original = snapshot.ToArray();
            for (int i = 0; i < 5; i++)
            {
                airport.Bridges.Simulate(1);
                Assert.That(airport.Bridges.BaggageExclusions(), Is.SameAs(snapshot),
                    "An unchanged parked gate must not rebuild all supported-type motion envelopes on every collision query.");
            }
            CollectionAssert.AreEqual(original, snapshot);
        }

        [Test] public void MovingTheStandAndEditingItsFamilyReplaceTheSnapshotAndUpdateItsGeometry()
        {
            var (airport, stand, bridge, family, _) = Create();
            var before = airport.Bridges.BaggageExclusions(); var drumBefore = before[0];
            stand.position += Vector3.right * 7;
            airport.Taxiways.Rebuild();
            var moved = airport.Bridges.BaggageExclusions();
            Assert.That(moved, Is.Not.SameAs(before));
            Assert.That(moved[0].center.x - drumBefore.center.x, Is.EqualTo(7).Within(.01f),
                "The collision envelope must follow the relocated stand.");
            Assert.That(airport.Bridges.BaggageExclusions(), Is.SameAs(moved));
            float width = moved[0].width;
            family.specification.rotundaRadius += 1;
            BridgeAssetRevision.Touch();
            var widened = airport.Bridges.BaggageExclusions();
            Assert.That(widened, Is.Not.SameAs(moved));
            Assert.That(widened[0].width, Is.GreaterThan(width + 1.9f),
                "A wider family asset must enlarge the physical drum exclusion without moving the airport layout.");
            AssertShapeInside(Layout(airport, bridge).Shape(airport.Bridges.PoseOf(Layout(airport, bridge))), widened);
            Assert.That(airport.Bridges.BaggageExclusions(), Is.SameAs(widened));
        }

        [Test] public void LiveDockingAndInterruptedRetractionStayInsideTheirCachedWholeMotionEnvelopes()
        {
            var (airport, stand, bridge, _, type) = Create();
            var layout = Layout(airport, bridge); var parked = airport.Bridges.PoseOf(layout);
            var plane = new GameObject("Parked cache-test aircraft"); plane.SetActive(false); spawned.Add(plane);
            var aircraft = plane.AddComponent<AirCity.Aircraft.Aircraft>(); aircraft.type = type;
            var position = layout.stand.ParkPosition(type);
            plane.transform.SetPositionAndRotation(new Vector3(position.x, 0, position.y),
                Quaternion.LookRotation(new Vector3(layout.stand.nose.x, 0, layout.stand.nose.y)));
            plane.SetActive(true);
            airport.Bridges.Simulate(.1f);
            airport.Bridges.Simulate(AirportBridges.SettleTime + .1f);
            Assert.That(airport.Bridges.StateOf(bridge), Is.EqualTo(BridgeState.Docking));
            var entry = airport.Bridges.ServicePlan(stand, type)[bridge];
            Assert.That(entry.Operates, Is.True);
            var docking = airport.Bridges.BaggageExclusions();
            AssertSweepInside(layout, parked, entry.docked, docking);
            airport.Bridges.Simulate(bridge.Spec.dockTime * .31f);
            var midway = airport.Bridges.PoseOf(layout);
            Assert.That(Vector2.Distance(midway.heading, parked.heading) + Mathf.Abs(midway.length - parked.length), Is.GreaterThan(.01f));
            Assert.That(airport.Bridges.BaggageExclusions(), Is.SameAs(docking),
                "Movement within the same journey must reuse its complete envelope.");
            AssertShapeInside(layout.Shape(midway), docking);
            airport.Bridges.Simulate(bridge.Spec.dockTime * .2f);
            var interrupted = airport.Bridges.PoseOf(layout);
            Assert.That(airport.Bridges.BaggageExclusions(), Is.SameAs(docking));
            plane.SetActive(false);
            airport.Bridges.Simulate(.05f);
            Assert.That(airport.Bridges.StateOf(bridge), Is.EqualTo(BridgeState.Retracting));
            var returning = airport.Bridges.BaggageExclusions();
            Assert.That(returning, Is.Not.SameAs(docking), "An interrupted move has a new actual departure pose.");
            AssertSweepInside(layout, interrupted, parked, returning);
            airport.Bridges.Simulate(bridge.Spec.dockTime * .15f);
            Assert.That(airport.Bridges.BaggageExclusions(), Is.SameAs(returning));
            AssertShapeInside(layout.Shape(airport.Bridges.PoseOf(layout)), returning);
        }

        static BridgeLayout Layout(Airport airport, JetBridge bridge) =>
            airport.Bridges.Layouts.SelectMany(entry => entry.bridges).Single(layout => layout.bridge == bridge);
        static void AssertSweepInside(BridgeLayout layout, BridgePose from, BridgePose to, IReadOnlyList<Rect> exclusions)
        {
            for (int step = 0; step <= 64; step++)
                AssertShapeInside(layout.Shape(BridgePose.Move(from, to, step / 64f)), exclusions);
        }
        static void AssertShapeInside(BridgeShape shape, IReadOnlyList<Rect> exclusions)
        {
            foreach (var part in shape.parts)
            {
                var actual = part.bounds;
                Assert.That(exclusions.Any(envelope => actual.xMin >= envelope.xMin - .002f && actual.xMax <= envelope.xMax + .002f &&
                    actual.yMin >= envelope.yMin - .002f && actual.yMax <= envelope.yMax + .002f), Is.True,
                    "A live bridge part extends outside every cached collision envelope: " + actual);
            }
        }
    }
}

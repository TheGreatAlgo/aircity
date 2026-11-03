using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.EditorTools.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Airfield
{
    public partial class TerminalBridgeTests
    {
        [Test]
        public void ServicePlan_ChoosesReachableDoorsWithoutMovingTheBridgeBases()
        {
            var (airport, stands, _) = TestAirport(SizeCode.E);
            var st = stands[0];
            Assert.AreEqual(2, airport.Bridges.SetBridges(st, 2));
            var positions = st.bridges.Select(b => b.rotunda).ToArray();
            // A saved label is a preference, not an aircraft-wide hard assignment.
            st.bridges[0].door = 6;
            airport.Taxiways.Rebuild();
            var plan = airport.Bridges.ServicePlan(st, Type("Widebody"));
            Assert.IsTrue(st.bridges.All(b => plan[b].Operates), Problems(airport, st));
            CollectionAssert.AreEquivalent(new[] { "L1", "L2" }, st.bridges.Select(b => plan[b].DoorName));
            CollectionAssert.AreEqual(positions, st.bridges.Select(b => b.rotunda));
        }

        [Test]
        public void Runtime_RefusesAReachableDockWhenTheFullClearanceCheckFails()
        {
            var (airport, stands, _) = TestAirport();
            var st = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(st, 1));
            var b = st.bridges[0];
            var duplicate = b.Clone();
            duplicate.id = System.Guid.NewGuid().ToString("N");
            st.bridges.Add(duplicate);
            airport.Taxiways.Rebuild();
            var l = BridgeLayout.Of(b, airport.Taxiways.Layout(st), airport.terminals);
            l.Dock(Type("Narrowbody"), out var geometric);
            Assert.AreEqual(BridgeProblem.None, geometric, "the door itself is reachable");
            Assert.IsFalse(airport.Bridges.ServicePlan(st, Type("Narrowbody"))[b].Operates);
            ParkOn(airport, st, "Narrowbody");
            RunUntil(airport, () => false, 100f);
            Assert.IsTrue(st.bridges.All(x => airport.Bridges.StateOf(x) == BridgeState.Parked));
        }

        [Test]
        public void AircraftConfiguration_MovesTheStopAndControlsStandEligibility()
        {
            var (airport, stands, _) = TestAirport(SizeCode.E);
            var st = stands[0];
            var type = Type("Narrowbody");
            var sl = airport.Taxiways.Layout(st);
            var was = sl.ParkPosition(type);
            var c = new StandServiceConfiguration { aircraft = type, stopOffset = -2f };
            st.serviceConfigurations.Add(c);
            airport.Taxiways.Rebuild();
            sl = airport.Taxiways.Layout(st);
            Assert.Less(Vector2.Distance(was - sl.axis * 2f, sl.ParkPosition(type)), 0.001f);
            Assert.AreEqual(StandProblem.None, airport.Taxiways.CheckStand(st, type));
            c.allowed = false;
            Assert.AreEqual(StandProblem.ServiceExcluded, airport.Taxiways.CheckStand(st, type));
            Assert.IsFalse(airport.Bridges.TypesFor(st).Contains(type));
            c.allowed = true; c.stopOffset = 200f;
            Assert.AreEqual(StandProblem.StopOutsideStand, airport.Taxiways.CheckStand(st, type));
        }

        [Test]
        public void AircraftConfiguration_LeavesAnUnusedBridgeParkedWhileTheOtherDocks()
        {
            var (airport, stands, _) = TestAirport(SizeCode.E);
            var st = stands[0];
            Assert.AreEqual(2, airport.Bridges.SetBridges(st, 2));
            var unused = st.bridges[0];
            st.serviceConfigurations.Add(new StandServiceConfiguration
            {
                aircraft = Type("Widebody"), unusedBridges = new List<string> { unused.id },
            });
            airport.Taxiways.Rebuild();
            var plan = airport.Bridges.ServicePlan(st, Type("Widebody"));
            Assert.AreEqual(BridgeProblem.NotInService, plan[unused].problem);
            Assert.IsTrue(plan[st.bridges[1]].Operates, Problems(airport, st));
            ParkOn(airport, st, "Widebody");
            RunUntil(airport, () => false, 100f);
            Assert.AreEqual(BridgeState.Parked, airport.Bridges.StateOf(unused));
            Assert.AreEqual(BridgeState.Docked, airport.Bridges.StateOf(st.bridges[1]));
        }

        [Test]
        public void BentCorridor_UsesEverySegmentForLengthSupportsAndClearances()
        {
            var stand = new StandLayout { stand = new Stand(), axis = Vector2.up, nose = Vector2.up };
            var b = new JetBridge { root = Vector2.zero, rotunda = new Vector2(20f, 30f) };
            b.linkBends.Add(new Vector3(0f, 4f, 20f));
            var l = BridgeLayout.Of(b, stand, null);
            Assert.AreEqual(2, l.LinkFootprints.Count);
            Assert.AreEqual(20f + Mathf.Sqrt(500f) - b.Spec.rotundaRadius, l.LinkLength, 0.001f);
            var columns = l.Columns(new List<Vector2[]>());
            Assert.IsTrue(columns.Any(p => Vector2.Distance(p, new Vector2(0f, 20f)) < 0.01f));
            var road = new[] { new Vector2(-4f, 18f), new Vector2(4f, 18f), new Vector2(4f, 22f), new Vector2(-4f, 22f) };
            Assert.IsNull(BridgeLayout.Of(b, stand, null).Columns(new[] { road }), "a supported bend cannot stand on a road");
        }

        [Test]
        public void TerminalConnections_UseTheirActualHeightAndRejectSteepDownhillCorridors()
        {
            var stand = new StandLayout { stand = new Stand(), axis = Vector2.up, nose = Vector2.up };
            var terminal = Terminal.Polygon(new[] { new Vector2(-10f, -10f), new Vector2(10f, -10f), new Vector2(10f, 0f), new Vector2(-10f, 0f) });
            terminal.SetStoreyHeight(0, 8f);
            var b = new JetBridge { root = Vector2.zero, rotunda = new Vector2(0f, 22.3f), rotundaFloor = 4f };
            var terminals = new[] { terminal };
            var l = BridgeLayout.Of(b, stand, terminals);
            Assert.AreEqual(8f, l.LinkStartFloor);
            Assert.AreEqual(0.2f, l.LinkSlope, 0.001f);
            Assert.AreEqual(BridgeProblem.LinkTooSteep, l.Placement(new BridgeSite { terminals = terminals }));
            b.rotundaFloor = 8f;
            Assert.AreEqual(0f, BridgeLayout.Of(b, stand, terminals).LinkSlope, 0.001f);
        }

        [Test]
        public void ImportedBridgeFamilies_ProvideTwoAndThreeSections_AndRigUsesTheSelectedFamily()
        {
            var definitions = VehicleSetup.Type("JetBridge").bridgeDefinitions;
            Assert.AreEqual(4, definitions.Length);
            Assert.IsTrue(definitions.All(d => d.specification.Valid));
            var two = definitions.Single(d => d.name == "Two-section long");
            Assert.AreEqual(2, two.specification.sections.Length);
            Assert.AreEqual(24.5f, two.specification.MaxTunnel, 0.001f);
            var (airport, stands, _) = TestAirport();
            var st = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(st, 1));
            st.bridges[0].definition = two;
            airport.Taxiways.Rebuild();
            var rig = airport.GetComponent<BridgeRig>();
            if (rig == null) rig = airport.gameObject.AddComponent<BridgeRig>();
            rig.prefab = VehicleSetup.Prefab("JetBridge"); rig.Refresh();
            var instance = rig.transform.Find("Bridges/Jet bridge");
            Assert.IsNotNull(instance);
            Assert.IsFalse(instance.Find("Tunnel_B").gameObject.activeSelf);
            Assert.IsTrue(instance.Find("Tunnel_A").gameObject.activeSelf);
            Assert.IsTrue(instance.Find("Tunnel_C").gameObject.activeSelf);
        }

        [Test]
        public void BranchHead_ConnectsThroughAnotherHead_AndSuppliesItsOwnBridges()
        {
            var st = new Stand();
            var sl = new StandLayout { stand = st, axis = Vector2.up, nose = Vector2.up };
            var terminal = Terminal.Polygon(new[] { new Vector2(-10f, -10f), new Vector2(10f, -10f), new Vector2(10f, 0f), new Vector2(-10f, 0f) });
            st.head = new BridgeHead { from = new Vector2(0f, 10f), to = new Vector2(0f, 30f), root = Vector2.zero };
            st.additionalHeads.Add(new BridgeHead { from = new Vector2(15f, 20f), to = new Vector2(15f, 40f), root = new Vector2(3.5f, 20f) });
            var b = new JetBridge { root = new Vector2(18.5f, 30f), rotunda = new Vector2(22f, 30f) };
            st.bridges.Add(b);
            var all = HeadLayout.All(sl, new[] { terminal });
            Assert.AreEqual(2, all.Count);
            Assert.IsTrue(all.All(h => h.Connected));
            Assert.AreSame(all[0], all[1].parent);
            var l = BridgeLayout.Of(b, sl, new[] { terminal });
            Assert.AreSame(st.additionalHeads[0], l.head.head);
            Assert.IsTrue(l.Connected);
        }

        [Test]
        public void GatePreset_CopiesGeometryAndConfigurationsWithoutSharingMutableLists()
        {
            var st = new Stand();
            st.bridges.Add(new JetBridge { rotunda = new Vector2(-15f, 90f) });
            st.bridges[0].linkBends.Add(new Vector3(-15f, 4f, 110f));
            st.serviceConfigurations.Add(new StandServiceConfiguration { aircraft = Type("Narrowbody"), stopOffset = -2f });
            var preset = ScriptableObject.CreateInstance<GateLayoutPreset>(); spawned.Add(preset);
            preset.Capture(st);
            preset.bridges[0].linkBends[0] = Vector3.zero;
            preset.aircraft[0].stopOffset = -5f;
            Assert.AreEqual(new Vector3(-15f, 4f, 110f), st.bridges[0].linkBends[0]);
            Assert.AreEqual(-2f, st.serviceConfigurations[0].stopOffset);
            var copy = st.At(Vector3.one);
            copy.bridges[0].linkBends.Clear();
            copy.serviceConfigurations[0].unusedBridges.Add(st.bridges[0].id);
            Assert.AreEqual(1, st.bridges[0].linkBends.Count);
            Assert.AreEqual(0, st.serviceConfigurations[0].unusedBridges.Count);
        }

        [Test]
        public void GatePreset_MirrorsThePhysicalGate_WithoutChangingTheSource()
        {
            var (airport, stands, _) = TestAirport();
            var st = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(st, 1));
            var at = st.bridges[0].rotunda;
            var preset = ScriptableObject.CreateInstance<GateLayoutPreset>(); spawned.Add(preset);
            preset.Capture(st);
            Assert.IsTrue(preset.Apply(airport, st, true, out var problem), BridgeLayout.Describe(problem));
            Assert.AreEqual(new Vector2(-at.x, at.y), st.bridges[0].rotunda);
            Assert.AreEqual(at, preset.bridges[0].rotunda);
            Assert.IsTrue(airport.Bridges.ServicePlan(st, Type("Narrowbody"))[st.bridges[0]].Operates);
        }

        [Test]
        public void RaisedRotunda_KeepsTheDrumUnstretched_AndAppearanceZonesAreImported()
        {
            var (airport, stands, terminal) = TestAirport();
            var st = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(st, 1));
            Assert.IsTrue(airport.ChangeTerminal(terminal, WithGroundStorey(terminal, 6.25f)));
            st.bridges[0].rotundaFloor = 6.25f;
            airport.Taxiways.Rebuild();
            var rig = airport.GetComponent<BridgeRig>();
            if (rig == null) rig = airport.gameObject.AddComponent<BridgeRig>();
            rig.prefab = VehicleSetup.Prefab("JetBridge"); rig.Refresh();
            var instance = rig.transform.Find("Bridges/Jet bridge");
            var drum = instance.Find("RotundaHigh");
            Assert.IsTrue(drum.gameObject.activeSelf);
            Assert.AreEqual(Vector3.one, drum.localScale);
            Assert.AreEqual(6.25f - JetBridgeRules.UpperFloor, drum.localPosition.y, 0.001f);
            Assert.IsTrue(instance.Find("RotundaHighColumn").gameObject.activeSelf);
            var mesh = drum.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsTrue(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color));
            Assert.AreEqual("AirCity/JetBridge", drum.GetComponent<Renderer>().sharedMaterial.shader.name);
        }

        static Terminal WithGroundStorey(Terminal terminal, float height)
        {
            var changed = terminal.Clone(); changed.SetStoreyHeight(0, height); changed.SetBoardingFloor(1);
            return changed;
        }

        [Test]
        public void ImportedBridgeMaterial_KeepsItsSurfaceMapsAndNeutralAppearance()
        {
            var mat = VehicleSetup.Prefab("JetBridge").GetComponentInChildren<Renderer>().sharedMaterial;
            Assert.AreEqual("AirCity/JetBridge", VehicleSetup.Type("JetBridge").bridgeShader.name);
            Assert.IsNotNull(mat.GetTexture("_BaseMap"), "the baked atlas must survive a fresh shader import");
            Assert.IsNotNull(mat.GetTexture("_MetallicGlossMap"));
            Assert.AreEqual(Color.white, mat.GetColor("_BaseColor"));
        }

        [Test]
        public void AutomaticParking_AvoidsTheBridgesOwnBentFixedCorridor()
        {
            var stand = new StandLayout { stand = new Stand(), axis = Vector2.up, nose = Vector2.up };
            var b = new JetBridge { root = new Vector2(0f, -20f), rotunda = Vector2.zero };
            b.linkBends.AddRange(new[] { new Vector3(25f, 4f, -20f), new Vector3(25f, 4f, 20f), new Vector3(0f, 4f, 20f) });
            var l = BridgeLayout.Of(b, stand, null);
            var pose = new BridgePose { heading = Vector2.right, length = 20f, floor = 4f, cabFloor = 4f };
            Assert.IsTrue(l.HitsOwnCorridor(pose));
            pose.heading = Vector2.left;
            Assert.IsFalse(l.HitsOwnCorridor(pose));
            Assert.IsTrue(l.Parked(new AirCity.Aircraft.AircraftType[0], out var parked));
            Assert.IsFalse(l.HitsOwnCorridor(parked));
        }

        [Test]
        public void BentCorridor_KeepsTheAppearanceOnEveryRenderedSegment()
        {
            var (airport, stands, _) = TestAirport();
            var st = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(st, 1));
            var b = st.bridges[0];
            b.linkBends.Add(new Vector3(b.root.x - 4f, 4f, (b.root.y + b.rotunda.y) * 0.5f));
            var appearance = ScriptableObject.CreateInstance<JetBridgeAppearance>(); spawned.Add(appearance);
            appearance.cladding = new Color(0.25f, 0.45f, 0.85f);
            b.appearance = appearance;
            airport.Taxiways.Rebuild();
            var rig = airport.GetComponent<BridgeRig>();
            if (rig == null) rig = airport.gameObject.AddComponent<BridgeRig>();
            rig.prefab = VehicleSetup.Prefab("JetBridge"); rig.Refresh();
            var links = rig.transform.Find("Bridges/Jet bridge").GetComponentsInChildren<Renderer>()
                .Where(r => r.name == "Link").ToArray();
            Assert.AreEqual(2, links.Length);
            var block = new MaterialPropertyBlock();
            foreach (var renderer in links)
            {
                renderer.GetPropertyBlock(block);
                Assert.Less(Vector4.Distance((Vector4)appearance.cladding, (Vector4)block.GetColor("_CladdingTint")), 1e-5f);
            }
        }
    }
}

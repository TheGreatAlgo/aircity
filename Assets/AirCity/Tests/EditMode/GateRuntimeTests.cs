using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Dev;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Airfield
{
    public partial class TerminalBridgeTests
    {
        [Test]
        public void GateWorkspace_ConfiguresAnUnplacedBridgeAndPlacesThatExactFamilyAndFinish()
        {
            var (airport, stands, _) = TestAirport(); var stand = stands[1];
            var family = ScriptableObject.CreateInstance<JetBridgeDefinition>(); spawned.Add(family);
            family.specification.sections = family.specification.sections.Take(2).ToArray();
            family.name = "Two sections";
            var camera = new GameObject("Gate camera"); spawned.Add(camera); camera.AddComponent<Camera>();
            var tool = camera.AddComponent<DevAirfieldTool>(); tool.airport = airport;
            airport.Taxiways.stands.Clear(); airport.Taxiways.Rebuild();
            using var designer = new DevGateDesigner(); designer.Open(airport, null);
            Assert.IsNull(designer.Stand);
            designer.BeginBridgePlacement(tool); designer.SetBridgeFamily(family);
            designer.SetBridgeGlazing(false); designer.SetBridgeTint(GlassTint.Blue);
            Assert.AreSame(family, designer.BridgeDraft.definition);
            Assert.AreEqual(0f, designer.BridgeDraft.appearance.glazingAmount);
            airport.Taxiways.stands.Add(stand); airport.Taxiways.Rebuild();
            designer.FollowPlacementStand(stand);
            Assert.AreSame(stand, designer.Stand, "the mock aircraft must follow the gate under the placement cursor");
            Assert.IsNotNull(designer.Aircraft);
            var sl = airport.Taxiways.Layout(stand);
            var suggestion = BridgeLayout.Suggest(sl, designer.BridgeDraft, new List<BridgeLayout>(), airport.Bridges.Site(stand));
            Assert.IsNotNull(suggestion);
            var layout = BridgeLayout.Of(suggestion, sl, airport.terminals);
            var preview = airport.Bridges.PreviewBridge(stand, layout.rotunda, layout.root, out var problem, out _, designer.BridgeDraft);
            Assert.IsNotNull(preview, BridgeLayout.Describe(problem));
            var bridge = airport.Bridges.AddBridge(stand, layout.rotunda, layout.root, out problem, out _, designer.BridgeDraft);
            Assert.IsNotNull(bridge, BridgeLayout.Describe(problem));
            Assert.AreSame(family, bridge.definition); Assert.AreEqual(2, bridge.Spec.sections.Length);
            Assert.AreEqual(FacadeStyles.Tint(GlassTint.Blue), bridge.appearance.glazing);
            Assert.AreNotEqual(designer.BridgeDraft.id, bridge.id);
            Assert.AreNotEqual(preview.id, bridge.id);
            var finish = bridge.appearance;
            designer.SetBridgeTint(GlassTint.Green);
            Assert.AreSame(finish, bridge.appearance);
            Assert.AreEqual(FacadeStyles.Tint(GlassTint.Blue), bridge.appearance.glazing, "changing the build palette must not repaint placed bridges");
        }

        [Test]
        public void GateWorkspace_PicksABridgeDirectlyAndDeletesOnlyThatBridge()
        {
            var (airport, stands, _) = TestAirport(); var stand = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(stand, 1));
            var bridge = stand.bridges[0];
            var layout = BridgeLayout.Of(bridge, airport.Taxiways.Layout(stand), airport.terminals);
            var rig = airport.gameObject.AddComponent<BridgeRig>();
            rig.prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AirCity/Vehicles/JetBridge/JetBridge.prefab"); rig.Refresh();
            var camera = new GameObject("Gate camera"); spawned.Add(camera); camera.AddComponent<Camera>();
            var tool = camera.AddComponent<DevAirfieldTool>(); tool.airport = airport;
            var toolbar = camera.AddComponent<DevToolbar>();
            var ray = new Ray(airport.transform.TransformPoint(new Vector3(layout.rotunda.x, 30f, layout.rotunda.y)), Vector3.down);
            Assert.IsTrue(tool.TrySelectBridge(ray), "no prior stand selection should be needed");
            Assert.AreSame(stand, tool.SelectedStand); Assert.AreSame(bridge, tool.SelectedBridge);
            Assert.IsTrue(toolbar.GateDesigner.IsOpen); Assert.IsFalse(toolbar.GateDesigner.NewBridgeMode);
            toolbar.GateDesigner.SetBridgeTint(GlassTint.Green);
            Assert.AreEqual(FacadeStyles.Tint(GlassTint.Green), bridge.appearance.glazing);
            toolbar.GateDesigner.DeleteCurrent();
            Assert.IsEmpty(stand.bridges); Assert.IsTrue(airport.Taxiways.stands.Contains(stand));
        }

        [Test]
        public void PlacedStand_KeepsItsSizeAndExit()
        {
            var (airport, stands, _) = TestAirport(); var stand = stands[1];
            var camera = new GameObject("Gate camera"); spawned.Add(camera); camera.AddComponent<Camera>();
            var tool = camera.AddComponent<DevAirfieldTool>(); tool.airport = airport;
            var (code, exit) = (stand.code, stand.exit);
            tool.SelectStand(stand);
            Assert.IsTrue(tool.StandFixed);
            tool.SetStandCode(SizeCode.A); tool.StepWidth(1); tool.SetStandExit(StandExit.TaxiOut);
            Assert.AreEqual(code, stand.code, "a placed stand keeps its size");
            Assert.AreEqual(exit, stand.exit, "and its push-back / taxi-out");
            Assert.AreEqual(code, tool.StandCode); Assert.AreEqual(exit, tool.StandExit);
            tool.SetTool(DevTool.Stand);
            Assert.IsFalse(tool.StandFixed);
            tool.SetStandCode(SizeCode.B); tool.SetStandExit(StandExit.TaxiOut);
            Assert.AreEqual(SizeCode.B, tool.StandCode, "new stands still take a size");
            Assert.AreEqual(StandExit.TaxiOut, tool.StandExit);
            Assert.AreEqual(code, stand.code); Assert.AreEqual(exit, stand.exit);
        }

        [Test]
        public void BridgePlacementPreview_ChecksReachBeforeClickAndDoesNotChangeTheLayout()
        {
            var (airport, stands, _) = TestAirport();
            var stand = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(stand, 1));
            var sl = airport.Taxiways.Layout(stand);
            var suggested = BridgeLayout.Of(stand.bridges[0], sl, airport.terminals);
            var at = suggested.rotunda; var root = suggested.root;
            airport.Bridges.RemoveBridge(stand, stand.bridges[0]);
            int version = airport.LayoutVersion;
            string before = JsonUtility.ToJson(stand);
            var preview = airport.Bridges.PreviewBridge(stand, at, root, out var problem, out _);
            Assert.IsNotNull(preview, BridgeLayout.Describe(problem));
            Assert.AreEqual(BridgeProblem.None, problem);
            Assert.AreEqual(version, airport.LayoutVersion);
            Assert.AreEqual(before, JsonUtility.ToJson(stand));
            var placed = airport.Bridges.AddBridge(stand, at, root, out problem, out _);
            Assert.IsNotNull(placed);
            Assert.AreEqual(preview.DoorName, placed.DoorName);
            Assert.AreEqual(preview.tunnel, placed.tunnel);
            Assert.AreEqual(preview.rotunda, placed.rotunda);
            Assert.AreEqual(preview.root, placed.root);

            airport.Bridges.RemoveBridge(stand, placed);
            airport.Taxiways.stands.Remove(stands[0]); airport.Taxiways.stands.Remove(stands[2]);
            airport.Taxiways.Rebuild();
            at -= sl.Right * 45f; root -= sl.Right * 45f;
            // There is space for equipment here, but no tunnel can reach the aircraft: the old green preview.
            Assert.AreEqual(BridgeProblem.None, airport.Bridges.WhereProblem(stand, at, root, new JetBridge()));
            version = airport.LayoutVersion;
            Assert.IsNull(airport.Bridges.PreviewBridge(stand, at, root, out var why, out _));
            Assert.AreNotEqual(BridgeProblem.None, why);
            Assert.AreEqual(version, airport.LayoutVersion);
            Assert.IsNull(airport.Bridges.AddBridge(stand, at, root, out problem, out _));
            Assert.AreEqual(why, problem);
            Assert.IsEmpty(stand.bridges);
        }

        [Test]
        public void BridgePlacementModel_UsesTheDockedPoseWithoutRegisteringOrSavingEquipment()
        {
            var (airport, stands, _) = TestAirport(); var stand = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(stand, 1));
            var original = BridgeLayout.Of(stand.bridges[0], airport.Taxiways.Layout(stand), airport.terminals);
            airport.Bridges.RemoveBridge(stand, stand.bridges[0]);
            var bridge = airport.Bridges.PreviewBridge(stand, original.rotunda, original.root, out var problem, out _);
            Assert.IsNotNull(bridge, BridgeLayout.Describe(problem));
            var layout = BridgeLayout.Of(bridge, airport.Taxiways.Layout(stand), airport.terminals);
            var pose = layout.Dock(Type("Narrowbody"), out problem);
            Assert.AreEqual(BridgeProblem.None, problem);
            var rig = airport.gameObject.AddComponent<BridgeRig>();
            rig.prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AirCity/Vehicles/JetBridge/JetBridge.prefab");
            Assert.IsNotNull(rig.prefab);
            int version = airport.LayoutVersion;
            rig.ShowPlacement(layout, pose, true);
            var model = rig.PlacementPreviewObject;
            Assert.IsNotNull(model);
            Assert.IsTrue(model.GetComponentsInChildren<Renderer>(true).Any(r => r.gameObject.activeInHierarchy));
            Assert.IsTrue(model.GetComponentsInChildren<Collider>(true).All(c => !c.enabled));
            Assert.IsTrue(model.GetComponentsInChildren<MonoBehaviour>(true).All(b => !b.enabled));
            Assert.IsTrue((model.hideFlags & HideFlags.DontSave) != 0);
            rig.Refresh();
            Assert.AreSame(model, rig.PlacementPreviewObject, "regular rig updates must keep the placement model");
            rig.ShowPlacement(layout, pose, false);
            Assert.AreSame(model, rig.PlacementPreviewObject);
            Assert.AreEqual(version, airport.LayoutVersion); Assert.IsEmpty(stand.bridges);
            rig.ClearPlacement(); Assert.IsNull(rig.PlacementPreviewObject);
            Assert.IsTrue(model == null);
        }

        [Test]
        public void RuntimePresets_ReloadAgainstNewAssetInstancesAndKeepAuthoredData()
        {
            string directory = Path.Combine(Path.GetTempPath(), "aircity-gates-" + Guid.NewGuid().ToString("N"));
            var family = ScriptableObject.CreateInstance<JetBridgeDefinition>(); spawned.Add(family); family.name = "Test family";
            var style = ScriptableObject.CreateInstance<JetBridgeAppearance>(); spawned.Add(style); style.name = "Test branding";
            style.cladding = Color.red; style.roof = Color.blue; style.glazingAmount = 0.2f;
            var type = Type("Narrowbody");
            var stand = new Stand();
            stand.head.appearance = style;
            stand.head.exterior.Override(0).lean = 5f;
            stand.head.exterior.Override(0).cladding = Color.green;
            stand.additionalHeads.Add(new BridgeHead { from = Vector2.one, to = new Vector2(20f, 1f), appearance = style });
            FacadeStyles.Apply(stand.additionalHeads[0].Exterior, FacadeStyle.Colonnade);
            FacadeStyles.SetTint(stand.additionalHeads[0].Exterior, GlassTint.Green);
            stand.additionalHeads[0].Exterior.roofShape.form = RoofForm.Scalloped;
            stand.additionalHeads[0].Exterior.roofShape.rise = 2.5f;
            stand.additionalHeads[0].Exterior.roofShape.across = true;
            stand.additionalHeads[0].Exterior.columnGap = 1.5f;
            stand.additionalHeads[0].Exterior.roofShape.overhang = 3f;
            stand.bridges.Add(new JetBridge { definition = family, appearance = style, rotunda = new Vector2(5f, 30f),
                linkBends = new List<Vector3> { new Vector3(2f, 4f, 10f) }, supportColumns = new List<Vector2> { Vector2.one } });
            stand.serviceConfigurations.Add(new StandServiceConfiguration { aircraft = type, stopOffset = -2f,
                unusedBridges = new List<string> { stand.bridges[0].id } });
            try
            {
                GatePresetStore.Entry entry;
                using (var store = new GatePresetStore(directory, new[] { family }, new[] { style }, new[] { type }))
                    entry = store.Save("My gate", stand);
                var nextFamily = Object.Instantiate(family); spawned.Add(nextFamily); nextFamily.name = family.name;
                var nextStyle = Object.Instantiate(style); spawned.Add(nextStyle); nextStyle.name = style.name;
                var nextType = Object.Instantiate(type); spawned.Add(nextType);
                using (var store = new GatePresetStore(directory, new[] { nextFamily }, new[] { nextStyle }, new[] { nextType }))
                {
                    Assert.AreEqual(entry.id, store.List().Single().id);
                    var loaded = store.Load(entry.id);
                    Assert.AreSame(nextFamily, loaded.bridges[0].definition);
                    Assert.AreSame(nextType, loaded.aircraft[0].aircraft);
                    Assert.AreNotSame(style, loaded.bridges[0].appearance);
                    Assert.AreNotSame(nextStyle, loaded.bridges[0].appearance);
                    Assert.AreEqual(Color.red, loaded.bridges[0].appearance.cladding);
                    Assert.AreEqual(Color.blue, loaded.additionalHeads[0].appearance.roof);
                    Assert.AreEqual(0.2f, loaded.head.appearance.glazingAmount);
                    Assert.AreEqual(HeadFoundation.Building, loaded.head.foundation);
                    Assert.AreEqual(5f, loaded.head.Exterior.ForFace(0).lean);
                    Assert.AreEqual(Color.green, loaded.head.Exterior.ForFace(0).cladding);
                    Assert.IsTrue(loaded.additionalHeads[0].Exterior.usesStyle);
                    Assert.AreEqual(FacadeStyle.Colonnade, loaded.additionalHeads[0].Exterior.style);
                    Assert.AreEqual(FacadeStyles.Tint(GlassTint.Green), loaded.additionalHeads[0].Exterior.facade.glassTint);
                    Assert.AreEqual(RoofForm.Scalloped, loaded.additionalHeads[0].Exterior.roofShape.form);
                    Assert.AreEqual(2.5f, loaded.additionalHeads[0].Exterior.roofShape.rise);
                    Assert.IsTrue(loaded.additionalHeads[0].Exterior.roofShape.across);
                    Assert.AreEqual(1.5f, loaded.additionalHeads[0].Exterior.columnGap);
                    Assert.AreEqual(3f, loaded.additionalHeads[0].Exterior.roofShape.overhang);
                    Assert.AreEqual(-2f, loaded.aircraft[0].stopOffset);
                    CollectionAssert.AreEqual(stand.serviceConfigurations[0].unusedBridges, loaded.aircraft[0].unusedBridges);
                    CollectionAssert.AreEqual(stand.bridges[0].linkBends, loaded.bridges[0].linkBends);
                    loaded.bridges[0].linkBends.Clear(); loaded.bridges[0].appearance.cladding = Color.green;
                    Assert.AreEqual(1, stand.bridges[0].linkBends.Count);
                    Assert.AreEqual(Color.red, style.cladding);
                    Assert.AreSame(family, stand.bridges[0].definition, "saving must not strip references from the source");
                }
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public void RuntimePresets_RejectUnresolvedAssetsAndInvalidFiles()
        {
            string directory = Path.Combine(Path.GetTempPath(), "aircity-gates-" + Guid.NewGuid().ToString("N"));
            var stand = new Stand();
            stand.serviceConfigurations.Add(new StandServiceConfiguration { aircraft = Type("Narrowbody") });
            try
            {
                using var store = new GatePresetStore(directory, Array.Empty<JetBridgeDefinition>(), Array.Empty<JetBridgeAppearance>(), Array.Empty<AircraftType>());
                var entry = store.Save("Needs an aircraft", stand);
                Assert.Throws<InvalidDataException>(() => store.Load(entry.id));
                Assert.Throws<ArgumentException>(() => store.Load("../somewhere-else"));
                File.WriteAllText(Path.Combine(directory, entry.id + ".json"), "{\"version\":2,\"name\":\"New format\"}");
                Assert.AreEqual(0, store.List().Count);
                Assert.Throws<InvalidDataException>(() => store.Load(entry.id));
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public void RuntimeAircraftPreview_DoesNotRegisterOrPickAnAircraftAndFollowsItsStop()
        {
            var (airport, stands, _) = TestAirport();
            var stand = stands[1]; var type = Type("Narrowbody");
            var source = new GameObject("Preview template"); spawned.Add(source); source.SetActive(false);
            var aircraft = source.AddComponent<AirCity.Aircraft.Aircraft>(); aircraft.type = type;
            var collider = source.AddComponent<BoxCollider>(); var renderer = source.AddComponent<MeshRenderer>();
            var parking = airport.Taxiways.Layout(stand).ParkPosition(type);
            source.transform.position = airport.transform.TransformPoint(new Vector3(parking.x, 0f, parking.y));
            source.SetActive(true);
            int count = AirCity.Aircraft.Aircraft.All.Count;
            using var preview = new GateAircraftPreview();
            Assert.IsTrue(preview.Show(airport, stand, type, source, 1f));
            var root = preview.PreviewObject;
            Assert.AreEqual(count, AirCity.Aircraft.Aircraft.All.Count);
            Assert.IsTrue(root.GetComponentsInChildren<MonoBehaviour>(true).All(x => !x.enabled));
            Assert.IsTrue(root.GetComponentsInChildren<Collider>(true).All(x => !x.enabled));
            Assert.IsTrue(aircraft.enabled); Assert.IsTrue(collider.enabled);
            Assert.IsTrue(renderer.forceRenderingOff, "the live parked aircraft must not overlap the preview");
            Assert.IsFalse(root.GetComponentInChildren<Renderer>(true).forceRenderingOff);
            var previewBlock = new MaterialPropertyBlock(); root.GetComponentInChildren<Renderer>(true).GetPropertyBlock(previewBlock);
            var tint = previewBlock.GetColor("_BaseColor");
            Assert.Less(Vector4.Distance(GateAircraftPreview.Tint, tint), .00001f);
            renderer.GetPropertyBlock(previewBlock);
            Assert.AreNotEqual(GateAircraftPreview.Tint, previewBlock.GetColor("_BaseColor"), "preview tint must never recolour the live aircraft");
            var original = root.transform.localPosition;
            stand.serviceConfigurations.Add(new StandServiceConfiguration { aircraft = type, stopOffset = -2f });
            airport.Taxiways.Rebuild();
            Assert.IsTrue(preview.Show(airport, stand, type, source, 0f));
            Assert.AreSame(root, preview.PreviewObject);
            Assert.AreEqual(2f, Vector3.Distance(original, root.transform.localPosition), 0.001f);
            preview.Dispose();
            Assert.IsTrue(root == null);
            Assert.IsFalse(renderer.forceRenderingOff, "closing must restore the live model");
            Assert.AreEqual(count, AirCity.Aircraft.Aircraft.All.Count);
        }

        [Test]
        public void RuntimeGateDesigner_RestoresAirportSpeedAndLoadsSavedLayoutsInANewSession()
        {
            string directory = Path.Combine(Path.GetTempPath(), "aircity-gates-" + Guid.NewGuid().ToString("N"));
            var (airport, stands, _) = TestAirport(); var stand = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(stand, 1));
            var basePosition = stand.bridges[0].rotunda;
            airport.timeScale = 5f;
            try
            {
                GatePresetStore.Entry entry;
                using (var designer = new DevGateDesigner(directory))
                {
                    designer.Open(airport, stand);
                    Assert.IsTrue(designer.PreviewEnabled, "opening the designer must show an aircraft immediately");
                    Assert.IsNotNull(designer.Aircraft);
                    Assert.AreSame(stand, designer.Stand); Assert.AreEqual(0f, airport.timeScale);
                    designer.Edit(st => st.serviceConfigurations.Add(new StandServiceConfiguration { aircraft = Type("Narrowbody"), stopOffset = -1f }));
                    entry = designer.SavePreset("Persistent gate");
                    designer.Close(); Assert.AreEqual(5f, airport.timeScale);
                }
                stand.bridges.Clear(); stand.serviceConfigurations.Clear(); airport.Taxiways.Rebuild();
                using (var designer = new DevGateDesigner(directory))
                {
                    designer.Open(airport, stand);
                    Assert.IsTrue(designer.ApplyPreset(entry.id, false), designer.Message);
                    Assert.AreEqual(basePosition, stand.bridges.Single().rotunda);
                    Assert.AreEqual(-1f, stand.ServiceFor(Type("Narrowbody")).stopOffset);
                    designer.SetPaused(false); Assert.AreEqual(5f, airport.timeScale);
                    designer.SetPaused(true); airport.timeScale = 20f;
                }
                Assert.AreEqual(20f, airport.timeScale, "closing must respect a speed changed by another control");
                Assert.IsFalse(DevGui.GateDesignerOpen); Assert.IsFalse(DevGui.KeyboardCaptured);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}

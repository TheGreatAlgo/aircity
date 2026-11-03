using System.Linq;
using AirCity.Airfield;
using AirCity.Dev;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Airfield
{
    public partial class TerminalBridgeTests
    {
        [Test]
        public void CorridorHandles_EditInStandSpace_RejectInvalidRoutes_AndCancelTheWholeGesture()
        {
            var (airport, stands, _) = TestAirport(); var stand = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(stand, 1)); var bridge = stand.bridges[0];
            var root = bridge.root; var rotunda = bridge.rotunda;
            using var handles = new GateLayoutHandles();
            handles.Bind(airport, stand, bridge, null); handles.Refresh();
            var insert = handles.Nodes.First(n => n.kind == GateLayoutHandles.Kind.Insert);
            Assert.IsTrue(handles.BeginEdit(insert), handles.Problem);
            Assert.IsTrue(handles.IsDragging); Assert.IsTrue(DevGui.WorldHandleCaptured);
            Assert.AreEqual(1, bridge.linkBends.Count);
            handles.Refresh(); var corner = handles.Nodes.First(n => n.kind == GateLayoutHandles.Kind.Bend);
            var next = corner.point + new Vector3(0.25f, 0f, 0f);
            Assert.IsTrue(handles.Move(corner, next), handles.Problem);
            Assert.Less(Vector3.Distance(next, bridge.linkBends[0]), 0.001f);
            var intoTerminal = new Vector3(root.x, 4f, root.y + 15f);
            Assert.IsFalse(handles.Move(corner, intoTerminal));
            Assert.Less(Vector3.Distance(next, bridge.linkBends[0]), 0.001f, "refused changes retain the last valid route");
            handles.Cancel();
            Assert.AreEqual(0, bridge.linkBends.Count, "Escape also removes a corner inserted by this gesture");
            Assert.AreEqual(root, bridge.root); Assert.AreEqual(rotunda, bridge.rotunda);
            Assert.IsFalse(DevGui.WorldHandleCaptured);
        }

        [Test]
        public void HeightHandles_RestoreTheAutomaticFamilyHeight()
        {
            var (airport, stands, terminal) = TestAirport(); var stand = stands[1];
            // Leave room for a shallow ramp. Auto-placement can otherwise put the rotunda against the wall,
            // where even a small height difference must be refused.
            var farther = terminal.Clone(); farther.corners = farther.corners.ConvertAll(p => p + Vector2.down * 20f);
            Assert.IsTrue(airport.ChangeTerminal(terminal, farther));
            Assert.AreEqual(1, airport.Bridges.SetBridges(stand, 1)); var bridge = stand.bridges[0];
            Assert.Greater(BridgeLayout.Of(bridge, airport.Taxiways.Layout(stand), airport.terminals).LinkLength, 2f);
            using var handles = new GateLayoutHandles(); handles.Bind(airport, stand, bridge, null); handles.Refresh();
            var node = handles.Nodes.First(n => n.kind == GateLayoutHandles.Kind.Base);
            float original = bridge.rotundaFloor;
            Assert.IsTrue(handles.BeginEdit(node));
            var raised = node.point + Vector3.up * 0.1f;
            Assert.IsTrue(handles.Move(node, raised, true), handles.Problem);
            Assert.AreEqual(raised.y, bridge.Floor, 0.001f);
            Assert.IsFalse(handles.Move(node, new Vector3(node.point.x, float.NaN, node.point.z), true));
            handles.Cancel(); Assert.AreEqual(original, bridge.rotundaFloor, "zero must remain the automatic family height");
            Assert.AreEqual(node.point.y, bridge.Floor, 0.001f);
        }

        [Test]
        public void HeightHandles_RefuseAConnectorSteeperThanTheFamilyAllows()
        {
            var (airport, stands, _) = TestAirport(); var stand = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(stand, 1)); var bridge = stand.bridges[0];
            using var handles = new GateLayoutHandles(); handles.Bind(airport, stand, bridge, null); handles.Refresh();
            var node = handles.Nodes.First(n => n.kind == GateLayoutHandles.Kind.Base);
            float length = BridgeLayout.Of(bridge, airport.Taxiways.Layout(stand), airport.terminals).LinkLength;
            Assert.IsFalse(handles.Move(node, node.point + Vector3.up * (length * bridge.Spec.maxSlope + 0.1f), true));
            Assert.AreEqual(0f, bridge.rotundaFloor); StringAssert.Contains("steep", handles.Problem);
        }

        [Test]
        public void ConcourseHeightHandle_CarriesAttachedBridges_AndCancelsTheirChanges()
        {
            var (airport, stands, _) = TestAirport(SizeCode.F); var stand = stands[0];
            Assert.AreEqual(3, airport.Bridges.SetBridges(stand, 3));
            var heights = stand.bridges.Select(b => b.Floor).ToArray();
            var saved = stand.bridges.Select(b => b.rotundaFloor).ToArray();
            using var handles = new GateLayoutHandles(); handles.Bind(airport, stand, null, stand.head); handles.Refresh();
            var node = handles.Nodes.First(n => n.kind == GateLayoutHandles.Kind.Base);
            Assert.IsTrue(handles.BeginEdit(node));
            Assert.IsTrue(handles.Move(node, node.point + Vector3.up * 0.1f, true), handles.Problem);
            for (int i = 0; i < stand.bridges.Count; i++) Assert.AreEqual(heights[i] + 0.1f, stand.bridges[i].Floor, 0.001f);
            handles.Cancel();
            CollectionAssert.AreEqual(saved, stand.bridges.Select(b => b.rotundaFloor));
            Assert.AreEqual(0f, stand.head.floor);
        }

        [Test]
        public void RemovedWorldCorner_ReturnsToAnAutomaticStraightCorridor()
        {
            var (airport, stands, _) = TestAirport(); var stand = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(stand, 1)); var bridge = stand.bridges[0];
            using var handles = new GateLayoutHandles(); handles.Bind(airport, stand, bridge, null); handles.Refresh();
            Assert.IsTrue(handles.InsertCorner(0), handles.Problem);
            Assert.IsTrue(handles.RemoveCorner(0), handles.Problem);
            Assert.AreEqual(0, bridge.linkBends.Count); Assert.AreEqual(0, bridge.supportColumns.Count);
            handles.Refresh(); Assert.AreEqual(1, handles.Nodes.Count(n => n.kind == GateLayoutHandles.Kind.Insert));
            Assert.IsFalse(handles.RemoveCorner(0));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Dev;
using AirCity.EditorTools.Game;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Game
{
    public sealed class RoadMountainToolTests
    {
        [TestCase(DevTool.Street)]
        [TestCase(DevTool.Road)]
        public void YosemiteValleyFloorIntoTheSouthWall(DevTool tool)
        {
            BuildAndResume(RegionPreset.Mountains, 873241, tool, new Vector2(1640, 40),
                new Vector2(1455, -35), new Vector2(1170, -730), false);
        }

        [TestCase(DevTool.Street)]
        [TestCase(DevTool.Road)]
        public void YosemiteRidgeAcrossARavineIntoTheOppositeWall(DevTool tool)
        {
            BuildAndResume(RegionPreset.Mountains, 42, tool, new Vector2(430, 1770),
                new Vector2(235, 1850), new Vector2(-595, 1850), true, new Vector2(-162.5f, 1887.5f));
        }

        [TestCase(-100, -1640, -100, -1840, -470, -2735)]
        [TestCase(-1590, -480, -1590, -690, -1005, -1275)]
        public void MoabServiceRoadAcrossAVariableCanyonSide(int ax, int az, int bx, int bz, int cx, int cz)
        {
            BuildAndResume(RegionPreset.Desert, 692835, DevTool.Road, new Vector2(ax, az),
                new Vector2(bx, bz), new Vector2(cx, cz), true);
        }

        static void BuildAndResume(RegionPreset preset, int seed, DevTool tool, Vector2 foot, Vector2 cliff, Vector2 far, bool ravine,
            Vector2? adjacentRock = null)
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects().ToHashSet();
            try
            {
                var state = CareerState.New(seed, preset: preset);
                Assert.IsTrue(state.StartRegion(out string why), why);
                state.mode = GameMode.Sandbox;
                foreach (CareerTool unlock in Enum.GetValues(typeof(CareerTool))) state.layout.Unlock(unlock);
                var game = NewGameSetup.Build();
                game.Initialize(state, false);
                game.Paused = true;
                using var drive = new RoadToolDriver(game);
                drive.tool.SetTool(tool);
                var world = state.world;
                var terrain = game.valley;
                var approachCamera = Geo.X0Z((foot - cliff).normalized * 230f, 460f);
                var fromGround = Geo.X0Z(foot, terrain.DisplayHeight(foot));
                var cliffSurface = Geo.X0Z(cliff, terrain.DisplayHeight(cliff));
                float face = cliffSurface.y - fromGround.y;
                float gradeLimit = tool == DevTool.Street ? PublicRoadConstruction.MaximumGroundGrade : AirsideTerrainPlan.MaximumRoadGrade;
                Assert.That(face, Is.GreaterThan(60f));
                Assert.That(face / Vector2.Distance(foot, cliff), Is.GreaterThan(gradeLimit));

                var net = game.Airport.Taxiways;
                bool street = tool == DevTool.Street;
                Vector3 Node(int id) => street ? drive.tool.Streets.network.Node(id).position
                    : game.Airport.transform.TransformPoint(net.Node(id).position);
                IEnumerable<(int id, RoadStructure structure, Alignment3D path)> Sections() => street
                    ? drive.tool.Streets.network.segments.Select(edge => (edge.id, edge.structure, drive.tool.Streets.network.Path(edge)))
                    : net.edges.Where(edge => edge.IsRoad).Select(edge => (edge.id, edge.structure, net.Path(edge)));
                void Aim(Vector3 point, Vector3 offset) => drive.GestureFrame(point, false, centre: true, cameraOffset: offset);
                void Click(Vector3 point)
                {
                    drive.GestureFrame(point, true);
                    drive.GestureFrame(point, false);
                }

                Aim(fromGround, approachCamera);
                Click(fromGround);
                int entry = drive.Chain;
                Assert.That(entry, Is.GreaterThanOrEqualTo(0));
                float floor = Node(entry).y;
                Aim(cliffSurface, approachCamera);
                Assert.IsNull(drive.Problem, drive.Describe());
                Assert.That(drive.Endpoint.y, Is.EqualTo(floor).Within(.04f), "The face should be approached at the working height.");
                Click(cliffSurface);
                int tip = drive.Chain;
                Assert.AreNotEqual(entry, tip, drive.Describe());
                var tunnelEnd = Node(tip);
                var old = Sections().Select(edge => edge.id).ToHashSet();
                Assert.That(Vector2.Distance(Geo.XZ(tunnelEnd), cliff), Is.LessThan(2f));
                Assert.That(world.Height(Geo.XZ(tunnelEnd)) - tunnelEnd.y, Is.GreaterThan(55f));
                Assert.That(Sections().Any(edge => edge.structure == RoadStructure.Tunnel));
                drive.tool.SetTool(DevTool.Select);
                drive.tool.SetTool(tool);
                drive.tool.SetRoadUnderground(true);

                // Reacquire the buried end from above the other side of the wall.
                var farSideCamera = Geo.X0Z((far - cliff).normalized * 370f, 580f);
                var cap = tunnelEnd + Vector3.up * .3f;
                Aim(cap, Geo.X0Z((cliff - foot).normalized * 430f, 110f));
                Assert.IsNotNull(drive.Snap, "An oblique view still needs to pick the tunnel through the slope.");
                Assert.That(drive.Endpoint.y, Is.EqualTo(floor).Within(.04f), drive.Describe());
                Aim(cap, farSideCamera);
                Assert.That(drive.Endpoint.y, Is.EqualTo(floor).Within(.04f), drive.Describe());
                Click(cap);
                Assert.AreEqual(tip, drive.Chain, "Clicking the tunnel outline must reuse the original node.");

                // The game treats the curved preview as a complete route, including both banks of any ravine.
                var target = Geo.X0Z(far, floor);
                var across = (far - Geo.XZ(tunnelEnd)).normalized;
                var dragged = target + Geo.X0Z(new Vector2(across.y, -across.x) * 18f);
                float rockBefore = adjacentRock.HasValue ? world.Height(adjacentRock.Value) : 0f;
                Aim(target, farSideCamera);
                drive.GestureFrame(target, true);
                drive.GestureFrame(dragged, true);
                Assert.IsNull(drive.Problem, drive.Describe());
                Assert.That(drive.Endpoint.y, Is.EqualTo(floor).Within(.04f), drive.Describe());
                drive.GestureFrame(dragged, false);
                Assert.AreNotEqual(tip, drive.Chain, drive.Describe());
                Assert.That(Node(drive.Chain).y, Is.EqualTo(floor).Within(.04f));
                Assert.That(Vector2.Distance(Geo.XZ(Node(drive.Chain)), far), Is.LessThan(2f));
                if (adjacentRock.HasValue)
                    Assert.That(world.Height(adjacentRock.Value), Is.EqualTo(rockBefore).Within(.01f),
                        "Grading the transition must preserve the rock face beside the pavement.");

                var added = Sections().Where(edge => !old.Contains(edge.id)).ToList();
                Assert.That(added.Any(edge => edge.path.shape.curved), "The committed extension should preserve the drag.");
                Assert.That(added.All(edge => edge.path.MaximumGrade <= gradeLimit + .001f));
                Assert.That(added.Any(edge => edge.structure == RoadStructure.Tunnel));
                Assert.AreEqual(ravine, added.Any(edge => edge.structure == RoadStructure.Elevated));
                Assert.That(added.Where(edge => edge.structure == RoadStructure.Tunnel).Sum(edge => edge.path.Length), Is.GreaterThan(170f));
                Vector2 origin = street ? Vector2.zero : Geo.XZ(game.Airport.transform.position);
                var cover = added.SelectMany(edge => Enumerable.Range(0, 30)
                    .Select(i => world.Height(edge.path.Horizontal(i / 29f) + origin) - floor)).ToList();
                Assert.That(cover.Max() - cover.Min(), Is.GreaterThan(100f), "The route must test changing terrain relief.");
                if (ravine) Assert.That(cover.Min(), Is.LessThan(-15f), "The deck must span a substantial drop.");
                else
                {
                    int deepTip = drive.Chain;
                    var deepEnd = Node(deepTip);
                    Assert.That(world.Height(Geo.XZ(deepEnd)) - floor, Is.GreaterThan(350f));
                    drive.tool.SetTool(DevTool.Select);
                    drive.tool.SetRoadUnderground(false);
                    drive.tool.SetTool(tool);
                    drive.tool.SetRoadUnderground(true);
                    var deepCap = deepEnd + Vector3.up * .3f;
                    Aim(deepCap, farSideCamera);
                    Click(deepCap);
                    Assert.AreEqual(deepTip, drive.Chain, "Reopen the deep end under the summit.");
                    Aim(deepEnd + Geo.X0Z(across * 100f), farSideCamera);
                    Assert.IsNull(drive.Problem, drive.Describe());
                    Assert.That(drive.Endpoint.y, Is.EqualTo(floor).Within(.04f), "The terrain cover must not change the floor.");
                }
                TestContext.WriteLine($"{world.landscapeSource}, seed {seed}, {tool}. Initial rise {face:0} m. " +
                    $"{added.Sum(e => e.path.Length):0} m curved extension with terrain {cover.Min():+0;-0;0} to {cover.Max():+0;-0;0} m relative to the road. " +
                    string.Join(", ", added.GroupBy(edge => edge.structure)
                        .Select(group => $"{group.Key} {group.Sum(edge => edge.path.Length):0} m")));
            }
            finally
            {
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    if (root != null && !roots.Contains(root)) Object.DestroyImmediate(root);
                Shader.SetGlobalFloat("_AirCityUndergroundCut", 0);
            }
        }
    }
}

using System;
using System.Linq;
using AirCity.Characters;
using AirCity.Dev;
using AirCity.EditorTools.Characters;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests
{
    public sealed class RampCrewTests
    {
        BaggageTurnaroundDemo demo;
        [TearDown] public void Close() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        void Load()
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath);
            demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            demo.startWithArrival = false; demo.autoDeparture = false; demo.Initialize();
            Assert.That(demo.Problem,Is.Null);
        }
        void Until(Func<bool> ready)
        {
            for (int i = 0; i < 8000 && !ready(); i++) demo.airport.Simulate(.25f);
            Assert.That(ready(),Is.True,$"{demo.Job.State}: {demo.Job.Waiting}");
        }

        [Test]
        public void SharedWorker_HasThreeSkinnedLODsWithOneMaterialAndNormalizedWeights()
        {
            Assert.That(RampCrewSetup.Type.height,Is.EqualTo(1.78f));
            var prefab = RampCrewSetup.Prefab;
            Assert.That(prefab.GetComponentsInChildren<LODGroup>().Length,Is.EqualTo(1));
            Assert.That(prefab.GetComponent<LODGroup>().GetLODs().Length,Is.EqualTo(3));
            var meshes = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(); Assert.That(meshes.Length,Is.EqualTo(3));
            foreach (var renderer in meshes)
            {
                Assert.That(renderer.sharedMaterials.Length,Is.EqualTo(1));
                Assert.That(renderer.bones.Length,Is.GreaterThan(15));
                foreach (var weight in renderer.sharedMesh.boneWeights)
                    Assert.That(weight.weight0+weight.weight1+weight.weight2+weight.weight3,Is.EqualTo(1f).Within(.001f));
            }
            Assert.That(meshes.OrderBy(m=>m.sharedMesh.vertexCount).First().sharedMesh.vertexCount,Is.LessThan(1000));
        }

        [Test]
        public void Worker_KneelsWithoutLegsPassingThroughTheFloorAndKeepsPalmsOnTargets()
        {
            var rig = Object.Instantiate(RampCrewSetup.Prefab).GetComponent<RampCrewRig>();
            var pose = RampCrewPose.Standing(Vector3.zero,Vector3.forward); rig.Apply(pose);
            pose.hipHeight = .38f; pose.lean = 40; pose.kneeling = true; pose.hands = true;
            pose.leftHand = new Vector3(-.2f,.30f,.48f); pose.rightHand = new Vector3(.2f,.30f,.48f); rig.Apply(pose);
            Assert.That(Vector3.Distance(rig.LeftPalm,pose.leftHand),Is.LessThan(.002f));
            Assert.That(Vector3.Distance(rig.RightPalm,pose.rightHand),Is.LessThan(.002f));
            var renderer = rig.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name.EndsWith("LOD0"));
            var mesh = new Mesh(); renderer.BakeMesh(mesh);
            var vertices = mesh.vertices.Select(v=>renderer.transform.TransformPoint(v)).ToArray();
            Assert.That(vertices.Min(v=>v.y),Is.GreaterThan(-.06f));
            Assert.That(vertices.Max(v=>v.y),Is.LessThan(1.20f));
            Object.DestroyImmediate(mesh); Object.DestroyImmediate(rig.gameObject);
        }

        [Test]
        public void Turnaround_NeverDrivesWithoutOperatorsAndWaitsForBothHandlers()
        {
            Load(); bool transferred = false, sawGroundOutside = false, sawHoldOutside = false;
            for (int i = 0; i < 8000 && !demo.Job.Finished; i++)
            {
                demo.airport.Simulate(.25f); var job = demo.Job; var fleet = job.Fleet; var crew = fleet.Crew;
                if (Mathf.Abs(fleet.TrainSpeed) > .001f) Assert.That(crew.GroundSeated,Is.True,job.State.ToString());
                if (Mathf.Abs(fleet.LoaderSpeed) > .001f) Assert.That(crew.HoldSeated,Is.True,job.State.ToString());
                sawGroundOutside |= !crew.GroundSeated; sawHoldOutside |= !crew.HoldSeated;
                if (job.TransferBatch != null)
                {
                    transferred = true; Assert.That(crew.GroundReady && crew.HoldReady,Is.True);
                    Assert.That(job.TransferInterval,Is.GreaterThanOrEqualTo(crew.GroundCycle));
                    Assert.That(demo.aircraft.CargoDoors.IsOpen(job.HoldConfiguration.cargoPoint),Is.True);
                    Reach(crew.GroundPose,"ground"); Reach(crew.HoldPose,"hold");
                    for (int worker = 0; worker < 2; worker++)
                    {
                        int held = 0;
                        for (int bag = 0; bag < job.TransferBatch.Count; bag++)
                        {
                            float e = job.TransferSeconds-bag*job.TransferInterval;
                            if (e >= 0 && e < job.BagTravelSeconds && crew.BagAt(e,job.TransferTo == job.Hold).holder == worker) held++;
                        }
                        Assert.That(held,Is.LessThanOrEqualTo(1),"Each worker can handle one bag at a time.");
                    }
                }
            }
            Assert.That(transferred && sawGroundOutside && sawHoldOutside,Is.True);
            Assert.That(demo.Job.DepartureReady,Is.True); Assert.That(demo.Job.Fleet.Crew.GroundSeated && demo.Job.Fleet.Crew.HoldSeated,Is.True);
            Assert.That(demo.aircraft.CargoDoors.AllClosed,Is.True); Assert.That(demo.airport.Baggage.Ledger.Count,Is.EqualTo(90));
        }

        static void Reach(RampCrewPose pose, string label)
        {
            if (!pose.hands) return;
            var up = Quaternion.AngleAxis(pose.lean,Vector3.right)*Vector3.up;
            var chest = Vector3.up*pose.hipHeight + up*.46f;
            var grip = new Vector3(0,-.65f,.76f).normalized*.075f;
            Assert.That(Vector3.Distance(chest+Vector3.left*.205f,pose.leftHand-grip),Is.LessThan(.595f),label+" left arm must reach without stretching");
            Assert.That(Vector3.Distance(chest+Vector3.right*.205f,pose.rightHand-grip),Is.LessThan(.595f),label+" right arm must reach without stretching");
        }

        [TestCase(true)] [TestCase(false)]
        public void BlockedAccess_FreezesBoardingAndActiveHandovers(bool boarding)
        {
            Load(); var crew = demo.Job.Fleet.Crew;
            Until(()=>boarding ? crew.HoldProgress > 2f : demo.Job.TransferSeconds > 3f);
            float ground = crew.GroundProgress, hold = crew.HoldProgress, transfer = demo.Job.TransferSeconds;
            var pose = boarding ? crew.HoldPose : crew.GroundPose;
            demo.airport.Simulate(0); demo.station.accessBlocked = true; demo.airport.Simulate(20);
            Assert.That(crew.GroundProgress,Is.EqualTo(ground)); Assert.That(crew.HoldProgress,Is.EqualTo(hold));
            Assert.That(demo.Job.TransferSeconds,Is.EqualTo(transfer));
            var after = boarding ? crew.HoldPose : crew.GroundPose;
            Assert.That(after.position,Is.EqualTo(pose.position)); Assert.That(after.rotation,Is.EqualTo(pose.rotation));
            demo.station.accessBlocked = false; Until(()=>demo.Job.DepartureReady);
        }

        [TestCase(true)] [TestCase(false)]
        public void Cancellation_RecallsWorkersBeforeWithdrawingAndCanRetry(bool boarding)
        {
            Load(); var crew = demo.Job.Fleet.Crew;
            Until(()=>boarding ? crew.HoldProgress > 2f : crew.GroundProgress > 2f);
            demo.Job.Cancel(); Until(()=>demo.Job.Finished);
            Assert.That(crew.GroundSeated && crew.HoldSeated,Is.True);
            Assert.That(demo.aircraft.CargoDoors.AllClosed,Is.True);
            Assert.That(demo.Job.Retry(out var why),Is.True,why); Until(()=>demo.Job.DepartureReady);
            Assert.That(demo.Job.Hold.Count,Is.EqualTo(60)); Assert.That(demo.airport.Baggage.Ledger.Count,Is.EqualTo(90));
        }

        [TestCase("BaggageTractor")] [TestCase("BeltLoader")] [TestCase("ContainerLoader")]
        public void OperatorWorkstations_KeepHandsOnAuthoredControls(string model)
        {
            var type = VehicleSetup.Type(model); Assert.That(type.crew,Is.Not.Null);
            var vehicle = new BaggageVehiclePose(new Vector2(10,20),new Vector2(1,0));
            var pose = BaggageCrew.Workstation(type,vehicle);
            var rig = Object.Instantiate(RampCrewSetup.Prefab).GetComponent<RampCrewRig>(); rig.Apply(pose);
            var p = vehicle.Point(type.crew.leftHand);
            Assert.That(Vector3.Distance(rig.LeftPalm,new Vector3(p.x,type.crew.leftHand.y,p.y)),Is.LessThan(.002f));
            Object.DestroyImmediate(rig.gameObject);
        }
    }
}

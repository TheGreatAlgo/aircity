using System.Collections.Generic;
using System.Linq;
using AirCity.Game;
using AirCity.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Tests.Rendering
{
    public class ShadowRangeTests
    {
        const float Fov = 45f, Aspect = 16f / 9f, Far = 20000f, GroundY = 100f;
        static readonly float[] Distances = { 35f, 150f, 600f, 1150f, 1500f, 4000f, 7500f };
        static readonly float[] Pitches = { 18f, 30f, 55f, 65f, 85f };
        static float Fade => (1f - ShadowRange.Border) * (1f - ShadowRange.Border);

        /// <summary>The CareerCamera pose for an orbit (Apply: never closer than 15 m above the ground).</summary>
        static (Vector3 position, Quaternion rotation) Pose(Vector3 pivot, float distance, float pitch, float yaw)
        {
            var position = pivot - Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward * distance;
            position.y = Mathf.Max(position.y, pivot.y + 15f);
            return (position, Quaternion.LookRotation(pivot - position));
        }

        static float Layout(Vector3 position, Quaternion rotation, out ShadowRange.Layout layout)
        {
            layout = ShadowRange.Compute(position, rotation, Fov, Aspect, Far, GroundY);
            layout.distance = ShadowRange.Quantise(layout.distance, 0f);
            layout.splits = ShadowRange.Splits(layout.nearGround, layout.distance);
            return layout.distance;
        }

        /// <summary>Every ground point on screen (a grid of screen rays against the ground plane, and a plane 10% of
        /// the camera height plus 20 m lower) lies inside the fully shadowed part of the range, before its fade,
        /// unless it is beyond the 12 km cap, or the horizon is in view and the point is within 2 degrees of it
        /// (beyond the cascades' span of 128 x the nearest ground).</summary>
        [Test]
        public void RangeCoversTheVisibleGroundAtEveryZoomAndPitch()
        {
            float tanV = Mathf.Tan(.5f * Fov * Mathf.Deg2Rad);
            foreach (float distance in Distances)
            foreach (float pitch in Pitches)
            foreach (float yaw in new[] { 0f, 137f })
            {
                var (position, rotation) = Pose(new Vector3(-400f, GroundY, 900f), distance, pitch, yaw);
                float range = Layout(position, rotation, out var layout);
                float lowered = GroundY - (.1f * (position.y - GroundY) + 20f);
                bool horizon = false;
                foreach (float plane in new[] { GroundY, lowered })
                for (int x = 0; x <= 16; x++)
                for (int y = 0; y <= 16; y++)
                {
                    var ray = rotation * new Vector3((x / 8f - 1f) * tanV * Aspect, (y / 8f - 1f) * tanV, 1f).normalized;
                    if (ray.y >= -1e-4f) { horizon = true; continue; }
                    float t = (position.y - plane) / -ray.y;
                    if (t > ShadowRange.MaxDistance * Fade) { horizon = true; continue; }
                    if (t > range * Fade + .01f)
                        Assert.Greater(ray.y, -Mathf.Sin(2f * Mathf.Deg2Rad), $"distance {distance} pitch {pitch} yaw {yaw}: ground at {t:0} m, " +
                            $"{Mathf.Asin(-ray.y) * Mathf.Rad2Deg:0.0} degrees below the horizon, is outside {layout}");
                }
                if (horizon) Assert.GreaterOrEqual(range, Mathf.Min(ShadowRange.MaxDistance, 100f * layout.nearGround), $"distance {distance} pitch {pitch}: horizon in view");
                Assert.That(layout.splits.x > 0f && layout.splits.x < layout.splits.y && layout.splits.y < layout.splits.z && layout.splits.z < 1f, layout.ToString());
                // The nearest ground on screen is inside the first cascade, so close-up shadows get the finest texels.
                Assert.LessOrEqual(layout.nearGround, layout.splits.x * range * 1.2f + 1f, layout.ToString());
            }
        }

        /// <summary>Low close-ups with the horizon in view keep the first cascade within a few times the nearest ground,
        /// so close-up shadows stay as fine as with the old 150 m range (first cascade 18 m, ground from ~24 m).</summary>
        [Test]
        public void LowCloseUpsKeepAFineFirstCascade()
        {
            foreach (float distance in new[] { 35f, 45f, 80f, 150f })
            foreach (float pitch in new[] { 18f, 22f, 30f })
            {
                var (position, rotation) = Pose(new Vector3(0f, GroundY, 0f), distance, pitch, 0f);
                float range = Layout(position, rotation, out var layout);
                Assert.LessOrEqual(layout.splits.x * range, 4f * layout.nearGround, layout.ToString());
                // The range still reaches the farthest ground, or ~110 x the nearest ground when that is nearer (horizon
                // in view). The camera's real pitch can be steeper than asked: it keeps 15 m above the pivot.
                Assert.GreaterOrEqual(range * 1.001f, Mathf.Min(layout.farGround / Fade, 100f * layout.nearGround), layout.ToString());
            }
        }

        /// <summary>The range and cascades cover what the old fixed 150 m did not: from the default site framing (1150 m,
        /// pitch 53) the nearest ground on screen was 965 m away.</summary>
        [Test]
        public void SiteFramingGetsShadowsWhereTheFixedRangeHadNone()
        {
            var (position, rotation) = Pose(new Vector3(0f, GroundY, 0f), 1150f, 53f, 0f);
            float range = Layout(position, rotation, out var layout);
            Assert.Greater(layout.nearGround, 150f);
            Assert.GreaterOrEqual(range * Fade, layout.farGround);
            Assert.Less(range, ShadowRange.MaxDistance);
        }

        /// <summary>Panning (same zoom, pitch and heading) never changes the cascades, so their texels do not swim.</summary>
        [Test]
        public void PanningKeepsTheCascadesFixed()
        {
            foreach (float distance in Distances)
            foreach (float pitch in Pitches)
            {
                ShadowRange.Layout first = default;
                for (int i = 0; i < 12; i++)
                {
                    var pivot = new Vector3(-2500f + i * 431.7f, GroundY, 1800f - i * 297.3f);
                    var (position, rotation) = Pose(pivot, distance, pitch, 40f);
                    var layout = ShadowRange.Compute(position, rotation, Fov, Aspect, Far, GroundY);
                    layout.distance = ShadowRange.Quantise(layout.distance, i == 0 ? 0f : first.distance);
                    layout.splits = ShadowRange.Splits(layout.nearGround, layout.distance);
                    if (i == 0) { first = layout; continue; }
                    Assert.AreEqual(first.distance, layout.distance, $"distance {distance} pitch {pitch}");
                    Assert.AreEqual(first.splits, layout.splits, $"distance {distance} pitch {pitch}");
                }
            }
        }

        /// <summary>Zooming changes the range in steps only; hovering at a step boundary does not flip it.</summary>
        [Test]
        public void ZoomChangesTheRangeInStepsWithHysteresis()
        {
            float current = 0f; var seen = new List<float>();
            for (float needed = 40f; needed < 14000f; needed *= 1.01f)
            {
                float next = ShadowRange.Quantise(needed, current);
                Assert.GreaterOrEqual(next, Mathf.Min(needed, ShadowRange.MaxDistance) - .01f);
                if (next != current) seen.Add(next);
                current = next;
            }
            // About six steps per doubling from 60 m to 12 km: at most ~46 distinct ranges on a full zoom out.
            Assert.LessOrEqual(seen.Count, 48); Assert.Greater(seen.Count, 30);
            // Hovering: needed wobbles 3% either side of a step boundary.
            float boundary = ShadowRange.MinDistance * Mathf.Pow(ShadowRange.Step, 10f);
            current = ShadowRange.Quantise(boundary * 1.02f, 0f);
            int changes = 0;
            for (int i = 0; i < 50; i++)
            {
                float next = ShadowRange.Quantise(boundary * (i % 2 == 0 ? .97f : 1.02f), current);
                if (next != current) changes++;
                current = next;
            }
            Assert.AreEqual(0, changes);
            // Zooming in far enough does come back down.
            Assert.Less(ShadowRange.Quantise(boundary / 3f, current), current);
        }

        /// <summary>
        /// A following camera renders with its range and the pipeline asset is back to its own values straight after;
        /// a camera that does not follow renders with the asset's values. Nothing is left changed for the editor to save.
        /// </summary>
        [Test]
        public void FollowingCameraRendersWithItsRangeAndTheAssetIsRestored()
        {
            if (!ShadowRange.TryReadPipeline(out var own)) { Assert.Ignore("URP is not the active pipeline."); return; }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Assert.Ignore("No graphics device."); return; }
            var root = new GameObject("Shadow range test");
            var rt = new RenderTexture(64, 36, 24);
            var during = new Dictionary<Camera, ShadowRange.Layout>();
            void Sample(ScriptableRenderContext context, Camera camera) { if (ShadowRange.TryReadPipeline(out var now)) during[camera] = now; }
            Camera following = null, other = null;
            try
            {
                following = NewCamera(root, "following", rt); other = NewCamera(root, "other", rt);
                var pivot = new Vector3(0f, GroundY, 0f);
                foreach (var camera in new[] { following, other })
                {
                    var (position, rotation) = Pose(pivot, 1500f, 55f, 0f);
                    camera.transform.SetPositionAndRotation(position, rotation);
                }
                ShadowRange.Follow(following, pivot);
                RenderPipelineManager.beginCameraRendering += Sample;
                following.Render(); other.Render();
                Assert.IsTrue(ShadowRange.TryGet(following, out var layout));
                Assert.IsTrue(during.ContainsKey(following) && during.ContainsKey(other));
                Assert.AreEqual(layout.distance, during[following].distance, .001f);
                Assert.AreEqual(layout.splits, during[following].splits);
                Assert.AreEqual(layout.border, during[following].border, .001f);
                Assert.Greater(during[following].distance, 3000f);
                AssertSame(own, during[other]);
                Assert.IsTrue(ShadowRange.TryReadPipeline(out var after)); AssertSame(own, after);
                Assert.AreEqual(own.distance, ShadowRange.Distance(other));
            }
            finally
            {
                RenderPipelineManager.beginCameraRendering -= Sample;
                ShadowRange.Unfollow(following);
                ShadowRange.Restore();
                Object.DestroyImmediate(root); rt.Release(); Object.DestroyImmediate(rt);
            }
            Assert.IsFalse(ShadowRange.Follows(following));
            Assert.IsTrue(ShadowRange.TryReadPipeline(out var end)); AssertSame(own, end);
        }

        static void AssertSame(ShadowRange.Layout expected, ShadowRange.Layout actual)
        {
            Assert.AreEqual(expected.distance, actual.distance); Assert.AreEqual(expected.splits, actual.splits);
            Assert.AreEqual(expected.border, actual.border); Assert.AreEqual(expected.resolution, actual.resolution);
        }

        /// <summary>The game camera hands its pose to the shadow range whenever it moves.</summary>
        [Test]
        public void CareerCameraFollows()
        {
            var go = new GameObject("Career camera follows");
            try
            {
                var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.fieldOfView = Fov; camera.aspect = Aspect;
                var view = go.AddComponent<CareerCamera>();
                view.FocusPoint(new Vector3(0f, GroundY, 0f), 4000f, 55f, 0f);
                Assert.IsTrue(ShadowRange.TryGet(camera, out var layout));
                Assert.Greater(layout.distance, 8000f);
                view.FocusPoint(new Vector3(0f, GroundY, 0f), 150f, 55f, 0f);
                Assert.IsTrue(ShadowRange.TryGet(camera, out layout));
                Assert.Less(layout.distance, 600f);
                ShadowRange.Unfollow(camera);
            }
            finally { Object.DestroyImmediate(go); }
        }

        static Camera NewCamera(GameObject root, string name, RenderTexture rt)
        {
            var go = new GameObject(name); go.transform.SetParent(root.transform);
            var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.targetTexture = rt;
            camera.fieldOfView = Fov; camera.aspect = Aspect; camera.nearClipPlane = .5f; camera.farClipPlane = Far;
            return camera;
        }
    }
}

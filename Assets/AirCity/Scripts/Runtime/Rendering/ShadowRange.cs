using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AirCity.Rendering
{
    /// <summary>
    /// Sun-shadow range that follows what a camera sees. The render-pipeline asset has one shadow distance (150 m)
    /// but the game camera orbits 35 to 7500 m out, so from a few hundred metres out no ground on screen got sun
    /// shadows. A camera script calls <see cref="Follow"/> with its camera and the ground point it orbits; while that
    /// camera renders, the shadow distance, cascade splits and fade border are set so the shadowed range covers the
    /// ground the camera can see, then put back as soon as the camera has rendered. Only the cameras that follow
    /// get the range (scene view, previews and other cameras keep the asset's own values), and nothing is changed
    /// between frames, so the asset on disk is never touched by playing or by the editor saving.
    ///
    /// To keep shadow edges steady, the distance moves in steps of 2^(1/6) with hysteresis and the splits depend
    /// only on the ratio of the nearest to the farthest visible ground, also stepped; panning (constant distance and
    /// pitch) therefore never changes the cascades, and zooming changes them only at step boundaries.
    /// </summary>
    public static class ShadowRange
    {
        /// <summary>Shadow distance and the end of the first three cascades as fractions of it (URP's cascade4Split).</summary>
        public struct Layout
        {
            public float distance, border;
            public Vector3 splits;
            /// <summary>Main-light shadow map size; 0 = keep the asset's.</summary>
            public int resolution;
            /// <summary>Nearest and farthest visible ground (camera distance) the layout was made for.</summary>
            public float nearGround, farGround;
            public override string ToString() => $"D {distance:0} splits {splits.x:0.###}/{splits.y:0.###}/{splits.z:0.###} border {border:0.###} ground {nearGround:0}-{farGround:0}";
        }

        /// <summary>Fade at the end of the range, as URP's cascade border: shadows fade from (1 - Border)^2 of the range.</summary>
        public const float Border = .1f;
        public const float MinDistance = 60f, MaxDistance = 12000f;
        /// <summary>Range steps: six per doubling.</summary>
        public static readonly float Step = Mathf.Pow(2f, 1f / 6f);
        /// <summary>The ground is taken as a plane at the focus height lowered by this share of the camera's height
        /// above it plus a fixed margin, so ground that falls away (valleys, coast) is still covered.</summary>
        const float DropShare = .1f, DropMargin = 20f;
        /// <summary>Span of the four cascades in doublings (range / 0.85 x nearest ground on screen), stepped in
        /// quarters. At most 7 (x128): with the horizon in view at a low close-up the range would otherwise run to the
        /// cap and stretch the first cascade over ~100 m, coarsening the close-up shadows; capped, ground farther than
        /// ~110 x the nearest ground (within a degree or so of the horizon) gets no sun shadow, faded out.</summary>
        public const float MinOctaves = 1f, MaxOctaves = 7f;
        /// <summary>Main-light shadow map size while a following camera renders (0 = the asset's own). Fixed; a
        /// static (not const) only so the shadow review can compare sizes.</summary>
        static int mapResolution = 0;
        /// <summary>Off: following cameras render with the asset's own values (the shadow review's "before").</summary>
        static bool enabled = true;

        sealed class Follower { public Camera camera; public Vector3 focus; public float distance; public Layout layout; }
        static readonly List<Follower> followers = new List<Follower>();
        static bool hooked;
        static UniversalRenderPipelineAsset heldAsset;
        static Camera holder;
        static Layout saved;

        /// <summary>Let the shadow range follow this camera's view of the ground around <paramref name="focus"/>
        /// (the point the camera orbits, on the ground). Call whenever the camera moves; cheap.</summary>
        public static void Follow(Camera camera, Vector3 focus)
        {
            if (camera == null) return;
            Hook();
            var follower = Find(camera);
            if (follower == null) { followers.RemoveAll(f => f.camera == null); followers.Add(follower = new Follower { camera = camera }); }
            follower.focus = focus;
        }

        public static void Unfollow(Camera camera) => followers.RemoveAll(f => f.camera == null || f.camera == camera);

        public static bool Follows(Camera camera) => camera != null && Find(camera) != null;

        /// <summary>The shadow distance a camera renders with: its followed range, else the pipeline's own.</summary>
        public static float Distance(Camera camera)
        {
            var follower = camera != null && enabled ? Find(camera) : null;
            if (follower != null) return Update(follower).distance;
            return TryReadPipeline(out var layout) ? layout.distance : 0f;
        }

        /// <summary>The layout a following camera renders with, as of its current pose.</summary>
        public static bool TryGet(Camera camera, out Layout layout)
        {
            var follower = camera != null ? Find(camera) : null;
            layout = follower != null ? Update(follower) : default;
            return follower != null;
        }

        /// <summary>The active URP asset's own shadow settings (between renders: what is on disk).</summary>
        public static bool TryReadPipeline(out Layout layout)
        {
            layout = default;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)) return false;
            layout = Read(asset); return true;
        }

        static Follower Find(Camera camera)
        {
            foreach (var f in followers) if (f.camera == camera) return f;
            return null;
        }

        static Layout Update(Follower follower)
        {
            var t = follower.camera.transform;
            var needed = Compute(t.position, t.rotation, follower.camera.fieldOfView, follower.camera.aspect,
                follower.camera.farClipPlane, follower.focus.y);
            follower.distance = Quantise(needed.distance, follower.distance);
            needed.distance = follower.distance;
            needed.resolution = mapResolution;
            needed.splits = Splits(needed.nearGround, needed.distance);
            return follower.layout = needed;
        }

        /// <summary>
        /// The range that covers the ground a camera sees. <paramref name="groundY"/> is the height of the ground the
        /// camera looks at. The farthest visible ground is the farthest frustum corner on the lowered ground plane (or,
        /// when the horizon is in view, the cap); the range is that over (1 - Border)^2 so the fade starts beyond it,
        /// limited to <see cref="MaxOctaves"/> doublings beyond the nearest ground.
        /// The distance returned is exact (not stepped); <see cref="Quantise"/> steps it.
        /// </summary>
        public static Layout Compute(Vector3 position, Quaternion rotation, float fieldOfView, float aspect, float far, float groundY)
        {
            float height = Mathf.Max(1f, position.y - groundY);
            float low = groundY - (DropShare * height + DropMargin);
            float tanV = Mathf.Tan(.5f * fieldOfView * Mathf.Deg2Rad);
            float cap = Mathf.Min(MaxDistance, far);
            float farGround = 0f, nearGround = float.MaxValue;
            for (int corner = 0; corner < 4; corner++)
            {
                var ray = Ray(rotation, tanV, aspect, corner & 1, corner >> 1);
                float t = Hit(position, ray, low);
                farGround = Mathf.Max(farGround, t < 0f ? float.MaxValue : t);
            }
            for (int i = 0; i <= 2; i++)
            {
                float t = Hit(position, Ray(rotation, tanV, aspect, i * .5f, 0f), groundY);
                if (t > 0f) nearGround = Mathf.Min(nearGround, t);
            }
            // Straight down in view (looking almost vertically): the nearest ground is right below.
            var down = Quaternion.Inverse(rotation) * Vector3.down;
            if (down.z > 0f && Mathf.Abs(down.y / down.z) <= tanV && Mathf.Abs(down.x / down.z) <= tanV * aspect)
                nearGround = Mathf.Min(nearGround, height);
            if (nearGround == float.MaxValue) nearGround = height;
            float fade = (1f - Border) * (1f - Border);
            float span = Mathf.Max(MinDistance, .85f * nearGround * Mathf.Pow(2f, MaxOctaves));
            float distance = Mathf.Clamp(Mathf.Min(farGround, cap) / fade, MinDistance, Mathf.Min(cap, span));
            return new Layout { distance = distance, border = Border, nearGround = nearGround, farGround = Mathf.Min(farGround, cap),
                splits = Splits(nearGround, distance), resolution = mapResolution };
        }

        /// <summary>
        /// Range in steps of <see cref="Step"/> from <see cref="MinDistance"/>, never below what is needed, and kept
        /// (hysteresis) until what is needed drops two steps below it, so a camera hovering at a step boundary does
        /// not flip the cascades back and forth.
        /// </summary>
        public static float Quantise(float needed, float current)
        {
            needed = Mathf.Clamp(needed, MinDistance, MaxDistance);
            if (current >= needed && current <= needed * Step * Step * 1.0001f) return current;
            float steps = Mathf.Ceil(Mathf.Log(needed / MinDistance, Step) - 1e-4f);
            return Mathf.Min(MaxDistance, MinDistance * Mathf.Pow(Step, Mathf.Max(0f, steps)));
        }

        /// <summary>
        /// Cascade ends as fractions of the range: geometric between the nearest visible ground (less 15%) and the
        /// range, so each cascade's texels keep about the same size relative to the screen pixels it covers. The ratio
        /// is stepped in quarter-doublings; the first cascade always reaches from the camera.
        /// </summary>
        public static Vector3 Splits(float nearGround, float distance)
        {
            float start = Mathf.Max(.85f * nearGround, 1e-3f);
            float octaves = Mathf.Log(Mathf.Max(1f, distance / start), 2f);
            octaves = Mathf.Clamp(Mathf.Round(octaves * 4f) / 4f, MinOctaves, MaxOctaves);
            return new Vector3(Mathf.Pow(2f, -.75f * octaves), Mathf.Pow(2f, -.5f * octaves), Mathf.Pow(2f, -.25f * octaves));
        }

        static Vector3 Ray(Quaternion rotation, float tanV, float aspect, float vx, float vy) =>
            rotation * new Vector3((2f * vx - 1f) * tanV * aspect, (2f * vy - 1f) * tanV, 1f).normalized;

        /// <summary>Distance along a unit ray to the plane at height y, or -1 if the ray does not go down to it.</summary>
        static float Hit(Vector3 origin, Vector3 ray, float y)
        {
            if (ray.y > -1e-4f || origin.y <= y) return -1f;
            return (origin.y - y) / -ray.y;
        }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
            Application.quitting += Restore;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Restore;
            UnityEditor.EditorApplication.quitting += Restore;
#endif
        }

        static void Begin(ScriptableRenderContext context, Camera camera)
        {
            if (holder != null) Restore();
            if (camera == null || camera.cameraType != CameraType.Game) return;
            var follower = Find(camera);
            if (follower == null || !enabled || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)) return;
            var layout = Update(follower);
            heldAsset = asset; holder = camera; saved = Read(asset);
            Write(asset, layout);
        }

        static void End(ScriptableRenderContext context, Camera camera)
        {
            if (holder != null && camera == holder) Restore();
        }

        /// <summary>Puts the asset's own values back (after every following camera's render, and on quit).</summary>
        public static void Restore()
        {
            if (heldAsset != null) Write(heldAsset, saved);
            heldAsset = null; holder = null;
        }

        static Layout Read(UniversalRenderPipelineAsset asset) => new Layout
            { distance = asset.shadowDistance, splits = asset.cascade4Split, border = asset.cascadeBorder, resolution = asset.mainLightShadowmapResolution };

        static void Write(UniversalRenderPipelineAsset asset, Layout layout)
        {
            asset.shadowDistance = layout.distance;
            asset.cascade4Split = layout.splits;
            asset.cascadeBorder = layout.border;
            if (layout.resolution > 0) asset.mainLightShadowmapResolution = layout.resolution;
        }
    }
}

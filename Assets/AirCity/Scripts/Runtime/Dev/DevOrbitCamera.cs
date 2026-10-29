using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    /// <summary>
    /// Development camera for looking around the sandbox: right-drag orbits, middle-drag or WASD
    /// pans across the ground, scroll zooms (towards the point under the cursor), Q/E rotate, F re-frames the origin.
    /// Not the game camera, which belongs to gameplay code once it exists.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class DevOrbitCamera : MonoBehaviour
    {
        public Vector3 pivot = Vector3.zero;
        public float distance = 40f;
        [Range(5f, 89f)] public float pitch = 40f;
        public float yaw = 200f;

        public Vector2 distanceLimits = new Vector2(4f, 600f);
        public float orbitSpeed = 0.25f, panSpeed = 1f, keyRotateSpeed = 90f;
        [Tooltip("How much closer (or further) one scroll-wheel notch takes the camera: 0.25 = a quarter of the distance.")]
        public float zoomStep = 0.25f;
        [Tooltip("How quickly the camera catches up with the zoom (higher = snappier).")]
        public float zoomSharpness = 18f;
        [Tooltip("Zoom towards the ground point under the cursor (else towards the pivot).")]
        public bool zoomToCursor = true;

        float targetDistance = -1f, applied = -1f;

        void LateUpdate()
        {
            var mouse = Mouse.current;
            var keys = Keyboard.current;
            float dt = Time.unscaledDeltaTime;
            // set from outside (the inspector, a script): zoom from there
            if (!Mathf.Approximately(distance, applied)) targetDistance = distance;

            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (mouse.rightButton.isPressed)
                {
                    yaw += delta.x * orbitSpeed;
                    pitch = Mathf.Clamp(pitch - delta.y * orbitSpeed, 5f, 89f);
                }
                if (mouse.middleButton.isPressed)
                    Pan(-delta.x * distance * 0.0015f * panSpeed, -delta.y * distance * 0.0015f * panSpeed);
                // the Input System reports a wheel notch as 1 on every platform; trackpads give fractions of one
                float scroll = Mathf.Clamp(mouse.scroll.ReadValue().y, -4f, 4f);
                // Shift+scroll turns a stand being placed instead (DevAirfieldTool)
                if (Mathf.Abs(scroll) > 0.001f && !DevGui.PointerOverHud && !DevAirfieldTool.TakesScroll)
                    targetDistance = Mathf.Clamp(targetDistance * Mathf.Pow(1f + zoomStep, -scroll), distanceLimits.x, distanceLimits.y);
            }

            if (keys != null)
            {
                float speed = distance * 0.8f * panSpeed * dt * (keys.leftShiftKey.isPressed ? 3f : 1f);
                float x = (keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0);
                float z = (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0);
                if (x != 0 || z != 0) Pan(x * speed, z * speed);
                if (keys.qKey.isPressed) yaw += keyRotateSpeed * dt;
                if (keys.eKey.isPressed) yaw -= keyRotateSpeed * dt;
                if (keys.fKey.wasPressedThisFrame) pivot = Vector3.zero;
            }

            targetDistance = Mathf.Clamp(targetDistance, distanceLimits.x, distanceLimits.y);
            if (!Mathf.Approximately(distance, targetDistance))
            {
                // ease in log space, so a notch feels the same close up and far out
                float t = 1f - Mathf.Exp(-zoomSharpness * dt);
                float next = Mathf.Exp(Mathf.Lerp(Mathf.Log(distance), Mathf.Log(targetDistance), t));
                if (Mathf.Abs(next - targetDistance) < targetDistance * 1e-3f) next = targetDistance;
                if (zoomToCursor && mouse != null && CursorOnGround(mouse.position.ReadValue(), out var under))
                    // scaling the view about the point under the cursor keeps that point under the cursor
                    pivot = under + (pivot - under) * (next / distance);
                distance = next;
            }
            applied = distance;

            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot - rot * Vector3.forward * distance, rot);
        }

        /// <summary>Move the pivot along the ground, relative to the view direction.</summary>
        void Pan(float right, float forward)
        {
            var yawRot = Quaternion.Euler(0f, yaw, 0f);
            pivot += yawRot * new Vector3(right, 0f, forward);
        }

        /// <summary>The ground point (at the pivot's height) under a screen point, if it's not too near the horizon.</summary>
        bool CursorOnGround(Vector2 screen, out Vector3 point)
        {
            point = default;
            var cam = GetComponent<Camera>();
            var ray = cam.ScreenPointToRay(screen);
            if (!new Plane(Vector3.up, pivot).Raycast(ray, out float hit)) return false;
            point = ray.GetPoint(hit);
            return Vector3.Distance(point, pivot) < distance * 4f;
        }
    }
}

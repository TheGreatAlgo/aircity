using AirCity.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Game
{
    [RequireComponent(typeof(Camera))]
    public class CareerCamera : MonoBehaviour
    {
        public Vector3 pivot;
        public float distance = 4000f, yaw, pitch = 55f;
        public ValleyView valley;
        public bool PointerOverHud { get; set; }
        public bool KeyboardCaptured { get; set; }
        public bool PlacementTakesScroll { get; set; }
        float targetDistance;
        Camera cam;

        public void FocusPoint(Vector3 point, float range, float tilt = 50f, float bearing = 0f)
        { pivot = point; distance = targetDistance = range; pitch = tilt; yaw = bearing; Apply(); }

        public void Frame(AirportSite site)
        {
            pivot = new Vector3(site.centre.x - 200f, site.elevation, site.centre.y - 40f);
            distance = targetDistance = 1150f; yaw = 0f; pitch = 53f; Apply();
        }
        public void Region()
        {
            pivot = new Vector3(-150f, 70f, 100f); distance = targetDistance = 5500f; yaw = 0f; pitch = 65f; Apply();
        }
        public void FrameRoad(Vector2 a, Vector2 b, float elevation)
        {
            var centre = (a + b) * .5f;
            pivot = new Vector3(centre.x - 180f, elevation, centre.y);
            distance = targetDistance = Mathf.Max(1000f, Vector2.Distance(a, b) * 1.65f); yaw = 0f; pitch = 58f; Apply();
        }
        void LateUpdate()
        {
            if (targetDistance <= 0f) targetDistance = distance;
            var mouse = Mouse.current; var keys = Keyboard.current;
            if (mouse != null && !PointerOverHud)
            {
                var delta = mouse.delta.ReadValue();
                if (mouse.rightButton.isPressed) { yaw += delta.x * .23f; pitch = Mathf.Clamp(pitch - delta.y * .2f, 18f, 85f); }
                if (mouse.middleButton.isPressed) Pan(-delta.x * distance * .0014f, -delta.y * distance * .0014f);
                float scroll = Mathf.Clamp(mouse.scroll.ReadValue().y, -4f, 4f);
                if (!PlacementTakesScroll)
                    targetDistance = Mathf.Clamp(targetDistance * Mathf.Pow(1.22f, -scroll), 35f, 7500f);
            }
            if (keys != null && !KeyboardCaptured)
            {
                float speed = distance * Time.unscaledDeltaTime * .7f;
                Pan(((keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0)) * speed,
                    ((keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0)) * speed);
                yaw += ((keys.eKey.isPressed ? 1 : 0) - (keys.qKey.isPressed ? 1 : 0)) * Time.unscaledDeltaTime * 60f;
            }
            distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));
            pivot.x = Mathf.Clamp(pivot.x, -3000f, 3000f); pivot.z = Mathf.Clamp(pivot.z, -3000f, 3000f);
            if (valley != null && valley.World != null) pivot.y = valley.World.Height(new Vector2(pivot.x, pivot.z));
            Apply();
        }
        void Pan(float x, float z) { pivot += Quaternion.Euler(0f, yaw, 0f) * new Vector3(x, 0f, z); }
        public void Apply()
        {
            cam ??= GetComponent<Camera>();
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var position = pivot - rot * Vector3.forward * distance;
            if (valley != null && valley.World != null)
                position.y = Mathf.Max(position.y, valley.World.Height(new Vector2(position.x, position.z)) + 15f);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(pivot - position));
            cam.nearClipPlane = Mathf.Max(.3f, distance * .0003f); cam.farClipPlane = 20000f;
            AirCity.Rendering.ShadowRange.Follow(cam, pivot);
        }
    }
}

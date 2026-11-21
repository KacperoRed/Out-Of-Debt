using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Kamera TPP (zza pleców) dla lokalnego gracza. Przy jeździe oddala się i ustawia za autem.
    /// Załóż na Main Camera.
    /// </summary>
    public class PlayerCamera : MonoBehaviour
    {
        public float mouseSensitivity = 3f;
        public float minPitch = -89f, maxPitch = 70f;

        [Header("Pieszo")]
        public float footDistance = 4.5f;
        public float aimDistance = 2.2f;
        public Vector3 footPivotOffset = new Vector3(0f, 1.6f, 0f);
        public float shoulderOffset = 0.6f;

        [Header("W aucie")]
        public float carDistance = 8f;
        public Vector3 carPivotOffset = new Vector3(0f, 1.8f, 0f);
        public float autoAlignDelay = 1.5f;

        [Header("Kolizje kamery")]
        public float collisionRadius = 0.25f;

        float _yaw, _pitch = 15f, _distance, _zoom = 1f, _lastMouseTime, _baseFov = -1f;

        void LateUpdate()
        {
            var player = PlayerController.Local;
            if (player == null) return;

            Vector2 look = GameInput.Look * mouseSensitivity;
            if (look.sqrMagnitude > 0.0001f) _lastMouseTime = Time.time;
            _yaw += look.x;
            _pitch = Mathf.Clamp(_pitch - look.y, minPitch, maxPitch);
            if (!GameInput.Aim) _zoom = Mathf.Clamp(_zoom - GameInput.Zoom * 0.1f, 0.6f, 1.8f);

            var vehicle = player.Vehicles != null ? player.Vehicles.CurrentVehicle : null;
            Vector3 pivot;
            float targetDist;
            Vector3 side = Vector3.zero;

            if (vehicle != null)
            {
                pivot = vehicle.transform.position + carPivotOffset;
                targetDist = carDistance * _zoom;
                // automatycznie ustaw kamerę za autem, gdy gracz nie rusza myszką
                if (Time.time - _lastMouseTime > autoAlignDelay && vehicle.SpeedKmh > 10f)
                {
                    float carYaw = vehicle.transform.eulerAngles.y;
                    _yaw = Mathf.LerpAngle(_yaw, carYaw, Time.deltaTime * 2f);
                    _pitch = Mathf.Lerp(_pitch, 12f, Time.deltaTime * 2f);
                }
            }
            else
            {
                pivot = player.transform.position + footPivotOffset;
                bool aiming = GameInput.Aim;
                targetDist = (aiming ? aimDistance : footDistance * _zoom);
                side = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right * shoulderOffset;
            }

            _distance = Mathf.Lerp(_distance, targetDist, Time.deltaTime * 8f);
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 origin = pivot + side;
            Vector3 desired = origin - rot * Vector3.forward * _distance;

            // nie wchodź kamerą w ściany
            Vector3 dir = desired - origin;
            float dist = dir.magnitude;
            if (Physics.SphereCast(origin, collisionRadius, dir.normalized, out var hit, dist, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<PlayerController>() == null
                && (vehicle == null || !hit.transform.IsChildOf(vehicle.transform)))
            {
                desired = origin + dir.normalized * Mathf.Max(0.3f, hit.distance);
            }

            transform.SetPositionAndRotation(desired, rot);

            // zoom celownika (np. snajperka): WeaponData.aimFov
            var cam = GetComponent<Camera>();
            if (cam != null)
            {
                if (_baseFov < 0f) _baseFov = cam.fieldOfView;
                float targetFov = _baseFov;
                if (vehicle == null && GameInput.Aim && player.Weapons != null && player.Weapons.Current.aimFov > 0f)
                    targetFov = player.Weapons.Current.aimFov;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, Time.deltaTime * 10f);
            }
        }
    }
}

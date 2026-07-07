using UnityEngine;

namespace SwingingPaint.Core
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Swinging Paint/Desktop Spectator Camera")]
    public class DesktopSpectatorCamera : MonoBehaviour
    {
        public enum CameraPreset
        {
            Overview,
            BucketCloseUp,
            CanvasCloseUp,
            SideView,
            TopDebug
        }

        private const float MinTargetDistanceSqr = 0.0001f;

        private static DesktopSpectatorCamera s_activeMouseLookCamera;

        [Header("Targets")]
        public Transform bucketTarget;
        public Transform canvasTarget;

        [Header("Movement")]
        public bool controlsEnabled = true;
        [Min(0.01f)]
        public float moveSpeed = 3f;
        [Min(1f)]
        public float fastMultiplier = 3f;
        [Range(0.05f, 1f)]
        public float slowMultiplier = 0.3f;
        [Min(0.01f)]
        public float mouseSensitivity = 2.2f;

        [Header("Display")]
        public bool showControlsHelp = true;

        [Header("Follow")]
        public bool followBucket;
        public bool lookAtBucket;
        public bool lookAtCanvas;
        [Min(0.01f)]
        public float smoothFollowSpeed = 6f;
        [Min(0.01f)]
        public float smoothLookSpeed = 8f;

        private Vector3 _initialPosition;
        private Quaternion _initialRotation;
        private bool _hasInitialPose;
        private Vector3 _followOffset;
        private bool _hasFollowOffset;
        private float _yaw;
        private float _pitch;
        private bool _mouseLookActive;

        public bool MouseLookActive => _mouseLookActive;
        public static bool IsAnyMouseLookActive => s_activeMouseLookCamera != null && s_activeMouseLookCamera._mouseLookActive;

        private void Awake()
        {
            ResolveTargets();
            CaptureInitialPose();
            SyncAnglesFromTransform();
        }

        private void OnEnable()
        {
            ResolveTargets();
            if (!_hasInitialPose)
            {
                CaptureInitialPose();
            }

            SyncAnglesFromTransform();
        }

        private void Update()
        {
            if (!controlsEnabled)
            {
                return;
            }

            HandleShortcuts();
            HandleMouseLook();
            HandleMovement(Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            if (!controlsEnabled || _mouseLookActive)
            {
                return;
            }

            ApplyFollowAndLook(Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            if (_mouseLookActive)
            {
                EndMouseLook();
            }
        }

        private void OnGUI()
        {
            if (!showControlsHelp || !controlsEnabled)
            {
                return;
            }

            float height = 122f;
            Rect helpRect = new Rect(12f, Mathf.Max(12f, Screen.height - height - 12f), 420f, height);
            GUILayout.BeginArea(helpRect, GUI.skin.box);
            GUILayout.Label("Camera / Controls");
            GUILayout.Label("RMB look | WASD move | Shift fast | Ctrl/Alt slow");
            GUILayout.Label("Space/E up | Q down | LMB near bucket grab/throw");
            GUILayout.Label("1-5 views | F focus bucket | C focus canvas | Home reset");
            GUILayout.Label("F1 dashboard | H hide/show this help | Esc unlock cursor");
            GUILayout.EndArea();
        }

        public void ResetCameraPose()
        {
            EndMouseLook();
            followBucket = false;
            lookAtBucket = false;
            lookAtCanvas = false;
            transform.SetPositionAndRotation(_initialPosition, _initialRotation);
            _hasFollowOffset = false;
            SyncAnglesFromTransform();
        }

        public void FocusBucket()
        {
            ResolveTargets();
            SetLookAtBucket(true);
            LookAtTargetImmediate(bucketTarget);
        }

        public void FocusCanvas()
        {
            ResolveTargets();
            SetLookAtCanvas(true);
            LookAtTargetImmediate(canvasTarget);
        }

        public void SetFollowBucket(bool enabled)
        {
            followBucket = enabled;
            if (followBucket)
            {
                CaptureFollowOffset();
            }
            else
            {
                _hasFollowOffset = false;
            }
        }

        public void SetLookAtBucket(bool enabled)
        {
            lookAtBucket = enabled;
            if (enabled)
            {
                lookAtCanvas = false;
            }
        }

        public void SetLookAtCanvas(bool enabled)
        {
            lookAtCanvas = enabled;
            if (enabled)
            {
                lookAtBucket = false;
            }
        }

        public void ApplyPreset(CameraPreset preset)
        {
            ResolveTargets();

            Vector3 bucket = GetTargetPosition(bucketTarget, new Vector3(0f, 3f, 0f));
            Vector3 canvas = GetTargetPosition(canvasTarget, Vector3.zero);
            Vector3 center = (bucket + canvas) * 0.5f;
            Vector3 position;
            Vector3 lookTarget;

            switch (preset)
            {
                case CameraPreset.BucketCloseUp:
                    position = bucket + new Vector3(1.2f, 0.65f, -1.8f);
                    lookTarget = bucket + new Vector3(0f, -0.1f, 0f);
                    break;
                case CameraPreset.CanvasCloseUp:
                    position = canvas + new Vector3(0f, 2.1f, -2.65f);
                    lookTarget = canvas + new Vector3(0f, 0.05f, 0f);
                    break;
                case CameraPreset.SideView:
                    position = center + new Vector3(-4.4f, 1.2f, -0.2f);
                    lookTarget = center;
                    break;
                case CameraPreset.TopDebug:
                    position = center + new Vector3(0f, 5.4f, -1.15f);
                    lookTarget = center + new Vector3(0f, -0.2f, 0f);
                    break;
                default:
                    position = center + new Vector3(0f, 2.25f, -6.2f);
                    lookTarget = center + new Vector3(0f, 0.2f, 0f);
                    break;
            }

            lookAtBucket = false;
            lookAtCanvas = false;
            ApplyPose(position, lookTarget);
        }

        private void HandleShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_mouseLookActive)
                {
                    EndMouseLook();
                }
                else if (Cursor.lockState != CursorLockMode.None)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }

                return;
            }

            if (Input.GetKeyDown(KeyCode.Home))
            {
                ResetCameraPose();
            }

            if (Input.GetKeyDown(KeyCode.H))
            {
                showControlsHelp = !showControlsHelp;
            }

            if (Input.GetKeyDown(KeyCode.F))
            {
                FocusBucket();
            }

            if (Input.GetKeyDown(KeyCode.C))
            {
                FocusCanvas();
            }

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                ApplyPreset(CameraPreset.Overview);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                ApplyPreset(CameraPreset.BucketCloseUp);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                ApplyPreset(CameraPreset.CanvasCloseUp);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                ApplyPreset(CameraPreset.SideView);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                ApplyPreset(CameraPreset.TopDebug);
            }
        }

        private void HandleMouseLook()
        {
            if (Input.GetMouseButtonDown(1))
            {
                BeginMouseLook();
            }

            if (_mouseLookActive && !Input.GetMouseButton(1))
            {
                EndMouseLook();
                return;
            }

            if (!_mouseLookActive)
            {
                return;
            }

            _yaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            _pitch -= Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch, -85f, 85f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void HandleMovement(float deltaTime)
        {
            Vector3 move = Vector3.zero;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up);

            if (forward.sqrMagnitude < MinTargetDistanceSqr)
            {
                forward = Vector3.forward;
            }

            if (right.sqrMagnitude < MinTargetDistanceSqr)
            {
                right = Vector3.right;
            }

            forward.Normalize();
            right.Normalize();

            if (Input.GetKey(KeyCode.W))
            {
                move += forward;
            }

            if (Input.GetKey(KeyCode.S))
            {
                move -= forward;
            }

            if (Input.GetKey(KeyCode.D))
            {
                move += right;
            }

            if (Input.GetKey(KeyCode.A))
            {
                move -= right;
            }

            if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.E))
            {
                move += Vector3.up;
            }

            if (Input.GetKey(KeyCode.Q))
            {
                move -= Vector3.up;
            }

            if (move.sqrMagnitude < MinTargetDistanceSqr)
            {
                return;
            }

            float speed = moveSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                speed *= fastMultiplier;
            }

            if (Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl) ||
                Input.GetKey(KeyCode.LeftAlt) ||
                Input.GetKey(KeyCode.RightAlt))
            {
                speed *= slowMultiplier;
            }

            transform.position += move.normalized * speed * Mathf.Max(0f, deltaTime);
            if (followBucket)
            {
                CaptureFollowOffset();
            }
        }

        private void ApplyFollowAndLook(float deltaTime)
        {
            float safeDeltaTime = Mathf.Max(0f, deltaTime);

            if (followBucket && bucketTarget != null)
            {
                if (!_hasFollowOffset)
                {
                    CaptureFollowOffset();
                }

                Vector3 targetPosition = bucketTarget.position + _followOffset;
                float followT = GetSmoothingFactor(smoothFollowSpeed, safeDeltaTime);
                transform.position = Vector3.Lerp(transform.position, targetPosition, followT);
            }

            Transform lookTarget = null;
            if (lookAtBucket && bucketTarget != null)
            {
                lookTarget = bucketTarget;
            }
            else if (lookAtCanvas && canvasTarget != null)
            {
                lookTarget = canvasTarget;
            }

            if (lookTarget != null)
            {
                LookAtPositionSmooth(lookTarget.position, safeDeltaTime);
            }
        }

        private void BeginMouseLook()
        {
            if (_mouseLookActive)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            s_activeMouseLookCamera = this;
            _mouseLookActive = true;
            SyncAnglesFromTransform();
        }

        private void EndMouseLook()
        {
            if (!_mouseLookActive)
            {
                return;
            }

            _mouseLookActive = false;
            if (s_activeMouseLookCamera == this)
            {
                s_activeMouseLookCamera = null;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SyncAnglesFromTransform();
        }

        private void CaptureInitialPose()
        {
            _initialPosition = transform.position;
            _initialRotation = transform.rotation;
            _hasInitialPose = true;
        }

        private void CaptureFollowOffset()
        {
            if (bucketTarget == null)
            {
                _hasFollowOffset = false;
                return;
            }

            _followOffset = transform.position - bucketTarget.position;
            _hasFollowOffset = true;
        }

        private void ApplyPose(Vector3 position, Vector3 lookTarget)
        {
            transform.position = position;
            Vector3 direction = lookTarget - position;
            if (direction.sqrMagnitude > MinTargetDistanceSqr)
            {
                transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }

            if (followBucket)
            {
                CaptureFollowOffset();
            }

            SyncAnglesFromTransform();
        }

        private void LookAtTargetImmediate(Transform target)
        {
            if (target == null)
            {
                return;
            }

            Vector3 direction = target.position - transform.position;
            if (direction.sqrMagnitude <= MinTargetDistanceSqr)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            SyncAnglesFromTransform();
        }

        private void LookAtPositionSmooth(Vector3 targetPosition, float deltaTime)
        {
            Vector3 direction = targetPosition - transform.position;
            if (direction.sqrMagnitude <= MinTargetDistanceSqr)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            float lookT = GetSmoothingFactor(smoothLookSpeed, deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, lookT);
            SyncAnglesFromTransform();
        }

        private void SyncAnglesFromTransform()
        {
            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            _pitch = Mathf.Clamp(_pitch, -85f, 85f);
        }

        private void ResolveTargets()
        {
            if (bucketTarget == null)
            {
                GameObject bucket = GameObject.Find("BucketRig");
                if (bucket != null)
                {
                    bucketTarget = bucket.transform;
                }
            }

            if (canvasTarget == null)
            {
                GameObject canvas = GameObject.Find("Canvas");
                if (canvas != null)
                {
                    canvasTarget = canvas.transform;
                }
            }
        }

        private static Vector3 GetTargetPosition(Transform target, Vector3 fallback)
        {
            return target != null ? target.position : fallback;
        }

        private static float GetSmoothingFactor(float speed, float deltaTime)
        {
            return 1f - Mathf.Exp(-Mathf.Max(0.01f, speed) * Mathf.Max(0f, deltaTime));
        }

        private void OnValidate()
        {
            moveSpeed = Mathf.Max(0.01f, moveSpeed);
            fastMultiplier = Mathf.Max(1f, fastMultiplier);
            slowMultiplier = Mathf.Clamp(slowMultiplier, 0.05f, 1f);
            mouseSensitivity = Mathf.Max(0.01f, mouseSensitivity);
            smoothFollowSpeed = Mathf.Max(0.01f, smoothFollowSpeed);
            smoothLookSpeed = Mathf.Max(0.01f, smoothLookSpeed);

            if (lookAtBucket && lookAtCanvas)
            {
                lookAtCanvas = false;
            }
        }
    }
}

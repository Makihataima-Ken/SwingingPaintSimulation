using SwingingPaint.BucketFluid;
using UnityEngine;

namespace SwingingPaint.BucketFluid.Core
{
    /// <summary>
    /// Samples BucketRig motion and converts it into local-space forces for the fluid solver.
    ///
    /// The paint particles will be simulated in bucket local space. When the bucket accelerates
    /// in world space, the liquid should appear to lag behind and climb the opposite wall. This
    /// component creates that sloshing input by converting both gravity and the opposite of the
    /// bucket's world acceleration into bucket-local coordinates.
    ///
    /// This component reads Transform data only. It does not use Unity's built-in physics engine.
    /// </summary>
    public class BucketMotionProvider : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Transform that defines the fluid's local simulation space. Defaults to this transform.")]
        public Transform bucketTransform;

        [Tooltip("Optional fluid settings source. If assigned, its gravity value is used.")]
        public BucketFluidSettings settings;

        [Header("Motion Sampling")]
        [Tooltip("Manual gravity magnitude used when no BucketFluidSettings component is assigned.")]
        public float gravity = 9.81f;

        [Tooltip("0 uses raw acceleration. Higher values retain more previous acceleration to reduce jitter.")]
        [Range(0f, 0.99f)]
        public float accelerationSmoothing = 0.25f;

        [Header("Debug Gizmos")]
        public bool drawDebugGizmos = true;
        public float velocityGizmoScale = 0.15f;
        public float accelerationGizmoScale = 0.04f;
        public Color velocityGizmoColor = Color.cyan;
        public Color accelerationGizmoColor = new Color(1f, 0.35f, 0.05f, 1f);

        public Transform BucketTransform => bucketTransform != null ? bucketTransform : transform;
        public Matrix4x4 LocalToWorldMatrix => BucketTransform.localToWorldMatrix;
        public Matrix4x4 WorldToLocalMatrix => BucketTransform.worldToLocalMatrix;

        public Vector3 WorldPosition { get; private set; }
        public Vector3 WorldVelocity { get; private set; }
        public Vector3 WorldAcceleration { get; private set; }
        public Vector3 LocalGravity { get; private set; }
        public Vector3 LocalInertialAcceleration { get; private set; }
        public Vector3 EffectiveLocalAcceleration { get; private set; }

        public Vector3 LocalVelocity => BucketTransform.InverseTransformDirection(WorldVelocity);

        /// <summary>
        /// Raw bucket acceleration converted to local space. Use EffectiveLocalAcceleration for
        /// fluid integration because it includes gravity plus inertial acceleration.
        /// </summary>
        public Vector3 LocalAcceleration => BucketTransform.InverseTransformDirection(WorldAcceleration);

        private Vector3 _previousPosition;
        private Vector3 _previousVelocity;
        private bool _hasPreviousSample;
        private bool _hasWarnedMissingSettings;

        private void Awake()
        {
            ResolveReferences();
            ResetMotionTracking();
        }

        private void Reset()
        {
            ResolveReferences();
            ResetMotionTracking();
        }

        private void FixedUpdate()
        {
            ResolveReferences();
            SampleMotion(Time.fixedDeltaTime);
            UpdateLocalAccelerationVectors();
        }

        public void ResetMotionTracking()
        {
            Transform target = BucketTransform;
            WorldPosition = target.position;
            WorldVelocity = Vector3.zero;
            WorldAcceleration = Vector3.zero;

            _previousPosition = WorldPosition;
            _previousVelocity = Vector3.zero;
            _hasPreviousSample = true;

            UpdateLocalAccelerationVectors();
        }

        private void ResolveReferences()
        {
            if (bucketTransform == null)
            {
                bucketTransform = transform;
            }

            if (settings == null)
            {
                settings = GetComponent<BucketFluidSettings>();
            }

            if (settings == null && !_hasWarnedMissingSettings)
            {
                _hasWarnedMissingSettings = true;
                Debug.LogWarning(
                    "BucketMotionProvider could not find BucketFluidSettings on this object. " +
                    "Assign the BucketRig BucketFluidSettings component to the settings field.",
                    this
                );
            }
        }

        private void SampleMotion(float deltaTime)
        {
            Transform target = BucketTransform;
            Vector3 currentPosition = target.position;

            if (deltaTime <= Mathf.Epsilon)
            {
                WorldPosition = currentPosition;
                WorldVelocity = Vector3.zero;
                WorldAcceleration = Vector3.zero;
                return;
            }

            if (!_hasPreviousSample)
            {
                ResetMotionTracking();
                return;
            }

            Vector3 currentVelocity = (currentPosition - _previousPosition) / deltaTime;
            Vector3 rawAcceleration = (currentVelocity - _previousVelocity) / deltaTime;

            float previousWeight = Mathf.Clamp01(accelerationSmoothing);
            WorldAcceleration = Vector3.Lerp(rawAcceleration, WorldAcceleration, previousWeight);
            WorldVelocity = currentVelocity;
            WorldPosition = currentPosition;

            _previousPosition = currentPosition;
            _previousVelocity = currentVelocity;
        }

        private void UpdateLocalAccelerationVectors()
        {
            Transform target = BucketTransform;
            float activeGravity = settings != null ? settings.gravity : gravity;

            LocalGravity = target.InverseTransformDirection(Vector3.down * activeGravity);
            LocalInertialAcceleration = target.InverseTransformDirection(-WorldAcceleration);
            EffectiveLocalAcceleration = LocalGravity + LocalInertialAcceleration;
        }

        private void OnValidate()
        {
            gravity = Mathf.Max(0f, gravity);
            accelerationSmoothing = Mathf.Clamp(accelerationSmoothing, 0f, 0.99f);
            velocityGizmoScale = Mathf.Max(0f, velocityGizmoScale);
            accelerationGizmoScale = Mathf.Max(0f, accelerationGizmoScale);
        }

        private void OnDrawGizmos()
        {
            if (!drawDebugGizmos)
            {
                return;
            }

            Transform target = bucketTransform != null ? bucketTransform : transform;
            Vector3 origin = Application.isPlaying ? WorldPosition : target.position;

            Gizmos.color = velocityGizmoColor;
            Gizmos.DrawLine(origin, origin + WorldVelocity * velocityGizmoScale);

            Gizmos.color = accelerationGizmoColor;
            Gizmos.DrawLine(origin, origin + WorldAcceleration * accelerationGizmoScale);
        }
    }
}

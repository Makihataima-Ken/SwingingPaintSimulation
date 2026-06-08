using UnityEngine;
using SwingingPaint.Core;

/// <summary>
/// Manually simulates a damped pendulum for the paint bucket.
///
/// This script does not use Rigidbody, Colliders, joints, or Unity's built-in physics engine.
/// The BucketRig position is calculated directly from pendulum angle, rope length, gravity,
/// damping, and a swing direction in the horizontal XZ plane. Assign bucketTransform to
/// BucketRig, not BucketModel. BucketRig is the motion point used by the custom physics;
/// BucketModel is only a visual child offset below that point.
/// </summary>
public class Pendulum : MonoBehaviour
{
    [Header("References")]
    [Tooltip("World-space pivot point the rope hangs from. If empty, this GameObject is used.")]
    public Transform anchorTransform;

    [Tooltip("Motion point moved manually by this script. Assign BucketRig here, not the visual BucketModel child.")]
    public Transform bucketTransform;

    [Tooltip("Optional global settings source. If assigned, its TimeScale and SimulationRunning values are used.")]
    public SimulationSettings simulationSettings;

    [Header("Pendulum Settings")]
    [Tooltip("Length of the rope in world units.")]
    public float ropeLength = 2.5f;

    [Tooltip("Starting angle from vertical, in degrees.")]
    public float initialAngleDegrees = 30f;

    [Tooltip("Starting angular velocity, in degrees per second.")]
    public float initialAngularVelocity = 0f;

    [Tooltip("Direction of the swing plane in the XZ canvas plane. 0 degrees swings along +X.")]
    public float directionAngleDegrees = 0f;

    [Tooltip("Damping applied to angular velocity. Higher values settle faster.")]
    public float damping = 0.05f;

    [Tooltip("Manual gravitational acceleration used by the pendulum equation.")]
    public float gravity = 9.81f;

    [Header("Debug Drawing")]
    [Tooltip("Draw anchor, rope, and swing direction gizmos in the Scene view.")]
    public bool drawDebugGizmos = true;

    [Tooltip("Length of the horizontal swing direction gizmo.")]
    public float debugDirectionLength = 1f;

    /// <summary>Current angle from vertical, in degrees.</summary>
    public float CurrentAngle => _currentAngleDegrees;

    /// <summary>Current angular velocity, in degrees per second.</summary>
    public float CurrentAngularVelocity => _currentAngularVelocity;

    /// <summary>Current rope length. Kept as a property for debug/UI scripts.</summary>
    public float CurrentRopeLength => ropeLength;

    /// <summary>No elastic stretch in this first-pass pendulum, so max extension is always zero.</summary>
    public float MaxRopeExtension => 0f;

    /// <summary>Current manually calculated bucket velocity in world units per second.</summary>
    public Vector3 BucketVelocity { get; private set; }

    private float _currentAngleDegrees;
    private float _currentAngularVelocity;
    private Vector3 _previousBucketPosition;
    private bool _hasPreviousBucketPosition;

    private void Awake()
    {
        if (anchorTransform == null)
        {
            anchorTransform = transform;
        }

        if (simulationSettings == null)
        {
            simulationSettings = FindObjectOfType<SimulationSettings>();
        }
    }

    private void Start()
    {
        ResetState(initialAngleDegrees, initialAngularVelocity);
    }

    private void Update()
    {
        if (anchorTransform == null || bucketTransform == null)
        {
            return;
        }

        if (SimulationManager.Instance != null && SimulationManager.Instance.IsPaused)
        {
            return;
        }

        if (simulationSettings != null && !simulationSettings.SimulationRunning)
        {
            return;
        }

        float timeScale = simulationSettings != null ? simulationSettings.TimeScale : 1f;
        float dt = Time.deltaTime * timeScale;

        if (dt <= Mathf.Epsilon)
        {
            BucketVelocity = Vector3.zero;
            return;
        }

        if (!_hasPreviousBucketPosition)
        {
            _previousBucketPosition = bucketTransform.position;
            _hasPreviousBucketPosition = true;
        }

        IntegratePendulum(dt);
        ApplyBucketPosition();
        UpdateBucketVelocity(dt);
    }

    /// <summary>
    /// Resets the pendulum to a specific angle and angular velocity.
    /// </summary>
    public void ResetState(float angleDegrees, float angularVelocityDegrees)
    {
        _currentAngleDegrees = angleDegrees;
        _currentAngularVelocity = angularVelocityDegrees;
        ApplyBucketPosition();
        ResetBucketVelocityTracking();
    }

    /// <summary>
    /// Returns the bucket's current world position, or Vector3.zero if no bucket is assigned.
    /// </summary>
    public Vector3 GetBucketPosition()
    {
        return bucketTransform != null ? bucketTransform.position : Vector3.zero;
    }

    private void IntegratePendulum(float deltaTime)
    {
        if (ropeLength <= 0f)
        {
            return;
        }

        const float maxDeltaTime = 0.05f;
        float dt = Mathf.Min(deltaTime, maxDeltaTime);

        // Store public/debug state in degrees, but integrate in radians because Mathf.Sin expects radians.
        float angleRadians = _currentAngleDegrees * Mathf.Deg2Rad;
        float angularVelocityRadians = _currentAngularVelocity * Mathf.Deg2Rad;

        // Damped pendulum equation:
        // angularAcceleration = -(gravity / ropeLength) * sin(angle) - damping * angularVelocity
        float angularAcceleration = -(gravity / ropeLength) * Mathf.Sin(angleRadians) - damping * angularVelocityRadians;

        // Semi-implicit Euler integration:
        // angularVelocity += angularAcceleration * dt
        // angle += angularVelocity * dt
        angularVelocityRadians += angularAcceleration * dt;
        angleRadians += angularVelocityRadians * dt;

        angleRadians = Mathf.Repeat(angleRadians + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;

        _currentAngleDegrees = angleRadians * Mathf.Rad2Deg;
        _currentAngularVelocity = angularVelocityRadians * Mathf.Rad2Deg;
    }

    private void ApplyBucketPosition()
    {
        if (anchorTransform == null || bucketTransform == null)
        {
            return;
        }

        float angleRadians = _currentAngleDegrees * Mathf.Deg2Rad;
        float directionRadians = directionAngleDegrees * Mathf.Deg2Rad;

        float x = anchorTransform.position.x + ropeLength * Mathf.Sin(angleRadians) * Mathf.Cos(directionRadians);
        float y = anchorTransform.position.y - ropeLength * Mathf.Cos(angleRadians);
        float z = anchorTransform.position.z + ropeLength * Mathf.Sin(angleRadians) * Mathf.Sin(directionRadians);

        // Move the bucket visually by assigning its Transform directly every frame.
        // This is custom motion only; no Rigidbody or Collider is involved.
        bucketTransform.position = new Vector3(x, y, z);
    }

    private void UpdateBucketVelocity(float dt)
    {
        Vector3 currentPosition = bucketTransform.position;
        BucketVelocity = (currentPosition - _previousBucketPosition) / dt;
        _previousBucketPosition = currentPosition;
    }

    private void ResetBucketVelocityTracking()
    {
        BucketVelocity = Vector3.zero;

        if (bucketTransform == null)
        {
            _hasPreviousBucketPosition = false;
            return;
        }

        _previousBucketPosition = bucketTransform.position;
        _hasPreviousBucketPosition = true;
    }

    private void OnValidate()
    {
        ropeLength = Mathf.Max(0.01f, ropeLength);
        damping = Mathf.Max(0f, damping);
        gravity = Mathf.Max(0f, gravity);
        debugDirectionLength = Mathf.Max(0f, debugDirectionLength);
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        Transform anchor = anchorTransform != null ? anchorTransform : transform;
        Vector3 anchorPosition = anchor.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(anchorPosition, 0.08f);

        float directionRadians = directionAngleDegrees * Mathf.Deg2Rad;
        Vector3 swingDirection = new Vector3(
            Mathf.Cos(directionRadians),
            0f,
            Mathf.Sin(directionRadians)
        );

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(anchorPosition, anchorPosition + swingDirection * debugDirectionLength);

        if (bucketTransform != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(anchorPosition, bucketTransform.position);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(bucketTransform.position, 0.12f);
        }
    }
}

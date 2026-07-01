using UnityEngine;

/// <summary>
/// Rotates BucketRig so the bucket visually hangs from the rope direction.
///
/// This is transform-only orientation. It does not use Rigidbody, Colliders, joints,
/// raycasts, MeshCollider, or Unity's built-in physics engine.
/// </summary>
public class BucketOrientationController : MonoBehaviour
{
    [Header("References")]
    public Transform pivotPoint;
    public Transform bucketRig;
    public Transform ropeAttachment;
    public Transform bucketVisualRoot;
    public Transform paintHole;

    [Header("Local Axes")]
    public Vector3 bucketLocalUpAxis = Vector3.up;
    public Vector3 bucketLocalForwardAxis = Vector3.forward;

    [Header("Motion")]
    [Min(0f)]
    public float rotationSmoothSpeed = 12f;
    [Min(0f)]
    public float maxVisualLagAngle;
    public float rollAmount;
    public bool alignToRope = true;

    [Header("Gizmos")]
    public bool drawGizmos = true;
    [Min(0f)]
    public float gizmoLength = 0.4f;

    [Header("Runtime Driving")]
    [Tooltip("Skip this component's fallback LateUpdate when SimulationManager is driving fixed-step simulation.")]
    public bool useSimulationManagerDriver = true;

    public Vector3 CurrentRopeDirection { get; private set; } = Vector3.down;
    public Quaternion CurrentTargetRotation { get; private set; } = Quaternion.identity;
    public Vector3 CurrentForwardDirection { get; private set; } = Vector3.forward;

    private Vector3 _previousForward;
    private Vector3 _previousPosition;
    private bool _hasForward;
    private bool _hasPosition;

    private void Awake()
    {
        ResolveReferences();
        ResetOrientation(true);
    }

    private void Reset()
    {
        ResolveReferences();
        ResetOrientation(true);
    }

    private void LateUpdate()
    {
        if (useSimulationManagerDriver &&
            SwingingPaint.Core.SimulationManager.Instance != null &&
            SwingingPaint.Core.SimulationManager.Instance.driveFixedStepSimulation)
        {
            return;
        }

        if (SwingingPaint.Core.SimulationManager.Instance != null &&
            SwingingPaint.Core.SimulationManager.Instance.IsPaused)
        {
            return;
        }

        StepOrientation(Time.deltaTime);
    }

    public void StepOrientation(float deltaTime, bool snap = false)
    {
        ResolveReferences();

        Transform target = GetBucketTransform();
        if (target == null || !alignToRope)
        {
            return;
        }

        Vector3 ropeDirection = GetRopeDirection(target);
        if (ropeDirection.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        ropeDirection.Normalize();
        Vector3 targetUp = -ropeDirection;
        Vector3 bucketVelocity = GetBucketVelocity(target, deltaTime);
        Vector3 forward = GetStableForward(target, targetUp, bucketVelocity);
        Quaternion targetRotation = BuildTargetRotation(forward, targetUp);

        CurrentRopeDirection = ropeDirection;
        CurrentForwardDirection = forward;
        CurrentTargetRotation = targetRotation;

        if (snap || rotationSmoothSpeed <= 0f || deltaTime <= Mathf.Epsilon)
        {
            target.rotation = targetRotation;
        }
        else
        {
            float blend = 1f - Mathf.Exp(-rotationSmoothSpeed * deltaTime);
            target.rotation = Quaternion.Slerp(target.rotation, targetRotation, Mathf.Clamp01(blend));
        }

        _previousForward = ProjectOntoPlane(target.TransformDirection(GetSafeLocalForwardAxis()), targetUp);
        if (_previousForward.sqrMagnitude <= 0.000001f)
        {
            _previousForward = forward;
        }
        else
        {
            _previousForward.Normalize();
        }

        _hasForward = true;
    }

    public void ResetOrientation(bool snap = true)
    {
        ResolveReferences();

        Transform target = GetBucketTransform();
        if (target == null)
        {
            _hasForward = false;
            _hasPosition = false;
            return;
        }

        _previousPosition = target.position;
        _hasPosition = true;
        _previousForward = ProjectOntoPlane(target.TransformDirection(GetSafeLocalForwardAxis()), GetInitialTargetUp(target));
        if (_previousForward.sqrMagnitude <= 0.000001f)
        {
            _previousForward = Vector3.forward;
        }
        else
        {
            _previousForward.Normalize();
        }

        _hasForward = true;
        StepOrientation(0f, snap);
        _previousPosition = target.position;
    }

    private void ResolveReferences()
    {
        if (bucketRig == null)
        {
            bucketRig = transform;
        }

        if (pivotPoint == null)
        {
            Transform root = bucketRig != null ? bucketRig.parent : transform.parent;
            if (root != null)
            {
                pivotPoint = root.Find("PivotPoint");
            }
        }

        if (ropeAttachment == null && bucketRig != null)
        {
            ropeAttachment = bucketRig.Find("RopeAttachment");
        }

        if (bucketVisualRoot == null && bucketRig != null)
        {
            bucketVisualRoot = bucketRig.Find("Bucket");
        }

        if (paintHole == null && bucketRig != null)
        {
            paintHole = bucketRig.Find("PaintHole");
        }
    }

    private Transform GetBucketTransform()
    {
        return bucketRig != null ? bucketRig : transform;
    }

    private Vector3 GetRopeDirection(Transform target)
    {
        if (pivotPoint == null)
        {
            return Vector3.down;
        }

        Vector3 referencePosition = target.position;
        Vector3 direction = referencePosition - pivotPoint.position;
        return direction.sqrMagnitude > 0.000001f ? direction : Vector3.down;
    }

    private Vector3 GetBucketVelocity(Transform target, float deltaTime)
    {
        Vector3 position = target.position;
        Vector3 velocity = Vector3.zero;

        if (_hasPosition && deltaTime > Mathf.Epsilon)
        {
            velocity = (position - _previousPosition) / deltaTime;
        }

        _previousPosition = position;
        _hasPosition = true;
        return velocity;
    }

    private Vector3 GetStableForward(Transform target, Vector3 targetUp, Vector3 bucketVelocity)
    {
        Vector3 forward = _hasForward ? _previousForward : target.TransformDirection(GetSafeLocalForwardAxis());
        forward = ProjectOntoPlane(forward, targetUp);

        if (forward.sqrMagnitude <= 0.000001f)
        {
            forward = ProjectOntoPlane(bucketVelocity, targetUp);
        }

        if (forward.sqrMagnitude <= 0.000001f)
        {
            forward = ProjectOntoPlane(Vector3.forward, targetUp);
        }

        if (forward.sqrMagnitude <= 0.000001f)
        {
            forward = ProjectOntoPlane(Vector3.right, targetUp);
        }

        return forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector3.forward;
    }

    private Quaternion BuildTargetRotation(Vector3 forwardWorld, Vector3 upWorld)
    {
        Quaternion worldBasis = Quaternion.LookRotation(forwardWorld, upWorld);
        Quaternion localBasis = Quaternion.LookRotation(GetSafeLocalForwardAxis(), GetSafeLocalUpAxis());
        Quaternion targetRotation = worldBasis * Quaternion.Inverse(localBasis);

        if (maxVisualLagAngle > 0f && Mathf.Abs(rollAmount) > 0.0001f)
        {
            targetRotation *= Quaternion.AngleAxis(Mathf.Clamp(rollAmount, -maxVisualLagAngle, maxVisualLagAngle), GetSafeLocalForwardAxis());
        }

        return targetRotation;
    }

    private Vector3 GetInitialTargetUp(Transform target)
    {
        Vector3 ropeDirection = GetRopeDirection(target);
        if (ropeDirection.sqrMagnitude <= 0.000001f)
        {
            return target.TransformDirection(GetSafeLocalUpAxis());
        }

        return -ropeDirection.normalized;
    }

    private Vector3 GetSafeLocalUpAxis()
    {
        return bucketLocalUpAxis.sqrMagnitude > 0.000001f ? bucketLocalUpAxis.normalized : Vector3.up;
    }

    private Vector3 GetSafeLocalForwardAxis()
    {
        Vector3 up = GetSafeLocalUpAxis();
        Vector3 forward = bucketLocalForwardAxis.sqrMagnitude > 0.000001f
            ? bucketLocalForwardAxis.normalized
            : Vector3.forward;

        forward = ProjectOntoPlane(forward, up);
        if (forward.sqrMagnitude <= 0.000001f)
        {
            forward = ProjectOntoPlane(Vector3.forward, up);
        }

        if (forward.sqrMagnitude <= 0.000001f)
        {
            forward = ProjectOntoPlane(Vector3.right, up);
        }

        return forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector3.forward;
    }

    private static Vector3 ProjectOntoPlane(Vector3 vector, Vector3 planeNormal)
    {
        if (planeNormal.sqrMagnitude <= 0.000001f)
        {
            return vector;
        }

        return Vector3.ProjectOnPlane(vector, planeNormal.normalized);
    }

    private void OnValidate()
    {
        rotationSmoothSpeed = Mathf.Max(0f, rotationSmoothSpeed);
        maxVisualLagAngle = Mathf.Max(0f, maxVisualLagAngle);
        gizmoLength = Mathf.Max(0f, gizmoLength);
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
        {
            return;
        }

        ResolveReferences();
        Transform target = GetBucketTransform();
        if (target == null)
        {
            return;
        }

        Vector3 origin = target.position;
        Vector3 ropeDirection = GetRopeDirection(target);
        if (ropeDirection.sqrMagnitude > 0.000001f)
        {
            ropeDirection.Normalize();
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(pivotPoint != null ? pivotPoint.position : origin - ropeDirection * gizmoLength, origin);
        }

        Gizmos.color = Color.green;
        Gizmos.DrawLine(origin, origin + target.TransformDirection(GetSafeLocalUpAxis()) * gizmoLength);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + target.TransformDirection(GetSafeLocalForwardAxis()) * gizmoLength);

        if (paintHole != null)
        {
            Gizmos.color = new Color(1f, 0.25f, 0.05f, 1f);
            Gizmos.DrawLine(paintHole.position, paintHole.position + target.TransformDirection(Vector3.down) * gizmoLength);
        }

        if (ropeAttachment != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 1f);
            Gizmos.DrawSphere(ropeAttachment.position, Mathf.Max(0.01f, gizmoLength * 0.08f));
        }
    }
}

using UnityEngine;
using UnityEngine.Serialization;
using SwingingPaint.Core;

/// <summary>
/// Manually simulates the swinging paint bucket.
///
/// The default mode is a custom 3D elastic pendulum: the bucket is a point mass moved by
/// gravity, rope tension, radial damping, air resistance, and an initial push velocity. It does
/// not use Rigidbody, Colliders, SpringJoint, any Unity joint, or Unity's built-in physics engine.
///
/// PlanarPendulum is kept as a fallback/debug comparison for the earlier single-plane elastic
/// pendulum model.
/// </summary>
public class Pendulum : MonoBehaviour
{
    public enum MotionMode
    {
        Physical3DPendulum,
        PlanarPendulum
    }

    [Header("References")]
    [Tooltip("World-space pivot point the rope hangs from. If empty, this GameObject is used.")]
    public Transform anchorTransform;

    [Tooltip("Motion point moved manually by this script. Assign BucketRig here, not the visual BucketModel child.")]
    public Transform bucketTransform;

    [Tooltip("Optional global settings source. If assigned, its TimeScale and SimulationRunning values are used.")]
    public SimulationSettings simulationSettings;

    [Header("Pendulum Settings")]
    [Tooltip("Starting angle from vertical, in degrees.")]
    public float initialAngleDegrees = 30f;

    [Tooltip("Starting forward angular velocity in the pull direction, in degrees per second.")]
    public float initialAngularVelocity = 0f;

    [Tooltip("Starting sideways angular velocity, in degrees per second. Non-zero values create a natural 3D pushed swing.")]
    public float initialLateralAngularVelocityDegrees = 25f;

    [Tooltip("Direction of the initial pull/push in the XZ plane. 0 degrees points along +X.")]
    public float directionAngleDegrees = 0f;

    [Tooltip("Tangential damping applied to swinging motion. Higher values settle the swing faster.")]
    public float damping = 0.05f;

    [Tooltip("Manual gravitational acceleration used by the pendulum equation.")]
    public float gravity = 9.81f;

    [Header("Motion Mode")]
    public MotionMode motionMode = MotionMode.Physical3DPendulum;

    [HideInInspector]
    public float swingDirectionDegrees = 0f;

    [Header("3D Physical Motion")]
    [Tooltip("Hard cap on world velocity for the 3D point-mass bucket.")]
    [Min(0.1f)]
    public float maxWorldSpeed = 8f;

    [Header("Moving Mass")]
    [Tooltip("Dry bucket mass in kilograms for the custom pendulum model.")]
    public float bucketMass = 1.2f;

    [Tooltip("Remaining paint mass inside the bucket in kilograms. Future pouring systems should reduce this value.")]
    public float paintMass = 1f;

    [Tooltip("Linear air resistance coefficient applied to custom motion.")]
    public float airResistance = 0.02f;

    [Tooltip("Maximum completed half-swings before motion is stopped. 0 means unlimited.")]
    public int swingCountLimit = 0;

    [Header("Elastic Rope")]
    [FormerlySerializedAs("ropeLength")]
    [Tooltip("Rest (unstretched) length of the rope in world units. The Hooke restoring force pulls the bucket back toward this length.")]
    public float restLength = 2f;

    [Tooltip("Spring stiffness k (Hooke's Law F = -k * x). Higher values make the rope stiffer and stretch less.")]
    public float ropeStiffness = 50f;

    [Tooltip("Damping applied to radial (stretch) motion. Higher values stop the rope bouncing in/out faster.")]
    public float ropeDamping = 0.5f;

    [Tooltip("Elasticity factor. Softens the spring (k_eff = stiffness / (1 + elasticity)); higher = more stretch under load.")]
    public float ropeElasticity = 0.5f;

    [Header("Stability")]
    [Tooltip("Fixed integration sub-step in seconds. Stiff springs need a small step; the frame's time is split into steps no larger than this.")]
    public float maxSubStep = 0.005f;

    [Tooltip("Largest frame time (seconds) ever fed to the integrator. Clamps lag/pause spikes so a long frame cannot explode the spring.")]
    public float maxFrameTime = 0.1f;

    [Tooltip("Hard safety cap on rope length as a multiple of restLength. The rope can never stretch beyond this.")]
    public float maxStretchMultiplier = 4f;

    [Tooltip("Hard cap on radial (stretch) speed, world units per second.")]
    public float maxRadialSpeed = 50f;

    [Tooltip("Hard cap on angular speed, radians per second, for planar fallback mode.")]
    public float maxAngularSpeed = 50f;

    [Header("Debug Drawing")]
    [Tooltip("Draw anchor, rope, and initial push direction gizmos in the Scene view.")]
    public bool drawDebugGizmos = true;

    [Tooltip("Length of the horizontal swing direction gizmo.")]
    public float debugDirectionLength = 1f;

    [Header("Runtime Driving")]
    [Tooltip("Skip this component's Update loop when SimulationManager is driving fixed-step simulation.")]
    public bool useSimulationManagerDriver = true;

    /// <summary>Smallest length the rope is allowed to shrink to. Avoids the 1/r singularity in the angular equation.</summary>
    private const float MinLength = 0.05f;

    /// <summary>Smallest mass used in divisions. Avoids invalid acceleration when values are edited aggressively.</summary>
    private const float MinMovingMass = 0.01f;

    /// <summary>Dead-zone around vertical so numerical jitter does not double-count planar swings.</summary>
    private const float SwingCountDeadZoneDegrees = 0.25f;

    /// <summary>World-space projection dead-zone for 3D swing counting.</summary>
    private const float SwingProjectionDeadZone = 0.01f;

    /// <summary>Hard cap on sub-steps per frame so a pathological dt can never spin forever.</summary>
    private const int MaxSubStepsPerFrame = 64;

    /// <summary>Current angle from vertical, in degrees.</summary>
    public float CurrentAngle => _currentAngleDegrees;

    /// <summary>Current angular velocity, in degrees per second.</summary>
    public float CurrentAngularVelocity => _currentAngularVelocity;

    /// <summary>Current (dynamic, possibly stretched) rope length in world units.</summary>
    public float CurrentRopeLength => _currentLength;

    /// <summary>Rest (unstretched) rope length. Convenience accessor for visual/UI scripts.</summary>
    public float RestLength => restLength;

    /// <summary>Total mass moved by the rope: dry bucket plus remaining paint.</summary>
    public float TotalMovingMass => Mathf.Max(MinMovingMass, bucketMass + paintMass);

    /// <summary>Number of completed half-swings across the center since the last reset.</summary>
    public int CompletedSwingCount => _completedSwingCount;

    /// <summary>True once swingCountLimit has been reached. A limit of 0 is unlimited.</summary>
    public bool SwingLimitReached => _swingLimitReached;

    /// <summary>
    /// Backward-compatible alias for the old fixed-length field. Reads/writes the rope rest length so
    /// existing callers keep compiling and behaving sensibly.
    /// </summary>
    public float ropeLength
    {
        get => restLength;
        set => restLength = value;
    }

    /// <summary>Largest rope extension (beyond rest length) observed since the last reset.</summary>
    public float MaxRopeExtension => _maxRopeExtension;

    /// <summary>Current positive rope extension beyond rest length.</summary>
    public float CurrentRopeExtension => Mathf.Max(0f, _currentLength - restLength);

    /// <summary>Current custom Hooke tension in the rope. Zero when the rope is slack.</summary>
    public float CurrentRopeTension => _currentRopeTension;

    /// <summary>Normalizes current tension against a safe visual reference for rendering feedback.</summary>
    public float NormalizedRopeTension
    {
        get
        {
            float reference = Mathf.Max(0.001f, EffectiveStiffness() * restLength * 0.25f);
            return Mathf.Clamp01(_currentRopeTension / reference);
        }
    }

    /// <summary>Current manually calculated bucket velocity in world units per second.</summary>
    public Vector3 BucketVelocity { get; private set; }

    private float _currentAngleDegrees;
    private float _currentAngularVelocity;
    private float _currentLength;
    private float _lengthVelocity;
    private float _maxRopeExtension;
    private float _currentRopeTension;
    private int _completedSwingCount;
    private int _lastSwingSide;
    private int _lastSwingProjectionSign;
    private bool _hasSwingCounterState;
    private bool _swingLimitReached;
    private Vector3 _previousBucketPosition;
    private bool _hasPreviousBucketPosition;
    private Vector3 _bucketPosition;
    private Vector3 _bucketVelocity;
    private Vector3 _initialPullDirection = Vector3.right;

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
        if (useSimulationManagerDriver &&
            SimulationManager.Instance != null &&
            SimulationManager.Instance.driveFixedStepSimulation)
        {
            return;
        }

        float timeScale = simulationSettings != null ? simulationSettings.TimeScale : 1f;
        StepSimulation(Time.deltaTime * timeScale);
    }

    public void StepSimulation(float dt)
    {
        if (anchorTransform == null || bucketTransform == null)
        {
            return;
        }

        if (SimulationManager.Instance != null && SimulationManager.Instance.IsPaused)
        {
            return;
        }

        if (_swingLimitReached)
        {
            StopActiveMotion();
            return;
        }

        if (simulationSettings != null && !simulationSettings.SimulationRunning)
        {
            return;
        }

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

        if (motionMode == MotionMode.Physical3DPendulum)
        {
            EnsureWorldStateFromTransform();
            IntegrateWorldPendulum(dt);
            ApplyBucketPosition();
            BucketVelocity = _bucketVelocity;
            _previousBucketPosition = bucketTransform.position;
            _hasPreviousBucketPosition = true;
        }
        else
        {
            IntegrateElasticPendulum(dt);
            ApplyBucketPosition();
            UpdateBucketVelocity(dt);
        }
    }

    /// <summary>
    /// Resets the pendulum to a specific angle and angular velocity.
    /// The rope length is pre-loaded to its static gravity equilibrium at that angle so the
    /// simulation does not "drop" and bounce on the first frame.
    /// </summary>
    public void ResetState(float angleDegrees, float angularVelocityDegrees)
    {
        _currentAngleDegrees = angleDegrees;
        _currentAngularVelocity = angularVelocityDegrees;

        float kEff = EffectiveStiffness();
        float movingMass = TotalMovingMass;
        float gravityAlongRope = gravity * Mathf.Cos(angleDegrees * Mathf.Deg2Rad);
        float equilibriumExtension = kEff > 0f ? movingMass * Mathf.Max(0f, gravityAlongRope) / kEff : 0f;

        _currentLength = Mathf.Clamp(restLength + equilibriumExtension, MinLength, restLength * Mathf.Max(1f, maxStretchMultiplier));
        _lengthVelocity = 0f;
        _currentRopeTension = kEff * Mathf.Max(0f, _currentLength - restLength);
        _maxRopeExtension = 0f;
        _completedSwingCount = 0;
        _lastSwingSide = SignWithDeadZone(_currentAngleDegrees);
        _hasSwingCounterState = _lastSwingSide != 0;
        _swingLimitReached = false;
        swingDirectionDegrees = directionAngleDegrees;

        if (motionMode == MotionMode.Physical3DPendulum)
        {
            InitializeWorldState(angleDegrees, angularVelocityDegrees);
        }

        ApplyBucketPosition();
        ResetBucketVelocityTracking();
    }

    /// <summary>
    /// Updates the remaining paint mass. Pouring will call this later; clamped here for safety.
    /// </summary>
    public void SetPaintMass(float value)
    {
        paintMass = Mathf.Max(0f, value);
    }

    /// <summary>
    /// Returns the bucket's current world position, or Vector3.zero if no bucket is assigned.
    /// </summary>
    public Vector3 GetBucketPosition()
    {
        return bucketTransform != null ? bucketTransform.position : _bucketPosition;
    }

    /// <summary>Spring stiffness after elasticity softening. Elasticity makes the rope stretch more.</summary>
    private float EffectiveStiffness()
    {
        return ropeStiffness / (1f + Mathf.Max(0f, ropeElasticity));
    }

    private void InitializeWorldState(float angleDegrees, float angularVelocityDegrees)
    {
        Vector3 anchorPosition = anchorTransform != null ? anchorTransform.position : transform.position;
        Vector3 pullDirection = GetHorizontalDirection(directionAngleDegrees);
        float angleRadians = angleDegrees * Mathf.Deg2Rad;

        Vector3 ropeOffset = Vector3.down * (_currentLength * Mathf.Cos(angleRadians)) +
                             pullDirection * (_currentLength * Mathf.Sin(angleRadians));
        if (ropeOffset.sqrMagnitude <= 0.000001f)
        {
            ropeOffset = Vector3.down * Mathf.Max(MinLength, _currentLength);
        }

        _bucketPosition = anchorPosition + ropeOffset;
        Vector3 ropeDirection = ropeOffset.normalized;

        Vector3 forwardTangent = ProjectOntoPlane(
            pullDirection * Mathf.Cos(angleRadians) + Vector3.up * Mathf.Sin(angleRadians),
            ropeDirection
        );
        forwardTangent = SafeNormalized(forwardTangent, ProjectOntoPlane(pullDirection, ropeDirection));

        Vector3 lateralTangent = ProjectOntoPlane(GetHorizontalLateralDirection(pullDirection), ropeDirection);
        lateralTangent = SafeNormalized(lateralTangent, Vector3.Cross(Vector3.up, ropeDirection));

        float forwardSpeed = angularVelocityDegrees * Mathf.Deg2Rad * _currentLength;
        float lateralSpeed = initialLateralAngularVelocityDegrees * Mathf.Deg2Rad * _currentLength;
        _bucketVelocity = forwardTangent * forwardSpeed + lateralTangent * lateralSpeed;

        if (_bucketVelocity.magnitude > maxWorldSpeed)
        {
            _bucketVelocity = _bucketVelocity.normalized * maxWorldSpeed;
        }

        Vector3 horizontalOffset = Vector3.ProjectOnPlane(ropeOffset, Vector3.up);
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(_bucketVelocity, Vector3.up);
        _initialPullDirection = horizontalOffset.sqrMagnitude > SwingProjectionDeadZone * SwingProjectionDeadZone
            ? horizontalOffset.normalized
            : SafeNormalized(horizontalVelocity, pullDirection);
        _lastSwingProjectionSign = SignWithDeadZone(Vector3.Dot(horizontalOffset, _initialPullDirection), SwingProjectionDeadZone);
        _hasSwingCounterState = _lastSwingProjectionSign != 0;

        UpdateWorldDerivedState();
    }

    private void EnsureWorldStateFromTransform()
    {
        if (_bucketPosition.sqrMagnitude > 0.000001f)
        {
            return;
        }

        _bucketPosition = bucketTransform != null ? bucketTransform.position : GetAnchorPosition() + Vector3.down * restLength;
        _bucketVelocity = Vector3.zero;
        _initialPullDirection = GetHorizontalDirection(directionAngleDegrees);
        UpdateWorldDerivedState();
    }

    private void IntegrateWorldPendulum(float deltaTime)
    {
        if (restLength <= 0f)
        {
            return;
        }

        float remaining = Mathf.Min(deltaTime, maxFrameTime);
        float step = Mathf.Max(0.0005f, maxSubStep);

        int guard = 0;
        while (remaining > 0f && guard++ < MaxSubStepsPerFrame && !_swingLimitReached)
        {
            float h = Mathf.Min(remaining, step);
            IntegrateWorldStep(h);
            remaining -= h;
        }
    }

    private void IntegrateWorldStep(float h)
    {
        Vector3 previousPosition = _bucketPosition;
        Vector3 anchorPosition = GetAnchorPosition();
        Vector3 offset = _bucketPosition - anchorPosition;
        float length = offset.magnitude;

        if (length <= MinLength)
        {
            length = MinLength;
            offset = Vector3.down * length;
            _bucketPosition = anchorPosition + offset;
        }

        Vector3 ropeDirection = offset / length;
        float kEff = EffectiveStiffness();
        float movingMass = TotalMovingMass;
        float extension = length - restLength;
        float springTension = extension > 0f ? kEff * extension : 0f;
        float radialSpeed = Vector3.Dot(_bucketVelocity, ropeDirection);

        Vector3 radialVelocity = ropeDirection * radialSpeed;
        Vector3 tangentialVelocity = _bucketVelocity - radialVelocity;
        Vector3 acceleration = Vector3.down * gravity;

        acceleration += -ropeDirection * (springTension / movingMass);
        acceleration += -ropeDirection * (ropeDamping * radialSpeed / movingMass);
        acceleration += -tangentialVelocity * Mathf.Max(0f, damping);
        acceleration += -_bucketVelocity * (airResistance / movingMass);

        _bucketVelocity += acceleration * h;
        if (_bucketVelocity.magnitude > maxWorldSpeed)
        {
            _bucketVelocity = _bucketVelocity.normalized * maxWorldSpeed;
        }

        _bucketPosition += _bucketVelocity * h;
        EnforceWorldRopeLimits(anchorPosition);
        UpdateWorldDerivedState();
        Update3DSwingCounter(previousPosition, _bucketPosition);
    }

    private void EnforceWorldRopeLimits(Vector3 anchorPosition)
    {
        Vector3 offset = _bucketPosition - anchorPosition;
        float length = offset.magnitude;

        if (length <= 0.000001f)
        {
            offset = Vector3.down * MinLength;
            length = MinLength;
        }

        Vector3 ropeDirection = offset / length;
        float maxLen = restLength * Mathf.Max(1f, maxStretchMultiplier);
        float clampedLength = Mathf.Clamp(length, MinLength, maxLen);

        if (!Mathf.Approximately(clampedLength, length))
        {
            float radialSpeed = Vector3.Dot(_bucketVelocity, ropeDirection);
            _bucketPosition = anchorPosition + ropeDirection * clampedLength;

            if ((length > maxLen && radialSpeed > 0f) || (length < MinLength && radialSpeed < 0f))
            {
                _bucketVelocity -= ropeDirection * radialSpeed;
            }
        }
    }

    private void UpdateWorldDerivedState()
    {
        Vector3 anchorPosition = GetAnchorPosition();
        Vector3 offset = _bucketPosition - anchorPosition;
        float length = Mathf.Max(MinLength, offset.magnitude);
        Vector3 ropeDirection = offset.sqrMagnitude > 0.000001f ? offset / length : Vector3.down;
        float extension = Mathf.Max(0f, length - restLength);
        Vector3 radialVelocity = ropeDirection * Vector3.Dot(_bucketVelocity, ropeDirection);
        Vector3 tangentialVelocity = _bucketVelocity - radialVelocity;

        _currentLength = length;
        _lengthVelocity = Vector3.Dot(_bucketVelocity, ropeDirection);
        _currentRopeTension = EffectiveStiffness() * extension;
        _currentAngleDegrees = Vector3.Angle(Vector3.down, ropeDirection);
        _currentAngularVelocity = tangentialVelocity.magnitude / length * Mathf.Rad2Deg;
        _maxRopeExtension = Mathf.Max(_maxRopeExtension, extension);
    }

    private void Update3DSwingCounter(Vector3 previousPosition, Vector3 currentPosition)
    {
        if (swingCountLimit <= 0 || _swingLimitReached)
        {
            return;
        }

        int currentSide = SignWithDeadZone(GetSwingProjection(currentPosition), SwingProjectionDeadZone);
        if (currentSide == 0)
        {
            return;
        }

        if (!_hasSwingCounterState)
        {
            int previousSide = SignWithDeadZone(GetSwingProjection(previousPosition), SwingProjectionDeadZone);
            _lastSwingProjectionSign = previousSide != 0 ? previousSide : currentSide;
            _hasSwingCounterState = true;
            return;
        }

        if (currentSide == _lastSwingProjectionSign)
        {
            return;
        }

        _lastSwingProjectionSign = currentSide;
        _completedSwingCount++;

        if (_completedSwingCount >= swingCountLimit)
        {
            _bucketVelocity = Vector3.zero;
            BucketVelocity = Vector3.zero;
            _swingLimitReached = true;
        }
    }

    private float GetSwingProjection(Vector3 position)
    {
        Vector3 offset = position - GetAnchorPosition();
        Vector3 horizontalOffset = Vector3.ProjectOnPlane(offset, Vector3.up);
        return Vector3.Dot(horizontalOffset, _initialPullDirection);
    }

    private void IntegrateElasticPendulum(float deltaTime)
    {
        if (restLength <= 0f)
        {
            return;
        }

        float remaining = Mathf.Min(deltaTime, maxFrameTime);
        float step = Mathf.Max(0.0005f, maxSubStep);

        int guard = 0;
        while (remaining > 0f && guard++ < MaxSubStepsPerFrame && !_swingLimitReached)
        {
            float h = Mathf.Min(remaining, step);
            IntegratePlanarStep(h);
            remaining -= h;
        }
    }

    /// <summary>
    /// Advances the planar fallback (r, theta) state by one sub-step using symplectic Euler.
    /// </summary>
    private void IntegratePlanarStep(float h)
    {
        float theta = _currentAngleDegrees * Mathf.Deg2Rad;
        float thetaDot = _currentAngularVelocity * Mathf.Deg2Rad;
        float r = _currentLength;
        float rDot = _lengthVelocity;

        float kEff = EffectiveStiffness();
        float movingMass = TotalMovingMass;
        float extension = r - restLength;

        float springTension = extension > 0f ? kEff * extension : 0f;
        float springAccel = springTension / movingMass;
        float radialDampingAccel = (ropeDamping + airResistance) * rDot / movingMass;

        float radialAccel = r * thetaDot * thetaDot
                            + gravity * Mathf.Cos(theta)
                            - springAccel
                            - radialDampingAccel;

        float safeR = Mathf.Max(r, MinLength);
        float angularAccel = (-gravity * Mathf.Sin(theta) - 2f * rDot * thetaDot) / safeR
                            - damping * thetaDot
                            - (airResistance * thetaDot / movingMass);

        rDot += radialAccel * h;
        thetaDot += angularAccel * h;

        rDot = Mathf.Clamp(rDot, -maxRadialSpeed, maxRadialSpeed);
        thetaDot = Mathf.Clamp(thetaDot, -maxAngularSpeed, maxAngularSpeed);

        r += rDot * h;
        theta += thetaDot * h;

        float maxLen = restLength * Mathf.Max(1f, maxStretchMultiplier);
        if (r > maxLen)
        {
            r = maxLen;
            if (rDot > 0f) rDot = 0f;
        }
        else if (r < MinLength)
        {
            r = MinLength;
            if (rDot < 0f) rDot = 0f;
        }

        theta = Mathf.Repeat(theta + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;

        float previousAngleDegrees = _currentAngleDegrees;
        _currentAngleDegrees = theta * Mathf.Rad2Deg;
        _currentAngularVelocity = thetaDot * Mathf.Rad2Deg;
        _currentLength = r;
        _lengthVelocity = rDot;
        _currentRopeTension = EffectiveStiffness() * Mathf.Max(0f, _currentLength - restLength);

        UpdatePlanarSwingCounter(previousAngleDegrees, _currentAngleDegrees);

        float positiveExtension = Mathf.Max(0f, r - restLength);
        if (positiveExtension > _maxRopeExtension)
        {
            _maxRopeExtension = positiveExtension;
        }
    }

    private void UpdatePlanarSwingCounter(float previousAngleDegrees, float currentAngleDegrees)
    {
        if (swingCountLimit <= 0 || _swingLimitReached)
        {
            return;
        }

        int currentSide = SignWithDeadZone(currentAngleDegrees);
        if (currentSide == 0)
        {
            return;
        }

        if (!_hasSwingCounterState)
        {
            int previousSide = SignWithDeadZone(previousAngleDegrees);
            _lastSwingSide = previousSide != 0 ? previousSide : currentSide;
            _hasSwingCounterState = true;
            return;
        }

        if (currentSide == _lastSwingSide)
        {
            return;
        }

        _lastSwingSide = currentSide;
        _completedSwingCount++;

        if (_completedSwingCount >= swingCountLimit)
        {
            StopActiveMotion();
            _swingLimitReached = true;
        }
    }

    private void ApplyBucketPosition()
    {
        if (anchorTransform == null || bucketTransform == null)
        {
            return;
        }

        if (motionMode == MotionMode.Physical3DPendulum)
        {
            bucketTransform.position = _bucketPosition;
            return;
        }

        ApplyPlanarPendulumPosition(directionAngleDegrees);
    }

    private void ApplyPlanarPendulumPosition(float directionDegrees)
    {
        float angleRadians = _currentAngleDegrees * Mathf.Deg2Rad;
        float directionRadians = directionDegrees * Mathf.Deg2Rad;
        float r = _currentLength;

        float x = anchorTransform.position.x + r * Mathf.Sin(angleRadians) * Mathf.Cos(directionRadians);
        float y = anchorTransform.position.y - r * Mathf.Cos(angleRadians);
        float z = anchorTransform.position.z + r * Mathf.Sin(angleRadians) * Mathf.Sin(directionRadians);

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

    private void StopActiveMotion()
    {
        _bucketVelocity = Vector3.zero;
        _currentAngularVelocity = 0f;
        _lengthVelocity = 0f;
        BucketVelocity = Vector3.zero;
    }

    private Vector3 GetAnchorPosition()
    {
        return anchorTransform != null ? anchorTransform.position : transform.position;
    }

    private static Vector3 GetHorizontalDirection(float yawDegrees)
    {
        float yawRadians = yawDegrees * Mathf.Deg2Rad;
        Vector3 direction = new Vector3(Mathf.Cos(yawRadians), 0f, Mathf.Sin(yawRadians));
        return direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.right;
    }

    private static Vector3 GetHorizontalLateralDirection(Vector3 pullDirection)
    {
        Vector3 lateral = Vector3.Cross(Vector3.up, pullDirection);
        return lateral.sqrMagnitude > 0.000001f ? lateral.normalized : Vector3.forward;
    }

    private static Vector3 SafeNormalized(Vector3 vector, Vector3 fallback)
    {
        if (vector.sqrMagnitude > 0.000001f)
        {
            return vector.normalized;
        }

        if (fallback.sqrMagnitude > 0.000001f)
        {
            return fallback.normalized;
        }

        return Vector3.right;
    }

    private static Vector3 ProjectOntoPlane(Vector3 vector, Vector3 planeNormal)
    {
        if (planeNormal.sqrMagnitude <= 0.000001f)
        {
            return vector;
        }

        return Vector3.ProjectOnPlane(vector, planeNormal.normalized);
    }

    private static int SignWithDeadZone(float angleDegrees)
    {
        return SignWithDeadZone(angleDegrees, SwingCountDeadZoneDegrees);
    }

    private static int SignWithDeadZone(float value, float deadZone)
    {
        if (value > deadZone)
        {
            return 1;
        }

        if (value < -deadZone)
        {
            return -1;
        }

        return 0;
    }

    private void OnValidate()
    {
        restLength = Mathf.Max(0.01f, restLength);
        ropeStiffness = Mathf.Max(0.01f, ropeStiffness);
        ropeDamping = Mathf.Max(0f, ropeDamping);
        ropeElasticity = Mathf.Max(0f, ropeElasticity);
        damping = Mathf.Max(0f, damping);
        gravity = Mathf.Max(0f, gravity);
        bucketMass = Mathf.Max(MinMovingMass, bucketMass);
        paintMass = Mathf.Max(0f, paintMass);
        airResistance = Mathf.Max(0f, airResistance);
        swingCountLimit = Mathf.Max(0, swingCountLimit);

        maxSubStep = Mathf.Clamp(maxSubStep, 0.0005f, 0.05f);
        maxFrameTime = Mathf.Clamp(maxFrameTime, maxSubStep, 0.5f);
        maxStretchMultiplier = Mathf.Max(1f, maxStretchMultiplier);
        maxRadialSpeed = Mathf.Max(0.1f, maxRadialSpeed);
        maxAngularSpeed = Mathf.Max(0.1f, maxAngularSpeed);
        maxWorldSpeed = Mathf.Max(0.1f, maxWorldSpeed);
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
        Vector3 pullDirection = GetHorizontalDirection(directionAngleDegrees);
        Vector3 lateralDirection = GetHorizontalLateralDirection(pullDirection);

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(anchorPosition, 0.08f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(anchorPosition, anchorPosition + pullDirection * debugDirectionLength);

        if (Mathf.Abs(initialLateralAngularVelocityDegrees) > 0.001f)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.9f, 1f);
            float lateralSign = Mathf.Sign(initialLateralAngularVelocityDegrees);
            Gizmos.DrawLine(anchorPosition, anchorPosition + lateralDirection * lateralSign * debugDirectionLength * 0.75f);
        }

        if (bucketTransform != null)
        {
            float extensionRatio = restLength > 0f
                ? Mathf.Clamp01((bucketTransform.position - anchorPosition).magnitude / restLength - 1f)
                : 0f;
            Gizmos.color = Color.Lerp(Color.white, Color.red, extensionRatio);
            Gizmos.DrawLine(anchorPosition, bucketTransform.position);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(bucketTransform.position, 0.12f);
        }
    }
}

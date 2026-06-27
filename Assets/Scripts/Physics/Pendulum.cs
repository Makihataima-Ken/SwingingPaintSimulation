using UnityEngine;
using UnityEngine.Serialization;
using SwingingPaint.Core;

/// <summary>
/// Manually simulates an ELASTIC (spring) pendulum for the paint bucket.
///
/// This script does not use Rigidbody, Colliders, SpringJoint, any Unity joint, or Unity's
/// built-in physics engine. Everything is integrated by hand.
///
/// Research model
/// --------------
/// The original implementation was a fixed-length damped pendulum: a single angle theta was
/// integrated and the bucket was placed on a circle of constant radius L. This upgrade turns
/// the rigid rod into a stretchy rope by making the rope length r a second dynamic degree of
/// freedom governed by Hooke's Law (F = -k * x).
///
/// The bucket is treated as a point mass in the vertical swing plane defined by
/// directionAngleDegrees (phi) in the horizontal XZ canvas plane. The state is the polar pair
/// (r, theta): r is the current rope length, theta is the angle from the downward vertical.
/// The elastic-pendulum equations of motion use total moving mass:
///
///     extension x   = r - restLength                      (signed stretch)
///     movingMass    = bucketMass + paintMass
///     springAccel   = (k_eff * x) / movingMass if x > 0 else 0
///     r''     = r * theta'^2 + g * cos(theta) - springAccel - dampingForce / movingMass
///     theta'' = (-g * sin(theta) - 2 * r' * theta') / r - damping * theta' - airDrag / movingMass
///
/// where:
///   * k_eff   = ropeStiffness / (1 + ropeElasticity)  -- elasticity softens the spring so a
///               higher ropeElasticity yields more stretch under the same load.
///   * the r*theta'^2 term is the centrifugal stretch from swinging,
///   * g*cos(theta) is gravity projected along the rope,
///   * -springAccel is the Hooke restoring force pulling the bucket back toward restLength,
///   * the -2*r'*theta'/r term is the Coriolis coupling between stretch and swing.
///
/// Position mapping (unchanged from the original, but now using the dynamic length r):
///     x = anchor.x + r * sin(theta) * cos(phi)
///     y = anchor.y - r * cos(theta)
///     z = anchor.z + r * sin(theta) * sin(phi)
///
/// Assign bucketTransform to BucketRig, not BucketModel. BucketRig is the motion point used by
/// the custom physics; BucketModel is only a visual child offset below that point.
/// </summary>
public class Pendulum : MonoBehaviour
{
    public enum MotionMode
    {
        PlanarPendulum,
        DiagonalPendulum,
        EllipseDemo,
        FigureEightDemo
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

    [Tooltip("Starting angular velocity, in degrees per second.")]
    public float initialAngularVelocity = 0f;

    [Tooltip("Direction of the swing plane in the XZ canvas plane. 0 degrees swings along +X.")]
    public float directionAngleDegrees = 0f;

    [Tooltip("Angular (swing) damping applied to angular velocity. Higher values settle the swing faster.")]
    public float damping = 0.05f;

    [Tooltip("Manual gravitational acceleration used by the pendulum equation.")]
    public float gravity = 9.81f;

    [Header("Motion Path Modes")]
    public MotionMode motionMode = MotionMode.PlanarPendulum;

    [Tooltip("Yaw for diagonal/demo motion paths in the XZ plane. 0 degrees points along +X.")]
    public float swingDirectionDegrees = 0f;

    [Tooltip("Demo path amplitude along local X before yaw is applied.")]
    [Min(0f)]
    public float pathAmplitudeX = 1f;

    [Tooltip("Demo path amplitude along local Z before yaw is applied.")]
    [Min(0f)]
    public float pathAmplitudeZ = 0.45f;

    [Tooltip("Angular path speed for demo modes, in radians per second.")]
    [Min(0f)]
    public float pathSpeed = 0.8f;

    [Tooltip("Demo path phase offset in degrees.")]
    public float pathPhase = 0f;

    [Tooltip("When enabled, demo paths solve Y from rope length so the bucket stays under the pivot.")]
    public bool preserveRopeLength = true;

    [Tooltip("When enabled, demo modes use the current elastic rope length. Otherwise they use restLength.")]
    public bool useElasticLength = true;

    [Tooltip("Optional exponential damping for demo path amplitude.")]
    [Min(0f)]
    public float demoDamping = 0f;

    [Tooltip("Draw sampled demo path gizmos in the Scene view.")]
    public bool drawMotionPathGizmo = true;

    [Tooltip("Number of samples used when drawing demo path gizmos.")]
    [Min(8)]
    public int gizmoPathSamples = 120;

    public Color pathGizmoColor = new Color(1f, 0.2f, 0.9f, 0.7f);

    [Header("Moving Mass")]
    [Tooltip("Dry bucket mass in kilograms for the custom pendulum model.")]
    public float bucketMass = 1.2f;

    [Tooltip("Remaining paint mass inside the bucket in kilograms. Future pouring systems should reduce this value.")]
    public float paintMass = 1f;

    [Tooltip("Linear air resistance coefficient applied to radial and angular custom motion.")]
    public float airResistance = 0.02f;

    [Tooltip("Maximum completed half-swings before motion is stopped. 0 means unlimited.")]
    public int swingCountLimit = 0;

    [Header("Elastic Rope")]
    [FormerlySerializedAs("ropeLength")]
    [Tooltip("Rest (unstretched) length of the rope in world units. The Hooke restoring force pulls the bucket back toward this length.")]
    public float restLength = 2f;

    [Tooltip("Spring stiffness k (Hooke's Law F = -k * x). Higher values make the rope stiffer and stretch less.")]
    public float ropeStiffness = 50f;

    [Tooltip("Damping applied to the radial (stretch) motion. Higher values stop the rope bouncing in/out faster.")]
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

    [Tooltip("Hard cap on angular speed, radians per second.")]
    public float maxAngularSpeed = 50f;

    [Header("Debug Drawing")]
    [Tooltip("Draw anchor, rope, and swing direction gizmos in the Scene view.")]
    public bool drawDebugGizmos = true;

    [Header("Runtime Driving")]
    [Tooltip("Skip this component's Update loop when SimulationManager is driving fixed-step simulation.")]
    public bool useSimulationManagerDriver = true;

    [Tooltip("Length of the horizontal swing direction gizmo.")]
    public float debugDirectionLength = 1f;

    /// <summary>Smallest length the rope is allowed to shrink to. Avoids the 1/r singularity in the angular equation.</summary>
    private const float MinLength = 0.05f;

    /// <summary>Smallest mass used in divisions. Avoids invalid acceleration when values are edited aggressively.</summary>
    private const float MinMovingMass = 0.01f;

    /// <summary>Dead-zone around vertical so numerical jitter does not double-count swings.</summary>
    private const float SwingCountDeadZoneDegrees = 0.25f;

    /// <summary>Hard cap on sub-steps per frame so a pathological dt can never spin forever.</summary>
    private const int MaxSubStepsPerFrame = 64;

    private const float MaxDemoHorizontalRopeRatioSqr = 0.95f;

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

    /// <summary>Number of completed half-swings across the vertical since the last reset.</summary>
    public int CompletedSwingCount => _completedSwingCount;

    /// <summary>True once swingCountLimit has been reached. A limit of 0 is unlimited.</summary>
    public bool SwingLimitReached => _swingLimitReached;

    /// <summary>
    /// Backward-compatible alias for the old fixed-length field. Reads/writes the rope rest length so
    /// existing callers (e.g. PendulumPhysicsTestController) keep compiling and behaving sensibly.
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
    private bool _hasSwingCounterState;
    private bool _swingLimitReached;
    private Vector3 _previousBucketPosition;
    private bool _hasPreviousBucketPosition;
    private float _demoTime;
    private float _demoInitialPhase;

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

        if (_swingLimitReached && UsesPhysicalPendulumIntegration())
        {
            BucketVelocity = Vector3.zero;
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

        if (UsesPhysicalPendulumIntegration())
        {
            IntegrateElasticPendulum(dt);
        }
        else
        {
            StepDemoRopeLength(dt);
            _demoTime += dt * pathSpeed;
        }

        ApplyBucketPosition();
        UpdateBucketVelocity(dt);
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

        // Static equilibrium extension: at rest the spring balances gravity along the rope,
        // k_eff * x / m = g * cos(theta)  =>  x = m * g * cos(theta) / k_eff.
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
        _demoTime = 0f;
        _demoInitialPhase = pathPhase * Mathf.Deg2Rad;

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
        return bucketTransform != null ? bucketTransform.position : Vector3.zero;
    }

    /// <summary>Spring stiffness after elasticity softening. Elasticity makes the rope stretch more.</summary>
    private float EffectiveStiffness()
    {
        return ropeStiffness / (1f + Mathf.Max(0f, ropeElasticity));
    }

    private void IntegrateElasticPendulum(float deltaTime)
    {
        if (restLength <= 0f)
        {
            return;
        }

        // Clamp the incoming frame time so a hitch or a long pause cannot inject a huge dt that
        // would blow up the stiff spring, then split it into small fixed sub-steps. Sub-stepping
        // gives FixedUpdate-grade stability while preserving the existing Update + TimeScale path.
        float remaining = Mathf.Min(deltaTime, maxFrameTime);
        float step = Mathf.Max(0.0005f, maxSubStep);

        int guard = 0;
        while (remaining > 0f && guard++ < MaxSubStepsPerFrame && !_swingLimitReached)
        {
            float h = Mathf.Min(remaining, step);
            IntegrateStep(h);
            remaining -= h;
        }
    }

    /// <summary>
    /// Advances the (r, theta) state by one sub-step using symplectic (semi-implicit) Euler.
    /// Velocities are updated from the current-position accelerations first, then positions are
    /// updated from the new velocities. This is energy-stable for oscillators, unlike explicit Euler.
    /// </summary>
    private void IntegrateStep(float h)
    {
        float theta = _currentAngleDegrees * Mathf.Deg2Rad;
        float thetaDot = _currentAngularVelocity * Mathf.Deg2Rad;
        float r = _currentLength;
        float rDot = _lengthVelocity;

        float kEff = EffectiveStiffness();
        float movingMass = TotalMovingMass;
        float extension = r - restLength;

        // Hooke's Law: a rope only pulls (inward), it never pushes. No force while slack (x <= 0).
        float springTension = extension > 0f ? kEff * extension : 0f;
        float springAccel = springTension / movingMass;
        float radialDampingAccel = (ropeDamping + airResistance) * rDot / movingMass;

        // Radial equation of motion: centrifugal + gravity-along-rope - spring - custom damping.
        float radialAccel = r * thetaDot * thetaDot
                            + gravity * Mathf.Cos(theta)
                            - springAccel
                            - radialDampingAccel;

        // Angular equation of motion. Guard the 1/r term against a near-zero length.
        float safeR = Mathf.Max(r, MinLength);
        float angularAccel = (-gravity * Mathf.Sin(theta) - 2f * rDot * thetaDot) / safeR
                            - damping * thetaDot
                            - (airResistance * thetaDot / movingMass);

        // Symplectic Euler: integrate velocities, then positions.
        rDot += radialAccel * h;
        thetaDot += angularAccel * h;

        // Clamp velocities so a transient spike cannot run away between sub-steps.
        rDot = Mathf.Clamp(rDot, -maxRadialSpeed, maxRadialSpeed);
        thetaDot = Mathf.Clamp(thetaDot, -maxAngularSpeed, maxAngularSpeed);

        r += rDot * h;
        theta += thetaDot * h;

        // Hard length clamp with anti-windup: if we hit a bound, kill the velocity pushing past it.
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

        // Keep theta in [-pi, pi] for numerical cleanliness and stable debug readouts.
        theta = Mathf.Repeat(theta + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;

        float previousAngleDegrees = _currentAngleDegrees;
        _currentAngleDegrees = theta * Mathf.Rad2Deg;
        _currentAngularVelocity = thetaDot * Mathf.Rad2Deg;
        _currentLength = r;
        _lengthVelocity = rDot;
        _currentRopeTension = EffectiveStiffness() * Mathf.Max(0f, _currentLength - restLength);

        UpdateSwingCounter(previousAngleDegrees, _currentAngleDegrees);

        float positiveExtension = Mathf.Max(0f, r - restLength);
        if (positiveExtension > _maxRopeExtension)
        {
            _maxRopeExtension = positiveExtension;
        }
    }

    private void UpdateSwingCounter(float previousAngleDegrees, float currentAngleDegrees)
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
            _currentAngularVelocity = 0f;
            _lengthVelocity = 0f;
            BucketVelocity = Vector3.zero;
            _swingLimitReached = true;
        }
    }

    private static int SignWithDeadZone(float angleDegrees)
    {
        if (angleDegrees > SwingCountDeadZoneDegrees)
        {
            return 1;
        }

        if (angleDegrees < -SwingCountDeadZoneDegrees)
        {
            return -1;
        }

        return 0;
    }

    private bool UsesPhysicalPendulumIntegration()
    {
        return motionMode == MotionMode.PlanarPendulum || motionMode == MotionMode.DiagonalPendulum;
    }

    private bool IsDemoMotionMode()
    {
        return motionMode == MotionMode.EllipseDemo || motionMode == MotionMode.FigureEightDemo;
    }

    private void ApplyBucketPosition()
    {
        if (anchorTransform == null || bucketTransform == null)
        {
            return;
        }

        switch (motionMode)
        {
            case MotionMode.DiagonalPendulum:
                ApplyPendulumPosition(swingDirectionDegrees);
                break;
            case MotionMode.EllipseDemo:
            case MotionMode.FigureEightDemo:
                ApplyDemoPathPosition(motionMode);
                break;
            case MotionMode.PlanarPendulum:
            default:
                ApplyPendulumPosition(directionAngleDegrees);
                break;
        }
    }

    private void ApplyPendulumPosition(float swingDirectionDegreesValue)
    {
        float angleRadians = _currentAngleDegrees * Mathf.Deg2Rad;
        float directionRadians = swingDirectionDegreesValue * Mathf.Deg2Rad;
        float r = _currentLength;

        float x = anchorTransform.position.x + r * Mathf.Sin(angleRadians) * Mathf.Cos(directionRadians);
        float y = anchorTransform.position.y - r * Mathf.Cos(angleRadians);
        float z = anchorTransform.position.z + r * Mathf.Sin(angleRadians) * Mathf.Sin(directionRadians);

        // Move the bucket visually by assigning its Transform directly every frame.
        // This is custom motion only; no Rigidbody or Collider is involved.
        bucketTransform.position = new Vector3(x, y, z);
    }

    private void ApplyDemoPathPosition(MotionMode mode)
    {
        float length = GetDemoRopeLength();
        float phase = _demoTime + _demoInitialPhase;
        float dampingScale = GetDemoDampingScale();
        Vector3 localOffset = CalculateDemoLocalOffset(mode, phase, length, dampingScale);
        Vector3 worldOffset = RotateHorizontal(localOffset, swingDirectionDegrees);

        bucketTransform.position = anchorTransform.position + worldOffset;
    }

    private void StepDemoRopeLength(float deltaTime)
    {
        if (!useElasticLength)
        {
            _currentLength = Mathf.Max(MinLength, restLength);
            _lengthVelocity = 0f;
            _currentRopeTension = 0f;
            return;
        }

        float remaining = Mathf.Min(deltaTime, maxFrameTime);
        float step = Mathf.Max(0.0005f, maxSubStep);
        int guard = 0;

        while (remaining > 0f && guard++ < MaxSubStepsPerFrame)
        {
            float h = Mathf.Min(remaining, step);
            IntegrateDemoLengthStep(h);
            remaining -= h;
        }
    }

    private void IntegrateDemoLengthStep(float h)
    {
        float kEff = EffectiveStiffness();
        float movingMass = TotalMovingMass;
        float extension = _currentLength - restLength;
        float springTension = extension > 0f ? kEff * extension : 0f;
        float springAccel = springTension / movingMass;
        float radialDampingAccel = (ropeDamping + airResistance) * _lengthVelocity / movingMass;
        float radialAccel = gravity - springAccel - radialDampingAccel;

        _lengthVelocity += radialAccel * h;
        _lengthVelocity = Mathf.Clamp(_lengthVelocity, -maxRadialSpeed, maxRadialSpeed);
        _currentLength += _lengthVelocity * h;

        float maxLen = restLength * Mathf.Max(1f, maxStretchMultiplier);
        if (_currentLength > maxLen)
        {
            _currentLength = maxLen;
            if (_lengthVelocity > 0f)
            {
                _lengthVelocity = 0f;
            }
        }
        else if (_currentLength < MinLength)
        {
            _currentLength = MinLength;
            if (_lengthVelocity < 0f)
            {
                _lengthVelocity = 0f;
            }
        }

        _currentRopeTension = EffectiveStiffness() * Mathf.Max(0f, _currentLength - restLength);
        _maxRopeExtension = Mathf.Max(_maxRopeExtension, Mathf.Max(0f, _currentLength - restLength));
    }

    private float GetDemoRopeLength()
    {
        float length = useElasticLength ? _currentLength : restLength;
        return Mathf.Max(MinLength, length);
    }

    private float GetDemoDampingScale()
    {
        if (demoDamping <= 0f || pathSpeed <= 0f)
        {
            return 1f;
        }

        float elapsedSeconds = _demoTime / pathSpeed;
        return Mathf.Exp(-demoDamping * Mathf.Max(0f, elapsedSeconds));
    }

    private Vector3 CalculateDemoLocalOffset(MotionMode mode, float phase, float length, float dampingScale)
    {
        float x;
        float z;

        switch (mode)
        {
            case MotionMode.FigureEightDemo:
                x = pathAmplitudeX * Mathf.Sin(phase);
                z = pathAmplitudeZ * Mathf.Sin(phase * 2f) * 0.5f;
                break;
            case MotionMode.EllipseDemo:
            default:
                x = pathAmplitudeX * Mathf.Sin(phase);
                z = pathAmplitudeZ * Mathf.Cos(phase);
                break;
        }

        x *= dampingScale;
        z *= dampingScale;

        Vector2 horizontal = ClampDemoHorizontalOffset(new Vector2(x, z), length);
        float y = -length;

        if (preserveRopeLength)
        {
            float safeVerticalSqr = Mathf.Max(MinLength * MinLength, length * length - horizontal.sqrMagnitude);
            y = -Mathf.Sqrt(safeVerticalSqr);
        }

        return new Vector3(horizontal.x, y, horizontal.y);
    }

    private static Vector2 ClampDemoHorizontalOffset(Vector2 horizontal, float length)
    {
        float maxHorizontalSqr = Mathf.Max(0f, length * length * MaxDemoHorizontalRopeRatioSqr);
        float horizontalSqr = horizontal.sqrMagnitude;

        if (horizontalSqr <= maxHorizontalSqr || horizontalSqr <= Mathf.Epsilon)
        {
            return horizontal;
        }

        return horizontal * Mathf.Sqrt(maxHorizontalSqr / horizontalSqr);
    }

    private static Vector3 RotateHorizontal(Vector3 localOffset, float yawDegrees)
    {
        float yawRadians = yawDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(yawRadians);
        float sin = Mathf.Sin(yawRadians);

        return new Vector3(
            localOffset.x * cos - localOffset.z * sin,
            localOffset.y,
            localOffset.x * sin + localOffset.z * cos
        );
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

        pathAmplitudeX = Mathf.Max(0f, pathAmplitudeX);
        pathAmplitudeZ = Mathf.Max(0f, pathAmplitudeZ);
        pathSpeed = Mathf.Max(0f, pathSpeed);
        demoDamping = Mathf.Max(0f, demoDamping);
        gizmoPathSamples = Mathf.Max(8, gizmoPathSamples);
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

        float directionDegrees = motionMode == MotionMode.PlanarPendulum
            ? directionAngleDegrees
            : swingDirectionDegrees;
        float directionRadians = directionDegrees * Mathf.Deg2Rad;
        Vector3 swingDirection = new Vector3(
            Mathf.Cos(directionRadians),
            0f,
            Mathf.Sin(directionRadians)
        );

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(anchorPosition, anchorPosition + swingDirection * debugDirectionLength);

        if (bucketTransform != null)
        {
            // Tint the rope red as it stretches beyond rest length so extension is visible in the Scene view.
            float extensionRatio = restLength > 0f
                ? Mathf.Clamp01((bucketTransform.position - anchorPosition).magnitude / restLength - 1f)
                : 0f;
            Gizmos.color = Color.Lerp(Color.white, Color.red, extensionRatio);
            Gizmos.DrawLine(anchorPosition, bucketTransform.position);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(bucketTransform.position, 0.12f);
        }

        if (drawMotionPathGizmo && IsDemoMotionMode())
        {
            DrawDemoPathGizmo(anchorPosition);
        }
    }

    private void DrawDemoPathGizmo(Vector3 anchorPosition)
    {
        int sampleCount = Mathf.Max(8, gizmoPathSamples);
        float length = Application.isPlaying ? GetDemoRopeLength() : Mathf.Max(MinLength, restLength);
        float phaseOffset = Application.isPlaying ? _demoInitialPhase : pathPhase * Mathf.Deg2Rad;
        Vector3 previous = Vector3.zero;
        bool hasPrevious = false;

        Gizmos.color = pathGizmoColor;

        for (int i = 0; i <= sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            float phase = phaseOffset + t * Mathf.PI * 2f;
            Vector3 localOffset = CalculateDemoLocalOffset(motionMode, phase, length, 1f);
            Vector3 worldPoint = anchorPosition + RotateHorizontal(localOffset, swingDirectionDegrees);

            if (hasPrevious)
            {
                Gizmos.DrawLine(previous, worldPoint);
            }

            previous = worldPoint;
            hasPrevious = true;
        }
    }
}

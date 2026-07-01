using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using SwingingPaint.Core;
using SpringRopeSolver = SwingingPaint.Physics.SpringRope;

/// <summary>
/// Manually simulates a 3D spring-rope pendulum for the paint bucket.
///
/// The bucket is the final particle of a custom mass-spring rope. The mouse can pin that final
/// particle while dragging, then release it with the sampled pointer velocity so the bucket can
/// be thrown in the camera plane without using Rigidbody, Colliders, joints, or Unity physics.
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

    [Tooltip("Initial direction of the swing in the XZ canvas plane. 0 degrees starts along +X.")]
    public float directionAngleDegrees = 0f;

    [Tooltip("Additional linear air damping applied to the rope particles. Higher values settle the swing faster.")]
    public float damping = 0.05f;

    [Tooltip("Manual gravitational acceleration used by the rope solver.")]
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
    [Tooltip("Rest (unstretched) length of the rope in world units.")]
    public float restLength = 2f;

    [Tooltip("Overall rope stiffness. Internally this is distributed across the rope's spring segments.")]
    public float ropeStiffness = 50f;

    [Tooltip("Damping applied along each spring segment.")]
    public float ropeDamping = 0.5f;

    [Tooltip("Elasticity factor. Softens the spring; higher values stretch more under the same load.")]
    public float ropeElasticity = 0.5f;

    [Header("Spring Rope Simulation")]
    [Tooltip("Number of point-mass particles used to build the rope. More particles make the rope shape smoother.")]
    public int ropeParticleCount = 8;

    [Tooltip("Mass distributed along the rope, excluding the bucket mass.")]
    public float ropeMass = 0.25f;

    [Tooltip("Optional i-to-i+2 spring stiffness that gives the rope a little shape memory.")]
    public float bendingStiffness = 2f;

    [Tooltip("How much springs resist compression. 0 behaves like a soft rope; 1 behaves more like a rod.")]
    [Range(0f, 1f)]
    public float compressionResistance = 0.1f;

    [Tooltip("Extra linear drag applied to every free rope particle.")]
    public float airDrag = 0.05f;

    [Tooltip("Position projection passes that prevent individual rope segments from over-stretching.")]
    public int constraintIterations = 4;

    [Header("Mouse Interaction")]
    [Tooltip("Allow the left mouse button to grab and throw the bucket.")]
    public bool enableMouseGrab = true;

    [Tooltip("Camera used to convert the mouse pointer into a world-space drag plane. If empty, Camera.main is used.")]
    public Camera interactionCamera;

    [Tooltip("Maximum screen-space distance from the bucket center that still starts a grab.")]
    public float mouseGrabRadiusPixels = 90f;

    [Tooltip("Do not start bucket grabs while the pointer is over Unity UI.")]
    public bool ignoreMouseWhenPointerOverUI = true;

    [Tooltip("Multiplier applied to the sampled mouse velocity when the bucket is released.")]
    public float throwVelocityMultiplier = 1f;

    [Tooltip("Hard cap on the release velocity applied to the bucket.")]
    public float maxThrowSpeed = 35f;

    [Tooltip("Maximum anchor-to-pointer distance while dragging, as a multiple of restLength.")]
    public float dragMaxStretchMultiplier = 4f;

    [Header("Stability")]
    [Tooltip("Fixed integration sub-step in seconds. Stiff springs need a small step; the frame's time is split into steps no larger than this.")]
    public float maxSubStep = 0.005f;

    [Tooltip("Largest frame time (seconds) ever fed to the integrator. Clamps lag/pause spikes so a long frame cannot explode the spring.")]
    public float maxFrameTime = 0.1f;

    [Tooltip("Hard safety cap on each rope segment as a multiple of its rest length.")]
    public float maxStretchMultiplier = 4f;

    [Tooltip("Hard cap on rope particle speed, world units per second.")]
    public float maxRadialSpeed = 50f;

    [Tooltip("Backward-compatible angular-speed cap field. The 3D rope solver uses maxRadialSpeed for particle speed.")]
    public float maxAngularSpeed = 50f;

    [Header("Debug Drawing")]
    [Tooltip("Draw anchor, rope, and initial push direction gizmos in the Scene view.")]
    public bool drawDebugGizmos = true;

    [Tooltip("Length of the horizontal initial direction gizmo.")]
    public float debugDirectionLength = 1f;

    [Header("Runtime Driving")]
    [Tooltip("Skip this component's Update loop when SimulationManager is driving fixed-step simulation.")]
    public bool useSimulationManagerDriver = true;

    private const float MinLength = 0.05f;
    private const float MinMovingMass = 0.01f;
    private const float SwingCountDeadZoneDegrees = 0.25f;
    private const float SwingProjectionDeadZone = 0.01f;
    private const int MaxSubStepsPerFrame = 64;

    public float CurrentAngle => _currentAngleDegrees;
    public float CurrentAngularVelocity => _currentAngularVelocity;
    public float CurrentRopeLength => _ropeBuilt ? _rope.EndToAnchorDistance : _currentLength;
    public float CurrentRopeContourLength => _ropeBuilt ? _rope.ContourLength : _currentLength;
    public float RestLength => restLength;

    /// <summary>Total mass moved by the rope: dry bucket plus remaining paint.</summary>
    public float TotalMovingMass => Mathf.Max(MinMovingMass, bucketMass + paintMass);

    /// <summary>Number of completed half-swings across the center since the last reset.</summary>
    public int CompletedSwingCount => _completedSwingCount;

    /// <summary>True once swingCountLimit has been reached. A limit of 0 is unlimited.</summary>
    public bool SwingLimitReached => _swingLimitReached;

    /// <summary>
    /// Backward-compatible alias for the old fixed-length field. Reads/writes the rope rest length.
    /// </summary>
    public float ropeLength
    {
        get => restLength;
        set => restLength = value;
    }

    public float MaxRopeExtension => _maxRopeExtension;
    public float CurrentRopeExtension => Mathf.Max(0f, CurrentRopeLength - restLength);
    public float CurrentRopeTension => _currentRopeTension;
    public float NormalizedRopeTension
    {
        get
        {
            float reference = Mathf.Max(0.001f, EffectiveStiffness() * restLength * 0.25f);
            return Mathf.Clamp01(_currentRopeTension / reference);
        }
    }

    public Vector3 BucketVelocity { get; private set; }
    public bool IsMouseDraggingBucket => _isDragging;
    public int RopePointCount => _ropeBuilt ? _rope.Count : 0;

    private readonly SpringRopeSolver _rope = new SpringRopeSolver();

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
    private Vector3 _initialPullDirection = Vector3.right;
    private Vector3 _previousBucketPosition;
    private bool _hasPreviousBucketPosition;
    private bool _ropeBuilt;
    private int _lastParticleCount;

    private bool _isDragging;
    private Plane _dragPlane;
    private Vector3 _dragOffset;
    private Vector3 _dragTargetPosition;
    private Vector3 _lastDragTargetPosition;
    private Vector3 _dragVelocity;
    private bool _hasDragSample;

    private void Awake()
    {
        ResolveReferences();
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
        ResolveReferences();

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

        EnsureRopeBuilt();
        ConfigureRope();
        _rope.SetAnchor(anchorTransform.position);

        HandleMouseInput(Time.unscaledDeltaTime);

        if (_isDragging)
        {
            _rope.SetEnd(_dragTargetPosition, _dragVelocity, true);
        }

        _rope.Step(Mathf.Min(dt, maxFrameTime), CalculateSubsteps(dt));
        ApplyBucketPositionFromRope();
        UpdateBucketVelocity(dt);
        UpdateReadoutState(dt);
    }

    /// <summary>
    /// Resets the pendulum to a specific angle and angular velocity.
    /// </summary>
    public void ResetState(float angleDegrees, float angularVelocityDegrees)
    {
        ResolveReferences();

        _currentAngleDegrees = angleDegrees;
        _currentAngularVelocity = angularVelocityDegrees;
        _lengthVelocity = 0f;
        _currentRopeTension = 0f;
        _maxRopeExtension = 0f;
        _completedSwingCount = 0;
        _lastSwingSide = SignWithDeadZone(_currentAngleDegrees);
        _lastSwingProjectionSign = 0;
        _hasSwingCounterState = false;
        _swingLimitReached = false;
        swingDirectionDegrees = directionAngleDegrees;
        _isDragging = false;
        _hasDragSample = false;

        if (anchorTransform == null || bucketTransform == null)
        {
            _ropeBuilt = false;
            ResetBucketVelocityTracking();
            return;
        }

        float theta = angleDegrees * Mathf.Deg2Rad;
        float thetaDot = angularVelocityDegrees * Mathf.Deg2Rad;
        float initialLength = InitialEquilibriumLength(theta);
        Vector3 horizontalDirection = InitialHorizontalDirection();
        Vector3 ropeDirection = horizontalDirection * Mathf.Sin(theta) + Vector3.down * Mathf.Cos(theta);
        Vector3 endPosition = anchorTransform.position + ropeDirection * initialLength;
        Vector3 tangentDirection = horizontalDirection * Mathf.Cos(theta) + Vector3.up * Mathf.Sin(theta);
        Vector3 lateralDirection = ProjectOntoPlane(GetHorizontalLateralDirection(horizontalDirection), ropeDirection);
        lateralDirection = SafeNormalized(lateralDirection, Vector3.Cross(Vector3.up, ropeDirection));
        Vector3 endVelocity = tangentDirection * (initialLength * thetaDot)
                              + lateralDirection * (initialLength * initialLateralAngularVelocityDegrees * Mathf.Deg2Rad);
        endVelocity = Vector3.ClampMagnitude(endVelocity, maxWorldSpeed);

        _currentLength = initialLength;
        _currentRopeTension = EffectiveStiffness() * Mathf.Max(0f, _currentLength - restLength);
        _initialPullDirection = horizontalDirection;
        _lastSwingProjectionSign = SignWithDeadZone(GetSwingProjection(endPosition), SwingProjectionDeadZone);
        _hasSwingCounterState = _lastSwingProjectionSign != 0;

        BuildRope(endPosition, endVelocity);
        ApplyBucketPositionFromRope();
        ResetBucketVelocityTracking();
        UpdateReadoutState(0f);
    }

    public Vector3 GetBucketPosition()
    {
        return bucketTransform != null ? bucketTransform.position : Vector3.zero;
    }

    public Vector3 GetRopePoint(int index)
    {
        if (!_ropeBuilt || index < 0 || index >= _rope.Count)
        {
            return Vector3.zero;
        }

        return _rope.Particles[index].Position;
    }

    private void ResolveReferences()
    {
        if (anchorTransform == null)
        {
            anchorTransform = transform;
        }

        if (simulationSettings == null)
        {
            simulationSettings = FindObjectOfType<SimulationSettings>();
        }

        if (interactionCamera == null)
        {
            interactionCamera = Camera.main;
        }
    }

    private void EnsureRopeBuilt()
    {
        if (!_ropeBuilt)
        {
            Vector3 endPosition = bucketTransform != null
                ? bucketTransform.position
                : anchorTransform.position + Vector3.down * restLength;
            BuildRope(endPosition, BucketVelocity);
            return;
        }

        if (_lastParticleCount != Mathf.Max(2, ropeParticleCount))
        {
            BuildRope(_rope.EndPosition, _rope.EndVelocity);
        }
    }

    private void BuildRope(Vector3 endPosition, Vector3 endVelocity)
    {
        ConfigureRope();
        int count = Mathf.Max(2, ropeParticleCount);
        _rope.Build(anchorTransform.position, endPosition, count, Mathf.Max(0.0001f, ropeMass), TotalMovingMass, endVelocity);
        _ropeBuilt = true;
        _lastParticleCount = count;
        _currentLength = _rope.EndToAnchorDistance;
    }

    private void ConfigureRope()
    {
        int segments = Mathf.Max(1, Mathf.Max(2, ropeParticleCount) - 1);
        float effectiveTotalStiffness = EffectiveStiffness();

        _rope.GravityAccel = Vector3.down * gravity;
        _rope.SegmentRestLength = restLength / segments;
        _rope.SegmentStiffness = effectiveTotalStiffness * segments;
        _rope.SegmentDamping = ropeDamping;
        _rope.BendingStiffness = Mathf.Max(0f, bendingStiffness);
        _rope.CompressionResistance = compressionResistance;
        _rope.AirDrag = airDrag + damping + Mathf.Max(0f, airResistance / TotalMovingMass);
        _rope.MaxSpeed = Mathf.Max(0.1f, maxRadialSpeed);
        _rope.MaxSegmentStretchMultiplier = Mathf.Max(1f, maxStretchMultiplier);
        _rope.ConstraintIterations = Mathf.Max(0, constraintIterations);
    }

    private int CalculateSubsteps(float dt)
    {
        float clampedDt = Mathf.Min(dt, maxFrameTime);
        float step = Mathf.Max(0.0005f, maxSubStep);
        return Mathf.Clamp(Mathf.CeilToInt(clampedDt / step), 1, MaxSubStepsPerFrame);
    }

    private float EffectiveStiffness()
    {
        return ropeStiffness / (1f + Mathf.Max(0f, ropeElasticity));
    }

    private float InitialEquilibriumLength(float theta)
    {
        float kEff = EffectiveStiffness();
        float gravityAlongRope = gravity * Mathf.Cos(theta);
        float equilibriumExtension = kEff > 0f ? TotalMovingMass * Mathf.Max(0f, gravityAlongRope) / kEff : 0f;
        return Mathf.Clamp(restLength + equilibriumExtension, MinLength, restLength * Mathf.Max(1f, maxStretchMultiplier));
    }

    private Vector3 InitialHorizontalDirection()
    {
        float directionRadians = directionAngleDegrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(directionRadians), 0f, Mathf.Sin(directionRadians)).normalized;
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
            StopActiveMotion();
            _swingLimitReached = true;
        }
    }

    private float GetSwingProjection(Vector3 position)
    {
        Vector3 offset = position - GetAnchorPosition();
        Vector3 horizontalOffset = Vector3.ProjectOnPlane(offset, Vector3.up);
        return Vector3.Dot(horizontalOffset, _initialPullDirection);
    }

    private void HandleMouseInput(float realDeltaTime)
    {
        if (!enableMouseGrab)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            TryBeginMouseDrag();
        }

        if (_isDragging && Input.GetMouseButton(0))
        {
            UpdateMouseDrag(realDeltaTime);
        }

        if (_isDragging && Input.GetMouseButtonUp(0))
        {
            EndMouseDrag(realDeltaTime);
        }
    }

    private void TryBeginMouseDrag()
    {
        if (bucketTransform == null)
        {
            return;
        }

        if (ignoreMouseWhenPointerOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        Camera cam = GetInteractionCamera();
        if (cam == null || !PointerIsCloseEnoughToBucket(cam))
        {
            return;
        }

        _dragPlane = new Plane(-cam.transform.forward, bucketTransform.position);
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!_dragPlane.Raycast(ray, out float enter))
        {
            return;
        }

        Vector3 hitPoint = ray.GetPoint(enter);
        _dragOffset = bucketTransform.position - hitPoint;
        _dragTargetPosition = ClampDragTarget(hitPoint + _dragOffset);
        _lastDragTargetPosition = _dragTargetPosition;
        _dragVelocity = Vector3.zero;
        _hasDragSample = false;
        _isDragging = true;
    }

    private void UpdateMouseDrag(float realDeltaTime)
    {
        if (!TryGetMouseDragPoint(out Vector3 target))
        {
            return;
        }

        target = ClampDragTarget(target);

        if (_hasDragSample && realDeltaTime > Mathf.Epsilon)
        {
            _dragVelocity = Vector3.ClampMagnitude((target - _lastDragTargetPosition) / realDeltaTime, maxThrowSpeed);
        }
        else
        {
            _dragVelocity = Vector3.zero;
            _hasDragSample = true;
        }

        _lastDragTargetPosition = target;
        _dragTargetPosition = target;
    }

    private void EndMouseDrag(float realDeltaTime)
    {
        UpdateMouseDrag(realDeltaTime);

        Vector3 releaseVelocity = Vector3.ClampMagnitude(_dragVelocity * throwVelocityMultiplier, maxThrowSpeed);
        _rope.SetEnd(_dragTargetPosition, releaseVelocity, false);
        BucketVelocity = releaseVelocity;

        _isDragging = false;
        _hasDragSample = false;
    }

    private bool TryGetMouseDragPoint(out Vector3 point)
    {
        point = _dragTargetPosition;

        Camera cam = GetInteractionCamera();
        if (cam == null)
        {
            return false;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!_dragPlane.Raycast(ray, out float enter))
        {
            return false;
        }

        point = ray.GetPoint(enter) + _dragOffset;
        return true;
    }

    private Camera GetInteractionCamera()
    {
        if (interactionCamera == null)
        {
            interactionCamera = Camera.main;
        }

        return interactionCamera;
    }

    private bool PointerIsCloseEnoughToBucket(Camera cam)
    {
        Vector3 bucketScreenPosition = cam.WorldToScreenPoint(bucketTransform.position);
        if (bucketScreenPosition.z < 0f)
        {
            return false;
        }

        Vector2 pointer = Input.mousePosition;
        Vector2 bucket = new Vector2(bucketScreenPosition.x, bucketScreenPosition.y);
        float radius = Mathf.Max(1f, mouseGrabRadiusPixels);
        return (pointer - bucket).sqrMagnitude <= radius * radius;
    }

    private Vector3 ClampDragTarget(Vector3 target)
    {
        if (anchorTransform == null)
        {
            return target;
        }

        Vector3 fromAnchor = target - anchorTransform.position;
        float maxDistance = restLength * Mathf.Max(1f, dragMaxStretchMultiplier);
        if (fromAnchor.sqrMagnitude > maxDistance * maxDistance)
        {
            target = anchorTransform.position + fromAnchor.normalized * maxDistance;
        }

        return target;
    }

    private void ApplyBucketPositionFromRope()
    {
        if (bucketTransform == null || !_ropeBuilt)
        {
            return;
        }

        bucketTransform.position = _rope.EndPosition;
    }

    private void UpdateBucketVelocity(float dt)
    {
        Vector3 currentPosition = bucketTransform.position;

        if (!_hasPreviousBucketPosition)
        {
            _previousBucketPosition = currentPosition;
            _hasPreviousBucketPosition = true;
            BucketVelocity = Vector3.zero;
            return;
        }

        Update3DSwingCounter(_previousBucketPosition, currentPosition);
        if (_swingLimitReached)
        {
            _previousBucketPosition = currentPosition;
            return;
        }

        BucketVelocity = (currentPosition - _previousBucketPosition) / dt;
        _previousBucketPosition = currentPosition;
    }

    private void UpdateReadoutState(float dt)
    {
        if (!_ropeBuilt)
        {
            return;
        }

        Vector3 anchorToBucket = _rope.EndPosition - _rope.AnchorPosition;
        float length = anchorToBucket.magnitude;
        float previousLength = _currentLength;
        float previousAngle = _currentAngleDegrees;

        if (length > 1e-5f)
        {
            float cosFromDown = Mathf.Clamp(Vector3.Dot(anchorToBucket / length, Vector3.down), -1f, 1f);
            _currentAngleDegrees = Mathf.Acos(cosFromDown) * Mathf.Rad2Deg;
        }
        else
        {
            _currentAngleDegrees = 0f;
        }

        if (dt > Mathf.Epsilon)
        {
            _currentAngularVelocity = Mathf.DeltaAngle(previousAngle, _currentAngleDegrees) / dt;
            _lengthVelocity = (length - previousLength) / dt;
        }

        _currentLength = length;

        float positiveExtension = Mathf.Max(0f, length - restLength);
        _currentRopeTension = EffectiveStiffness() * positiveExtension;
        if (positiveExtension > _maxRopeExtension)
        {
            _maxRopeExtension = positiveExtension;
        }
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
        _currentAngularVelocity = 0f;
        _lengthVelocity = 0f;
        BucketVelocity = Vector3.zero;
        _dragVelocity = Vector3.zero;
        _isDragging = false;

        if (_ropeBuilt)
        {
            _rope.SetEnd(_rope.EndPosition, Vector3.zero, false);
        }
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

        ropeParticleCount = Mathf.Max(2, ropeParticleCount);
        ropeMass = Mathf.Max(0.0001f, ropeMass);
        bucketMass = Mathf.Max(MinMovingMass, bucketMass);
        paintMass = Mathf.Max(0f, paintMass);
        airResistance = Mathf.Max(0f, airResistance);
        swingCountLimit = Mathf.Max(0, swingCountLimit);
        bendingStiffness = Mathf.Max(0f, bendingStiffness);
        compressionResistance = Mathf.Clamp01(compressionResistance);
        airDrag = Mathf.Max(0f, airDrag);
        constraintIterations = Mathf.Max(0, constraintIterations);

        mouseGrabRadiusPixels = Mathf.Max(1f, mouseGrabRadiusPixels);
        throwVelocityMultiplier = Mathf.Max(0f, throwVelocityMultiplier);
        maxThrowSpeed = Mathf.Max(0.1f, maxThrowSpeed);
        dragMaxStretchMultiplier = Mathf.Max(1f, dragMaxStretchMultiplier);

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

        Vector3 swingDirection = InitialHorizontalDirection();
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(anchorPosition, anchorPosition + swingDirection * debugDirectionLength);

        if (_ropeBuilt && _rope.Count > 1)
        {
            float extensionRatio = restLength > 0f
                ? Mathf.Clamp01(_rope.EndToAnchorDistance / restLength - 1f)
                : 0f;

            Gizmos.color = Color.Lerp(Color.white, Color.red, extensionRatio);
            for (int i = 1; i < _rope.Count; i++)
            {
                Gizmos.DrawLine(_rope.Particles[i - 1].Position, _rope.Particles[i].Position);
            }

            Gizmos.color = _isDragging ? Color.magenta : Color.green;
            Gizmos.DrawWireSphere(_rope.EndPosition, 0.12f);
        }
        else if (bucketTransform != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(anchorPosition, bucketTransform.position);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(bucketTransform.position, 0.12f);
        }
    }
}

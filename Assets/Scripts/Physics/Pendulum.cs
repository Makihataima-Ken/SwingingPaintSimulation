using UnityEngine;
using SwingingPaint.Core;

/// <summary>
/// Simulates a damped pendulum motion with elastic rope physics using custom mathematics.
///
/// Elasticity Model (Hooke's Law):
///     F_spring = -k * x
/// where k = ropeStiffness and x = extension beyond rest length.
///
/// The equation of motion for a pendulum with a non-rigid rope:
///     angularAccel = - (g / L) * sin(theta) - damping * angularVelocity
/// where L is the instantaneous length of the rope.
///
/// The rope length L(t) is governed by the balance between centripetal force
/// and the elastic restoring force:
///     d^2L/dt^2 = (L * angularVelocity^2) - g * cos(theta) - (k/m) * (L - restLength) - c * dL/dt
///
/// For numerical stability, we integrate length change with semi-implicit Euler,
/// clamping to prevent negative length and limiting velocity.
///
/// Design decisions:
/// - No Rigidbody, no SpringJoint (custom mathematical simulation).
/// - Reads all parameters from PhysicsSettings to avoid hardcoded values.
/// - Provides ResetState() for runtime restart.
/// - Exposes read-only current state for the debug panel.
/// </summary>
public class Pendulum : MonoBehaviour
{
    [Header("Settings Reference")]
    [Tooltip("Centralized physics settings. If null, will search for PhysicsSettings in project assets.")]
    public PhysicsSettings settings;

    [Header("References")]
    [Tooltip("The transform of the bucket at the end of the pendulum.")]
    public Transform bucket;

    // ------------------------------------------------------------------
    // Exposed read-only state for debug/UI
    // ------------------------------------------------------------------

    /// <summary>Current angle from the vertical in degrees.</summary>
    public float CurrentAngle => _angle;

    /// <summary>Current angular velocity in degrees per second.</summary>
    public float CurrentAngularVelocity => _angularVelocity;

    /// <summary>Instantaneous rope length including elastic stretch.</summary>
    public float CurrentRopeLength => _currentLength;

    /// <summary>Maximum rope extension observed since last reset.</summary>
    public float MaxRopeExtension => _maxRopeExtension;

    /// <summary>Returns the current world position of the bucket.</summary>
    public Vector3 GetBucketPosition() => bucket != null ? bucket.position : Vector3.zero;

    // ------------------------------------------------------------------
    // Private state
    // ------------------------------------------------------------------

    private float _angle;            // current angle (degrees)
    private float _angularVelocity;  // current angular velocity (deg/s)
    private float _currentLength;  // instantaneous rope length
    private float _lengthVelocity;   // rate of change of rope length (m/s)
    private float _maxRopeExtension;

    private void Awake()
    {
        // Auto-resolve settings if not assigned in the Inspector
        if (settings == null)
        {
            settings = Resources.Load<PhysicsSettings>("PhysicsSettings");
        }
    }

    private void Start()
    {
        if (settings == null)
        {
            Debug.LogError("Pendulum: No PhysicsSettings assigned or found!", this);
            enabled = false;
            return;
        }

        // Initialize from settings
        _angle = settings.InitialAngle;
        _angularVelocity = settings.AngularVelocity;
        _currentLength = settings.RestLength;
        _lengthVelocity = 0f;
        _maxRopeExtension = 0f;
    }

    private void Update()
    {
        if (settings == null || bucket == null)
            return;

        float dt = Time.deltaTime;

        // If the simulation is paused, do not integrate physics
        if (SimulationManager.Instance != null && SimulationManager.Instance.IsPaused)
            return;

        // Update parameters from settings (live tuning)
        IntegrateElasticPendulum(dt);
        ApplyBucketPosition();
    }

    // ------------------------------------------------------------------
    // Core Physics Integration
    // ------------------------------------------------------------------

    /// <summary>
    /// Integrates the pendulum motion with elastic rope using semi-implicit Euler.
    ///
    /// Physics equations:
    ///   theta:        angle from vertical (radians)
    ///   omega:        angular velocity (rad/s)
    ///   L:            current rope length
    ///   vL:           rate of change of rope length
    ///
    /// Angular motion (simple pendulum with variable length):
    ///   alpha = - (g / L) * sin(theta) - damping * omega
    ///
    /// Elastic length motion:
    ///   centripetal = L * omega^2
    ///   gravitational = -g * cos(theta)
    ///   elastic = - (k / m) * (L - restLength)
    ///   dampingL = - damping * vL
    ///   aL = centripetal + gravitational + elastic + dampingL
    ///
        /// We assume unit mass (m = 1) for the bucket because we are modeling
        /// a pure mathematical pendulum. If mass needs to vary, it must be factored in
        /// by dividing the stiffness by the actual mass.
    /// </summary>
    private void IntegrateElasticPendulum(float dt)
    {
        // Clamp dt for numerical stability at low framerates
        const float MAX_DT = 0.05f;
        if (dt > MAX_DT)
            dt = MAX_DT;

        float thetaRad = _angle * Mathf.Deg2Rad;
        float omegaRad = _angularVelocity * Mathf.Deg2Rad;

        float g = settings.Gravity;
        float damping = settings.Damping;
        float restLength = settings.RestLength;
        float stiffness = settings.RopeStiffness;
        float elasticity = settings.RopeElasticity;

        // --------------------------------------------------------------
        // Angular motion
        // --------------------------------------------------------------
        float angularAccel = -(g / _currentLength) * Mathf.Sin(thetaRad) - damping * omegaRad;
        omegaRad += angularAccel * dt;
        thetaRad += omegaRad * dt;

        // Normalize thetaRad to [-pi, pi] to prevent long-term drift
        thetaRad = Mathf.Repeat(thetaRad + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;

        // --------------------------------------------------------------
        // Elastic rope motion
        // --------------------------------------------------------------
        // Effective stiffness combines settings stiffness and elasticity factor
        float effectiveStiffness = stiffness * (1f + elasticity);

        // Forces acting along the rope (per unit mass):
        // 1. Centripetal: L * omega^2  (outward, positive)
        // 2. Gravitational component: -g * cos(theta)  (inward when below pivot)
        // 3. Elastic restoring force: -k * (L - restLength)  (inward if stretched)
        // 4. Damping on length change

        float centripetal = _currentLength * (omegaRad * omegaRad);
        float gravityComponent = -g * Mathf.Cos(thetaRad);
        float elasticForce = -effectiveStiffness * (_currentLength - restLength);
        float lengthDamping = -damping * _lengthVelocity;

        float lengthAccel = centripetal + gravityComponent + elasticForce + lengthDamping;

        // Integrate length
        _lengthVelocity += lengthAccel * dt;
        _currentLength += _lengthVelocity * dt;

        // --------------------------------------------------------------
        // Numerical safety
        // --------------------------------------------------------------

        // Prevent the rope from collapsing or stretching to infinity
        _currentLength = Mathf.Clamp(_currentLength, restLength * 0.1f, restLength * 3f);

        // Clamp length velocity to prevent instability
        const float MAX_LENGTH_VELOCITY = 10f;
        _lengthVelocity = Mathf.Clamp(_lengthVelocity, -MAX_LENGTH_VELOCITY, MAX_LENGTH_VELOCITY);

        // Track max extension for debug
        float extension = _currentLength - restLength;
        if (extension > _maxRopeExtension)
            _maxRopeExtension = extension;

        // Write back
        _angle = thetaRad * Mathf.Rad2Deg;
        _angularVelocity = omegaRad * Mathf.Rad2Deg;
    }

    // ------------------------------------------------------------------
    // Position Update
    // ------------------------------------------------------------------

    private void ApplyBucketPosition()
    {
        float thetaRad = _angle * Mathf.Deg2Rad;
        float dirRad = settings.Direction * Mathf.Deg2Rad;

        float x = _currentLength * Mathf.Sin(thetaRad) * Mathf.Cos(dirRad);
        float z = _currentLength * Mathf.Sin(thetaRad) * Mathf.Sin(dirRad);
        float y = -_currentLength * Mathf.Cos(thetaRad);

        bucket.localPosition = new Vector3(x, y, z);
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// Resets the pendulum to a new initial state.
    /// Called by SimulationManager on reset/restart.
    /// </summary>
    public void ResetState(float newAngle, float newAngularVelocity)
    {
        _angle = newAngle;
        _angularVelocity = newAngularVelocity;
        _currentLength = settings != null ? settings.RestLength : 2f;
        _lengthVelocity = 0f;
        _maxRopeExtension = 0f;

        ApplyBucketPosition();
    }
}

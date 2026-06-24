using System;
using UnityEngine;

namespace SwingingPaint.Core
{
    /// <summary>
    /// Centralized container for all simulation parameters in the Swinging Paint Simulation.
    ///
    /// Design decisions:
    /// - Single source of truth: all systems (Pendulum, Paint, UI) read from this asset.
    /// - ScriptableObject so it can be stored as an asset, edited in the Inspector, and referenced by multiple systems.
    /// - Uses OnValidate to clamp values and fire the Changed event when values are modified in the Inspector.
    /// - Uses a runtime Changed event to notify systems of live updates without polling.
    /// </summary>
    [CreateAssetMenu(fileName = "PhysicsSettings", menuName = "SwingingPaint/Physics Settings")]
    public class PhysicsSettings : ScriptableObject
    {
        // ------------------------------------------------------------------
        // Physics Parameters
        // ------------------------------------------------------------------

        [Header("Pendulum Physics")]
        [Tooltip("Gravitational acceleration (m/s^2). Default: 9.81")]
        [SerializeField] private float gravity = 9.81f;

        [Tooltip("Damping coefficient. Higher values cause faster oscillation decay.")]
        [SerializeField] private float damping = 0.05f;

        [Tooltip("Initial angle from the vertical in degrees.")]
        [SerializeField] private float initialAngle = 30f;

        [Tooltip("Initial angular velocity in degrees per second.")]
        [SerializeField] private float angularVelocity = 0f;

        [Tooltip("Direction in the XZ plane in degrees (0 = along X axis).")]
        [SerializeField] private float direction = 0f;

        [Tooltip("Dry bucket mass in kilograms.")]
        [SerializeField] private float bucketMass = 1.2f;

        [Tooltip("Remaining paint mass in kilograms.")]
        [SerializeField] private float paintMass = 1f;

        [Tooltip("Linear air resistance coefficient for custom motion.")]
        [SerializeField] private float airResistance = 0.02f;

        [Tooltip("Maximum completed half-swings before motion stops. 0 means unlimited.")]
        [SerializeField] private int swingCountLimit = 0;

        // ------------------------------------------------------------------
        // Rope Elasticity Parameters
        // ------------------------------------------------------------------

        [Header("Rope Elasticity")]
        [Tooltip("Rest (unstretched) length of the rope.")]
        [SerializeField] private float restLength = 2f;

        [Tooltip("Spring stiffness k. Higher values make the rope stiffer (Hooke's Law: F = -k * x).")]
        [SerializeField] private float ropeStiffness = 50f;

        [Tooltip("Rope elasticity factor. Controls how much the rope stretches under load. Higher = more stretchy.")]
        [SerializeField] private float ropeElasticity = 0.5f;

        // ------------------------------------------------------------------
        // Paint Parameters
        // ------------------------------------------------------------------

        [Header("Paint Properties")]
        [Tooltip("Rate at which paint flows from the bucket (units per second).")]
        [SerializeField] private float paintFlowRate = 1.0f;

        [Tooltip("Viscosity of the paint. Higher values cause the paint to spread less.")]
        [SerializeField] private float paintViscosity = 0.5f;

        [Tooltip("Total quantity of paint available in the bucket.")]
        [SerializeField] private float paintQuantity = 100f;

        [Tooltip("Rate at which the surface absorbs paint.")]
        [SerializeField] private float surfaceAbsorption = 0.1f;

        [Tooltip("Radius over which paint spreads upon contact with the surface.")]
        [SerializeField] private float paintSpreadRadius = 0.2f;

        // ------------------------------------------------------------------
        // Properties (expose values read-only; use Setters for mutation)
        // ------------------------------------------------------------------

        public float Gravity => gravity;
        public float Damping => damping;
        public float InitialAngle => initialAngle;
        public float AngularVelocity => angularVelocity;
        public float Direction => direction;
        public float BucketMass => bucketMass;
        public float PaintMass => paintMass;
        public float TotalMovingMass => bucketMass + paintMass;
        public float AirResistance => airResistance;
        public int SwingCountLimit => swingCountLimit;

        public float RestLength => restLength;
        public float RopeStiffness => ropeStiffness;
        public float RopeElasticity => ropeElasticity;

        public float PaintFlowRate => paintFlowRate;
        public float PaintViscosity => paintViscosity;
        public float PaintQuantity => paintQuantity;
        public float SurfaceAbsorption => surfaceAbsorption;
        public float PaintSpreadRadius => paintSpreadRadius;

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        /// <summary>
        /// Fired whenever any parameter changes. Listeners should re-read the property they care about.
        /// </summary>
        public event Action OnSettingsChanged;

        // ------------------------------------------------------------------
        // Setters (fire event on change)
        // ------------------------------------------------------------------

        public void SetGravity(float value)
        {
            if (Mathf.Approximately(gravity, value)) return;
            gravity = value;
            NotifyChanged();
        }

        public void SetDamping(float value)
        {
            if (Mathf.Approximately(damping, value)) return;
            damping = Mathf.Max(0f, value);
            NotifyChanged();
        }

        public void SetInitialAngle(float value)
        {
            if (Mathf.Approximately(initialAngle, value)) return;
            initialAngle = value;
            NotifyChanged();
        }

        public void SetAngularVelocity(float value)
        {
            if (Mathf.Approximately(angularVelocity, value)) return;
            angularVelocity = value;
            NotifyChanged();
        }

        public void SetDirection(float value)
        {
            if (Mathf.Approximately(direction, value)) return;
            direction = value;
            NotifyChanged();
        }

        public void SetBucketMass(float value)
        {
            if (Mathf.Approximately(bucketMass, value)) return;
            bucketMass = Mathf.Max(0.01f, value);
            NotifyChanged();
        }

        public void SetPaintMass(float value)
        {
            if (Mathf.Approximately(paintMass, value)) return;
            paintMass = Mathf.Max(0f, value);
            NotifyChanged();
        }

        public void SetAirResistance(float value)
        {
            if (Mathf.Approximately(airResistance, value)) return;
            airResistance = Mathf.Max(0f, value);
            NotifyChanged();
        }

        public void SetSwingCountLimit(int value)
        {
            if (swingCountLimit == value) return;
            swingCountLimit = Mathf.Max(0, value);
            NotifyChanged();
        }

        public void SetRestLength(float value)
        {
            if (Mathf.Approximately(restLength, value)) return;
            restLength = Mathf.Max(0.01f, value);
            NotifyChanged();
        }

        public void SetRopeStiffness(float value)
        {
            if (Mathf.Approximately(ropeStiffness, value)) return;
            ropeStiffness = Mathf.Max(0.01f, value);
            NotifyChanged();
        }

        public void SetRopeElasticity(float value)
        {
            if (Mathf.Approximately(ropeElasticity, value)) return;
            ropeElasticity = Mathf.Max(0f, value);
            NotifyChanged();
        }

        public void SetPaintFlowRate(float value)
        {
            if (Mathf.Approximately(paintFlowRate, value)) return;
            paintFlowRate = Mathf.Max(0f, value);
            NotifyChanged();
        }

        public void SetPaintViscosity(float value)
        {
            if (Mathf.Approximately(paintViscosity, value)) return;
            paintViscosity = Mathf.Max(0f, value);
            NotifyChanged();
        }

        public void SetPaintQuantity(float value)
        {
            if (Mathf.Approximately(paintQuantity, value)) return;
            paintQuantity = Mathf.Max(0f, value);
            NotifyChanged();
        }

        public void SetSurfaceAbsorption(float value)
        {
            if (Mathf.Approximately(surfaceAbsorption, value)) return;
            surfaceAbsorption = Mathf.Max(0f, value);
            NotifyChanged();
        }

        public void SetPaintSpreadRadius(float value)
        {
            if (Mathf.Approximately(paintSpreadRadius, value)) return;
            paintSpreadRadius = Mathf.Max(0.01f, value);
            NotifyChanged();
        }

        // ------------------------------------------------------------------
        // Validation
        // ------------------------------------------------------------------

        private void OnValidate()
        {
            // Ensure all values are within safe ranges when edited in the Inspector
            gravity = Mathf.Max(0f, gravity);
            damping = Mathf.Max(0f, damping);
            bucketMass = Mathf.Max(0.01f, bucketMass);
            paintMass = Mathf.Max(0f, paintMass);
            airResistance = Mathf.Max(0f, airResistance);
            swingCountLimit = Mathf.Max(0, swingCountLimit);
            restLength = Mathf.Max(0.01f, restLength);
            ropeStiffness = Mathf.Max(0.01f, ropeStiffness);
            ropeElasticity = Mathf.Max(0f, ropeElasticity);
            paintFlowRate = Mathf.Max(0f, paintFlowRate);
            paintViscosity = Mathf.Max(0f, paintViscosity);
            paintQuantity = Mathf.Max(0f, paintQuantity);
            surfaceAbsorption = Mathf.Max(0f, surfaceAbsorption);
            paintSpreadRadius = Mathf.Max(0.01f, paintSpreadRadius);
        }

        private void NotifyChanged()
        {
            OnSettingsChanged?.Invoke();
        }
    }
}

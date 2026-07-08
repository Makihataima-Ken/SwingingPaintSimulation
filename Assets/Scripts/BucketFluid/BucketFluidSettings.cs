using UnityEngine;

namespace SwingingPaint.BucketFluid
{
    /// <summary>
    /// Inspector-facing settings for realistic paint liquid inside the bucket.
    ///
    /// Attach this component to BucketRig or the bucket object that defines the local fluid
    /// space. These values are pure custom-simulation data for the GPU particle solver;
    /// they do not use built-in physics.
    /// </summary>
    public class BucketFluidSettings : MonoBehaviour
    {
        // Lightweight particle count for iteration while tuning. Lower counts are faster but
        // show less detail in waves, splashes, and surface shape.
        [Header("Particle Count")]
        [Tooltip("Lightweight particle count for development and tuning.")]
        public int developmentParticleCount = 1200;

        // High-detail particle count for final presentation. More particles improve visual
        // continuity and fluid mass, but require significantly more GPU work.
        [Tooltip("High-detail particle count for presentation mode.")]
        public int presentationParticleCount = 12000;

        // Switches between development and presentation counts without changing tuned values.
        [Tooltip("Use presentationParticleCount instead of developmentParticleCount.")]
        public bool presentationMode;

        /// <summary>
        /// The particle count currently used by the simulator.
        /// </summary>
        public int ActiveParticleCount => presentationMode ? presentationParticleCount : developmentParticleCount;

        // Radius of one paint particle in bucket-local units. Larger particles make the fluid
        // look thicker and fill volume faster, while smaller particles allow finer detail.
        [Header("Particle Shape")]
        [Tooltip("Local-space particle radius used for particle size and boundary spacing.")]
        public float particleRadius = 0.035f;

        // Neighbor interaction radius. Higher values produce smoother, more cohesive liquid;
        // lower values preserve sharper detail but can become noisy.
        [Tooltip("Neighbor radius used by density, pressure, viscosity, and cohesion calculations.")]
        public float smoothingRadius = 0.11f;

        // Custom downward acceleration applied to local-space particles. This is manual solver
        // gravity, not Unity physics gravity.
        [Header("Fluid Physics")]
        [Tooltip("Manual gravity magnitude applied by the custom fluid solver.")]
        public float gravity = 9.81f;

        // Target mass density. Higher values make paint resist compression and feel heavier.
        [Tooltip("Target density used by pressure calculations.")]
        public float restDensity = 1000f;

        // Main pressure strength. Higher stiffness keeps the fluid volume stable, but values
        // that are too high may require more solver iterations.
        [Tooltip("Strength of standard pressure response.")]
        public float pressureStiffness = 120f;

        // Extra short-range pressure strength. This helps prevent particles from clumping
        // unnaturally during compression and fast bucket motion.
        [Tooltip("Strength of near-pressure response for close particles.")]
        public float nearPressureStiffness = 180f;

        // Internal friction. Higher viscosity makes paint syrupy and slow to shear; lower
        // viscosity makes it splash and slosh more like water.
        [Tooltip("Internal fluid friction; higher values make paint thicker.")]
        public float viscosity = 1.2f;

        // Surface smoothing force. Higher values keep the visible liquid surface rounded and
        // resistant to tiny ripples.
        [Tooltip("Surface smoothing force for rounded liquid behavior.")]
        public float surfaceTension = 0.25f;

        // Attraction between nearby particles. Higher cohesion helps paint hold together as a
        // continuous mass instead of separating into mist.
        [Tooltip("Particle attraction that keeps paint mass connected.")]
        public float cohesion = 0.2f;

        // Velocity drag applied inside the bucket. Higher drag removes energy from sloshing and
        // makes heavy paint settle faster.
        [Tooltip("Velocity drag applied by the custom fluid solver.")]
        public float drag = 0.03f;

        // Number of fixed solver steps per frame. More substeps improve stability during fast
        // bucket movement at a higher GPU cost.
        [Header("Solver")]
        [Tooltip("Fixed simulation substeps per rendered frame.")]
        public int substeps = 1;

        // Constraint/pressure refinement passes per substep. More iterations improve volume
        // preservation and reduce particle overlap.
        [Tooltip("Solver refinement iterations per substep.")]
        public int solverIterations = 3;

        // Velocity safety clamp. This prevents unstable particles from gaining extreme speeds
        // when the bucket moves sharply.
        [Tooltip("Maximum local-space particle speed.")]
        public float maxVelocity = 8f;

        // General solver damping. Higher damping calms oscillations; lower damping keeps more
        // lively sloshing energy.
        [Tooltip("Global velocity damping applied by the custom solver.")]
        public float damping = 0.02f;

        // Initial paint fill amount as a fraction of the bucket's interior height. Lower values
        // leave visible empty space for sloshing; higher values make spilling more likely later.
        [Header("Fluid Fill")]
        [Tooltip("Initial local-space fill height as a fraction of the bucket interior.")]
        [Range(0.01f, 1f)]
        public float fillHeightPercent = 0.85f;

        // Small deterministic spawn offset as a fraction of particle spacing. This breaks up
        // visible perfect rows while keeping the initial mass safely inside the bucket.
        [Tooltip("Initial particle jitter as a fraction of spawn spacing.")]
        [Range(0f, 0.45f)]
        public float spawnJitter = 0.16f;

        // Seed used by Reset Fluid. Keeping this deterministic makes a tuned fill reproducible;
        // use Randomize Fluid Fill on GPUFluidSimulator to try another arrangement.
        [Tooltip("Deterministic seed used for the initial fill pattern.")]
        public int randomSeed = 12345;

        // Hex packing gives dense circular layers that read more like a filled bucket than a
        // square grid. Disabling it uses concentric rings instead.
        [Tooltip("Use staggered hexagonal circular layers for initial particle placement.")]
        public bool useHexPacking = true;

        // Base color used by the renderer. Rich opaque colors make the liquid read more like
        // paint than water.
        [Header("Paint Appearance")]
        [Tooltip("Base paint color used by fluid rendering.")]
        public Color paintColor = new Color(0.05f, 0.22f, 0.95f, 1f);

        // Visual size multiplier for rendered particles. Increasing this can hide gaps at low
        // particle counts.
        [Tooltip("Rendered particle size in world/local visual units.")]
        public float particleVisualSize = 0.045f;

        // Render opacity. Higher opacity creates thick paint; lower opacity looks more watery or
        // transparent.
        [Tooltip("Fluid rendering opacity.")]
        [Range(0f, 1f)]
        public float opacity = 0.92f;

        // Material smoothness. Higher smoothness creates wetter highlights.
        [Tooltip("Material smoothness for wet paint highlights.")]
        [Range(0f, 1f)]
        public float smoothness = 0.75f;

        // Material metallic value. Paint should usually stay at zero for realism.
        [Tooltip("Material metallic value; realistic paint should usually be non-metallic.")]
        [Range(0f, 1f)]
        public float metallic = 0f;

        // Enables the GPU compute path. This project is GPU-only for fluid simulation, so the
        // value is forced on during validation.
        [Header("Performance")]
        [Tooltip("GPU-only fluid simulation. This is forced on; there is no CPU simulation fallback.")]
        public bool useGPU = true;

        // Enables additional debug readouts and validation aids. Disable for presentation.
        [Tooltip("Enable debug information for fluid development.")]
        public bool enableDebug = false;

        [System.NonSerialized] private Color _lastValidatedPaintColor;
        [System.NonSerialized] private float _lastValidatedOpacity;
        [System.NonSerialized] private bool _hasValidatedPaintAppearance;

        private void OnEnable()
        {
            CacheValidatedPaintAppearance();
        }

        private void OnValidate()
        {
            useGPU = true;

            developmentParticleCount = Mathf.Max(1, developmentParticleCount);
            presentationParticleCount = Mathf.Max(1, presentationParticleCount);

            particleRadius = Mathf.Max(0.0001f, particleRadius);
            smoothingRadius = Mathf.Max(particleRadius + 0.0001f, smoothingRadius);

            gravity = Mathf.Max(0f, gravity);
            restDensity = Mathf.Max(0.0001f, restDensity);
            pressureStiffness = Mathf.Max(0f, pressureStiffness);
            nearPressureStiffness = Mathf.Max(0f, nearPressureStiffness);
            viscosity = Mathf.Max(0f, viscosity);
            surfaceTension = Mathf.Max(0f, surfaceTension);
            cohesion = Mathf.Max(0f, cohesion);
            drag = Mathf.Max(0f, drag);

            substeps = Mathf.Max(1, substeps);
            solverIterations = Mathf.Max(1, solverIterations);
            maxVelocity = Mathf.Max(0.0001f, maxVelocity);
            damping = Mathf.Max(0f, damping);

            fillHeightPercent = Mathf.Clamp01(fillHeightPercent);
            fillHeightPercent = Mathf.Max(0.01f, fillHeightPercent);
            spawnJitter = Mathf.Clamp(spawnJitter, 0f, 0.45f);

            particleVisualSize = Mathf.Max(0.0001f, particleVisualSize);
            opacity = Mathf.Clamp01(opacity);
            smoothness = Mathf.Clamp01(smoothness);
            metallic = Mathf.Clamp01(metallic);

            bool paintAppearanceChanged = _hasValidatedPaintAppearance &&
                                          (paintColor != _lastValidatedPaintColor ||
                                           !Mathf.Approximately(opacity, _lastValidatedOpacity));
            CacheValidatedPaintAppearance();

            if (Application.isPlaying && paintAppearanceChanged)
            {
                SyncPaintAppearanceToPhysicsSettings();
            }
        }

        private void CacheValidatedPaintAppearance()
        {
            _lastValidatedPaintColor = paintColor;
            _lastValidatedOpacity = opacity;
            _hasValidatedPaintAppearance = true;
        }

        private void SyncPaintAppearanceToPhysicsSettings()
        {
            SwingingPaint.Core.PhysicsSettings physicsSettings = null;

            if (SwingingPaint.Core.SimulationManager.Instance != null)
            {
                physicsSettings = SwingingPaint.Core.SimulationManager.Instance.physicsSettings;
            }

            if (physicsSettings == null)
            {
                return;
            }

            Color syncedColor = paintColor;
            syncedColor.a = opacity;
            physicsSettings.SetPaintColor(syncedColor);
        }
    }
}

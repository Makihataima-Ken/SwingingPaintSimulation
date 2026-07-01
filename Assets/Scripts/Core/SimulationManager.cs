using UnityEngine;
using SwingingPaint.BucketFluid.Core;
using SwingingPaint.Paint;
using SwingingPaint.Surface;
using SwingingPaint.UI;
using SwingingPaint.Core;

namespace SwingingPaint.Core
{
    /// <summary>
    /// Central coordinator for the Swinging Paint Simulation.
    ///
    /// Responsibilities:
    /// - Holds the reference to PhysicsSettings (single source of truth).
    /// - Manages simulation state (running, paused).
    /// - Provides reset/restart logic that restores initial conditions.
    /// - Notifies all systems of parameter changes via events.
    ///
    /// Design decisions:
    /// - Singleton pattern via static Instance for easy access from UI and other systems.
    /// - Does not perform physics itself; delegates to Pendulum and other systems.
    /// - Reset captures the initial state at start so Restart can restore it exactly.
    /// </summary>
    public class SimulationManager : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Reference to the centralized physics settings asset.")]
        public PhysicsSettings physicsSettings;

        [Header("References")]
        [Tooltip("The Pendulum component driving the simulation.")]
        public Pendulum pendulum;
        public global::BucketOrientationController bucketOrientationController;
        public SimulationSettings simulationSettings;
        public BucketMotionProvider bucketMotionProvider;
        public GPUFluidSimulator fluidSimulator;
        public GPUFluidOutflowController gpuOutflowController;
        public PaintEmitter paintEmitter;
        public CanvasPaintSurface paintSurface;
        public UIController uiController;

        [Header("Fixed Step Runtime")]
        public bool driveFixedStepSimulation = true;
        [Min(0.001f)]
        public float fixedTimestep = 1f / 60f;
        [Min(1)]
        public int maxStepsPerRenderedFrame = 4;
        [Tooltip("Maximum simulated time kept in the accumulator after a frame hitch.")]
        public float maxAccumulatedTime = 0.12f;

        private bool _isPaused = false;
        private float _initialAngle;
        private float _initialAngularVelocity;
        private float _accumulator;
        private PhysicsSettings _subscribedSettings;

        /// <summary>
        /// Global access to the active SimulationManager.
        /// </summary>
        public static SimulationManager Instance { get; private set; }

        /// <summary>
        /// True when the simulation is paused.
        /// </summary>
        public bool IsPaused => _isPaused;

        /// <summary>
        /// Fired when the simulation is paused.
        /// </summary>
        public event System.Action OnPaused;

        /// <summary>
        /// Fired when the simulation is resumed.
        /// </summary>
        public event System.Action OnResumed;

        /// <summary>
        /// Fired when the simulation is reset or restarted.
        /// </summary>
        public event System.Action OnReset;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Multiple SimulationManagers detected. Destroying duplicate.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            SubscribeToSettings();
        }

        private void OnDisable()
        {
            UnsubscribeFromSettings();
        }

        private void Start()
        {
            ResolveRuntimeReferences();

            if (physicsSettings == null)
            {
                Debug.LogError("SimulationManager: No PhysicsSettings assigned!", this);
                enabled = false;
                return;
            }

            SubscribeToSettings();

            // Capture initial pendulum state for reset/restart
            if (pendulum != null)
            {
                _initialAngle = pendulum.CurrentAngle;
                _initialAngularVelocity = pendulum.CurrentAngularVelocity;
            }
        }

        private void Update()
        {
            if (!driveFixedStepSimulation)
            {
                return;
            }

            if (_isPaused || (simulationSettings != null && !simulationSettings.SimulationRunning))
            {
                _accumulator = 0f;
                return;
            }

            float timeScale = simulationSettings != null ? simulationSettings.TimeScale : 1f;
            float frameDeltaTime = Mathf.Max(0f, Time.deltaTime * timeScale);
            float stepDeltaTime = Mathf.Max(0.001f, fixedTimestep);
            _accumulator = Mathf.Min(_accumulator + frameDeltaTime, Mathf.Max(stepDeltaTime, maxAccumulatedTime));

            int steps = 0;
            int maxSteps = Mathf.Max(1, maxStepsPerRenderedFrame);
            while (_accumulator >= stepDeltaTime && steps < maxSteps)
            {
                StepSimulation(stepDeltaTime);
                _accumulator -= stepDeltaTime;
                steps++;
            }

            if (steps == maxSteps && _accumulator >= stepDeltaTime)
            {
                _accumulator = Mathf.Min(_accumulator, stepDeltaTime);
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromSettings();

            if (Instance == this)
                Instance = null;
        }

        // ------------------------------------------------------------------
        // Simulation Control
        // ------------------------------------------------------------------

        /// <summary>
        /// Pauses the simulation. Update loops should check IsPaused.
        /// </summary>
        public void PauseSimulation()
        {
            if (_isPaused) return;
            _isPaused = true;
            OnPaused?.Invoke();
            Debug.Log("[SimulationManager] Simulation paused.");
        }

        /// <summary>
        /// Resumes the simulation.
        /// </summary>
        public void ResumeSimulation()
        {
            if (!_isPaused) return;
            _isPaused = false;
            OnResumed?.Invoke();
            Debug.Log("[SimulationManager] Simulation resumed.");
        }

        /// <summary>
        /// Resets the simulation to its initial state using current settings.
        /// Keeps the simulation running (or paused) as it was.
        /// </summary>
        public void ResetSimulation()
        {
            ResolveRuntimeReferences();
            _accumulator = 0f;

            ApplyResetRequiredSettings();
            ApplyImmediateSettings();

            if (pendulum != null)
            {
                pendulum.ResetState(physicsSettings.InitialAngle, physicsSettings.AngularVelocity);
            }

            if (bucketOrientationController != null)
            {
                bucketOrientationController.ResetOrientation(true);
            }

            if (bucketMotionProvider != null)
            {
                bucketMotionProvider.ResetMotionTracking();
            }

            if (fluidSimulator != null)
            {
                fluidSimulator.ResetFluid();
            }

            if (gpuOutflowController != null)
            {
                gpuOutflowController.ResetOutflow();
            }

            if (paintEmitter != null)
            {
                paintEmitter.ResetEmitter();
            }

            if (paintSurface != null)
            {
                paintSurface.ClearPaint();
            }

            OnReset?.Invoke();
            Debug.Log("[SimulationManager] Simulation reset.");
        }

        /// <summary>
        /// Restarts the simulation: resets and ensures it is running.
        /// </summary>
        public void RestartSimulation()
        {
            _isPaused = false;
            ResetSimulation();
            OnResumed?.Invoke();
            Debug.Log("[SimulationManager] Simulation restarted.");
        }

        /// <summary>
        /// Resets all PhysicsSettings to their default values.
        /// Safe immediate values are applied through OnSettingsChanged; reset-required values
        /// are applied the next time the simulation is reset or restarted.
        /// </summary>
        public void ResetParametersToDefaults()
        {
            if (physicsSettings == null) return;

            physicsSettings.SetGravity(9.81f);
            physicsSettings.SetDamping(0.05f);
            physicsSettings.SetInitialAngle(30f);
            physicsSettings.SetAngularVelocity(0f);
            physicsSettings.SetInitialLateralAngularVelocity(25f);
            physicsSettings.SetDirection(0f);
            physicsSettings.SetBucketMass(1.2f);
            physicsSettings.SetPaintMass(1f);
            physicsSettings.SetAirResistance(0.02f);
            physicsSettings.SetSwingCountLimit(0);
            physicsSettings.SetRestLength(2f);
            physicsSettings.SetRopeStiffness(50f);
            physicsSettings.SetRopeElasticity(0.5f);
            physicsSettings.SetPaintFlowRate(1.0f);
            physicsSettings.SetPaintViscosity(0.5f);
            physicsSettings.SetPaintQuantity(100f);
            physicsSettings.SetPaintHoleDiameter(0.035f);
            physicsSettings.SetSurfaceAbsorption(0.1f);
            physicsSettings.SetPaintSpreadRadius(0.2f);

            Debug.Log("[SimulationManager] Parameters reset to defaults.");
        }

        private void SubscribeToSettings()
        {
            if (_subscribedSettings == physicsSettings)
            {
                return;
            }

            UnsubscribeFromSettings();

            if (physicsSettings == null)
            {
                return;
            }

            _subscribedSettings = physicsSettings;
            _subscribedSettings.OnSettingsChanged += HandleSettingsChanged;
        }

        private void UnsubscribeFromSettings()
        {
            if (_subscribedSettings == null)
            {
                return;
            }

            _subscribedSettings.OnSettingsChanged -= HandleSettingsChanged;
            _subscribedSettings = null;
        }

        private void HandleSettingsChanged()
        {
            ResolveRuntimeReferences();
            ApplyImmediateSettings();
        }

        private void ApplyImmediateSettings()
        {
            if (physicsSettings == null)
            {
                return;
            }

            if (pendulum != null)
            {
                pendulum.gravity = physicsSettings.Gravity;
                pendulum.airResistance = physicsSettings.AirResistance;
            }

            if (bucketMotionProvider != null)
            {
                bucketMotionProvider.gravity = physicsSettings.Gravity;
            }

            if (fluidSimulator != null && fluidSimulator.settings != null)
            {
                fluidSimulator.settings.gravity = physicsSettings.Gravity;
            }

            if (paintEmitter != null)
            {
                paintEmitter.physicsSettings = physicsSettings;
                paintEmitter.gravity = physicsSettings.Gravity;
                paintEmitter.airResistance = physicsSettings.AirResistance;
                paintEmitter.holeDiameter = physicsSettings.PaintHoleDiameter;
                paintEmitter.defaultFlowRate = physicsSettings.PaintFlowRate;
                paintEmitter.defaultPaintViscosity = physicsSettings.PaintViscosity;
                paintEmitter.defaultPaintQuantity = physicsSettings.PaintQuantity;
            }

            if (gpuOutflowController != null)
            {
                gpuOutflowController.physicsSettings = physicsSettings;
                gpuOutflowController.holeDiameter = physicsSettings.PaintHoleDiameter;
            }

            if (paintSurface != null)
            {
                paintSurface.physicsSettings = physicsSettings;
                paintSurface.defaultAbsorption = physicsSettings.SurfaceAbsorption;
            }

            if (uiController != null)
            {
                uiController.physicsSettings = physicsSettings;
                uiController.RefreshUI();
            }
        }

        private void ApplyResetRequiredSettings()
        {
            if (physicsSettings == null || pendulum == null)
            {
                return;
            }

            pendulum.initialAngleDegrees = physicsSettings.InitialAngle;
            pendulum.initialAngularVelocity = physicsSettings.AngularVelocity;
            pendulum.initialLateralAngularVelocityDegrees = physicsSettings.InitialLateralAngularVelocity;
            pendulum.directionAngleDegrees = physicsSettings.Direction;
            pendulum.swingDirectionDegrees = physicsSettings.Direction;
            pendulum.damping = physicsSettings.Damping;
            pendulum.bucketMass = physicsSettings.BucketMass;
            pendulum.paintMass = physicsSettings.PaintMass;
            pendulum.swingCountLimit = physicsSettings.SwingCountLimit;
            pendulum.restLength = physicsSettings.RestLength;
            pendulum.ropeStiffness = physicsSettings.RopeStiffness;
            pendulum.ropeElasticity = physicsSettings.RopeElasticity;
        }

        private void StepSimulation(float deltaTime)
        {
            // Step order: pendulum position, bucket orientation, bucket motion sample, fluid, falling paint/deposition.
            if (pendulum != null)
            {
                pendulum.StepSimulation(deltaTime);
            }

            if (bucketOrientationController != null)
            {
                bucketOrientationController.StepOrientation(deltaTime);
            }

            if (bucketMotionProvider != null)
            {
                bucketMotionProvider.StepMotion(deltaTime);
            }

            if (fluidSimulator != null)
            {
                fluidSimulator.StepSimulation(deltaTime);
            }

            if (gpuOutflowController == null && fluidSimulator != null)
            {
                gpuOutflowController = fluidSimulator.outflowController;
            }

            bool gpuOutflowCanRun = gpuOutflowController != null && gpuOutflowController.CanRunPrimaryOutflow;
            if (paintEmitter != null && !gpuOutflowCanRun)
            {
                paintEmitter.Step(deltaTime);
            }
        }

        private void ResolveRuntimeReferences()
        {
            if (simulationSettings == null && pendulum != null)
            {
                simulationSettings = pendulum.simulationSettings;
            }

            if (pendulum == null)
            {
                pendulum = FindObjectOfType<Pendulum>();
            }

            if (bucketMotionProvider == null)
            {
                bucketMotionProvider = FindObjectOfType<BucketMotionProvider>();
            }

            if (bucketOrientationController == null)
            {
                Transform bucketTransform = pendulum != null
                    ? pendulum.bucketTransform
                    : bucketMotionProvider != null
                        ? bucketMotionProvider.BucketTransform
                        : null;

                if (bucketTransform != null)
                {
                    bucketOrientationController = bucketTransform.GetComponent<global::BucketOrientationController>();
                }
            }

            if (bucketOrientationController == null)
            {
                bucketOrientationController = FindObjectOfType<global::BucketOrientationController>();
            }

            if (fluidSimulator == null)
            {
                fluidSimulator = FindObjectOfType<GPUFluidSimulator>();
            }

            if (gpuOutflowController == null && fluidSimulator != null)
            {
                gpuOutflowController = fluidSimulator.outflowController;
            }

            if (gpuOutflowController == null)
            {
                gpuOutflowController = FindObjectOfType<GPUFluidOutflowController>();
            }

            if (paintEmitter == null)
            {
                paintEmitter = FindObjectOfType<PaintEmitter>();
            }

            if (paintSurface == null)
            {
                paintSurface = FindObjectOfType<CanvasPaintSurface>();
            }

            if (uiController == null)
            {
                uiController = FindObjectOfType<UIController>();
            }

            if (physicsSettings == null)
            {
                physicsSettings = Resources.Load<PhysicsSettings>("PhysicsSettings");
                SubscribeToSettings();
            }

            if (paintEmitter != null && paintEmitter.physicsSettings == null)
            {
                paintEmitter.physicsSettings = physicsSettings;
            }

            if (gpuOutflowController != null && gpuOutflowController.physicsSettings == null)
            {
                gpuOutflowController.physicsSettings = physicsSettings;
            }

            if (paintSurface != null && paintSurface.physicsSettings == null)
            {
                paintSurface.physicsSettings = physicsSettings;
            }

            if (uiController != null && uiController.physicsSettings == null)
            {
                uiController.physicsSettings = physicsSettings;
            }
        }

        private void OnValidate()
        {
            fixedTimestep = Mathf.Max(0.001f, fixedTimestep);
            maxStepsPerRenderedFrame = Mathf.Max(1, maxStepsPerRenderedFrame);
            maxAccumulatedTime = Mathf.Max(fixedTimestep, maxAccumulatedTime);
        }
    }
}

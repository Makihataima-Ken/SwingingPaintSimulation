using UnityEngine;
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

        private bool _isPaused = false;
        private float _initialAngle;
        private float _initialAngularVelocity;

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

        private void Start()
        {
            if (physicsSettings == null)
            {
                Debug.LogError("SimulationManager: No PhysicsSettings assigned!", this);
                enabled = false;
                return;
            }

            // Capture initial pendulum state for reset/restart
            if (pendulum != null)
            {
                _initialAngle = pendulum.CurrentAngle;
                _initialAngularVelocity = pendulum.CurrentAngularVelocity;
            }
        }

        private void OnDestroy()
        {
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
            if (pendulum != null)
            {
                pendulum.ResetState(physicsSettings.InitialAngle, physicsSettings.AngularVelocity);
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
        /// The Pendulum will be notified via OnSettingsChanged.
        /// </summary>
        public void ResetParametersToDefaults()
        {
            if (physicsSettings == null) return;

            physicsSettings.SetGravity(9.81f);
            physicsSettings.SetDamping(0.05f);
            physicsSettings.SetInitialAngle(30f);
            physicsSettings.SetAngularVelocity(0f);
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
            physicsSettings.SetSurfaceAbsorption(0.1f);
            physicsSettings.SetPaintSpreadRadius(0.2f);

            Debug.Log("[SimulationManager] Parameters reset to defaults.");
        }
    }
}

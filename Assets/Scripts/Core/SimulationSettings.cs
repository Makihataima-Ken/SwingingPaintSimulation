using UnityEngine;

namespace SwingingPaint.Core
{
    /// <summary>
    /// Stores global simulation settings for the Swinging Paint Bucket Simulation.
    ///
    /// This class is only a data container. It does not use Rigidbody, Colliders,
    /// Unity physics queries, or any built-in physics simulation.
    /// </summary>
    public class SimulationSettings : MonoBehaviour
    {
        [Header("Global Physics Settings")]
        [Tooltip("Manual gravity acceleration used by custom simulation systems.")]
        [SerializeField] private float gravity = 9.81f;

        [Tooltip("Manual air resistance applied by custom particle and pendulum systems.")]
        [SerializeField] private float airResistance = 0.02f;

        [Tooltip("Multiplier for custom simulation time. 1 is real time, 0 pauses motion calculations.")]
        [SerializeField] private float timeScale = 1f;

        [Tooltip("Whether custom simulation systems should advance.")]
        [SerializeField] private bool simulationRunning = true;

        [Tooltip("Fixed timestep for custom simulation updates when deterministic stepping is needed.")]
        [SerializeField] private float fixedSimulationDeltaTime = 0.01666667f;

        public float Gravity => gravity;
        public float AirResistance => airResistance;
        public float TimeScale => timeScale;
        public bool SimulationRunning => simulationRunning;
        public float FixedSimulationDeltaTime => fixedSimulationDeltaTime;

        private void OnValidate()
        {
            gravity = Mathf.Max(0f, gravity);
            airResistance = Mathf.Max(0f, airResistance);
            timeScale = Mathf.Max(0f, timeScale);
            fixedSimulationDeltaTime = Mathf.Max(0.0001f, fixedSimulationDeltaTime);
        }
    }
}

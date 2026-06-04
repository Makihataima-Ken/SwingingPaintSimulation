using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SwingingPaint.Core;

namespace SwingingPaint.UI
{
    /// <summary>
    /// Runtime physics tuning panel for the Swinging Paint Simulation.
    ///
    /// Design decisions:
    /// - Uses Unity UI (Canvas, Panels, Sliders, InputFields, Buttons, Text).
    /// - Each parameter has a slider + input field pair for both coarse and precise control.
    /// - Text labels show current values beside sliders.
    /// - Values are applied immediately (live updates) without scene reloads.
    /// - Separated into groups: Physics, Rope, and Paint.
    /// - Self-initializes missing references by searching the scene (useful for prefabs).
    ///
    /// UI Setup (in Unity Editor):
    /// 1. Create a Canvas (Screen Space - Overlay).
    /// 2. Create a child Panel named "PhysicsPanel".
    /// 3. Inside PhysicsPanel, create rows for each parameter:
    ///    - ParameterNameText (TextMeshProUGUI)
    ///    - Slider
    ///    - ValueInput (TMP_InputField)
    ///    - ValueText (TextMeshProUGUI) - optional, can share with input field
    /// 4. Reference them in the inspector or this script will auto-find by name.
    /// </summary>
    public class UIController : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // Settings Reference
        // ------------------------------------------------------------------

        [Header("Settings Reference")]
        [Tooltip("Reference to the PhysicsSettings ScriptableObject. If left empty, searches for an existing asset.")]
        public PhysicsSettings physicsSettings;

        // ------------------------------------------------------------------
        // UI Controls - Physics
        // ------------------------------------------------------------------

        [Header("UI Controls - Physics")]
        public Slider gravitySlider;
        public TMP_InputField gravityInput;
        public TextMeshProUGUI gravityValueLabel;

        public Slider dampingSlider;
        public TMP_InputField dampingInput;
        public TextMeshProUGUI dampingValueLabel;

        public Slider initialAngleSlider;
        public TMP_InputField initialAngleInput;
        public TextMeshProUGUI initialAngleValueLabel;

        // ------------------------------------------------------------------
        // UI Controls - Rope
        // ------------------------------------------------------------------

        [Header("UI Controls - Rope")]
        public Slider restLengthSlider;
        public TMP_InputField restLengthInput;
        public TextMeshProUGUI restLengthValueLabel;

        public Slider ropeStiffnessSlider;
        public TMP_InputField ropeStiffnessInput;
        public TextMeshProUGUI ropeStiffnessValueLabel;

        public Slider ropeElasticitySlider;
        public TMP_InputField ropeElasticityInput;
        public TextMeshProUGUI ropeElasticityValueLabel;

        // ------------------------------------------------------------------
        // UI Controls - Paint
        // ------------------------------------------------------------------

        [Header("UI Controls - Paint")]
        public Slider paintFlowSlider;
        public TMP_InputField paintFlowInput;
        public TextMeshProUGUI paintFlowValueLabel;

        public Slider paintViscositySlider;
        public TMP_InputField paintViscosityInput;
        public TextMeshProUGUI paintViscosityValueLabel;

        public Slider surfaceAbsorptionSlider;
        public TMP_InputField surfaceAbsorptionInput;
        public TextMeshProUGUI surfaceAbsorptionValueLabel;

        // ------------------------------------------------------------------
        // UI Controls - Simulation
        // ------------------------------------------------------------------

        [Header("UI Controls - Simulation")]
        public Button resetParametersButton;
        public Button pauseButton;
        public Button resumeButton;
        public Button restartButton;

        // ------------------------------------------------------------------
        // Debug Panel
        // ------------------------------------------------------------------

        [Header("Debug Panel")]
        public TextMeshProUGUI debugAngleText;
        public TextMeshProUGUI debugAngularVelocityText;
        public TextMeshProUGUI debugRopeLengthText;
        public TextMeshProUGUI debugMaxExtensionText;
        public TextMeshProUGUI debugBucketPositionText;

        // ------------------------------------------------------------------
        // Private
        // ------------------------------------------------------------------

        private Pendulum _pendulum;
        private bool _initialized = false;

        // Track active UI elements to avoid null-checks in every frame
        private readonly List<Slider> _sliders = new List<Slider>();
        private readonly List<TMP_InputField> _inputs = new List<TMP_InputField>();
        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            if (physicsSettings == null)
            {
                Debug.LogError("UIController: PhysicsSettings is not assigned! Assign it in the Inspector or place a PhysicsSettings asset in a Resources folder.", this);
                enabled = false;
                return;
            }

            _pendulum = FindObjectOfType<Pendulum>();

            InitializeUI();
            BindEvents();
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized)
                return;

            UpdateDebugPanel();
        }

        // ------------------------------------------------------------------
        // Reference Resolution
        // ------------------------------------------------------------------

        /// <summary>
        /// Attempts to auto-resolve references that were not assigned in the Inspector.
        /// </summary>
        private void ResolveReferences()
        {
            if (physicsSettings == null)
            {
                physicsSettings = Resources.Load<PhysicsSettings>("PhysicsSettings");
            }
        }

        // ------------------------------------------------------------------
        // Initialization
        // ------------------------------------------------------------------

        private void InitializeUI()
        {
            // Physics
            SetupSlider(gravitySlider, gravityInput, gravityValueLabel, 0f, 20f, physicsSettings.Gravity);
            SetupSlider(dampingSlider, dampingInput, dampingValueLabel, 0f, 1f, physicsSettings.Damping);
            SetupSlider(initialAngleSlider, initialAngleInput, initialAngleValueLabel, -90f, 90f, physicsSettings.InitialAngle);

            // Rope
            SetupSlider(restLengthSlider, restLengthInput, restLengthValueLabel, 0.5f, 5f, physicsSettings.RestLength);
            SetupSlider(ropeStiffnessSlider, ropeStiffnessInput, ropeStiffnessValueLabel, 1f, 200f, physicsSettings.RopeStiffness);
            SetupSlider(ropeElasticitySlider, ropeElasticityInput, ropeElasticityValueLabel, 0f, 2f, physicsSettings.RopeElasticity);

            // Paint
            SetupSlider(paintFlowSlider, paintFlowInput, paintFlowValueLabel, 0f, 10f, physicsSettings.PaintFlowRate);
            SetupSlider(paintViscositySlider, paintViscosityInput, paintViscosityValueLabel, 0f, 5f, physicsSettings.PaintViscosity);
            SetupSlider(surfaceAbsorptionSlider, surfaceAbsorptionInput, surfaceAbsorptionValueLabel, 0f, 1f, physicsSettings.SurfaceAbsorption);
        }

        private void SetupSlider(Slider slider, TMP_InputField input, TextMeshProUGUI label, float min, float max, float initialValue)
        {
            if (slider != null)
            {
                slider.minValue = min;
                slider.maxValue = max;
                slider.value = Mathf.Clamp(initialValue, min, max);
            }
            if (input != null)
            {
                input.text = initialValue.ToString("F3");
            }
            if (label != null)
            {
                label.text = initialValue.ToString("F3");
            }
        }

        // ------------------------------------------------------------------
        // Event Binding
        // ------------------------------------------------------------------

        private void BindEvents()
        {
            // Physics
            BindSlider(gravitySlider, gravityInput, gravityValueLabel, physicsSettings.SetGravity);
            BindSlider(dampingSlider, dampingInput, dampingValueLabel, physicsSettings.SetDamping);
            BindSlider(initialAngleSlider, initialAngleInput, initialAngleValueLabel, physicsSettings.SetInitialAngle);

            // Rope
            BindSlider(restLengthSlider, restLengthInput, restLengthValueLabel, physicsSettings.SetRestLength);
            BindSlider(ropeStiffnessSlider, ropeStiffnessInput, ropeStiffnessValueLabel, physicsSettings.SetRopeStiffness);
            BindSlider(ropeElasticitySlider, ropeElasticityInput, ropeElasticityValueLabel, physicsSettings.SetRopeElasticity);

            // Paint
            BindSlider(paintFlowSlider, paintFlowInput, paintFlowValueLabel, physicsSettings.SetPaintFlowRate);
            BindSlider(paintViscositySlider, paintViscosityInput, paintViscosityValueLabel, physicsSettings.SetPaintViscosity);
            BindSlider(surfaceAbsorptionSlider, surfaceAbsorptionInput, surfaceAbsorptionValueLabel, physicsSettings.SetSurfaceAbsorption);

            // Buttons
            if (resetParametersButton != null)
                resetParametersButton.onClick.AddListener(OnResetParametersClicked);
            if (pauseButton != null)
                pauseButton.onClick.AddListener(OnPauseClicked);
            if (resumeButton != null)
                resumeButton.onClick.AddListener(OnResumeClicked);
            if (restartButton != null)
                restartButton.onClick.AddListener(OnRestartClicked);
        }

        /// <summary>
        /// Binds a Slider and an InputField to a settings setter, keeping both in sync.
        /// </summary>
        private void BindSlider(Slider slider, TMP_InputField input, TextMeshProUGUI label, System.Action<float> setter)
        {
            if (slider != null)
            {
                slider.onValueChanged.AddListener(val =>
                {
                    setter(val);
                    if (input != null) input.text = val.ToString("F3");
                    if (label != null) label.text = val.ToString("F3");
                });
            }

            if (input != null)
            {
                input.onEndEdit.AddListener(text =>
                {
                    if (float.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float val))
                    {
                        if (slider != null)
                        {
                            val = Mathf.Clamp(val, slider.minValue, slider.maxValue);
                            slider.value = val;
                        }
                        setter(val);
                        if (label != null) label.text = val.ToString("F3");
                    }
                });
            }
        }

        // ------------------------------------------------------------------
        // Button Handlers
        // ------------------------------------------------------------------

        private void OnResetParametersClicked()
        {
            if (SwingingPaint.Core.SimulationManager.Instance != null)
            {
                SwingingPaint.Core.SimulationManager.Instance.ResetParametersToDefaults();
                RefreshUI();
            }
        }

        private void OnPauseClicked()
        {
            if (SwingingPaint.Core.SimulationManager.Instance != null)
                SwingingPaint.Core.SimulationManager.Instance.PauseSimulation();
        }

        private void OnResumeClicked()
        {
            if (SwingingPaint.Core.SimulationManager.Instance != null)
                SwingingPaint.Core.SimulationManager.Instance.ResumeSimulation();
        }

        private void OnRestartClicked()
        {
            if (SwingingPaint.Core.SimulationManager.Instance != null)
                SwingingPaint.Core.SimulationManager.Instance.RestartSimulation();
        }

        // ------------------------------------------------------------------
        // UI Refresh
        // ------------------------------------------------------------------

        /// <summary>
        /// Refreshes all UI controls to match the current PhysicsSettings values.
        /// Call this after a mass reset or when settings are changed externally.
        /// </summary>
        public void RefreshUI()
        {
            if (physicsSettings == null)
                return;

            SetupSlider(gravitySlider, gravityInput, gravityValueLabel, 0f, 20f, physicsSettings.Gravity);
            SetupSlider(dampingSlider, dampingInput, dampingValueLabel, 0f, 1f, physicsSettings.Damping);
            SetupSlider(initialAngleSlider, initialAngleInput, initialAngleValueLabel, -90f, 90f, physicsSettings.InitialAngle);

            SetupSlider(restLengthSlider, restLengthInput, restLengthValueLabel, 0.5f, 5f, physicsSettings.RestLength);
            SetupSlider(ropeStiffnessSlider, ropeStiffnessInput, ropeStiffnessValueLabel, 1f, 200f, physicsSettings.RopeStiffness);
            SetupSlider(ropeElasticitySlider, ropeElasticityInput, ropeElasticityValueLabel, 0f, 2f, physicsSettings.RopeElasticity);

            SetupSlider(paintFlowSlider, paintFlowInput, paintFlowValueLabel, 0f, 10f, physicsSettings.PaintFlowRate);
            SetupSlider(paintViscositySlider, paintViscosityInput, paintViscosityValueLabel, 0f, 5f, physicsSettings.PaintViscosity);
            SetupSlider(surfaceAbsorptionSlider, surfaceAbsorptionInput, surfaceAbsorptionValueLabel, 0f, 1f, physicsSettings.SurfaceAbsorption);
        }

        // ------------------------------------------------------------------
        // Debug Panel
        // ------------------------------------------------------------------

        private void UpdateDebugPanel()
        {
            if (_pendulum == null) return;

            if (debugAngleText != null)
                debugAngleText.text = $"Angle: {_pendulum.CurrentAngle:F2} deg";

            if (debugAngularVelocityText != null)
                debugAngularVelocityText.text = $"Ang Velocity: {_pendulum.CurrentAngularVelocity:F2} deg/s";

            if (debugRopeLengthText != null)
                debugRopeLengthText.text = $"Rope Length: {_pendulum.CurrentRopeLength:F3} m";

            if (debugMaxExtensionText != null)
                debugMaxExtensionText.text = $"Max Extension: {_pendulum.MaxRopeExtension:F3} m";

            if (debugBucketPositionText != null)
            {
                Vector3 pos = _pendulum.GetBucketPosition();
                debugBucketPositionText.text = $"Bucket: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})";
            }
        }
    }
}

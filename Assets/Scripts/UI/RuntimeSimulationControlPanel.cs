using SwingingPaint.BucketFluid.Core;
using SwingingPaint.Core;
using SwingingPaint.Surface;
using UnityEngine;

namespace SwingingPaint.UI
{
    /// <summary>
    /// Compact play-mode control panel for presentation tuning.
    /// It edits PresentationTuningControls so runtime UI and Inspector tuning share one path.
    /// </summary>
    public class RuntimeSimulationControlPanel : MonoBehaviour
    {
        [Header("Display")]
        public bool showPanel = true;
        public KeyCode toggleKey = KeyCode.F1;
        public Rect panelRect = new Rect(12f, 12f, 360f, 520f);
        [Min(260f)]
        public float maxPanelWidth = 380f;
        [Min(220f)]
        public float maxPanelHeight = 560f;

        [Header("References")]
        public SimulationManager simulationManager;
        public PresentationTuningControls tuningControls;
        public PhysicsSettings physicsSettings;
        public GPUFluidOutflowController gpuOutflowController;
        public CanvasPaintSurface paintSurface;
        public SurfaceMaterialProfile[] surfaceMaterialProfiles;

        private Vector2 _scroll;
        private bool _restartRecommended;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                showPanel = !showPanel;
            }
        }

        private void OnGUI()
        {
            if (!showPanel)
            {
                return;
            }

            ResolveReferences();
            ClampPanelRectToScreen();
            panelRect = GUILayout.Window(GetInstanceID(), panelRect, DrawPanel, "Simulation Control");
        }

        private void DrawPanel(int windowId)
        {
            float contentWidth = Mathf.Max(220f, panelRect.width - 18f);
            float contentHeight = Mathf.Max(120f, panelRect.height - 42f);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(contentWidth), GUILayout.Height(contentHeight));

            DrawTopControls();
            DrawStatus();

            if (tuningControls == null)
            {
                GUILayout.Space(8f);
                GUILayout.Label("PresentationTuningControls: n/a");
                GUILayout.Label("Attach or enable it to edit simulation parameters.");
                GUILayout.EndScrollView();
                GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
                return;
            }

            DrawPaintControls();
            DrawStreamControls();
            DrawMotionControls();
            DrawCanvasControls();

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void DrawTopControls()
        {
            GUILayout.BeginHorizontal();

            bool paused = simulationManager != null && simulationManager.IsPaused;
            string pauseLabel = paused ? "Resume" : "Pause";
            if (GUILayout.Button(pauseLabel))
            {
                if (simulationManager != null)
                {
                    if (paused)
                    {
                        simulationManager.ResumeSimulation();
                    }
                    else
                    {
                        simulationManager.PauseSimulation();
                    }
                }
            }

            if (GUILayout.Button("Restart"))
            {
                ApplyTuning();
                simulationManager?.RestartSimulation();
                _restartRecommended = false;
            }

            if (GUILayout.Button("Reset Sim"))
            {
                ApplyTuning();
                simulationManager?.ResetSimulation();
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Reset Params"))
            {
                simulationManager?.ResetParametersToDefaults();
                tuningControls?.PullCurrentValuesFromScene();
                _restartRecommended = true;
            }

            if (GUILayout.Button("Clear Canvas"))
            {
                paintSurface?.ClearPaint();
            }

            if (GUILayout.Button("Hide"))
            {
                showPanel = false;
            }

            GUILayout.EndHorizontal();
            GUILayout.Label($"Toggle: {toggleKey}");

            if (_restartRecommended)
            {
                GUILayout.Label("Restart recommended for initial motion / reset-required changes.");
            }
        }

        private void DrawStatus()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Status");
            GUILayout.Label($"State: {(simulationManager != null ? (simulationManager.IsPaused ? "Paused" : "Running") : "n/a")}");
            GUILayout.Label($"Remaining Paint: {(gpuOutflowController != null ? (gpuOutflowController.RemainingPaintFraction * 100f).ToString("F1") + "%" : "n/a")}");
            GUILayout.Label($"Active Outflow: {(gpuOutflowController != null ? gpuOutflowController.ActiveOutflowParticles.ToString() : "n/a")}");
            GUILayout.Label($"Emitted/Tick: {(gpuOutflowController != null ? gpuOutflowController.EmittedParticlesThisTick.ToString() : "n/a")}");
            GUILayout.Label($"Canvas Writes/Tick: {(gpuOutflowController != null ? gpuOutflowController.CanvasGpuWritesThisTick.ToString() : "n/a")}");
            GUILayout.Label($"Physical Flow: {(gpuOutflowController != null ? gpuOutflowController.CurrentPhysicalFlowRateCubicMetersPerSecond.ToString("F6") : "n/a")}");
        }

        private void DrawPaintControls()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Paint");

            Color color = tuningControls.paintColor;
            bool colorChanged = false;
            colorChanged |= DrawSlider("Color R", ref color.r, 0f, 1f, false);
            colorChanged |= DrawSlider("Color G", ref color.g, 0f, 1f, false);
            colorChanged |= DrawSlider("Color B", ref color.b, 0f, 1f, false);
            if (colorChanged)
            {
                color.a = Mathf.Clamp01(color.a);
                tuningControls.paintColor = color;
                ApplyTuning();
            }

            DrawTuningSlider("Flow Rate", ref tuningControls.flowRate, 0f, 10f);
            DrawTuningSlider("Hole Diameter", ref tuningControls.holeDiameter, 0f, 0.12f);
            DrawTuningSlider("Viscosity", ref tuningControls.viscosity, 0f, 5f);
            DrawTuningSlider("Paint Quantity", ref tuningControls.logicalPaintQuantity, 0f, 250f);
            DrawTuningToggle("Infinite Paint", ref tuningControls.infinitePaintSupplyForTuning);
        }

        private void DrawStreamControls()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Stream");
            DrawTuningSlider("Stream Width", ref tuningControls.streamWidth, 0.5f, 4f);
            DrawTuningSlider("Continuity", ref tuningControls.streamContinuity, 0f, 1f);
            DrawTuningSlider("Visual Continuity", ref tuningControls.streamVisualContinuity, 0f, 1f);
            DrawTuningSlider("Trail Length", ref tuningControls.streamTrailLength, 0.5f, 4f);
            DrawTuningSlider("Outflow Lifetime", ref tuningControls.outflowLifetime, 0.1f, 60f);
            DrawTuningSlider("Stream Opacity", ref tuningControls.streamOpacity, 0f, 2f);
        }

        private void DrawMotionControls()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Motion");
            if (physicsSettings != null)
            {
                float gravity = physicsSettings.Gravity;
                if (DrawSlider("Gravity", ref gravity, 0f, 20f, false))
                {
                    physicsSettings.SetGravity(gravity);
                }
            }
            else
            {
                GUILayout.Label("Gravity: n/a");
            }

            DrawTuningSlider("Start Angle", ref tuningControls.startAngle, -90f, 90f, true);
            DrawTuningSlider("Side Push", ref tuningControls.sidePushVelocity, -720f, 720f, true);
            DrawTuningSlider("Swing Direction", ref tuningControls.swingDirection, -180f, 180f, true);
            DrawTuningSlider("Rope Length", ref tuningControls.ropeLength, 0.5f, 5f, true);
            DrawTuningSlider("Motion Damping", ref tuningControls.motionDamping, 0f, 1f);
        }

        private void DrawCanvasControls()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Canvas Result");
            DrawCanvasSlopeControls();
            DrawSurfaceMaterialControls();
            DrawTuningSlider("Downhill Flow", ref tuningControls.downhillFlowSpeed, 0.1f, 4f);
            DrawTuningSlider("Rivulets", ref tuningControls.rivuletStrength, 0f, 2f);
            DrawTuningSlider("Wet Trail", ref tuningControls.wetTrailRetention, 0f, 1f);
            DrawTuningSlider("Absorption", ref tuningControls.surfaceAbsorption, 0f, 1f);
            DrawTuningSlider("Max Impact Radius", ref tuningControls.maxImpactRadius, 0.001f, 0.58f);
            DrawTuningSlider("Mark Opacity", ref tuningControls.markOpacity, 0f, 2f);
            DrawTuningSlider("Surface Spread", ref tuningControls.surfaceSpread, 0.05f, 3f);
            DrawTuningSlider("Splatter", ref tuningControls.splatterStrength, 0f, 3f);
            DrawTuningSlider("Sliding", ref tuningControls.slidingStrength, 0f, 3f);
            DrawTuningSlider("Stroke Density", ref tuningControls.canvasStrokeDensity, 0.25f, 3f);
            DrawTuningSlider("Stroke Overlap", ref tuningControls.canvasStrokeOverlap, 0.5f, 2f);
        }

        private void DrawCanvasSlopeControls()
        {
            DrawTuningToggle("Enable Canvas Slope", ref tuningControls.canvasSlopeEnabled);
            DrawTuningSlider("Slope X", ref tuningControls.canvasSlopeX, -60f, 60f);
            DrawTuningSlider("Slope Z", ref tuningControls.canvasSlopeZ, -60f, 60f);

            if (GUILayout.Button("Reset Slope"))
            {
                tuningControls.canvasSlopeEnabled = false;
                tuningControls.canvasSlopeX = 0f;
                tuningControls.canvasSlopeZ = 0f;
                ApplyTuning();
            }
        }

        private void DrawSurfaceMaterialControls()
        {
            SurfaceMaterialProfile currentProfile = tuningControls.surfaceMaterialProfile;
            string currentName = currentProfile != null ? currentProfile.name : "Fallback";
            GUILayout.Label($"Material: {currentName}");

            int buttonsInRow = 0;
            GUILayout.BeginHorizontal();
            DrawSurfaceMaterialButton("Fallback", null, currentProfile == null);
            buttonsInRow++;

            if (surfaceMaterialProfiles != null)
            {
                for (int i = 0; i < surfaceMaterialProfiles.Length; i++)
                {
                    SurfaceMaterialProfile profile = surfaceMaterialProfiles[i];
                    if (profile == null)
                    {
                        continue;
                    }

                    if (buttonsInRow >= 3)
                    {
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        buttonsInRow = 0;
                    }

                    DrawSurfaceMaterialButton(profile.name, profile, currentProfile == profile);
                    buttonsInRow++;
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawSurfaceMaterialButton(string label, SurfaceMaterialProfile profile, bool selected)
        {
            GUI.enabled = !selected;
            if (GUILayout.Button(selected ? $"[{label}]" : label))
            {
                tuningControls.surfaceMaterialProfile = profile;
                tuningControls.PullSurfaceMaterialControlValues();
                ApplyTuning();
            }

            GUI.enabled = true;
        }

        private void DrawTuningSlider(string label, ref float value, float min, float max, bool restartRequired = false)
        {
            if (DrawSlider(label, ref value, min, max, restartRequired))
            {
                ApplyTuning();
                if (restartRequired)
                {
                    _restartRecommended = true;
                }
            }
        }

        private bool DrawSlider(string label, ref float value, float min, float max, bool restartRequired)
        {
            float sliderWidth = Mathf.Max(80f, panelRect.width - 230f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(115f));
            float newValue = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(sliderWidth));
            GUILayout.Label(newValue.ToString("F3"), GUILayout.Width(50f));
            GUILayout.EndHorizontal();

            if (Mathf.Approximately(value, newValue))
            {
                return false;
            }

            value = newValue;
            return true;
        }

        private void DrawTuningToggle(string label, ref bool value)
        {
            bool newValue = GUILayout.Toggle(value, label);
            if (newValue == value)
            {
                return;
            }

            value = newValue;
            ApplyTuning();
        }

        private void ApplyTuning()
        {
            tuningControls?.ApplyTuning();
        }

        private void ResolveReferences()
        {
            if (simulationManager == null)
            {
                simulationManager = SimulationManager.Instance != null
                    ? SimulationManager.Instance
                    : FindObjectOfType<SimulationManager>();
            }

            if (tuningControls == null)
            {
                tuningControls = FindObjectOfType<PresentationTuningControls>();
            }

            if (physicsSettings == null)
            {
                if (simulationManager != null)
                {
                    physicsSettings = simulationManager.physicsSettings;
                }

                if (physicsSettings == null)
                {
                    physicsSettings = Resources.Load<PhysicsSettings>("PhysicsSettings");
                }
            }

            if (gpuOutflowController == null)
            {
                if (tuningControls != null)
                {
                    gpuOutflowController = tuningControls.outflowController;
                }

                if (gpuOutflowController == null && simulationManager != null)
                {
                    gpuOutflowController = simulationManager.gpuOutflowController;
                }

                if (gpuOutflowController == null)
                {
                    gpuOutflowController = FindObjectOfType<GPUFluidOutflowController>();
                }
            }

            if (paintSurface == null)
            {
                if (tuningControls != null)
                {
                    paintSurface = tuningControls.paintSurface;
                }

                if (paintSurface == null && simulationManager != null)
                {
                    paintSurface = simulationManager.paintSurface;
                }

                if (paintSurface == null)
                {
                    paintSurface = FindObjectOfType<CanvasPaintSurface>();
                }
            }
        }

        private void OnValidate()
        {
            maxPanelWidth = Mathf.Max(260f, maxPanelWidth);
            maxPanelHeight = Mathf.Max(220f, maxPanelHeight);
            panelRect.width = Mathf.Clamp(panelRect.width, 260f, maxPanelWidth);
            panelRect.height = Mathf.Clamp(panelRect.height, 220f, maxPanelHeight);
        }

        private void ClampPanelRectToScreen()
        {
            float screenWidth = Mathf.Max(1f, Screen.width);
            float screenHeight = Mathf.Max(1f, Screen.height);
            float safeMaxWidth = Mathf.Min(maxPanelWidth, Mathf.Max(260f, screenWidth - 24f));
            float safeMaxHeight = Mathf.Min(maxPanelHeight, Mathf.Max(220f, screenHeight - 24f));

            panelRect.width = Mathf.Clamp(panelRect.width, 260f, safeMaxWidth);
            panelRect.height = Mathf.Clamp(panelRect.height, 220f, safeMaxHeight);
            panelRect.x = Mathf.Clamp(panelRect.x, 0f, Mathf.Max(0f, screenWidth - panelRect.width));
            panelRect.y = Mathf.Clamp(panelRect.y, 0f, Mathf.Max(0f, screenHeight - panelRect.height));
        }
    }
}

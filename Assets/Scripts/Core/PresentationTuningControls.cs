using SwingingPaint.BucketFluid;
using SwingingPaint.BucketFluid.Core;
using SwingingPaint.BucketFluid.Rendering;
using SwingingPaint.Surface;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SwingingPaint.Core
{
    /// <summary>
    /// One inspector-facing control panel for the parameters that matter during presentation tuning.
    /// The lower-level components keep their detailed values, but this component applies the common
    /// settings to them so testing does not require hunting through multiple inspectors.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("SwingingPaint/Presentation Tuning Controls")]
    public class PresentationTuningControls : MonoBehaviour
    {
        [Header("00 - How To Use")]
        [Tooltip("When enabled, changing values here applies them to PhysicsSettings, fluid, outflow, renderer, and canvas. Keep disabled when tuning lower-level components directly.")]
        public bool autoApply = false;

        [Tooltip("Enable only when you intentionally want the component to search the scene and refill missing references.")]
        public bool autoResolveReferences = true;

        [Header("References - Auto Filled")]
        public SimulationManager simulationManager;
        public PhysicsSettings physicsSettings;
        public BucketFluidSettings fluidSettings;
        public GPUFluidSimulator fluidSimulator;
        public GPUFluidOutflowController outflowController;
        public GPUOutflowRenderer outflowRenderer;
        public CanvasPaintSurface paintSurface;

        [Header("01 - Main Paint Controls - Change These First")]
        [Tooltip("The official paint color. Applies to bucket fluid, falling stream, and deposited paint.")]
        public Color paintColor = new Color(0.05f, 0.22f, 0.95f, 1f);

        [Tooltip("How full the bucket starts after reset. Needs Reset/Restart to refill the bucket.")]
        [Range(0.05f, 1f)]
        public float bucketFillAmount = 0.85f;

        [Tooltip("Higher values push more paint through the hole. Applies immediately.")]
        [Min(0f)]
        public float flowRate = 0.5f;

        [Tooltip("Hole diameter at PaintHole. Bigger hole means stronger/faster pour. Applies immediately.")]
        [Min(0f)]
        public float holeDiameter = 0.035f;

        [Tooltip("Higher viscosity means heavier paint: more cohesive stream and less spread on the surface. Applies immediately.")]
        [Min(0f)]
        public float viscosity = 1.2f;

        [Tooltip("Logical paint quantity used by the central settings/fallback emitter. GPU bucket amount is controlled by Bucket Fill Amount for now.")]
        [Min(0f)]
        public float logicalPaintQuantity = 100f;

        [Tooltip("Tuning only: keeps the bucket from emptying while still emitting paint. Disable for final realistic runs.")]
        public bool infinitePaintSupplyForTuning = true;

        [Tooltip("When enabled, changing Paint Color recolors paint that is already falling. Keep enabled for live presentation tuning.")]
        public bool livePaintColorWhileFalling = true;

        [Header("02 - Falling Stream Look - Visual Tuning")]
        [Tooltip("Visual width of the falling stream. This does not change bucket fill amount.")]
        [Range(0.5f, 4f)]
        public float streamWidth = 2.4f;

        [Tooltip("0 = more broken droplets, 1 = more continuous paint string.")]
        [Range(0f, 1f)]
        public float streamContinuity = 0.65f;

        [Tooltip("Length of the visual trail for each falling paint particle.")]
        [Range(0.5f, 4f)]
        public float streamTrailLength = 1.6f;

        [Tooltip("Opacity multiplier for stream connector ribbons.")]
        [Range(0f, 2f)]
        public float streamOpacity = 1.1f;

        [Header("03 - Canvas / Ground Result")]
        [Tooltip("Higher absorption reduces spread. Applies immediately.")]
        [Range(0f, 1f)]
        public float surfaceAbsorption = 0.1f;

        [Tooltip("Maximum world-space impact radius for a paint mark.")]
        [Min(0.001f)]
        public float maxImpactRadius = 0.45f;

        [Tooltip("Strength/opacity of deposited paint marks.")]
        [Range(0f, 2f)]
        public float markOpacity = 1f;

        [Tooltip("Extra spread caused by high flow rate.")]
        [Range(0f, 3f)]
        public float flowSpreadBoost = 0.4f;

        [Tooltip("Base spread multiplier for real paint deposition.")]
        [Range(0.05f, 3f)]
        public float surfaceSpread = 1.6f;

        [Tooltip("Organic edge/noise strength for paint marks.")]
        [Range(0f, 1f)]
        public float edgeIrregularity = 0.45f;

        [Tooltip("Small satellite splashes created by high-speed impacts.")]
        [Range(0f, 3f)]
        public float splatterStrength = 0.65f;

        [Tooltip("Stretches marks along the incoming velocity direction.")]
        [Range(1f, 4f)]
        public float directionalStretch = 1.8f;

        [Tooltip("Paint sliding/dripping strength on tilted or low-absorption surfaces.")]
        [Range(0f, 3f)]
        public float slidingStrength = 0.35f;

        [Header("03B - Physical Pour Model")]
        [Tooltip("Torricelli discharge coefficient. Around 0.6 is realistic for a simple hole.")]
        [Range(0.05f, 1f)]
        public float dischargeCoefficient = 0.62f;

        [Tooltip("How strongly viscosity reduces physical outflow speed.")]
        [Range(0f, 5f)]
        public float viscosityFlowDamping = 0.8f;

        [Tooltip("Small controlled turbulence added while paint falls through air.")]
        [Range(0f, 2f)]
        public float fallingAirTurbulence = 0.18f;

        [Header("04 - Motion Basics")]
        [Tooltip("Initial release angle. Needs Restart to restart from this angle.")]
        [Range(-90f, 90f)]
        public float startAngle = 30f;

        [Tooltip("Side push at release. Needs Restart to restart with this push.")]
        [Range(-720f, 720f)]
        public float sidePushVelocity = 25f;

        [Tooltip("Swing direction in the XZ plane. Needs Restart to restart in this direction.")]
        public float swingDirection = 0f;

        [Tooltip("Rope rest length. Needs Restart for a clean test.")]
        [Min(0.01f)]
        public float ropeLength = 2f;

        [Tooltip("Motion damping. Higher values reduce swing faster.")]
        [Min(0f)]
        public float motionDamping = 0.05f;

        [Header("05 - Quality / Performance")]
        [Tooltip("Use Presentation Particle Count instead of Development Particle Count. Needs Reset/Restart.")]
        public bool presentationMode;

        [Tooltip("Particle count for fast tuning. Needs Reset/Restart.")]
        [Min(1)]
        public int developmentParticleCount = 1200;

        [Tooltip("Particle count for final presentation. Needs Reset/Restart and costs more GPU time.")]
        [Min(1)]
        public int presentationParticleCount = 12000;

        private bool _runtimeReferencesApplied;

        [ContextMenu("Apply Tuning To Scene")]
        public void ApplyTuning()
        {
            ApplyTuning(restartSimulation: false);
        }

        [ContextMenu("Apply Tuning And Restart Simulation")]
        public void ApplyTuningAndRestart()
        {
            ApplyTuning(restartSimulation: true);
        }

        private void OnEnable()
        {
            _runtimeReferencesApplied = false;
            ResolveReferences();
        }

        private void Start()
        {
            _runtimeReferencesApplied = outflowController != null && outflowRenderer != null;
        }

        private void Update()
        {
            if (!Application.isPlaying || !autoApply || _runtimeReferencesApplied)
            {
                return;
            }

            ApplyTuning(restartSimulation: false);
            _runtimeReferencesApplied = outflowController != null && outflowRenderer != null;
        }

        private void OnValidate()
        {
            ClampValues();

            if (autoApply)
            {
                ApplyTuning(restartSimulation: false);
            }
        }

        [ContextMenu("Pull Current Values From Scene")]
        public void PullCurrentValuesFromScene()
        {
            ResolveReferences();

            if (physicsSettings != null)
            {
                paintColor = physicsSettings.PaintColor;
                flowRate = physicsSettings.PaintFlowRate;
                holeDiameter = physicsSettings.PaintHoleDiameter;
                viscosity = physicsSettings.PaintViscosity;
                logicalPaintQuantity = physicsSettings.PaintQuantity;
                surfaceAbsorption = physicsSettings.SurfaceAbsorption;
                startAngle = physicsSettings.InitialAngle;
                sidePushVelocity = physicsSettings.InitialLateralAngularVelocity;
                swingDirection = physicsSettings.Direction;
                ropeLength = physicsSettings.RestLength;
                motionDamping = physicsSettings.Damping;
            }

            if (fluidSettings != null)
            {
                bucketFillAmount = fluidSettings.fillHeightPercent;
                presentationMode = fluidSettings.presentationMode;
                developmentParticleCount = fluidSettings.developmentParticleCount;
                presentationParticleCount = fluidSettings.presentationParticleCount;
            }

            if (outflowController != null)
            {
                streamWidth = outflowController.streamRadiusMultiplier;
                streamContinuity = Mathf.InverseLerp(0.06f, 0.24f, outflowController.streamBreakDistance);
                infinitePaintSupplyForTuning = outflowController.infinitePaintSupplyForTuning;
                livePaintColorWhileFalling = outflowController.livePaintColorWhileFalling;
            }

            if (outflowRenderer != null)
            {
                streamTrailLength = outflowRenderer.trailLengthMultiplier;
                streamOpacity = outflowRenderer.connectorOpacityMultiplier;
            }

            if (paintSurface != null)
            {
                maxImpactRadius = paintSurface.maxImpactRadius;
                markOpacity = paintSurface.opacityMultiplier;
                flowSpreadBoost = paintSurface.flowSpreadBoost;
                surfaceSpread = paintSurface.surfaceSpread;
                edgeIrregularity = paintSurface.edgeIrregularity;
                splatterStrength = paintSurface.splatterStrength;
                directionalStretch = paintSurface.directionalStretch;
                slidingStrength = paintSurface.slidingStrength;
            }

            if (outflowController != null)
            {
                dischargeCoefficient = outflowController.dischargeCoefficient;
                viscosityFlowDamping = outflowController.viscosityFlowDamping;
                fallingAirTurbulence = outflowController.fallingAirTurbulence;
            }

            ClampValues();
        }

        private void ApplyTuning(bool restartSimulation)
        {
            ClampValues();
            ResolveReferences();

            if (physicsSettings != null)
            {
                physicsSettings.SetPaintColor(paintColor);
                physicsSettings.SetPaintFlowRate(flowRate);
                physicsSettings.SetPaintHoleDiameter(holeDiameter);
                physicsSettings.SetPaintViscosity(viscosity);
                physicsSettings.SetPaintQuantity(logicalPaintQuantity);
                physicsSettings.SetSurfaceAbsorption(surfaceAbsorption);
                physicsSettings.SetPaintSpreadRadius(maxImpactRadius);
                physicsSettings.SetInitialAngle(startAngle);
                physicsSettings.SetInitialLateralAngularVelocity(sidePushVelocity);
                physicsSettings.SetDirection(swingDirection);
                physicsSettings.SetRestLength(ropeLength);
                physicsSettings.SetDamping(motionDamping);
                MarkDirty(physicsSettings);
            }

            if (fluidSettings != null)
            {
                fluidSettings.paintColor = paintColor;
                fluidSettings.opacity = Mathf.Clamp01(paintColor.a);
                fluidSettings.fillHeightPercent = bucketFillAmount;
                fluidSettings.viscosity = viscosity;
                fluidSettings.presentationMode = presentationMode;
                fluidSettings.developmentParticleCount = developmentParticleCount;
                fluidSettings.presentationParticleCount = presentationParticleCount;
                MarkDirty(fluidSettings);
            }

            if (outflowController != null)
            {
                outflowController.infinitePaintSupplyForTuning = infinitePaintSupplyForTuning;
                outflowController.livePaintColorWhileFalling = livePaintColorWhileFalling;
                outflowController.holeDiameter = holeDiameter;
                outflowController.streamRadiusMultiplier = streamWidth;
                outflowController.streamBreakDistance = Mathf.Lerp(0.06f, 0.24f, streamContinuity);
                outflowController.maxAdaptiveStreamBreakDistance = Mathf.Lerp(0.18f, 0.60f, streamContinuity);
                outflowController.minimumContinuousStreamExtractions = Mathf.RoundToInt(Mathf.Lerp(2f, 18f, streamContinuity));
                outflowController.maxExtractionsPerSubstep = Mathf.RoundToInt(Mathf.Lerp(4f, 24f, streamContinuity));
                outflowController.usePhysicalPourModel = true;
                outflowController.dischargeCoefficient = dischargeCoefficient;
                outflowController.viscosityFlowDamping = viscosityFlowDamping;
                outflowController.fallingAirTurbulence = fallingAirTurbulence;
                MarkDirty(outflowController);
            }

            if (outflowRenderer != null)
            {
                outflowRenderer.minimumVisualStreamRadiusMultiplier = Mathf.Max(0.5f, streamWidth * 0.9f);
                outflowRenderer.particleDiameterMultiplier = Mathf.Lerp(1.2f, 2.4f, Mathf.InverseLerp(0.5f, 4f, streamWidth));
                outflowRenderer.trailLengthMultiplier = streamTrailLength;
                outflowRenderer.connectorOpacityMultiplier = streamOpacity;
                MarkDirty(outflowRenderer);
            }

            if (paintSurface != null)
            {
                paintSurface.defaultAbsorption = surfaceAbsorption;
                paintSurface.minImpactRadius = Mathf.Min(paintSurface.minImpactRadius, maxImpactRadius);
                paintSurface.maxImpactRadius = maxImpactRadius;
                paintSurface.opacityMultiplier = markOpacity;
                paintSurface.flowSpreadBoost = flowSpreadBoost;
                paintSurface.surfaceSpread = surfaceSpread;
                paintSurface.edgeIrregularity = edgeIrregularity;
                paintSurface.splatterStrength = splatterStrength;
                paintSurface.directionalStretch = directionalStretch;
                paintSurface.slidingStrength = slidingStrength;
                MarkDirty(paintSurface);
            }

            if (simulationManager != null)
            {
                simulationManager.physicsSettings = physicsSettings;
                MarkDirty(simulationManager);

                if (restartSimulation && Application.isPlaying)
                {
                    simulationManager.RestartSimulation();
                }
            }
        }

        private void ResolveReferences()
        {
            if (!autoResolveReferences)
            {
                return;
            }

            if (simulationManager == null)
            {
                simulationManager = FindObjectOfType<SimulationManager>();
            }

            if (physicsSettings == null && simulationManager != null)
            {
                physicsSettings = simulationManager.physicsSettings;
            }

            if (physicsSettings == null)
            {
                physicsSettings = Resources.Load<PhysicsSettings>("PhysicsSettings");
            }

            if (fluidSettings == null)
            {
                fluidSettings = FindObjectOfType<BucketFluidSettings>();
            }

            if (fluidSimulator == null)
            {
                fluidSimulator = FindObjectOfType<GPUFluidSimulator>();
            }

            if (outflowController == null && fluidSimulator != null)
            {
                outflowController = fluidSimulator.outflowController;
            }

            if (outflowController == null)
            {
                outflowController = FindObjectOfType<GPUFluidOutflowController>();
            }

            if (outflowRenderer == null)
            {
                outflowRenderer = FindObjectOfType<GPUOutflowRenderer>();
            }

            if (paintSurface == null)
            {
                paintSurface = FindObjectOfType<CanvasPaintSurface>();
            }
        }

        private void ClampValues()
        {
            paintColor.r = Mathf.Clamp01(paintColor.r);
            paintColor.g = Mathf.Clamp01(paintColor.g);
            paintColor.b = Mathf.Clamp01(paintColor.b);
            paintColor.a = Mathf.Clamp01(paintColor.a);
            bucketFillAmount = Mathf.Clamp(bucketFillAmount, 0.05f, 1f);
            flowRate = Mathf.Max(0f, flowRate);
            holeDiameter = Mathf.Max(0f, holeDiameter);
            viscosity = Mathf.Max(0f, viscosity);
            logicalPaintQuantity = Mathf.Max(0f, logicalPaintQuantity);
            streamWidth = Mathf.Clamp(streamWidth, 0.5f, 4f);
            streamContinuity = Mathf.Clamp01(streamContinuity);
            streamTrailLength = Mathf.Clamp(streamTrailLength, 0.5f, 4f);
            streamOpacity = Mathf.Clamp(streamOpacity, 0f, 2f);
            surfaceAbsorption = Mathf.Clamp01(surfaceAbsorption);
            maxImpactRadius = Mathf.Max(0.001f, maxImpactRadius);
            markOpacity = Mathf.Clamp(markOpacity, 0f, 2f);
            flowSpreadBoost = Mathf.Clamp(flowSpreadBoost, 0f, 3f);
            surfaceSpread = Mathf.Clamp(surfaceSpread, 0.05f, 3f);
            edgeIrregularity = Mathf.Clamp01(edgeIrregularity);
            splatterStrength = Mathf.Clamp(splatterStrength, 0f, 3f);
            directionalStretch = Mathf.Clamp(directionalStretch, 1f, 4f);
            slidingStrength = Mathf.Clamp(slidingStrength, 0f, 3f);
            dischargeCoefficient = Mathf.Clamp(dischargeCoefficient, 0.05f, 1f);
            viscosityFlowDamping = Mathf.Clamp(viscosityFlowDamping, 0f, 5f);
            fallingAirTurbulence = Mathf.Clamp(fallingAirTurbulence, 0f, 2f);
            startAngle = Mathf.Clamp(startAngle, -90f, 90f);
            sidePushVelocity = Mathf.Clamp(sidePushVelocity, -720f, 720f);
            ropeLength = Mathf.Max(0.01f, ropeLength);
            motionDamping = Mathf.Max(0f, motionDamping);
            developmentParticleCount = Mathf.Max(1, developmentParticleCount);
            presentationParticleCount = Mathf.Max(1, presentationParticleCount);
        }

        private static void MarkDirty(Object target)
        {
#if UNITY_EDITOR
            if (target != null && !Application.isPlaying)
            {
                EditorUtility.SetDirty(target);
            }
#endif
        }
    }
}

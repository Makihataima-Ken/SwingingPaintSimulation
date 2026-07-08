using SwingingPaint.BucketFluid.Core;
using SwingingPaint.Surface;
using UnityEngine;

namespace SwingingPaint.BucketFluid.Rendering
{
    /// <summary>
    /// Lightweight runtime debug readout for the bucket fluid and paint outflow systems.
    ///
    /// This is intentionally small and optional. It reports buffer state, bucket motion,
    /// GPU outflow, and canvas values.
    /// </summary>
    public class FluidDebugPanel : MonoBehaviour
    {
        [Header("References")]
        public GPUFluidSimulator simulator;
        public BucketMotionProvider motionProvider;
        public BucketFluidVolumeRenderer fluidVolumeRenderer;
        public GPUFluidRenderer fluidRenderer;
        public GPUFluidOutflowController gpuOutflowController;
        public CanvasPaintSurface paintSurface;

        [Header("Display")]
        public bool showPanel = false;
        public Rect panelRect = new Rect(12f, 12f, 380f, 700f);

        [Header("Debug View")]
        public bool forceParticleDebugView = true;
        public bool hideVolumeInParticleDebugView = true;

        private void Awake()
        {
            ResolveReferences();
            ValidateReferences();
        }

        private void Reset()
        {
            ResolveReferences();
            ValidateReferences();
        }

        private void OnGUI()
        {
            if (!showPanel)
            {
                return;
            }

            ResolveReferences();
            ApplyDebugViewOverride();

            GUILayout.BeginArea(panelRect, GUI.skin.box);
            GUILayout.Label("Bucket Fluid Debug");
            DrawControls();
            DrawSimulatorBufferStatus();
            DrawFluidRenderingStatus();
            DrawSimulationStatus();
            DrawSpatialGridStatus();
            DrawParticleValidationStatus();
            DrawGpuOutflowStatus();
            DrawCanvasPaintStatus();
            DrawGpuOutflowStreamStatus();
            DrawBoundaryStatus();
            GUILayout.EndArea();
        }

        private void DrawSimulatorBufferStatus()
        {
            GUILayout.Label($"Simulator Assigned: {simulator != null}");
            GUILayout.Label($"Particle Buffer Valid: {simulator != null && simulator.ParticleBufferValid}");
            GUILayout.Label($"Particle Buffer Count: {(simulator != null ? simulator.ParticleBufferCount : 0)}");
            GUILayout.Label($"Particle Stride: {(simulator != null ? simulator.ParticleStride : 0)}");
        }

        private void DrawFluidRenderingStatus()
        {
            GUILayout.Label($"Fluid Volume Enabled: {fluidVolumeRenderer != null && fluidVolumeRenderer.renderEnabled}");
            GUILayout.Label($"Fluid Volume Mesh Valid: {fluidVolumeRenderer != null && fluidVolumeRenderer.MeshValid}");
            GUILayout.Label($"Fluid Volume Fill: {(fluidVolumeRenderer != null ? fluidVolumeRenderer.CurrentFillFraction * 100f : 0f):F1}%");
            GUILayout.Label($"Fluid Volume Fill Y: {(fluidVolumeRenderer != null ? fluidVolumeRenderer.CurrentFillLocalY : 0f):F4}");
            GUILayout.Label($"Particle Cloud Enabled: {fluidRenderer != null && fluidRenderer.renderEnabled}");
            GUILayout.Label($"Mesh Assigned: {fluidRenderer != null && fluidRenderer.ParticleMeshAssigned}");
            GUILayout.Label($"Material Assigned: {fluidRenderer != null && fluidRenderer.ParticleMaterialAssigned}");
            GUILayout.Label($"Rendered Instances: {(fluidRenderer != null ? fluidRenderer.RenderedInstanceCount : 0)}");
            GUILayout.Label($"Indirect Args Count: {(fluidRenderer != null ? fluidRenderer.IndirectArgsInstanceCount : 0)}");
        }

        private void DrawSimulationStatus()
        {
            GUILayout.Label($"Simulation Running: {simulator != null && simulator.SimulationRunning}");
            GUILayout.Label($"Simulation Paused: {simulator != null && simulator.pauseSimulation}");
            GUILayout.Label($"Pause After Reset: {simulator != null && simulator.pauseAfterReset}");
            GUILayout.Label($"Substeps: {(simulator != null ? simulator.LastSimulationSubsteps : 0)}");
            GUILayout.Label($"Initialized: {(simulator != null && simulator.IsInitialized)}");
            GUILayout.Label($"Target Particles: {(simulator != null ? simulator.TargetParticleCount : 0)}");
            GUILayout.Label($"Initialized Particles: {(simulator != null ? simulator.InitializedParticleCount : 0)}");
            GUILayout.Label($"Active Particles: {(simulator != null ? simulator.RuntimeActiveParticleCount : 0)}");
            GUILayout.Label($"Reset Spacing: {(simulator != null ? simulator.LastResetSpacing : 0f):F4}");
            GUILayout.Label($"Reset Layers: {(simulator != null ? simulator.LastResetLayerCount : 0)}");
            GUILayout.Label($"Reset Max Radius: {(simulator != null ? simulator.LastResetMaxRadius : 0f):F4}");
            GUILayout.Label($"Reset Fill Top Y: {(simulator != null ? simulator.LastResetFillTopY : 0f):F4}");
            GUILayout.Label($"Reset Bounds Min: {FormatVector(simulator != null && simulator.HasLastResetBounds ? simulator.LastResetBoundsMin : Vector3.zero)}");
            GUILayout.Label($"Reset Bounds Max: {FormatVector(simulator != null && simulator.HasLastResetBounds ? simulator.LastResetBoundsMax : Vector3.zero)}");
        }

        private void DrawSpatialGridStatus()
        {
            GUILayout.Label($"Spatial Grid Enabled: {simulator != null && simulator.SpatialGridEnabled}");
            GUILayout.Label($"Spatial Grid Buffers: {simulator != null && simulator.SpatialGridBufferValid}");
            GUILayout.Label($"Hash Table Size: {(simulator != null ? simulator.SpatialHashTableSize : 0)}");
            GUILayout.Label($"Cell Size: {(simulator != null ? simulator.SpatialCellSize : 0f):F4}");
            GUILayout.Label($"Max Particles/Cell: {(simulator != null ? simulator.SpatialMaxParticlesPerCell : 0)}");
            GUILayout.Label($"Grid Inserted: {(simulator != null && simulator.SpatialGridCountersAvailable ? simulator.SpatialGridInsertedCount.ToString() : "n/a")}");
            GUILayout.Label($"Grid Overflow: {(simulator != null && simulator.SpatialGridCountersAvailable ? simulator.SpatialGridOverflowCount.ToString() : "n/a")}");
        }

        private void DrawParticleValidationStatus()
        {
            GUILayout.Label($"Validation Available: {simulator != null && simulator.ParticleValidationAvailable}");
            GUILayout.Label($"Invalid Particles: {(simulator != null && simulator.ParticleValidationAvailable ? simulator.InvalidParticleCount.ToString() : "n/a")}");
            GUILayout.Label($"Boundary Leaks: {(simulator != null && simulator.ParticleValidationAvailable ? simulator.BoundaryLeakCount.ToString() : "n/a")}");
            GUILayout.Label($"Max Velocity: {(simulator != null && simulator.ParticleValidationAvailable ? simulator.MaxObservedVelocity.ToString("F2") : "n/a")}");
            GUILayout.Label($"Average Density: {(simulator != null && simulator.ParticleValidationAvailable ? simulator.AverageObservedDensity.ToString("F2") : "n/a")}");
            GUILayout.Label($"Effective Accel: {FormatVector(motionProvider != null ? motionProvider.EffectiveLocalAcceleration : Vector3.zero)}");
            GUILayout.Label("Boundary Source: BucketFluidBoundary");
            GUILayout.Label($"Boundary Collision: {simulator != null && simulator.BoundaryCollisionEnabled}");
        }

        private void DrawGpuOutflowStatus()
        {
            GUILayout.Label($"GPU Outflow Enabled: {gpuOutflowController != null && gpuOutflowController.gpuOutflowEnabled}");
            GUILayout.Label($"GPU Infinite Supply: {gpuOutflowController != null && gpuOutflowController.infinitePaintSupplyForTuning}");
            GUILayout.Label($"GPU Bucket Holes: {(gpuOutflowController != null ? gpuOutflowController.EffectiveHoleCount : 0)}");
            GUILayout.Label($"GPU Outflow Capacity: {(gpuOutflowController != null ? gpuOutflowController.OutflowCapacity : 0)}");
            GUILayout.Label($"GPU Outflow Active: {(gpuOutflowController != null ? gpuOutflowController.ActiveOutflowParticles : 0)}");
            GUILayout.Label($"GPU Paint Remaining: {(gpuOutflowController != null ? gpuOutflowController.RemainingPaintQuantityUnits : 0f):F2} units / {(gpuOutflowController != null ? gpuOutflowController.RemainingPaintFraction * 100f : 0f):F1}%");
            GUILayout.Label($"GPU Physical Flow: {(gpuOutflowController != null ? gpuOutflowController.CurrentPhysicalFlowRateCubicMetersPerSecond : 0f):F6} m3/s");
            GUILayout.Label($"GPU Extraction Budget/Substep: {(gpuOutflowController != null ? gpuOutflowController.CurrentExtractionBudget : 0)}");
            GUILayout.Label($"GPU Outflow Radius: {(gpuOutflowController != null ? gpuOutflowController.EffectiveOutflowParticleRadius : 0f):F4}");
            GUILayout.Label($"GPU Drain Capture Radius: {(gpuOutflowController != null ? gpuOutflowController.EffectiveDrainCaptureRadius : 0f):F4}");
            GUILayout.Label($"GPU Outflow Emitted/Tick: {(gpuOutflowController != null ? gpuOutflowController.EmittedParticlesThisTick : 0)}");
            GUILayout.Label($"GPU Outflow Impacts/Tick: {(gpuOutflowController != null ? gpuOutflowController.DepositedImpactsThisTick : 0)}");
            GUILayout.Label($"GPU Canvas Writes/Tick: {(gpuOutflowController != null ? gpuOutflowController.CanvasGpuWritesThisTick : 0)}");
        }

        private void DrawCanvasPaintStatus()
        {
            GUILayout.Label($"Canvas Stateful Ready: {paintSurface != null && paintSurface.StatefulGpuPaintReady}");
            GUILayout.Label($"Canvas Stateful Enabled: {paintSurface != null && paintSurface.statefulGpuPaint}");
            GUILayout.Label($"Canvas Quality: {(paintSurface != null ? paintSurface.qualityPreset.ToString() : "n/a")}");
            GUILayout.Label($"Canvas RT Memory: {(paintSurface != null ? paintSurface.EstimatedGpuPaintMemoryMB : 0f):F2} MB");
            GUILayout.Label($"Canvas Diffusion Iterations: {(paintSurface != null ? paintSurface.CurrentDiffusionIterations : 0)}");
            GUILayout.Label($"Canvas Dry/Composite Interval: {(paintSurface != null ? paintSurface.CurrentDryCompositeInterval : 0)}");
            GUILayout.Label($"Canvas Brush Radius Cap: {(paintSurface != null ? paintSurface.CurrentBrushRadiusPixelCap : 0)} px");
            GUILayout.Label($"Canvas Surface Profile: {(paintSurface != null && paintSurface.surfaceMaterialProfile != null ? paintSurface.surfaceMaterialProfile.name : "fallback")}");
        }

        private void DrawGpuOutflowStreamStatus()
        {
            GUILayout.Label($"GPU Stream Connectors: {(gpuOutflowController != null ? gpuOutflowController.StreamConnectorCount : 0)}");
            GUILayout.Label($"GPU Outflow Overflow/Tick: {(gpuOutflowController != null ? gpuOutflowController.BufferOverflowThisTick : 0)}");
            GUILayout.Label($"GPU Outflow Avg Density: {(gpuOutflowController != null ? gpuOutflowController.AverageOutflowDensity : 0f):F3}");
        }

        private void DrawBoundaryStatus()
        {
            if (simulator != null && simulator.boundary != null)
            {
                GUILayout.Label($"Wall Damping/Friction: {simulator.boundary.wallDamping:F2} / {simulator.boundary.wallFriction:F2}");
                GUILayout.Label($"Clamp Top: {simulator.boundary.clampTop}");
                GUILayout.Label($"Bottom/Top Y: {simulator.boundary.bottomY:F3} / {simulator.boundary.topY:F3}");
                GUILayout.Label($"Bottom/Top Radius: {simulator.boundary.bottomRadius:F3} / {simulator.boundary.topRadius:F3}");
            }
        }

        private void LateUpdate()
        {
            if (!showPanel)
            {
                return;
            }

            ResolveReferences();
            ApplyDebugViewOverride();
        }

        private void DrawControls()
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Reset Fluid") && simulator != null)
            {
                simulator.ResetFluid();
            }

            if (GUILayout.Button("Pause") && simulator != null)
            {
                simulator.PauseSimulation();
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Resume") && simulator != null)
            {
                simulator.ResumeSimulation();
            }

            if (GUILayout.Button("Step Once") && simulator != null)
            {
                simulator.StepSimulationOnce();
            }

            GUILayout.EndHorizontal();
        }

        private void ResolveReferences()
        {
            if (simulator == null)
            {
                simulator = GetComponent<GPUFluidSimulator>();
            }

            if (simulator == null)
            {
                simulator = GetComponentInParent<GPUFluidSimulator>();
            }

            if (motionProvider == null)
            {
                motionProvider = GetComponent<BucketMotionProvider>();
            }

            if (motionProvider == null)
            {
                motionProvider = GetComponentInParent<BucketMotionProvider>();
            }

            if (fluidRenderer == null)
            {
                fluidRenderer = GetComponentInChildren<GPUFluidRenderer>();
            }

            if (fluidRenderer == null)
            {
                fluidRenderer = GetComponentInParent<GPUFluidRenderer>();
            }

            if (fluidVolumeRenderer == null)
            {
                fluidVolumeRenderer = GetComponentInChildren<BucketFluidVolumeRenderer>();
            }

            if (fluidVolumeRenderer == null)
            {
                fluidVolumeRenderer = GetComponentInParent<BucketFluidVolumeRenderer>();
            }

            if (gpuOutflowController == null)
            {
                gpuOutflowController = GetComponentInChildren<GPUFluidOutflowController>();
            }

            if (gpuOutflowController == null)
            {
                gpuOutflowController = GetComponentInParent<GPUFluidOutflowController>();
            }

            if (paintSurface == null && gpuOutflowController != null)
            {
                paintSurface = gpuOutflowController.paintSurface;
            }

            if (paintSurface == null)
            {
                paintSurface = FindObjectOfType<CanvasPaintSurface>();
            }
        }

        private void ApplyDebugViewOverride()
        {
            if (!forceParticleDebugView)
            {
                return;
            }

            if (fluidRenderer != null)
            {
                fluidRenderer.renderEnabled = true;
            }

            if (fluidVolumeRenderer != null)
            {
                fluidVolumeRenderer.disableParticleCloudInPresentation = false;

                if (hideVolumeInParticleDebugView)
                {
                    fluidVolumeRenderer.renderEnabled = false;
                }
            }
        }

        private void ValidateReferences()
        {
            if (simulator == null || motionProvider == null)
            {
                Debug.LogWarning(
                    "FluidDebugPanel setup is incomplete. Assign the GPUFluidSimulator and " +
                    "BucketMotionProvider from BucketRig.",
                    this
                );
            }
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
        }
    }
}

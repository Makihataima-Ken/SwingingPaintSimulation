using SwingingPaint.BucketFluid.Core;
using SwingingPaint.Paint;
using UnityEngine;

namespace SwingingPaint.BucketFluid.Rendering
{
    /// <summary>
    /// Lightweight runtime debug readout for the bucket fluid scaffold.
    ///
    /// This is intentionally small and optional. It reports buffer state and bucket motion
    /// values while the real GPU solver is being developed.
    /// </summary>
    public class FluidDebugPanel : MonoBehaviour
    {
        [Header("References")]
        public GPUFluidSimulator simulator;
        public BucketMotionProvider motionProvider;
        public GPUFluidRenderer fluidRenderer;
        public PaintEmitter paintEmitter;

        [Header("Display")]
        public bool showPanel = true;
        public Rect panelRect = new Rect(12f, 12f, 380f, 700f);

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

            GUILayout.BeginArea(panelRect, GUI.skin.box);
            GUILayout.Label("Bucket Fluid Debug");
            DrawControls();
            GUILayout.Label($"Simulator Assigned: {simulator != null}");
            GUILayout.Label($"Particle Buffer Valid: {simulator != null && simulator.ParticleBufferValid}");
            GUILayout.Label($"Particle Buffer Count: {(simulator != null ? simulator.ParticleBufferCount : 0)}");
            GUILayout.Label($"Particle Stride: {(simulator != null ? simulator.ParticleStride : 0)}");
            GUILayout.Label($"Renderer Enabled: {fluidRenderer != null && fluidRenderer.renderEnabled}");
            GUILayout.Label($"Mesh Assigned: {fluidRenderer != null && fluidRenderer.ParticleMeshAssigned}");
            GUILayout.Label($"Material Assigned: {fluidRenderer != null && fluidRenderer.ParticleMaterialAssigned}");
            GUILayout.Label($"Rendered Instances: {(fluidRenderer != null ? fluidRenderer.RenderedInstanceCount : 0)}");
            GUILayout.Label($"Indirect Args Count: {(fluidRenderer != null ? fluidRenderer.IndirectArgsInstanceCount : 0)}");
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
            GUILayout.Label($"Spatial Grid Enabled: {simulator != null && simulator.SpatialGridEnabled}");
            GUILayout.Label($"Spatial Grid Buffers: {simulator != null && simulator.SpatialGridBufferValid}");
            GUILayout.Label($"Hash Table Size: {(simulator != null ? simulator.SpatialHashTableSize : 0)}");
            GUILayout.Label($"Cell Size: {(simulator != null ? simulator.SpatialCellSize : 0f):F4}");
            GUILayout.Label($"Max Particles/Cell: {(simulator != null ? simulator.SpatialMaxParticlesPerCell : 0)}");
            GUILayout.Label($"Grid Inserted: {(simulator != null && simulator.SpatialGridCountersAvailable ? simulator.SpatialGridInsertedCount.ToString() : "n/a")}");
            GUILayout.Label($"Grid Overflow: {(simulator != null && simulator.SpatialGridCountersAvailable ? simulator.SpatialGridOverflowCount.ToString() : "n/a")}");
            GUILayout.Label($"Validation Available: {simulator != null && simulator.ParticleValidationAvailable}");
            GUILayout.Label($"Invalid Particles: {(simulator != null && simulator.ParticleValidationAvailable ? simulator.InvalidParticleCount.ToString() : "n/a")}");
            GUILayout.Label($"Boundary Leaks: {(simulator != null && simulator.ParticleValidationAvailable ? simulator.BoundaryLeakCount.ToString() : "n/a")}");
            GUILayout.Label($"Max Velocity: {(simulator != null && simulator.ParticleValidationAvailable ? simulator.MaxObservedVelocity.ToString("F2") : "n/a")}");
            GUILayout.Label($"Average Density: {(simulator != null && simulator.ParticleValidationAvailable ? simulator.AverageObservedDensity.ToString("F2") : "n/a")}");
            GUILayout.Label($"Effective Accel: {FormatVector(motionProvider != null ? motionProvider.EffectiveLocalAcceleration : Vector3.zero)}");
            GUILayout.Label("Boundary Source: BucketFluidBoundary");
            GUILayout.Label($"Boundary Collision: {simulator != null && simulator.BoundaryCollisionEnabled}");
            GUILayout.Label($"Paint Stream Enabled: {paintEmitter != null && paintEmitter.continuousStreamMode}");
            GUILayout.Label($"Paint Render Mode: {(paintEmitter != null ? paintEmitter.renderMode.ToString() : "n/a")}");
            GUILayout.Label($"Paint Emission Accumulator: {(paintEmitter != null ? paintEmitter.EmissionAccumulator : 0f):F4}");
            GUILayout.Label($"Paint Emitted/Tick: {(paintEmitter != null ? paintEmitter.LastEmittedParticlesPerTick : 0)}");
            GUILayout.Label($"Active Falling Paint: {(paintEmitter != null ? paintEmitter.ActiveDropletCount : 0)}");
            GUILayout.Label($"Stream Segments: {(paintEmitter != null ? paintEmitter.StreamRenderSegments : 0)}");
            GUILayout.Label($"Avg Stream Spacing: {(paintEmitter != null ? paintEmitter.AverageStreamSpacing : 0f):F4}");
            GUILayout.Label($"Max Stream Spacing: {(paintEmitter != null ? paintEmitter.MaxStreamSpacing : 0f):F4}");
            GUILayout.Label($"Broken Stream Segments: {(paintEmitter != null ? paintEmitter.BrokenStreamSegmentCount : 0)}");
            GUILayout.Label($"Avg Falling Speed: {(paintEmitter != null ? paintEmitter.AverageFallingSpeed : 0f):F3}");
            GUILayout.Label($"Adaptive Break Distance: {(paintEmitter != null ? paintEmitter.CurrentAdaptiveBreakDistance : 0f):F4}");
            GUILayout.Label($"Current Flow Rate: {(paintEmitter != null ? paintEmitter.CurrentFlowRate : 0f):F3}");
            GUILayout.Label($"Remaining Paint: {(paintEmitter != null ? paintEmitter.RemainingPaintQuantity : 0f):F3}");

            if (simulator != null && simulator.boundary != null)
            {
                GUILayout.Label($"Wall Damping/Friction: {simulator.boundary.wallDamping:F2} / {simulator.boundary.wallFriction:F2}");
                GUILayout.Label($"Clamp Top: {simulator.boundary.clampTop}");
                GUILayout.Label($"Bottom/Top Y: {simulator.boundary.bottomY:F3} / {simulator.boundary.topY:F3}");
                GUILayout.Label($"Bottom/Top Radius: {simulator.boundary.bottomRadius:F3} / {simulator.boundary.topRadius:F3}");
            }

            GUILayout.EndArea();
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

            if (paintEmitter == null)
            {
                paintEmitter = GetComponentInChildren<PaintEmitter>();
            }

            if (paintEmitter == null)
            {
                paintEmitter = GetComponentInParent<PaintEmitter>();
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

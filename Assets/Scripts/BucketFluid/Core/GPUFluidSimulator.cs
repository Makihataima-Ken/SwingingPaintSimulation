using System.Collections.Generic;
using System.Runtime.InteropServices;
using SwingingPaint.BucketFluid;
using SwingingPaint.Core;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SwingingPaint.BucketFluid.Core
{
    /// <summary>
    /// Setup coordinator and particle-buffer owner for the bucket fluid simulation.
    ///
    /// This script initializes particle data in BucketRig local space, uploads it to one GPU
    /// ComputeBuffer, and dispatches the early custom motion and boundary kernels. Particle
    /// positions stay local until rendering code converts them to world space.
    /// </summary>
    public class GPUFluidSimulator : MonoBehaviour
    {
        private const string ComputeShaderPath = "Assets/Shaders/BucketFluid/BucketFluid.compute";
        private const int ThreadGroupSize = 64;
        private const float WallSpawnMargin = 0.001f;
        private const string ParticlesBufferName = "particles";
        private const string ParticleCountName = "_ParticleCount";
        private const string DeltaTimeName = "DeltaTime";
        private const string EffectiveLocalAccelerationName = "EffectiveLocalAcceleration";
        private const string DragName = "Drag";
        private const string MaxVelocityName = "MaxVelocity";
        private const string BoundaryLocalCenterOffsetName = "BoundaryLocalCenterOffset";
        private const string BottomYName = "BottomY";
        private const string TopYName = "TopY";
        private const string BottomRadiusName = "BottomRadius";
        private const string TopRadiusName = "TopRadius";
        private const string ClampTopName = "ClampTop";
        private const string WallDampingName = "WallDamping";
        private const string WallFrictionName = "WallFriction";
        private const string ParticleRadiusName = "ParticleRadius";
        private const string SmoothingRadiusName = "SmoothingRadius";
        private const string RestDensityName = "RestDensity";
        private const string PressureStiffnessName = "PressureStiffness";
        private const string NearPressureStiffnessName = "NearPressureStiffness";
        private const string ViscosityName = "Viscosity";
        private const string SurfaceTensionName = "SurfaceTension";
        private const string CohesionName = "Cohesion";
        private const string DampingName = "Damping";
        private const string CellParticleCountsBufferName = "cellParticleCounts";
        private const string CellParticleIndicesBufferName = "cellParticleIndices";
        private const string SpatialGridCountersBufferName = "spatialGridCounters";
        private const string SpatialCellSizeName = "SpatialCellSize";
        private const string HashTableSizeName = "HashTableSize";
        private const string MaxParticlesPerCellName = "MaxParticlesPerCell";
        private static readonly int ParticlesBufferId = Shader.PropertyToID(ParticlesBufferName);
        private static readonly int ParticleCountId = Shader.PropertyToID(ParticleCountName);
        private static readonly int DeltaTimeId = Shader.PropertyToID(DeltaTimeName);
        private static readonly int EffectiveLocalAccelerationId = Shader.PropertyToID(EffectiveLocalAccelerationName);
        private static readonly int DragId = Shader.PropertyToID(DragName);
        private static readonly int MaxVelocityId = Shader.PropertyToID(MaxVelocityName);
        private static readonly int BoundaryLocalCenterOffsetId = Shader.PropertyToID(BoundaryLocalCenterOffsetName);
        private static readonly int BottomYId = Shader.PropertyToID(BottomYName);
        private static readonly int TopYId = Shader.PropertyToID(TopYName);
        private static readonly int BottomRadiusId = Shader.PropertyToID(BottomRadiusName);
        private static readonly int TopRadiusId = Shader.PropertyToID(TopRadiusName);
        private static readonly int ClampTopId = Shader.PropertyToID(ClampTopName);
        private static readonly int WallDampingId = Shader.PropertyToID(WallDampingName);
        private static readonly int WallFrictionId = Shader.PropertyToID(WallFrictionName);
        private static readonly int ParticleRadiusId = Shader.PropertyToID(ParticleRadiusName);
        private static readonly int SmoothingRadiusId = Shader.PropertyToID(SmoothingRadiusName);
        private static readonly int RestDensityId = Shader.PropertyToID(RestDensityName);
        private static readonly int PressureStiffnessId = Shader.PropertyToID(PressureStiffnessName);
        private static readonly int NearPressureStiffnessId = Shader.PropertyToID(NearPressureStiffnessName);
        private static readonly int ViscosityId = Shader.PropertyToID(ViscosityName);
        private static readonly int SurfaceTensionId = Shader.PropertyToID(SurfaceTensionName);
        private static readonly int CohesionId = Shader.PropertyToID(CohesionName);
        private static readonly int DampingId = Shader.PropertyToID(DampingName);
        private static readonly int CellParticleCountsBufferId = Shader.PropertyToID(CellParticleCountsBufferName);
        private static readonly int CellParticleIndicesBufferId = Shader.PropertyToID(CellParticleIndicesBufferName);
        private static readonly int SpatialGridCountersBufferId = Shader.PropertyToID(SpatialGridCountersBufferName);
        private static readonly int SpatialCellSizeId = Shader.PropertyToID(SpatialCellSizeName);
        private static readonly int HashTableSizeId = Shader.PropertyToID(HashTableSizeName);
        private static readonly int MaxParticlesPerCellId = Shader.PropertyToID(MaxParticlesPerCellName);

        /// <summary>
        /// CPU-side copy of the particle layout used by BucketFluid.compute.
        ///
        /// Important: this field order must match the HLSL FluidParticle struct exactly.
        /// Vector3 maps to float3 and every following float completes a 16-byte lane.
        /// Changing this order requires changing the compute shader struct in the same commit.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FluidParticle
        {
            public Vector3 positionLocal;
            public float density;

            public Vector3 predictedPositionLocal;
            public float nearDensity;

            public Vector3 velocityLocal;
            public float pressure;

            public Vector3 deltaPosition;
            public float nearPressure;

            public int active;
            public int cellHash;
            public int cellIndex;
            public float padding;
        }

        public const int ExpectedFluidParticleStride = 80;
        public static readonly int FluidParticleStride = Marshal.SizeOf(typeof(FluidParticle));
        public static bool HasValidParticleStride => FluidParticleStride == ExpectedFluidParticleStride;

        [Header("References")]
        public BucketFluidSettings settings;
        public ComputeShader fluidComputeShader;
        public BucketMotionProvider motionProvider;
        public BucketFluidBoundary boundary;
        public GPUFluidOutflowController outflowController;

        [Header("Simulation")]
        [Tooltip("Keeps particles inside the mathematical bucket boundary after predicted motion.")]
        public bool boundaryCollisionEnabled = true;
        public bool pauseSimulation;
        public bool pauseAfterReset = false;
        public bool stepSimulationOnce;
        [Tooltip("Skip this component's FixedUpdate loop when SimulationManager is driving fixed-step simulation.")]
        public bool useSimulationManagerDriver = true;

        [Header("Spatial Grid")]
        public bool spatialGridEnabled = true;
        [Min(64)]
        public int hashTableSize = 4096;
        [Min(1)]
        public int maxParticlesPerCell = 64;
        [Tooltip("Seconds between async debug readbacks of grid counters.")]
        public float spatialGridDebugReadbackInterval = 0.25f;

        [Tooltip("Seconds between async debug readbacks of particle validation data.")]
        public float particleValidationReadbackInterval = 0.25f;

        public bool IsInitialized { get; private set; }
        public int TargetParticleCount => settings != null ? settings.ActiveParticleCount : 0;
        public int ActiveParticleCount => RuntimeActiveParticleCount;
        public int InitializedParticleCount { get; private set; }
        public int RuntimeActiveParticleCount { get; private set; }
        public bool HasRequiredReferences => settings != null && fluidComputeShader != null && motionProvider != null && boundary != null;
        public bool HasGpuComputeSupport => SystemInfo.supportsComputeShaders;
        public bool HasParticleBuffer => _particleBuffer != null;
        public bool ParticleBufferValid => _particleBuffer != null && _particleBuffer.IsValid();
        public int ParticleBufferCount => _particleBuffer != null ? _particleBuffer.count : 0;
        public int ParticleStride => FluidParticleStride;
        public Matrix4x4 BucketLocalToWorldMatrix => motionProvider != null ? motionProvider.LocalToWorldMatrix : transform.localToWorldMatrix;
        public bool SimulationRunning { get; private set; }
        public int LastSimulationSubsteps { get; private set; }
        public bool BoundaryCollisionEnabled => boundaryCollisionEnabled && boundary != null;
        public float LastResetSpacing { get; private set; }
        public int LastResetLayerCount { get; private set; }
        public float LastResetMaxRadius { get; private set; }
        public float LastResetFillTopY { get; private set; }
        public bool HasLastResetBounds { get; private set; }
        public Vector3 LastResetBoundsMin { get; private set; }
        public Vector3 LastResetBoundsMax { get; private set; }
        public bool SpatialGridEnabled => spatialGridEnabled;
        public bool SpatialGridBufferValid =>
            _cellParticleCounts != null &&
            _cellParticleCounts.IsValid() &&
            _cellParticleIndices != null &&
            _cellParticleIndices.IsValid() &&
            _spatialGridCounters != null &&
            _spatialGridCounters.IsValid();
        public int SpatialHashTableSize => _activeHashTableSize;
        public int SpatialMaxParticlesPerCell => _activeMaxParticlesPerCell;
        public float SpatialCellSize => settings != null ? Mathf.Max(0.0001f, settings.smoothingRadius) : 0f;
        public bool SpatialGridCountersAvailable { get; private set; }
        public int SpatialGridInsertedCount { get; private set; }
        public int SpatialGridOverflowCount { get; private set; }
        public bool ParticleValidationAvailable { get; private set; }
        public int InvalidParticleCount { get; private set; }
        public int BoundaryLeakCount { get; private set; }
        public float MaxObservedVelocity { get; private set; }
        public float AverageObservedDensity { get; private set; }

        /// <summary>
        /// Structured buffer containing FluidParticle records in bucket-local space.
        /// </summary>
        public ComputeBuffer ParticleBuffer => _particleBuffer;

        private ComputeBuffer _particleBuffer;
        private ComputeBuffer _cellParticleCounts;
        private ComputeBuffer _cellParticleIndices;
        private ComputeBuffer _spatialGridCounters;
        private bool _hasWarnedMissingReferences;
        private bool _hasWarnedInvalidParticleBuffer;
        private bool _hasLoggedKernelError;
        private bool _hasWarnedSpatialGridMissing;
        private bool _hasWarnedSpatialGridOverflow;
        private bool _hasWarnedInvalidParticles;
        private bool _hasWarnedBoundaryLeaks;
        private bool _spatialGridReadbackPending;
        private bool _particleValidationReadbackPending;
        private float _nextSpatialGridReadbackTime;
        private float _nextParticleValidationReadbackTime;
        private bool _kernelsResolved;
        private int _applyForcesKernel;
        private int _predictPositionsKernel;
        private int _clearSpatialGridKernel;
        private int _buildSpatialGridKernel;
        private int _computeDensityKernel;
        private int _solveConstraintsKernel;
        private int _applyViscosityKernel;
        private int _resolveBoundaryKernel;
        private int _updateParticlesKernel;
        private int _activeHashTableSize;
        private int _activeMaxParticlesPerCell;

        private void Awake()
        {
            ResolveReferences();
            ValidateSetup();
        }

        private void OnEnable()
        {
            ResetFluid();
        }

        private void FixedUpdate()
        {
            if (useSimulationManagerDriver &&
                SimulationManager.Instance != null &&
                SimulationManager.Instance.driveFixedStepSimulation)
            {
                return;
            }

            if (pauseSimulation && !stepSimulationOnce)
            {
                SimulationRunning = false;
                LastSimulationSubsteps = 0;
                return;
            }

            bool clearStepRequest = stepSimulationOnce;
            StepSimulation(Time.fixedDeltaTime);

            if (clearStepRequest)
            {
                stepSimulationOnce = false;
                pauseSimulation = true;
            }
        }

        private void Reset()
        {
            ResolveReferences();
            ValidateSetup();
        }

        private void OnDisable()
        {
            ReleaseParticleBuffer();
        }

        private void OnDestroy()
        {
            ReleaseParticleBuffer();
        }

        public void ResetSimulation()
        {
            ResetFluid();
        }

        [ContextMenu("Reset Fluid")]
        public void ResetFluid()
        {
            ResolveReferences();

            if (!HasRequiredReferences)
            {
                ReleaseParticleBuffer();
                ValidateSetup();
                return;
            }

            if (!ValidateGpuPath())
            {
                ReleaseParticleBuffer();
                return;
            }

            if (!HasValidParticleStride)
            {
                ReleaseParticleBuffer();
                Debug.LogError(
                    $"FluidParticle stride mismatch. C# stride is {FluidParticleStride} bytes, " +
                    $"expected {ExpectedFluidParticleStride}. Check GPUFluidSimulator.FluidParticle " +
                    "against BucketFluid.compute.",
                    this
                );
                return;
            }

            ReleaseParticleBuffer();

            int targetParticleCount = TargetParticleCount;
            FluidParticle[] particles = CreateInitialParticles(targetParticleCount);
            int activeBeforeUpload = CountActiveParticles(particles);

            if (settings.enableDebug)
            {
                LogResetDiagnostics(particles, targetParticleCount, activeBeforeUpload);
            }

            _particleBuffer = new ComputeBuffer(particles.Length, FluidParticleStride, ComputeBufferType.Structured);
            _particleBuffer.SetData(particles);

            InitializedParticleCount = activeBeforeUpload;
            RuntimeActiveParticleCount = activeBeforeUpload;
            IsInitialized = true;
            SimulationRunning = false;
            _hasWarnedInvalidParticleBuffer = false;
            _hasWarnedInvalidParticles = false;
            _hasWarnedBoundaryLeaks = false;
            ParticleValidationAvailable = false;
            InvalidParticleCount = 0;
            BoundaryLeakCount = 0;
            MaxObservedVelocity = 0f;
            AverageObservedDensity = 0f;
            EnsureSpatialGridBuffers();
            BuildSpatialGridForCurrentParticles();

            if (outflowController != null && outflowController.gpuOutflowEnabled)
            {
                outflowController.ResetOutflow();
            }

            if (pauseAfterReset)
            {
                pauseSimulation = true;
                stepSimulationOnce = false;
                if (settings.enableDebug)
                {
                    Debug.Log("Fluid reset and simulation paused for inspection.", this);
                }
            }
            else
            {
                pauseSimulation = false;
                stepSimulationOnce = false;
            }
        }

        [ContextMenu("Randomize Fluid Fill")]
        public void RandomizeFluidFill()
        {
            ResolveReferences();

            if (settings == null)
            {
                ValidateSetup();
                return;
            }

            settings.randomSeed = UnityEngine.Random.Range(1, int.MaxValue);

#if UNITY_EDITOR
            EditorUtility.SetDirty(settings);
#endif

            ResetFluid();
        }

        [ContextMenu("Pause Simulation")]
        public void PauseSimulation()
        {
            pauseSimulation = true;
            stepSimulationOnce = false;
        }

        [ContextMenu("Resume Simulation")]
        public void ResumeSimulation()
        {
            pauseSimulation = false;
            stepSimulationOnce = false;
        }

        [ContextMenu("Step Simulation Once")]
        public void StepSimulationOnce()
        {
            pauseSimulation = true;
            stepSimulationOnce = true;
        }

        [ContextMenu("Reset Fluid And Pause")]
        public void ResetFluidAndPause()
        {
            pauseAfterReset = true;
            ResetFluid();
        }

        private void ResolveReferences()
        {
            if (settings == null)
            {
                settings = GetComponent<BucketFluidSettings>();
            }

            if (motionProvider == null)
            {
                motionProvider = GetComponent<BucketMotionProvider>();
            }

            if (boundary == null)
            {
                boundary = GetComponent<BucketFluidBoundary>();
            }

            if (outflowController == null)
            {
                outflowController = GetComponentInChildren<GPUFluidOutflowController>();
            }

            if (outflowController == null)
            {
                Transform paintHole = transform.Find("PaintHole");
                if (paintHole != null)
                {
                    outflowController = paintHole.GetComponent<GPUFluidOutflowController>();
                    if (outflowController == null)
                    {
                        outflowController = paintHole.gameObject.AddComponent<GPUFluidOutflowController>();
                    }
                }
            }

            if (outflowController != null)
            {
                outflowController.simulator = this;
                outflowController.settings = settings;
                outflowController.motionProvider = motionProvider;
                outflowController.boundary = boundary;
            }

#if UNITY_EDITOR
            if (fluidComputeShader == null)
            {
                fluidComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath);
            }
#endif
        }

        public void StepSimulation(float deltaTime)
        {
            SimulationRunning = false;
            LastSimulationSubsteps = 0;

            if (!IsInitialized || !HasRequiredReferences || deltaTime <= Mathf.Epsilon)
            {
                return;
            }

            if (!ValidateGpuPath())
            {
                return;
            }

            if (SimulationManager.Instance != null && SimulationManager.Instance.IsPaused)
            {
                return;
            }

            if (_particleBuffer == null || InitializedParticleCount <= 0)
            {
                if (!_hasWarnedInvalidParticleBuffer)
                {
                    _hasWarnedInvalidParticleBuffer = true;
                    Debug.LogWarning("GPUFluidSimulator cannot simulate because the particle buffer is missing or empty. Use Reset Fluid.", this);
                }

                return;
            }

            if (!ResolveSimulationKernels())
            {
                return;
            }

            int substeps = Mathf.Max(1, settings.substeps);
            float substepDeltaTime = deltaTime / substeps;
            int groups = Mathf.CeilToInt(InitializedParticleCount / (float)ThreadGroupSize);

            for (int i = 0; i < substeps; i++)
            {
                SetSimulationShaderParameters(substepDeltaTime);
                fluidComputeShader.Dispatch(_applyForcesKernel, groups, 1, 1);
                fluidComputeShader.Dispatch(_predictPositionsKernel, groups, 1, 1);

                if (outflowController != null && outflowController.gpuOutflowEnabled)
                {
                    outflowController.StepOutflowFromBucket(this, _particleBuffer, InitializedParticleCount, substepDeltaTime);
                }

                DispatchSpatialGrid(groups);

                if (SpatialGridBufferValid)
                {
                    fluidComputeShader.Dispatch(_applyViscosityKernel, groups, 1, 1);
                    DispatchSpatialGrid(groups);
                    fluidComputeShader.Dispatch(_computeDensityKernel, groups, 1, 1);

                    int solverIterations = Mathf.Max(1, settings.solverIterations);
                    for (int solverIteration = 0; solverIteration < solverIterations; solverIteration++)
                    {
                        fluidComputeShader.Dispatch(_solveConstraintsKernel, groups, 1, 1);

                        if (BoundaryCollisionEnabled)
                        {
                            fluidComputeShader.Dispatch(_resolveBoundaryKernel, groups, 1, 1);
                        }
                    }
                }
                else if (!_hasWarnedSpatialGridMissing)
                {
                    _hasWarnedSpatialGridMissing = true;
                    Debug.LogWarning(
                        "Fluid density, pressure, viscosity, and cohesion kernels require spatial grid buffers. " +
                        "Use Reset Fluid to rebuild them.",
                        this
                    );
                }

                if (BoundaryCollisionEnabled)
                {
                    fluidComputeShader.Dispatch(_resolveBoundaryKernel, groups, 1, 1);
                }

                fluidComputeShader.Dispatch(_updateParticlesKernel, groups, 1, 1);
            }

            SimulationRunning = true;
            LastSimulationSubsteps = substeps;
            RequestSpatialGridCountersReadback();
            RequestParticleValidationReadback();
        }

        private void ValidateSetup()
        {
            IsInitialized = HasRequiredReferences && _particleBuffer != null;

            List<string> missingFields = new List<string>();

            if (settings == null)
            {
                missingFields.Add("Missing Settings");
            }

            if (motionProvider == null)
            {
                missingFields.Add("Missing Motion Provider");
            }

            if (boundary == null)
            {
                missingFields.Add("Missing Boundary (BucketFluidBoundary is required; BucketFluidSettings boundary values are deprecated and not used)");
            }

            if (fluidComputeShader == null)
            {
                missingFields.Add("Missing Fluid Compute Shader");
            }

            if (settings != null && !settings.useGPU)
            {
                missingFields.Add("GPU fluid disabled in settings (this project has no CPU fluid simulation fallback)");
            }

            if (!HasGpuComputeSupport)
            {
                missingFields.Add("Current graphics device does not support compute shaders");
            }

            if (missingFields.Count == 0)
            {
                _hasWarnedMissingReferences = false;
                return;
            }

            if (_hasWarnedMissingReferences)
            {
                return;
            }

            _hasWarnedMissingReferences = true;

            Debug.LogWarning(
                "GPUFluidSimulator setup is incomplete:\n- " + string.Join("\n- ", missingFields),
                this
            );
        }

        private bool ResolveSimulationKernels()
        {
            if (_kernelsResolved)
            {
                return true;
            }

            if (fluidComputeShader == null)
            {
                LogKernelError("Missing Fluid Compute Shader");
                return false;
            }

            if (!TryFindKernel("ApplyForces", out _applyForcesKernel) ||
                !TryFindKernel("PredictPositions", out _predictPositionsKernel) ||
                !TryFindKernel("ClearSpatialGrid", out _clearSpatialGridKernel) ||
                !TryFindKernel("BuildSpatialGrid", out _buildSpatialGridKernel) ||
                !TryFindKernel("ComputeDensity", out _computeDensityKernel) ||
                !TryFindKernel("SolveConstraints", out _solveConstraintsKernel) ||
                !TryFindKernel("ApplyViscosity", out _applyViscosityKernel) ||
                !TryFindKernel("ResolveBoundary", out _resolveBoundaryKernel) ||
                !TryFindKernel("UpdateParticles", out _updateParticlesKernel))
            {
                return false;
            }

            _kernelsResolved = true;
            _hasLoggedKernelError = false;
            return true;
        }

        private bool ValidateGpuPath()
        {
            if (settings == null)
            {
                return false;
            }

            if (!settings.useGPU)
            {
                Debug.LogError("GPUFluidSimulator is GPU-only, but BucketFluidSettings.useGPU is false.", this);
                return false;
            }

            if (!HasGpuComputeSupport)
            {
                Debug.LogError("GPUFluidSimulator requires compute shader support. No CPU fluid fallback is available.", this);
                return false;
            }

            if (fluidComputeShader == null)
            {
                Debug.LogError("GPUFluidSimulator requires BucketFluid.compute. No CPU fluid fallback is available.", this);
                return false;
            }

            return true;
        }

        private bool TryFindKernel(string kernelName, out int kernel)
        {
            kernel = -1;

            if (!fluidComputeShader.HasKernel(kernelName))
            {
                LogKernelError($"Missing compute kernel: {kernelName}");
                return false;
            }

            kernel = fluidComputeShader.FindKernel(kernelName);
            return true;
        }

        private void SetSimulationShaderParameters(float deltaTime)
        {
            fluidComputeShader.SetInt(ParticleCountId, InitializedParticleCount);
            fluidComputeShader.SetFloat(DeltaTimeId, deltaTime);
            fluidComputeShader.SetVector(EffectiveLocalAccelerationId, motionProvider.EffectiveLocalAcceleration);
            fluidComputeShader.SetFloat(DragId, settings.drag);
            fluidComputeShader.SetFloat(MaxVelocityId, settings.maxVelocity);
            fluidComputeShader.SetVector(BoundaryLocalCenterOffsetId, boundary.boundaryLocalCenterOffset);
            fluidComputeShader.SetFloat(BottomYId, boundary.bottomY);
            fluidComputeShader.SetFloat(TopYId, boundary.topY);
            fluidComputeShader.SetFloat(BottomRadiusId, boundary.bottomRadius);
            fluidComputeShader.SetFloat(TopRadiusId, boundary.topRadius);
            fluidComputeShader.SetInt(ClampTopId, boundary.clampTop ? 1 : 0);
            fluidComputeShader.SetFloat(WallDampingId, boundary.wallDamping);
            fluidComputeShader.SetFloat(WallFrictionId, boundary.wallFriction);
            fluidComputeShader.SetFloat(ParticleRadiusId, settings.particleRadius);
            fluidComputeShader.SetFloat(SmoothingRadiusId, settings.smoothingRadius);
            fluidComputeShader.SetFloat(RestDensityId, settings.restDensity);
            fluidComputeShader.SetFloat(PressureStiffnessId, settings.pressureStiffness);
            fluidComputeShader.SetFloat(NearPressureStiffnessId, settings.nearPressureStiffness);
            fluidComputeShader.SetFloat(ViscosityId, settings.viscosity);
            fluidComputeShader.SetFloat(SurfaceTensionId, settings.surfaceTension);
            fluidComputeShader.SetFloat(CohesionId, settings.cohesion);
            fluidComputeShader.SetFloat(DampingId, settings.damping);
            fluidComputeShader.SetFloat(SpatialCellSizeId, SpatialCellSize);
            fluidComputeShader.SetInt(HashTableSizeId, _activeHashTableSize);
            fluidComputeShader.SetInt(MaxParticlesPerCellId, _activeMaxParticlesPerCell);

            fluidComputeShader.SetBuffer(_applyForcesKernel, ParticlesBufferId, _particleBuffer);
            fluidComputeShader.SetBuffer(_predictPositionsKernel, ParticlesBufferId, _particleBuffer);
            fluidComputeShader.SetBuffer(_computeDensityKernel, ParticlesBufferId, _particleBuffer);
            fluidComputeShader.SetBuffer(_solveConstraintsKernel, ParticlesBufferId, _particleBuffer);
            fluidComputeShader.SetBuffer(_applyViscosityKernel, ParticlesBufferId, _particleBuffer);
            fluidComputeShader.SetBuffer(_resolveBoundaryKernel, ParticlesBufferId, _particleBuffer);
            fluidComputeShader.SetBuffer(_updateParticlesKernel, ParticlesBufferId, _particleBuffer);

            if (SpatialGridBufferValid)
            {
                fluidComputeShader.SetBuffer(_clearSpatialGridKernel, CellParticleCountsBufferId, _cellParticleCounts);
                fluidComputeShader.SetBuffer(_clearSpatialGridKernel, SpatialGridCountersBufferId, _spatialGridCounters);

                fluidComputeShader.SetBuffer(_buildSpatialGridKernel, ParticlesBufferId, _particleBuffer);
                fluidComputeShader.SetBuffer(_buildSpatialGridKernel, CellParticleCountsBufferId, _cellParticleCounts);
                fluidComputeShader.SetBuffer(_buildSpatialGridKernel, CellParticleIndicesBufferId, _cellParticleIndices);
                fluidComputeShader.SetBuffer(_buildSpatialGridKernel, SpatialGridCountersBufferId, _spatialGridCounters);

                fluidComputeShader.SetBuffer(_computeDensityKernel, CellParticleCountsBufferId, _cellParticleCounts);
                fluidComputeShader.SetBuffer(_computeDensityKernel, CellParticleIndicesBufferId, _cellParticleIndices);

                fluidComputeShader.SetBuffer(_solveConstraintsKernel, CellParticleCountsBufferId, _cellParticleCounts);
                fluidComputeShader.SetBuffer(_solveConstraintsKernel, CellParticleIndicesBufferId, _cellParticleIndices);

                fluidComputeShader.SetBuffer(_applyViscosityKernel, CellParticleCountsBufferId, _cellParticleCounts);
                fluidComputeShader.SetBuffer(_applyViscosityKernel, CellParticleIndicesBufferId, _cellParticleIndices);
            }
        }

        private void DispatchSpatialGrid(int particleGroups)
        {
            if (!spatialGridEnabled)
            {
                return;
            }

            if (!SpatialGridBufferValid)
            {
                if (!_hasWarnedSpatialGridMissing)
                {
                    _hasWarnedSpatialGridMissing = true;
                    Debug.LogWarning("Spatial grid is enabled, but its buffers are missing. Use Reset Fluid to rebuild them.", this);
                }

                return;
            }

            int clearGroups = Mathf.Max(1, Mathf.CeilToInt(_activeHashTableSize / (float)ThreadGroupSize));
            fluidComputeShader.Dispatch(_clearSpatialGridKernel, clearGroups, 1, 1);
            fluidComputeShader.Dispatch(_buildSpatialGridKernel, particleGroups, 1, 1);
        }

        private void BuildSpatialGridForCurrentParticles()
        {
            if (!spatialGridEnabled || !ParticleBufferValid || InitializedParticleCount <= 0)
            {
                return;
            }

            if (!ResolveSimulationKernels())
            {
                return;
            }

            int groups = Mathf.Max(1, Mathf.CeilToInt(InitializedParticleCount / (float)ThreadGroupSize));
            SetSimulationShaderParameters(0f);
            DispatchSpatialGrid(groups);
            RequestSpatialGridCountersReadback();
        }

        private void LogKernelError(string message)
        {
            if (_hasLoggedKernelError)
            {
                return;
            }

            _hasLoggedKernelError = true;
            Debug.LogError($"GPUFluidSimulator compute setup error: {message}", this);
        }

        private void EnsureSpatialGridBuffers()
        {
            if (!spatialGridEnabled)
            {
                ReleaseSpatialGridBuffers();
                return;
            }

            int safeHashTableSize = GetSafeHashTableSize();
            int safeMaxParticlesPerCell = GetSafeMaxParticlesPerCell();
            long indexCountLong = (long)safeHashTableSize * safeMaxParticlesPerCell;

            if (indexCountLong > int.MaxValue)
            {
                safeHashTableSize = Mathf.Max(1, int.MaxValue / safeMaxParticlesPerCell);
                indexCountLong = (long)safeHashTableSize * safeMaxParticlesPerCell;
                Debug.LogWarning(
                    "Spatial grid index buffer request was too large. Hash table size was clamped to " +
                    $"{safeHashTableSize}.",
                    this
                );
            }

            int indexCount = Mathf.Max(1, (int)indexCountLong);

            if (_cellParticleCounts != null &&
                _cellParticleCounts.IsValid() &&
                _cellParticleCounts.count == safeHashTableSize &&
                _cellParticleIndices != null &&
                _cellParticleIndices.IsValid() &&
                _cellParticleIndices.count == indexCount &&
                _spatialGridCounters != null &&
                _spatialGridCounters.IsValid() &&
                _spatialGridCounters.count == 2)
            {
                _activeHashTableSize = safeHashTableSize;
                _activeMaxParticlesPerCell = safeMaxParticlesPerCell;
                _hasWarnedSpatialGridMissing = false;
                return;
            }

            ReleaseSpatialGridBuffers();

            _cellParticleCounts = new ComputeBuffer(safeHashTableSize, sizeof(uint), ComputeBufferType.Structured);
            _cellParticleIndices = new ComputeBuffer(indexCount, sizeof(uint), ComputeBufferType.Structured);
            _spatialGridCounters = new ComputeBuffer(2, sizeof(uint), ComputeBufferType.Structured);
            _activeHashTableSize = safeHashTableSize;
            _activeMaxParticlesPerCell = safeMaxParticlesPerCell;
            _hasWarnedSpatialGridMissing = false;
            _hasWarnedSpatialGridOverflow = false;
            SpatialGridCountersAvailable = false;
            SpatialGridInsertedCount = 0;
            SpatialGridOverflowCount = 0;
        }

        private int GetSafeHashTableSize()
        {
            return Mathf.Max(64, hashTableSize);
        }

        private int GetSafeMaxParticlesPerCell()
        {
            return Mathf.Max(1, maxParticlesPerCell);
        }

        private void RequestSpatialGridCountersReadback()
        {
            if (!Application.isPlaying ||
                settings == null ||
                !settings.enableDebug ||
                !SpatialGridBufferValid ||
                _spatialGridReadbackPending ||
                Time.unscaledTime < _nextSpatialGridReadbackTime)
            {
                return;
            }

            _spatialGridReadbackPending = true;
            _nextSpatialGridReadbackTime = Time.unscaledTime + Mathf.Max(0.05f, spatialGridDebugReadbackInterval);

            AsyncGPUReadback.Request(_spatialGridCounters, request =>
            {
                _spatialGridReadbackPending = false;

                if (request.hasError || _spatialGridCounters == null)
                {
                    return;
                }

                var data = request.GetData<uint>();
                if (data.Length < 2)
                {
                    return;
                }

                SpatialGridInsertedCount = data[0] > int.MaxValue ? int.MaxValue : (int)data[0];
                SpatialGridOverflowCount = data[1] > int.MaxValue ? int.MaxValue : (int)data[1];
                SpatialGridCountersAvailable = true;

                if (SpatialGridOverflowCount > 0 && !_hasWarnedSpatialGridOverflow)
                {
                    _hasWarnedSpatialGridOverflow = true;
                    Debug.LogWarning(
                        $"Spatial grid overflowed by {SpatialGridOverflowCount} particles. " +
                        "Increase hashTableSize or maxParticlesPerCell before enabling density/pressure.",
                        this
                    );
                }
            });
        }

        private void RequestParticleValidationReadback()
        {
            if (!Application.isPlaying ||
                settings == null ||
                !settings.enableDebug ||
                _particleBuffer == null ||
                !_particleBuffer.IsValid() ||
                _particleValidationReadbackPending ||
                Time.unscaledTime < _nextParticleValidationReadbackTime)
            {
                return;
            }

            ComputeBuffer requestedBuffer = _particleBuffer;
            _particleValidationReadbackPending = true;
            _nextParticleValidationReadbackTime = Time.unscaledTime + Mathf.Max(0.05f, particleValidationReadbackInterval);

            AsyncGPUReadback.Request(requestedBuffer, request =>
            {
                _particleValidationReadbackPending = false;

                if (request.hasError || requestedBuffer == null || requestedBuffer != _particleBuffer)
                {
                    return;
                }

                var data = request.GetData<FluidParticle>();
                int activeCount = 0;
                int invalidCount = 0;
                int boundaryLeakCount = 0;
                float maxVelocity = 0f;
                double densityTotal = 0d;

                int particleCount = Mathf.Min(InitializedParticleCount, data.Length);
                for (int i = 0; i < particleCount; i++)
                {
                    FluidParticle particle = data[i];
                    if (particle.active == 0)
                    {
                        continue;
                    }

                    activeCount++;

                    bool invalid =
                        !IsFinite(particle.positionLocal) ||
                        !IsFinite(particle.predictedPositionLocal) ||
                        !IsFinite(particle.velocityLocal) ||
                        !IsFinite(particle.density) ||
                        !IsFinite(particle.nearDensity);

                    if (invalid)
                    {
                        invalidCount++;
                        continue;
                    }

                    float velocityMagnitude = particle.velocityLocal.magnitude;
                    maxVelocity = Mathf.Max(maxVelocity, velocityMagnitude);
                    densityTotal += particle.density;

                    if (settings != null &&
                        settings.maxVelocity > 0f &&
                        velocityMagnitude > settings.maxVelocity * 1.25f)
                    {
                        invalidCount++;
                    }

                    if (boundary != null &&
                        settings != null &&
                        !boundary.IsInsideWithRadius(particle.positionLocal, settings.particleRadius))
                    {
                        boundaryLeakCount++;
                    }
                }

                RuntimeActiveParticleCount = activeCount;
                InvalidParticleCount = invalidCount;
                BoundaryLeakCount = boundaryLeakCount;
                MaxObservedVelocity = maxVelocity;
                AverageObservedDensity = activeCount > 0 ? (float)(densityTotal / activeCount) : 0f;
                ParticleValidationAvailable = true;

                if (InvalidParticleCount > 0 && !_hasWarnedInvalidParticles)
                {
                    _hasWarnedInvalidParticles = true;
                    Debug.LogWarning(
                        $"Fluid validation found {InvalidParticleCount} invalid particles. " +
                        "The GPU solver will clamp invalid velocities, but settings may be too aggressive.",
                        this
                    );
                }

                if (BoundaryLeakCount > 0 && !_hasWarnedBoundaryLeaks)
                {
                    _hasWarnedBoundaryLeaks = true;
                    Debug.LogWarning(
                        $"Fluid validation found {BoundaryLeakCount} particles outside the bucket boundary. " +
                        "Check bucket boundary dimensions, particle radius, and solver stability.",
                        this
                    );
                }
            });
        }

        private FluidParticle[] CreateInitialParticles(int targetCount)
        {
            int safeTargetCount = Mathf.Max(1, targetCount);
            FluidParticle[] particles = new FluidParticle[safeTargetCount];

            float bottomLocalY = boundary.boundaryLocalCenterOffset.y + boundary.bottomY;
            float topLocalY = boundary.boundaryLocalCenterOffset.y + boundary.topY;
            float fillTopY = Mathf.Lerp(bottomLocalY, topLocalY, settings.fillHeightPercent);
            float particleRadius = Mathf.Max(0.0001f, settings.particleRadius);

            float safeBottomY = bottomLocalY + particleRadius;
            float safeFillTopY = Mathf.Max(safeBottomY, fillTopY - particleRadius);
            LastResetFillTopY = safeFillTopY;
            LastResetSpacing = 0f;
            LastResetLayerCount = 0;
            LastResetMaxRadius = 0f;

            float fillVolume = EstimateFrustumVolume(boundary, safeBottomY, safeFillTopY);
            float spacing = CalculateInitialSpacing(fillVolume, safeTargetCount, particleRadius);

            int initializedCount = FillParticlesWithCircularLayers(
                particles,
                safeBottomY,
                safeFillTopY,
                particleRadius,
                spacing
            );

            if (initializedCount < safeTargetCount)
            {
                initializedCount = FillParticlesWithLayeredSpiral(
                    particles,
                    initializedCount,
                    safeTargetCount,
                    safeBottomY,
                    safeFillTopY,
                    particleRadius
                );
            }

            if (initializedCount < safeTargetCount)
            {
                Debug.LogWarning(
                    $"Only initialized {initializedCount}/{safeTargetCount} particles inside the bucket volume. " +
                    "Increase fillHeightPercent, bucket radii, or reduce particle count.",
                    this
                );
            }

            return particles;
        }

        private void LogResetDiagnostics(FluidParticle[] particles, int targetCount, int activeCount)
        {
            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            List<string> firstPositions = new List<string>();

            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].active == 0)
                {
                    continue;
                }

                Vector3 position = particles[i].positionLocal;
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);

                if (firstPositions.Count < 5)
                {
                    firstPositions.Add($"[{i}] {FormatVector(position)}");
                }
            }

            string boundsText = activeCount > 0
                ? $"min {FormatVector(min)}, max {FormatVector(max)}"
                : "no active particle bounds";

            string firstPositionsText = firstPositions.Count > 0
                ? string.Join(", ", firstPositions)
                : "none";

            HasLastResetBounds = activeCount > 0;
            LastResetBoundsMin = HasLastResetBounds ? min : Vector3.zero;
            LastResetBoundsMax = HasLastResetBounds ? max : Vector3.zero;

            string boundsMinText = activeCount > 0 ? FormatVector(min) : "none";
            string boundsMaxText = activeCount > 0 ? FormatVector(max) : "none";

            Debug.Log(
                $"ResetFluid initialized {activeCount}/{targetCount}, maxRadiusUsed={LastResetMaxRadius:F4}, " +
                $"boundaryTopRadius={boundary.topRadius:F4}, boundaryBottomRadius={boundary.bottomRadius:F4}, " +
                $"boundsMin={boundsMinText}, boundsMax={boundsMaxText}, spacing={LastResetSpacing:F4}, fillTopY={LastResetFillTopY:F4}\n" +
                "Boundary Source = BucketFluidBoundary\n" +
                $"Boundary values: bottomY {boundary.bottomY:F4}, topY {boundary.topY:F4}, bottomRadius {boundary.bottomRadius:F4}, topRadius {boundary.topRadius:F4}\n" +
                $"ParticleBuffer upload count: {particles.Length}\n" +
                $"Active before upload: {activeCount}\n" +
                $"First positions: {firstPositionsText}\n" +
                $"Local bounds: {boundsText}\n" +
                $"Reset spacing/layers/max radius/fill top Y: {LastResetSpacing:F4} / {LastResetLayerCount} / {LastResetMaxRadius:F4} / {LastResetFillTopY:F4}",
                this
            );

            if (activeCount <= 1)
            {
                Debug.LogWarning(
                    "ResetFluid initialized one or fewer particles. Likely causes: particleRadius is too large, " +
                    "boundary radius is too small, fillHeightPercent is too low, or calculated spacing is too large.",
                    this
                );
            }
        }

        private int FillParticlesWithCircularLayers(
            FluidParticle[] particles,
            float safeBottomY,
            float safeFillTopY,
            float particleRadius,
            float initialSpacing)
        {
            int initializedCount = 0;
            float spacing = initialSpacing;

            for (int attempt = 0; attempt < 4 && initializedCount < particles.Length; attempt++)
            {
                initializedCount = 0;
                System.Array.Clear(particles, 0, particles.Length);

                LastResetSpacing = spacing;
                LastResetLayerCount = 0;
                LastResetMaxRadius = 0f;

                float layerSpacing = Mathf.Max(0.002f, spacing * 0.9f);
                int layerIndex = 0;

                for (float y = safeBottomY; y <= safeFillTopY + layerSpacing * 0.25f && initializedCount < particles.Length; y += layerSpacing)
                {
                    float layerY = Mathf.Min(y, safeFillTopY);
                    float allowedRadius = GetSpawnAllowedRadius(layerY, particleRadius);

                    if (allowedRadius <= 0.0001f)
                    {
                        layerIndex++;
                        LastResetLayerCount = layerIndex;
                        continue;
                    }

                    initializedCount = AddRingPackedLayer(
                        particles,
                        initializedCount,
                        layerY,
                        allowedRadius,
                        spacing,
                        safeBottomY,
                        safeFillTopY,
                        particleRadius,
                        layerIndex
                    );

                    layerIndex++;
                    LastResetLayerCount = layerIndex;
                }

                spacing *= 0.82f;
            }

            return initializedCount;
        }

        private float GetSpawnAllowedRadius(float y, float particleRadius)
        {
            return Mathf.Max(0f, boundary.GetRadiusAtY(y) - particleRadius - WallSpawnMargin);
        }

        private int AddRingPackedLayer(
            FluidParticle[] particles,
            int startIndex,
            float y,
            float allowedRadius,
            float spacing,
            float safeBottomY,
            float safeFillTopY,
            float particleRadius,
            int layerIndex)
        {
            int initializedCount = startIndex;
            Vector3 center = boundary.boundaryLocalCenterOffset;

            if (TryCreateSpawnParticle(new Vector3(center.x, y, center.z), initializedCount, layerIndex, spacing, safeBottomY, safeFillTopY, particleRadius, out FluidParticle centerParticle))
            {
                particles[initializedCount] = centerParticle;
                initializedCount++;
            }

            float ringRadius = spacing;
            float lastRingRadius = 0f;

            while (ringRadius < allowedRadius && initializedCount < particles.Length)
            {
                initializedCount = AddParticleRing(
                    particles,
                    initializedCount,
                    center,
                    y,
                    ringRadius,
                    spacing,
                    safeBottomY,
                    safeFillTopY,
                    particleRadius,
                    layerIndex
                );

                lastRingRadius = ringRadius;
                ringRadius += spacing;
            }

            if (allowedRadius > 0f &&
                allowedRadius - lastRingRadius > spacing * 0.2f &&
                initializedCount < particles.Length)
            {
                initializedCount = AddParticleRing(
                    particles,
                    initializedCount,
                    center,
                    y,
                    allowedRadius,
                    spacing,
                    safeBottomY,
                    safeFillTopY,
                    particleRadius,
                    layerIndex
                );
            }

            return initializedCount;
        }

        private int AddParticleRing(
            FluidParticle[] particles,
            int startIndex,
            Vector3 center,
            float y,
            float ringRadius,
            float spacing,
            float safeBottomY,
            float safeFillTopY,
            float particleRadius,
            int layerIndex)
        {
            int initializedCount = startIndex;
            float circumference = Mathf.PI * 2f * ringRadius;
            int particlesOnRing = Mathf.Max(6, Mathf.CeilToInt(circumference / spacing));
            float startAngle = Seeded01(layerIndex, particlesOnRing) * Mathf.PI * 2f;
            LastResetMaxRadius = Mathf.Max(LastResetMaxRadius, ringRadius);

            for (int i = 0; i < particlesOnRing && initializedCount < particles.Length; i++)
            {
                float angle = startAngle + i * Mathf.PI * 2f / particlesOnRing;
                Vector3 candidate = new Vector3(
                    center.x + Mathf.Cos(angle) * ringRadius,
                    y,
                    center.z + Mathf.Sin(angle) * ringRadius
                );

                if (TryCreateSpawnParticle(candidate, initializedCount, layerIndex, spacing, safeBottomY, safeFillTopY, particleRadius, out FluidParticle particle))
                {
                    particles[initializedCount] = particle;
                    initializedCount++;
                }
            }

            return initializedCount;
        }

        private bool TryCreateSpawnParticle(
            Vector3 candidate,
            int particleIndex,
            int layerIndex,
            float spacing,
            float safeBottomY,
            float safeFillTopY,
            float particleRadius,
            out FluidParticle particle)
        {
            float jitterAmplitude = Mathf.Clamp01(settings.spawnJitter) * spacing;
            candidate += GetDeterministicJitter(particleIndex, layerIndex, jitterAmplitude);
            candidate.y = Mathf.Clamp(candidate.y, safeBottomY, safeFillTopY);

            if (!IsInsideFillVolume(candidate, safeBottomY, safeFillTopY, particleRadius))
            {
                candidate = ClampInsideFillVolume(candidate, safeBottomY, safeFillTopY, particleRadius);
            }

            if (!IsInsideFillVolume(candidate, safeBottomY, safeFillTopY, particleRadius))
            {
                particle = default(FluidParticle);
                return false;
            }

            particle = CreateParticle(candidate);
            return true;
        }

        private int FillParticlesWithLayeredSpiral(
            FluidParticle[] particles,
            int startIndex,
            int targetCount,
            float safeBottomY,
            float safeFillTopY,
            float particleRadius)
        {
            const float goldenAngle = 2.39996323f;
            int initializedCount = startIndex;
            int remaining = targetCount - startIndex;

            for (int i = 0; i < remaining && initializedCount < targetCount; i++)
            {
                float normalized = (i + 0.5f) / Mathf.Max(1, remaining);
                float y = Mathf.Lerp(safeBottomY, safeFillTopY, normalized);
                float allowedRadius = GetSpawnAllowedRadius(y, particleRadius);
                float radial = allowedRadius * Mathf.Sqrt(Seeded01(i, 3));
                LastResetMaxRadius = Mathf.Max(LastResetMaxRadius, radial);
                float angle = i * goldenAngle;
                Vector3 center = boundary.boundaryLocalCenterOffset;
                Vector3 candidate = new Vector3(center.x + Mathf.Cos(angle) * radial, y, center.z + Mathf.Sin(angle) * radial);
                candidate += GetDeterministicJitter(initializedCount, i, particleRadius * settings.spawnJitter);

                if (!IsInsideFillVolume(candidate, safeBottomY, safeFillTopY, particleRadius))
                {
                    candidate = ClampInsideFillVolume(candidate, safeBottomY, safeFillTopY, particleRadius);
                }

                particles[initializedCount] = CreateParticle(candidate);
                initializedCount++;
            }

            return initializedCount;
        }

        private FluidParticle CreateParticle(Vector3 positionLocal)
        {
            return new FluidParticle
            {
                positionLocal = positionLocal,
                density = settings.restDensity,
                predictedPositionLocal = positionLocal,
                nearDensity = 0f,
                velocityLocal = Vector3.zero,
                pressure = 0f,
                deltaPosition = Vector3.zero,
                nearPressure = 0f,
                active = 1,
                cellHash = 0,
                cellIndex = 0,
                padding = 0f
            };
        }

        private static float CalculateInitialSpacing(float fillVolume, int targetCount, float particleRadius)
        {
            float volumeBasedSpacing = Mathf.Pow(Mathf.Max(0.000001f, fillVolume / Mathf.Max(1, targetCount)), 1f / 3f);
            float radiusBasedSpacing = Mathf.Max(0.002f, particleRadius * 1.25f);
            return Mathf.Max(0.002f, Mathf.Min(radiusBasedSpacing, volumeBasedSpacing));
        }

        private static float EstimateFrustumVolume(BucketFluidBoundary boundary, float fromY, float toY)
        {
            float height = Mathf.Max(0.0001f, toY - fromY);
            float radiusA = boundary.GetRadiusAtY(fromY);
            float radiusB = boundary.GetRadiusAtY(toY);
            return Mathf.PI * height * (radiusA * radiusA + radiusA * radiusB + radiusB * radiusB) / 3f;
        }

        private bool IsInsideFillVolume(
            Vector3 localPosition,
            float safeBottomY,
            float safeFillTopY,
            float particleRadius)
        {
            if (localPosition.y < safeBottomY || localPosition.y > safeFillTopY)
            {
                return false;
            }

            return boundary.IsInsideWithRadius(localPosition, particleRadius);
        }

        private Vector3 ClampInsideFillVolume(
            Vector3 localPosition,
            float safeBottomY,
            float safeFillTopY,
            float particleRadius)
        {
            localPosition.y = Mathf.Clamp(localPosition.y, safeBottomY, safeFillTopY);
            return boundary.ClosestPointInsideWithRadius(localPosition, particleRadius);
        }

        private Vector3 GetDeterministicJitter(int particleIndex, int layerIndex, float amplitude)
        {
            return new Vector3(
                (Seeded01(particleIndex, layerIndex * 3 + 1) - 0.5f) * amplitude,
                (Seeded01(particleIndex, layerIndex * 3 + 2) - 0.5f) * amplitude * 0.35f,
                (Seeded01(particleIndex, layerIndex * 3 + 3) - 0.5f) * amplitude
            );
        }

        private float Seeded01(int index, int salt)
        {
            unchecked
            {
                int seed = settings != null ? settings.randomSeed : 0;
                seed ^= index * 73856093;
                seed ^= salt * 19349663;
                return Deterministic01(seed);
            }
        }

        private static float Deterministic01(int seed)
        {
            return Mathf.Repeat(Mathf.Sin(seed * 12.9898f) * 43758.5453f, 1f);
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:F4}, {value.y:F4}, {value.z:F4})";
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static int CountActiveParticles(IReadOnlyList<FluidParticle> particles)
        {
            int count = 0;
            for (int i = 0; i < particles.Count; i++)
            {
                if (particles[i].active != 0)
                {
                    count++;
                }
            }

            return count;
        }

        private void OnValidate()
        {
            hashTableSize = Mathf.Max(64, hashTableSize);
            maxParticlesPerCell = Mathf.Max(1, maxParticlesPerCell);
            spatialGridDebugReadbackInterval = Mathf.Max(0.05f, spatialGridDebugReadbackInterval);
            particleValidationReadbackInterval = Mathf.Max(0.05f, particleValidationReadbackInterval);
        }

        private void ReleaseParticleBuffer()
        {
            if (_particleBuffer != null)
            {
                _particleBuffer.Release();
                _particleBuffer = null;
            }

            ReleaseSpatialGridBuffers();
            InitializedParticleCount = 0;
            RuntimeActiveParticleCount = 0;
            IsInitialized = false;
            _particleValidationReadbackPending = false;
            _nextParticleValidationReadbackTime = 0f;
            ParticleValidationAvailable = false;
            InvalidParticleCount = 0;
            BoundaryLeakCount = 0;
            MaxObservedVelocity = 0f;
            AverageObservedDensity = 0f;
        }

        private void ReleaseSpatialGridBuffers()
        {
            if (_cellParticleCounts != null)
            {
                _cellParticleCounts.Release();
                _cellParticleCounts = null;
            }

            if (_cellParticleIndices != null)
            {
                _cellParticleIndices.Release();
                _cellParticleIndices = null;
            }

            if (_spatialGridCounters != null)
            {
                _spatialGridCounters.Release();
                _spatialGridCounters = null;
            }

            _activeHashTableSize = 0;
            _activeMaxParticlesPerCell = 0;
            _spatialGridReadbackPending = false;
            _nextSpatialGridReadbackTime = 0f;
            SpatialGridCountersAvailable = false;
            SpatialGridInsertedCount = 0;
            SpatialGridOverflowCount = 0;
        }
    }
}

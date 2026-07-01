using System.Runtime.InteropServices;
using SwingingPaint.BucketFluid.Rendering;
using SwingingPaint.Core;
using SwingingPaint.Paint;
using SwingingPaint.Surface;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SwingingPaint.BucketFluid.Core
{
    /// <summary>
    /// GPU bridge from bucket-local fluid particles to a world-space falling paint stream.
    /// This uses compute buffers only and does not use Unity physics, raycasts, colliders, or particles.
    /// </summary>
    public class GPUFluidOutflowController : MonoBehaviour
    {
        private const string ComputeShaderPath = "Assets/Shaders/BucketFluid/GPUFluidOutflow.compute";
        private const int ThreadGroupSize = 64;
        private const int CounterCount = 9;

        private static readonly int BucketParticlesId = Shader.PropertyToID("bucketParticles");
        private static readonly int OutflowParticlesId = Shader.PropertyToID("outflowParticles");
        private static readonly int OutflowCellCountsId = Shader.PropertyToID("outflowCellCounts");
        private static readonly int OutflowCellIndicesId = Shader.PropertyToID("outflowCellIndices");
        private static readonly int OutflowCountersId = Shader.PropertyToID("outflowCounters");
        private static readonly int IndirectArgsId = Shader.PropertyToID("indirectArgs");
        private static readonly int CanvasTextureId = Shader.PropertyToID("canvasTexture");

        private static readonly int BucketParticleCountId = Shader.PropertyToID("BucketParticleCount");
        private static readonly int OutflowCapacityId = Shader.PropertyToID("OutflowCapacity");
        private static readonly int HashTableSizeId = Shader.PropertyToID("HashTableSize");
        private static readonly int MaxParticlesPerCellId = Shader.PropertyToID("MaxParticlesPerCell");
        private static readonly int IndexCountPerInstanceId = Shader.PropertyToID("IndexCountPerInstance");
        private static readonly int IndexStartId = Shader.PropertyToID("IndexStart");
        private static readonly int BaseVertexId = Shader.PropertyToID("BaseVertex");
        private static readonly int FrameIndexId = Shader.PropertyToID("FrameIndex");
        private static readonly int DeltaTimeId = Shader.PropertyToID("DeltaTime");
        private static readonly int MaxExtractionsPerStepId = Shader.PropertyToID("MaxExtractionsPerStep");
        private static readonly int ConsumeBucketParticlesId = Shader.PropertyToID("ConsumeBucketParticles");
        private static readonly int LivePaintColorId = Shader.PropertyToID("LivePaintColor");
        private static readonly int PhysicalPourModeId = Shader.PropertyToID("PhysicalPourMode");
        private static readonly int PhysicalExitSpeedId = Shader.PropertyToID("PhysicalExitSpeed");
        private static readonly int PhysicalEmissionTurbulenceId = Shader.PropertyToID("PhysicalEmissionTurbulence");
        private static readonly int FallingAirTurbulenceId = Shader.PropertyToID("FallingAirTurbulence");

        private static readonly int BucketLocalToWorldId = Shader.PropertyToID("BucketLocalToWorld");
        private static readonly int BucketWorldVelocityId = Shader.PropertyToID("BucketWorldVelocity");
        private static readonly int EffectiveWorldGravityId = Shader.PropertyToID("EffectiveWorldGravity");
        private static readonly int HoleLocalPositionId = Shader.PropertyToID("HoleLocalPosition");
        private static readonly int EmitLocalPositionId = Shader.PropertyToID("EmitLocalPosition");
        private static readonly int HoleLocalDirectionId = Shader.PropertyToID("HoleLocalDirection");
        private static readonly int HoleRadiusId = Shader.PropertyToID("HoleRadius");
        private static readonly int BucketParticleRadiusId = Shader.PropertyToID("BucketParticleRadius");
        private static readonly int ParticleRadiusId = Shader.PropertyToID("ParticleRadius");
        private static readonly int PaintFlowRateId = Shader.PropertyToID("PaintFlowRate");
        private static readonly int DrainProbeDepthId = Shader.PropertyToID("DrainProbeDepth");
        private static readonly int DrainCaptureRadiusId = Shader.PropertyToID("DrainCaptureRadius");
        private static readonly int MinimumOutflowSpeedId = Shader.PropertyToID("MinimumOutflowSpeed");
        private static readonly int RequiredOutwardVelocityId = Shader.PropertyToID("RequiredOutwardVelocity");
        private static readonly int OutflowSpawnSpacingId = Shader.PropertyToID("OutflowSpawnSpacing");

        private static readonly int SmoothingRadiusId = Shader.PropertyToID("SmoothingRadius");
        private static readonly int RestDensityId = Shader.PropertyToID("RestDensity");
        private static readonly int PressureStiffnessId = Shader.PropertyToID("PressureStiffness");
        private static readonly int NearPressureStiffnessId = Shader.PropertyToID("NearPressureStiffness");
        private static readonly int ViscosityId = Shader.PropertyToID("Viscosity");
        private static readonly int CohesionId = Shader.PropertyToID("Cohesion");
        private static readonly int SurfaceTensionId = Shader.PropertyToID("SurfaceTension");
        private static readonly int DampingId = Shader.PropertyToID("Damping");
        private static readonly int DragId = Shader.PropertyToID("Drag");
        private static readonly int MaxFallingSpeedId = Shader.PropertyToID("MaxFallingSpeed");
        private static readonly int OutflowLifetimeId = Shader.PropertyToID("OutflowLifetime");
        private static readonly int SpatialCellSizeId = Shader.PropertyToID("SpatialCellSize");
        private static readonly int StreamBreakDistanceId = Shader.PropertyToID("StreamBreakDistance");
        private static readonly int MaxAdaptiveStreamBreakDistanceId = Shader.PropertyToID("MaxAdaptiveStreamBreakDistance");
        private static readonly int StreamRadiusMultiplierId = Shader.PropertyToID("StreamRadiusMultiplier");

        private static readonly int PaintColorId = Shader.PropertyToID("PaintColor");
        private static readonly int ParticleAmountId = Shader.PropertyToID("ParticleAmount");
        private static readonly int CanvasPlanePointId = Shader.PropertyToID("CanvasPlanePoint");
        private static readonly int CanvasPlaneNormalId = Shader.PropertyToID("CanvasPlaneNormal");
        private static readonly int CanvasWorldToLocalId = Shader.PropertyToID("CanvasWorldToLocal");
        private static readonly int CanvasTextureWidthId = Shader.PropertyToID("CanvasTextureWidth");
        private static readonly int CanvasTextureHeightId = Shader.PropertyToID("CanvasTextureHeight");
        private static readonly int CanvasWorldSizeId = Shader.PropertyToID("CanvasWorldSize");
        private static readonly int CanvasContactMultiplierId = Shader.PropertyToID("CanvasContactMultiplier");
        private static readonly int CanvasOpacityMultiplierId = Shader.PropertyToID("CanvasOpacityMultiplier");
        private static readonly int CanvasFlowSpreadBoostId = Shader.PropertyToID("CanvasFlowSpreadBoost");
        private static readonly int CanvasAbsorptionId = Shader.PropertyToID("CanvasAbsorption");
        private static readonly int CanvasMinImpactRadiusId = Shader.PropertyToID("CanvasMinImpactRadius");
        private static readonly int CanvasMaxImpactRadiusId = Shader.PropertyToID("CanvasMaxImpactRadius");
        private static readonly int CanvasSurfaceSpreadId = Shader.PropertyToID("CanvasSurfaceSpread");
        private static readonly int CanvasEdgeIrregularityId = Shader.PropertyToID("CanvasEdgeIrregularity");
        private static readonly int CanvasSplatterStrengthId = Shader.PropertyToID("CanvasSplatterStrength");
        private static readonly int CanvasDirectionalStretchId = Shader.PropertyToID("CanvasDirectionalStretch");
        private static readonly int CanvasSlipStrengthId = Shader.PropertyToID("CanvasSlipStrength");

        [StructLayout(LayoutKind.Sequential)]
        public struct OutflowParticle
        {
            public Vector3 positionWorld;
            public float density;

            public Vector3 previousPositionWorld;
            public float nearDensity;

            public Vector3 velocityWorld;
            public float radius;

            public Vector4 color;

            public float amount;
            public float wetness;
            public float age;
            public float lifetime;

            public int active;
            public int cellHash;
            public int cellIndex;
            public float padding;
        }

        public const int ExpectedOutflowParticleStride = 96;
        public static readonly int OutflowParticleStride = Marshal.SizeOf(typeof(OutflowParticle));
        public static bool HasValidOutflowParticleStride => OutflowParticleStride == ExpectedOutflowParticleStride;

        [Header("References")]
        public GPUFluidSimulator simulator;
        public BucketMotionProvider motionProvider;
        public BucketFluidSettings settings;
        public BucketFluidBoundary boundary;
        public CanvasPaintSurface paintSurface;
        public PhysicsSettings physicsSettings;
        public Transform paintHoleTransform;
        public ComputeShader outflowComputeShader;

        [Header("Outflow")]
        public bool gpuOutflowEnabled = true;
        [Tooltip("Use Torricelli/viscosity based emission from PaintHole instead of deleting bucket particles near the drain plane.")]
        public bool usePhysicalPourModel = true;
        [Range(0.05f, 1f)]
        public float dischargeCoefficient = 0.62f;
        [Min(0f)]
        public float viscosityFlowDamping = 0.8f;
        [Min(0.000001f)]
        public float paintQuantityUnitToCubicMeters = 0.00001f;
        [Min(0.000001f)]
        public float particleVolumeMultiplier = 1f;
        [Min(1)]
        public int maxPhysicalParticlesPerStep = 96;
        [Range(0f, 1f)]
        public float physicalEmissionTurbulence = 0.045f;
        [Tooltip("Small deterministic air turbulence applied to falling GPU paint particles.")]
        [Range(0f, 2f)]
        public float fallingAirTurbulence = 0.18f;
        [Tooltip("Tuning only. When enabled, paint can leave the bucket without deleting the source GPU fluid particles.")]
        public bool infinitePaintSupplyForTuning;
        [Tooltip("Tuning/presentation mode. Recolors active falling GPU paint particles when Paint Color changes.")]
        public bool livePaintColorWhileFalling = true;
        [Min(0f)]
        public float holeDiameter = 0.035f;
        [Min(0.1f)]
        public float outflowLifetime = 6f;
        [Min(0.1f)]
        public float maxFallingSpeed = 12f;
        [Min(0.0001f)]
        public float particleAmount = 0.02f;
        public bool useBoundaryBottomAsDrainPlane = true;
        [Min(0f)]
        public float drainProbeDepth = 0.008f;
        [Min(0f)]
        public float drainCaptureRadius = 0.085f;
        [Min(0f)]
        public float minimumOutflowSpeed = 0.55f;
        [Min(1)]
        public int maxExtractionsPerSubstep = 8;
        [Tooltip("Minimum extraction budget used for a continuous visual stream, even when older scene data keeps Max Extractions low.")]
        [Min(1)]
        public int minimumContinuousStreamExtractions = 6;
        public float requiredOutwardVelocity = -0.03f;
        [Range(0.1f, 1f)]
        public float outflowRadiusFromHole = 0.45f;
        [Range(0.1f, 1f)]
        public float maxOutflowRadiusFromBucketParticle = 0.38f;
        [Min(0f)]
        public float outflowSpawnSpacing = 0.012f;
        [Min(0.001f)]
        public float streamBreakDistance = 0.14f;
        [Min(0.001f)]
        public float maxAdaptiveStreamBreakDistance = 0.45f;
        [Min(0.1f)]
        public float streamRadiusMultiplier = 2.4f;

        [Header("Capacity")]
        [Min(64)]
        public int developmentOutflowCapacity = 16384;
        [Min(64)]
        public int presentationOutflowCapacity = 65536;
        [Min(64)]
        public int hashTableSize = 4096;
        [Min(1)]
        public int maxParticlesPerCell = 64;

        [Header("Debug")]
        public float counterReadbackInterval = 0.25f;

        public bool IsReady => gpuOutflowEnabled &&
                               outflowComputeShader != null &&
                               simulator != null &&
                               simulator.ParticleBufferValid &&
                               settings != null &&
                               motionProvider != null &&
                               paintHoleTransform != null &&
                               paintSurface != null &&
                               paintSurface.PaintTexture != null &&
                               _outflowBuffer != null &&
                               _outflowBuffer.IsValid();
        public int OutflowCapacity => _activeOutflowCapacity;
        public int ActiveOutflowParticles { get; private set; }
        public int EmittedParticlesThisTick { get; private set; }
        public int TotalAllocatedOutflowParticles { get; private set; }
        public int BufferOverflowThisTick { get; private set; }
        public int DepositedImpactsThisTick { get; private set; }
        public int CanvasGpuWritesThisTick { get; private set; }
        public int StreamConnectorCount { get; private set; }
        public int CurrentExtractionBudget { get; private set; }
        public float AverageOutflowDensity { get; private set; }
        public float CurrentPhysicalFlowRateCubicMetersPerSecond { get; private set; }
        public float CurrentPhysicalExitSpeed { get; private set; }
        public float RemainingPaintVolumeCubicMeters { get; private set; }
        public bool GpuCanvasWritesEnabled => paintSurface != null && paintSurface.GpuCanvasModeEnabled;
        public bool CanRunPrimaryOutflow => gpuOutflowEnabled && outflowComputeShader != null && _kernelsResolved;
        public float EffectiveOutflowParticleRadius => GetOutflowParticleRadius(
            settings != null ? settings.particleRadius : 0.035f,
            GetHoleDiameter());
        public float EffectiveVisualStreamRadiusMultiplier => Mathf.Max(streamRadiusMultiplier, 2.2f);
        public float EffectiveDrainCaptureRadius => GetSafeDrainCaptureRadius(
            settings != null ? settings.particleRadius : 0.035f,
            GetHoleDiameter());
        public ComputeBuffer OutflowParticleBuffer => _outflowBuffer;
        public ComputeBuffer IndirectArgsBuffer => _indirectArgsBuffer;

        private ComputeBuffer _outflowBuffer;
        private ComputeBuffer _outflowCellCounts;
        private ComputeBuffer _outflowCellIndices;
        private ComputeBuffer _outflowCounters;
        private ComputeBuffer _indirectArgsBuffer;
        private int _activeOutflowCapacity;
        private int _activeHashTableSize;
        private int _activeMaxParticlesPerCell;
        private uint _frameIndex;
        private bool _kernelsResolved;
        private bool _hasWarnedSetup;
        private bool _counterReadbackPending;
        private float _nextCounterReadbackTime;
        private float _physicalEmissionVolumeAccumulator;
        private bool _paintVolumeInitialized;
        private uint _indexCountPerInstance = 6;
        private uint _indexStart;
        private uint _baseVertex;

        private int _clearFrameCountersKernel;
        private int _extractOutflowKernel;
        private int _predictOutflowKernel;
        private int _clearOutflowGridKernel;
        private int _buildOutflowGridKernel;
        private int _computeOutflowDensityKernel;
        private int _solveOutflowConstraintsKernel;
        private int _applyOutflowViscosityKernel;
        private int _detectCanvasImpactsKernel;
        private int _updateOutflowParticlesKernel;
        private int _buildStreamConnectorsKernel;
        private int _buildIndirectArgsKernel;

        private void Awake()
        {
            ResolveReferences();
            EnsureBuffers();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureBuffers();
            ResetOutflow();
        }

        private void OnDisable()
        {
            ReleaseBuffers();
        }

        private void OnDestroy()
        {
            ReleaseBuffers();
        }

        public void SetRenderMeshArgs(Mesh mesh)
        {
            if (mesh == null)
            {
                _indexCountPerInstance = 6;
                _indexStart = 0;
                _baseVertex = 0;
                return;
            }

            _indexCountPerInstance = (uint)mesh.GetIndexCount(0);
            _indexStart = (uint)mesh.GetIndexStart(0);
            _baseVertex = (uint)mesh.GetBaseVertex(0);
        }

        public bool StepOutflowFromBucket(
            GPUFluidSimulator sourceSimulator,
            ComputeBuffer bucketParticleBuffer,
            int bucketParticleCount,
            float deltaTime)
        {
            if (!gpuOutflowEnabled || deltaTime <= Mathf.Epsilon)
            {
                return false;
            }

            if (sourceSimulator != null)
            {
                simulator = sourceSimulator;
            }

            ResolveReferences();

            if (!ValidateSetup(bucketParticleBuffer, bucketParticleCount))
            {
                return false;
            }

            EnsureBuffers();

            if (!ResolveKernels())
            {
                return false;
            }

            SetShaderParameters(bucketParticleBuffer, bucketParticleCount, deltaTime);

            int bucketGroups = Mathf.Max(1, Mathf.CeilToInt(bucketParticleCount / (float)ThreadGroupSize));
            int outflowGroups = Mathf.Max(1, Mathf.CeilToInt(_activeOutflowCapacity / (float)ThreadGroupSize));
            int gridGroups = Mathf.Max(1, Mathf.CeilToInt(_activeHashTableSize / (float)ThreadGroupSize));

            outflowComputeShader.Dispatch(_clearFrameCountersKernel, 1, 1, 1);
            outflowComputeShader.Dispatch(_extractOutflowKernel, bucketGroups, 1, 1);
            outflowComputeShader.Dispatch(_predictOutflowKernel, outflowGroups, 1, 1);
            outflowComputeShader.Dispatch(_clearOutflowGridKernel, gridGroups, 1, 1);
            outflowComputeShader.Dispatch(_buildOutflowGridKernel, outflowGroups, 1, 1);
            outflowComputeShader.Dispatch(_computeOutflowDensityKernel, outflowGroups, 1, 1);

            int solverIterations = settings != null ? Mathf.Max(1, settings.solverIterations) : 2;
            for (int i = 0; i < solverIterations; i++)
            {
                outflowComputeShader.Dispatch(_solveOutflowConstraintsKernel, outflowGroups, 1, 1);
            }

            outflowComputeShader.Dispatch(_applyOutflowViscosityKernel, outflowGroups, 1, 1);
            outflowComputeShader.Dispatch(_detectCanvasImpactsKernel, outflowGroups, 1, 1);
            outflowComputeShader.Dispatch(_updateOutflowParticlesKernel, outflowGroups, 1, 1);
            outflowComputeShader.Dispatch(_buildStreamConnectorsKernel, outflowGroups, 1, 1);
            outflowComputeShader.Dispatch(_buildIndirectArgsKernel, outflowGroups, 1, 1);
            _frameIndex++;

            RequestCounterReadback();
            return true;
        }

        [ContextMenu("Reset GPU Outflow")]
        public void ResetOutflow()
        {
            ResolveReferences();
            EnsureBuffers();

            if (_outflowBuffer != null)
            {
                _outflowBuffer.SetData(new OutflowParticle[_activeOutflowCapacity]);
            }

            if (_outflowCounters != null)
            {
                _outflowCounters.SetData(new uint[CounterCount]);
            }

            if (_indirectArgsBuffer != null)
            {
                _indirectArgsBuffer.SetData(new uint[] { _indexCountPerInstance, (uint)_activeOutflowCapacity, _indexStart, _baseVertex, 0u });
            }

            ActiveOutflowParticles = 0;
            EmittedParticlesThisTick = 0;
            TotalAllocatedOutflowParticles = 0;
            BufferOverflowThisTick = 0;
            DepositedImpactsThisTick = 0;
            CanvasGpuWritesThisTick = 0;
            StreamConnectorCount = 0;
            AverageOutflowDensity = 0f;
            _physicalEmissionVolumeAccumulator = 0f;
            ResetPaintVolume();
            _frameIndex = 1;
        }

        private void ResolveReferences()
        {
            if (paintHoleTransform == null)
            {
                paintHoleTransform = transform;
            }

            if (simulator == null)
            {
                simulator = GetComponentInParent<GPUFluidSimulator>();
            }

            if (settings == null && simulator != null)
            {
                settings = simulator.settings;
            }

            if (motionProvider == null && simulator != null)
            {
                motionProvider = simulator.motionProvider;
            }

            if (boundary == null && simulator != null)
            {
                boundary = simulator.boundary;
            }

            if (paintSurface == null)
            {
                paintSurface = FindObjectOfType<CanvasPaintSurface>();
            }

            if (physicsSettings == null && SimulationManager.Instance != null)
            {
                physicsSettings = SimulationManager.Instance.physicsSettings;
            }

            if (physicsSettings == null)
            {
                physicsSettings = Resources.Load<PhysicsSettings>("PhysicsSettings");
            }

            GPUOutflowRenderer outflowRenderer = GetComponent<GPUOutflowRenderer>();
            if (outflowRenderer == null)
            {
                outflowRenderer = gameObject.AddComponent<GPUOutflowRenderer>();
            }

            outflowRenderer.outflowController = this;

#if UNITY_EDITOR
            if (outflowComputeShader == null)
            {
                outflowComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath);
            }
#endif
        }

        private bool ValidateSetup(ComputeBuffer bucketParticleBuffer, int bucketParticleCount)
        {
            if (!HasValidOutflowParticleStride ||
                outflowComputeShader == null ||
                simulator == null ||
                settings == null ||
                motionProvider == null ||
                paintHoleTransform == null ||
                paintSurface == null ||
                bucketParticleBuffer == null ||
                bucketParticleCount <= 0)
            {
                WarnSetupOnce("GPU outflow setup is incomplete.");
                return false;
            }

            if (paintSurface.PaintTexture == null)
            {
                paintSurface.ClearPaint();
            }

            if (paintSurface.PaintTexture == null || !paintSurface.PaintTexture.enableRandomWrite)
            {
                WarnSetupOnce("Canvas paint texture must support random writes for GPU outflow deposition.");
                return false;
            }

            _hasWarnedSetup = false;
            return true;
        }

        private void WarnSetupOnce(string message)
        {
            if (_hasWarnedSetup)
            {
                return;
            }

            _hasWarnedSetup = true;
            Debug.LogWarning(message, this);
        }

        private bool ResolveKernels()
        {
            if (_kernelsResolved)
            {
                return true;
            }

            if (outflowComputeShader == null)
            {
                return false;
            }

            if (!TryFindKernel("ClearFrameCounters", out _clearFrameCountersKernel) ||
                !TryFindKernel("ExtractOutflow", out _extractOutflowKernel) ||
                !TryFindKernel("PredictOutflow", out _predictOutflowKernel) ||
                !TryFindKernel("ClearOutflowGrid", out _clearOutflowGridKernel) ||
                !TryFindKernel("BuildOutflowGrid", out _buildOutflowGridKernel) ||
                !TryFindKernel("ComputeOutflowDensity", out _computeOutflowDensityKernel) ||
                !TryFindKernel("SolveOutflowConstraints", out _solveOutflowConstraintsKernel) ||
                !TryFindKernel("ApplyOutflowViscosity", out _applyOutflowViscosityKernel) ||
                !TryFindKernel("DetectCanvasImpacts", out _detectCanvasImpactsKernel) ||
                !TryFindKernel("UpdateOutflowParticles", out _updateOutflowParticlesKernel) ||
                !TryFindKernel("BuildStreamConnectors", out _buildStreamConnectorsKernel) ||
                !TryFindKernel("BuildIndirectArgs", out _buildIndirectArgsKernel))
            {
                return false;
            }

            _kernelsResolved = true;
            return true;
        }

        private bool TryFindKernel(string kernelName, out int kernel)
        {
            kernel = -1;

            if (!outflowComputeShader.HasKernel(kernelName))
            {
                Debug.LogError($"GPUFluidOutflowController missing compute kernel: {kernelName}", this);
                return false;
            }

            kernel = outflowComputeShader.FindKernel(kernelName);
            return true;
        }

        private void EnsureBuffers()
        {
            int capacity = GetSafeOutflowCapacity();
            int safeHashTableSize = Mathf.Max(64, hashTableSize);
            int safeMaxParticlesPerCell = Mathf.Max(1, maxParticlesPerCell);
            bool needsRebuild =
                _outflowBuffer == null ||
                !_outflowBuffer.IsValid() ||
                _activeOutflowCapacity != capacity ||
                _activeHashTableSize != safeHashTableSize ||
                _activeMaxParticlesPerCell != safeMaxParticlesPerCell;

            if (!needsRebuild)
            {
                return;
            }

            ReleaseBuffers();

            _activeOutflowCapacity = capacity;
            _activeHashTableSize = safeHashTableSize;
            _activeMaxParticlesPerCell = safeMaxParticlesPerCell;

            _outflowBuffer = new ComputeBuffer(_activeOutflowCapacity, OutflowParticleStride, ComputeBufferType.Structured);
            _outflowCellCounts = new ComputeBuffer(_activeHashTableSize, sizeof(uint), ComputeBufferType.Structured);
            _outflowCellIndices = new ComputeBuffer(_activeHashTableSize * _activeMaxParticlesPerCell, sizeof(uint), ComputeBufferType.Structured);
            _outflowCounters = new ComputeBuffer(CounterCount, sizeof(uint), ComputeBufferType.Structured);
            _indirectArgsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
            ResetOutflow();
        }

        private int GetSafeOutflowCapacity()
        {
            int requested = settings != null && settings.presentationMode
                ? presentationOutflowCapacity
                : developmentOutflowCapacity;
            return Mathf.Max(64, requested);
        }

        private void SetShaderParameters(ComputeBuffer bucketParticleBuffer, int bucketParticleCount, float deltaTime)
        {
            float particleRadius = settings != null ? settings.particleRadius : 0.035f;
            float outflowParticleRadius = GetOutflowParticleRadius(particleRadius, GetHoleDiameter());
            float viscosity = physicsSettings != null ? physicsSettings.PaintViscosity : settings != null ? settings.viscosity : 0.45f;
            float flowRate = physicsSettings != null ? physicsSettings.PaintFlowRate : 1f;
            float holeDiameter = GetHoleDiameter();
            float effectiveFlowRate = GetEffectiveOutflowRate(flowRate, holeDiameter);
            Vector3 gravity = Vector3.down * (physicsSettings != null ? physicsSettings.Gravity : settings != null ? settings.gravity : 9.81f);
            float gravityMagnitude = gravity.magnitude;
            float physicalHead = GetPhysicalPaintHead();
            float physicalFlowRate = GetTorricelliFlowRate(flowRate, holeDiameter, viscosity, physicalHead, gravityMagnitude);
            float physicalExitSpeed = GetPhysicalExitSpeed(physicalHead, gravityMagnitude, viscosity);
            float particleVolume = GetOutflowParticleVolume(outflowParticleRadius);
            int physicalEmissionBudget = GetPhysicalEmissionBudget(physicalFlowRate, particleVolume, deltaTime);
            float shaderPaintFlowRate = usePhysicalPourModel ? flowRate : effectiveFlowRate;
            float amountScale = Mathf.Clamp(
                (outflowParticleRadius * outflowParticleRadius) / Mathf.Max(0.000001f, particleRadius * particleRadius),
                0.06f,
                1f);
            Color color;
            if (physicsSettings != null)
            {
                color = physicsSettings.PaintColor;
                color.a = Mathf.Clamp01(color.a);
            }
            else if (settings != null)
            {
                color = settings.paintColor;
                color.a = Mathf.Clamp01(settings.opacity);
            }
            else
            {
                color = Color.blue;
            }

            Matrix4x4 localToWorld = simulator != null ? simulator.BucketLocalToWorldMatrix : transform.localToWorldMatrix;
            Vector3 bucketVelocity = motionProvider != null ? motionProvider.WorldVelocity : Vector3.zero;
            Vector3 holeLocalPosition = simulator != null
                ? simulator.transform.InverseTransformPoint(paintHoleTransform.position)
                : paintHoleTransform.localPosition;
            Vector3 emitLocalPosition = holeLocalPosition;
            if (useBoundaryBottomAsDrainPlane && boundary != null)
            {
                holeLocalPosition = boundary.GetBottomCenterLocal();
            }

            Vector3 holeLocalDirection = simulator != null
                ? simulator.transform.InverseTransformDirection(paintHoleTransform.TransformDirection(Vector3.down))
                : Vector3.down;

            if (holeLocalDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                holeLocalDirection = Vector3.down;
            }

            Transform canvasTransform = paintSurface.transform;
            Vector3 canvasPoint = canvasTransform.TransformPoint(new Vector3(0f, paintSurface.surfaceLocalY, 0f));
            Vector3 canvasNormal = canvasTransform.up.sqrMagnitude > Mathf.Epsilon
                ? canvasTransform.up.normalized
                : Vector3.up;

            outflowComputeShader.SetInt(BucketParticleCountId, bucketParticleCount);
            outflowComputeShader.SetInt(OutflowCapacityId, _activeOutflowCapacity);
            outflowComputeShader.SetInt(HashTableSizeId, _activeHashTableSize);
            outflowComputeShader.SetInt(MaxParticlesPerCellId, _activeMaxParticlesPerCell);
            outflowComputeShader.SetInt(IndexCountPerInstanceId, unchecked((int)_indexCountPerInstance));
            outflowComputeShader.SetInt(IndexStartId, unchecked((int)_indexStart));
            outflowComputeShader.SetInt(BaseVertexId, unchecked((int)_baseVertex));
            outflowComputeShader.SetInt(FrameIndexId, unchecked((int)_frameIndex));
            outflowComputeShader.SetFloat(DeltaTimeId, deltaTime);
            CurrentExtractionBudget = usePhysicalPourModel
                ? physicalEmissionBudget
                : GetExtractionBudget(effectiveFlowRate, viscosity);
            outflowComputeShader.SetInt(MaxExtractionsPerStepId, CurrentExtractionBudget);
            outflowComputeShader.SetInt(ConsumeBucketParticlesId, infinitePaintSupplyForTuning ? 0 : 1);
            outflowComputeShader.SetInt(LivePaintColorId, livePaintColorWhileFalling ? 1 : 0);
            outflowComputeShader.SetInt(PhysicalPourModeId, usePhysicalPourModel ? 1 : 0);
            outflowComputeShader.SetFloat(PhysicalExitSpeedId, physicalExitSpeed);
            outflowComputeShader.SetFloat(PhysicalEmissionTurbulenceId, physicalEmissionTurbulence);
            outflowComputeShader.SetFloat(FallingAirTurbulenceId, fallingAirTurbulence);
            outflowComputeShader.SetMatrix(BucketLocalToWorldId, localToWorld);
            outflowComputeShader.SetVector(BucketWorldVelocityId, bucketVelocity);
            outflowComputeShader.SetVector(EffectiveWorldGravityId, gravity);
            outflowComputeShader.SetVector(HoleLocalPositionId, holeLocalPosition);
            outflowComputeShader.SetVector(EmitLocalPositionId, emitLocalPosition);
            outflowComputeShader.SetVector(HoleLocalDirectionId, holeLocalDirection.normalized);
            outflowComputeShader.SetFloat(HoleRadiusId, Mathf.Max(0f, holeDiameter) * 0.5f);
            outflowComputeShader.SetFloat(BucketParticleRadiusId, particleRadius);
            outflowComputeShader.SetFloat(ParticleRadiusId, outflowParticleRadius);
            outflowComputeShader.SetFloat(PaintFlowRateId, shaderPaintFlowRate);
            outflowComputeShader.SetFloat(DrainProbeDepthId, GetSafeDrainProbeDepth(particleRadius));
            outflowComputeShader.SetFloat(DrainCaptureRadiusId, GetSafeDrainCaptureRadius(particleRadius, GetHoleDiameter()));
            outflowComputeShader.SetFloat(MinimumOutflowSpeedId, minimumOutflowSpeed * Mathf.Max(0.25f, Mathf.Sqrt(Mathf.Max(0.01f, effectiveFlowRate))));
            outflowComputeShader.SetFloat(RequiredOutwardVelocityId, requiredOutwardVelocity);
            outflowComputeShader.SetFloat(OutflowSpawnSpacingId, GetOutflowSpawnSpacing(outflowParticleRadius));
            outflowComputeShader.SetFloat(SmoothingRadiusId, settings != null ? settings.smoothingRadius * 0.75f : 0.075f);
            outflowComputeShader.SetFloat(RestDensityId, settings != null ? settings.restDensity : 1000f);
            outflowComputeShader.SetFloat(PressureStiffnessId, settings != null ? settings.pressureStiffness : 120f);
            outflowComputeShader.SetFloat(NearPressureStiffnessId, settings != null ? settings.nearPressureStiffness : 180f);
            outflowComputeShader.SetFloat(ViscosityId, viscosity);
            outflowComputeShader.SetFloat(CohesionId, settings != null ? settings.cohesion : 0.2f);
            outflowComputeShader.SetFloat(SurfaceTensionId, settings != null ? settings.surfaceTension : 0.25f);
            outflowComputeShader.SetFloat(DampingId, settings != null ? settings.damping : 0.02f);
            outflowComputeShader.SetFloat(DragId, settings != null ? settings.drag : 0.03f);
            outflowComputeShader.SetFloat(MaxFallingSpeedId, maxFallingSpeed);
            outflowComputeShader.SetFloat(OutflowLifetimeId, outflowLifetime);
            outflowComputeShader.SetFloat(SpatialCellSizeId, Mathf.Max(0.0001f, settings != null ? settings.smoothingRadius * 0.75f : 0.075f));
            float effectiveStreamBreakDistance = GetEffectiveStreamBreakDistance(outflowParticleRadius);
            outflowComputeShader.SetFloat(StreamBreakDistanceId, effectiveStreamBreakDistance);
            outflowComputeShader.SetFloat(MaxAdaptiveStreamBreakDistanceId, Mathf.Max(maxAdaptiveStreamBreakDistance, effectiveStreamBreakDistance * 2.25f));
            outflowComputeShader.SetFloat(StreamRadiusMultiplierId, EffectiveVisualStreamRadiusMultiplier);
            outflowComputeShader.SetVector(PaintColorId, color);
            float particleAmountScale = usePhysicalPourModel
                ? Mathf.Clamp(particleVolume / Mathf.Max(0.0000001f, GetOutflowParticleVolume(0.0065f)), 0.08f, 3f)
                : Mathf.Clamp(effectiveFlowRate, 0.15f, 3f);
            outflowComputeShader.SetFloat(ParticleAmountId, particleAmount * amountScale * particleAmountScale);
            outflowComputeShader.SetVector(CanvasPlanePointId, canvasPoint);
            outflowComputeShader.SetVector(CanvasPlaneNormalId, canvasNormal);
            outflowComputeShader.SetMatrix(CanvasWorldToLocalId, canvasTransform.worldToLocalMatrix);
            outflowComputeShader.SetInt(CanvasTextureWidthId, paintSurface.PaintTexture.width);
            outflowComputeShader.SetInt(CanvasTextureHeightId, paintSurface.PaintTexture.height);
            Vector2 canvasWorldSize = paintSurface.CanvasWorldSize;
            outflowComputeShader.SetVector(CanvasWorldSizeId, new Vector4(canvasWorldSize.x, canvasWorldSize.y, 0f, 0f));
            outflowComputeShader.SetFloat(CanvasContactMultiplierId, Mathf.Max(0f, paintSurface.contactOffsetMultiplier));
            outflowComputeShader.SetFloat(CanvasOpacityMultiplierId, Mathf.Max(0f, paintSurface.opacityMultiplier));
            outflowComputeShader.SetFloat(CanvasFlowSpreadBoostId, Mathf.Max(0f, paintSurface.flowSpreadBoost));
            outflowComputeShader.SetFloat(CanvasAbsorptionId, physicsSettings != null ? physicsSettings.SurfaceAbsorption : paintSurface.defaultAbsorption);
            outflowComputeShader.SetFloat(CanvasMinImpactRadiusId, paintSurface.minImpactRadius);
            outflowComputeShader.SetFloat(CanvasMaxImpactRadiusId, paintSurface.maxImpactRadius);
            outflowComputeShader.SetFloat(CanvasSurfaceSpreadId, paintSurface.surfaceSpread);
            outflowComputeShader.SetFloat(CanvasEdgeIrregularityId, paintSurface.edgeIrregularity);
            outflowComputeShader.SetFloat(CanvasSplatterStrengthId, paintSurface.splatterStrength);
            outflowComputeShader.SetFloat(CanvasDirectionalStretchId, paintSurface.directionalStretch);
            outflowComputeShader.SetFloat(CanvasSlipStrengthId, paintSurface.slidingStrength);

            BindBuffers(_clearFrameCountersKernel, bucketParticleBuffer);
            BindBuffers(_extractOutflowKernel, bucketParticleBuffer);
            BindBuffers(_predictOutflowKernel, bucketParticleBuffer);
            BindBuffers(_clearOutflowGridKernel, bucketParticleBuffer);
            BindBuffers(_buildOutflowGridKernel, bucketParticleBuffer);
            BindBuffers(_computeOutflowDensityKernel, bucketParticleBuffer);
            BindBuffers(_solveOutflowConstraintsKernel, bucketParticleBuffer);
            BindBuffers(_applyOutflowViscosityKernel, bucketParticleBuffer);
            BindBuffers(_detectCanvasImpactsKernel, bucketParticleBuffer);
            BindBuffers(_updateOutflowParticlesKernel, bucketParticleBuffer);
            BindBuffers(_buildStreamConnectorsKernel, bucketParticleBuffer);
            BindBuffers(_buildIndirectArgsKernel, bucketParticleBuffer);
        }

        private float GetHoleDiameter()
        {
            if (physicsSettings != null)
            {
                return physicsSettings.PaintHoleDiameter;
            }

            return holeDiameter;
        }

        private int GetExtractionBudget(float flowRate, float viscosity)
        {
            if (flowRate <= 0.005f)
            {
                return 0;
            }

            float flowScale = Mathf.Sqrt(Mathf.Max(0.01f, flowRate));
            float viscosityFlowScale = Mathf.Lerp(1.15f, 0.85f, Mathf.Clamp01(Mathf.Max(0f, viscosity) / 5f));
            int safeBaseBudget = Mathf.Max(
                Mathf.Clamp(maxExtractionsPerSubstep, 1, 64),
                Mathf.Clamp(minimumContinuousStreamExtractions, 1, 64));
            return Mathf.Clamp(Mathf.RoundToInt(safeBaseBudget * flowScale * viscosityFlowScale), 1, 64);
        }

        private float GetPhysicalPaintHead()
        {
            if (boundary == null)
            {
                return Mathf.Max(0.01f, settings != null ? settings.fillHeightPercent * 0.25f : 0.15f);
            }

            float fillPercent = settings != null ? settings.fillHeightPercent : 0.55f;
            return Mathf.Max(0.005f, boundary.Height * Mathf.Clamp01(fillPercent));
        }

        private float GetPhysicalExitSpeed(float paintHead, float gravityMagnitude, float viscosity)
        {
            float invViscosity = 1f / (1f + Mathf.Max(0f, viscosityFlowDamping) * Mathf.Max(0f, viscosity));
            return Mathf.Max(0f, dischargeCoefficient) *
                   Mathf.Sqrt(Mathf.Max(0f, 2f * gravityMagnitude * Mathf.Max(0f, paintHead))) *
                   invViscosity;
        }

        private float GetTorricelliFlowRate(float flowMultiplier, float diameter, float viscosity, float paintHead, float gravityMagnitude)
        {
            float radius = Mathf.Max(0f, diameter) * 0.5f;
            if (flowMultiplier <= 0f || radius <= 0.00025f || paintHead <= 0f)
            {
                CurrentPhysicalFlowRateCubicMetersPerSecond = 0f;
                CurrentPhysicalExitSpeed = 0f;
                return 0f;
            }

            float area = Mathf.PI * radius * radius;
            float exitSpeed = GetPhysicalExitSpeed(paintHead, gravityMagnitude, viscosity);
            float flowRate = Mathf.Max(0f, flowMultiplier) * area * exitSpeed;
            CurrentPhysicalFlowRateCubicMetersPerSecond = flowRate;
            CurrentPhysicalExitSpeed = exitSpeed;
            return flowRate;
        }

        private float GetOutflowParticleVolume(float radius)
        {
            float safeRadius = Mathf.Max(0.0005f, radius);
            return 4f / 3f * Mathf.PI * safeRadius * safeRadius * safeRadius * Mathf.Max(0.000001f, particleVolumeMultiplier);
        }

        private int GetPhysicalEmissionBudget(float flowRate, float particleVolume, float deltaTime)
        {
            if (!usePhysicalPourModel || flowRate <= 0f || particleVolume <= 0f || deltaTime <= 0f)
            {
                return 0;
            }

            EnsurePaintVolumeInitialized();

            float emittedVolumeThisStep = flowRate * deltaTime;
            if (!infinitePaintSupplyForTuning)
            {
                emittedVolumeThisStep = Mathf.Min(emittedVolumeThisStep, RemainingPaintVolumeCubicMeters);
            }

            if (emittedVolumeThisStep <= 0f)
            {
                return 0;
            }

            _physicalEmissionVolumeAccumulator += emittedVolumeThisStep;
            int budget = Mathf.FloorToInt(_physicalEmissionVolumeAccumulator / particleVolume);
            budget = Mathf.Clamp(budget, 0, Mathf.Max(1, maxPhysicalParticlesPerStep));
            float consumedVolume = budget * particleVolume;
            _physicalEmissionVolumeAccumulator = Mathf.Max(0f, _physicalEmissionVolumeAccumulator - consumedVolume);

            if (!infinitePaintSupplyForTuning)
            {
                RemainingPaintVolumeCubicMeters = Mathf.Max(0f, RemainingPaintVolumeCubicMeters - consumedVolume);
            }

            return budget;
        }

        private void EnsurePaintVolumeInitialized()
        {
            if (_paintVolumeInitialized)
            {
                return;
            }

            ResetPaintVolume();
        }

        private void ResetPaintVolume()
        {
            float quantity = physicsSettings != null ? physicsSettings.PaintQuantity : 100f;
            RemainingPaintVolumeCubicMeters = Mathf.Max(0f, quantity) * Mathf.Max(0.000001f, paintQuantityUnitToCubicMeters);
            _paintVolumeInitialized = true;
        }

        private static float GetEffectiveOutflowRate(float flowRate, float diameter)
        {
            float safeFlowRate = Mathf.Max(0f, flowRate);
            float safeDiameter = Mathf.Max(0f, diameter);
            if (safeFlowRate <= 0f || safeDiameter <= 0.0005f)
            {
                return 0f;
            }

            const float referenceDiameter = 0.035f;
            float diameterScale = safeDiameter / referenceDiameter;
            return safeFlowRate * diameterScale * diameterScale;
        }

        private float GetOutflowParticleRadius(float bucketParticleRadius, float diameter)
        {
            float holeRadius = Mathf.Max(0.001f, diameter * 0.5f);
            float fromHole = holeRadius * Mathf.Clamp(outflowRadiusFromHole, 0.1f, 1f);
            float fromBucketParticle = Mathf.Max(0.001f, bucketParticleRadius) * Mathf.Clamp(maxOutflowRadiusFromBucketParticle, 0.1f, 1f);
            return Mathf.Clamp(Mathf.Min(fromHole, fromBucketParticle), 0.0035f, Mathf.Max(0.0035f, bucketParticleRadius));
        }

        private float GetOutflowSpawnSpacing(float outflowParticleRadius)
        {
            return Mathf.Max(outflowParticleRadius * 0.8f, outflowSpawnSpacing);
        }

        private float GetEffectiveStreamBreakDistance(float outflowParticleRadius)
        {
            float spacing = GetOutflowSpawnSpacing(outflowParticleRadius);
            float radiusDrivenBreak = Mathf.Max(outflowParticleRadius * 8f, spacing * 4f);
            return Mathf.Max(streamBreakDistance, radiusDrivenBreak);
        }

        private float GetSafeDrainProbeDepth(float particleRadius)
        {
            return Mathf.Min(Mathf.Max(0f, drainProbeDepth), Mathf.Max(0.001f, particleRadius * 0.35f));
        }

        private float GetSafeDrainCaptureRadius(float bucketParticleRadius, float diameter)
        {
            float holeRadius = Mathf.Max(0.001f, diameter * 0.5f);
            float minimumUsefulRadius = holeRadius + bucketParticleRadius * 0.55f;
            float maximumSafeRadius = Mathf.Max(minimumUsefulRadius, bucketParticleRadius * 2.6f);
            return Mathf.Clamp(drainCaptureRadius, minimumUsefulRadius, maximumSafeRadius);
        }

        private void BindBuffers(int kernel, ComputeBuffer bucketParticleBuffer)
        {
            outflowComputeShader.SetBuffer(kernel, BucketParticlesId, bucketParticleBuffer);
            outflowComputeShader.SetBuffer(kernel, OutflowParticlesId, _outflowBuffer);
            outflowComputeShader.SetBuffer(kernel, OutflowCellCountsId, _outflowCellCounts);
            outflowComputeShader.SetBuffer(kernel, OutflowCellIndicesId, _outflowCellIndices);
            outflowComputeShader.SetBuffer(kernel, OutflowCountersId, _outflowCounters);
            outflowComputeShader.SetBuffer(kernel, IndirectArgsId, _indirectArgsBuffer);

            if (paintSurface != null && paintSurface.PaintTexture != null)
            {
                outflowComputeShader.SetTexture(kernel, CanvasTextureId, paintSurface.PaintTexture);
            }
        }

        private void RequestCounterReadback()
        {
            if (_outflowCounters == null ||
                _counterReadbackPending ||
                Time.unscaledTime < _nextCounterReadbackTime)
            {
                return;
            }

            _counterReadbackPending = true;
            AsyncGPUReadback.Request(_outflowCounters, request =>
            {
                _counterReadbackPending = false;
                _nextCounterReadbackTime = Time.unscaledTime + Mathf.Max(0.05f, counterReadbackInterval);

                if (request.hasError)
                {
                    return;
                }

                var data = request.GetData<uint>();
                if (data.Length < CounterCount)
                {
                    return;
                }

                TotalAllocatedOutflowParticles = unchecked((int)data[0]);
                EmittedParticlesThisTick = unchecked((int)data[1]);
                BufferOverflowThisTick = unchecked((int)data[2]);
                DepositedImpactsThisTick = unchecked((int)data[3]);
                CanvasGpuWritesThisTick = unchecked((int)data[4]);
                ActiveOutflowParticles = unchecked((int)data[5]);
                AverageOutflowDensity = data[5] > 0 ? data[6] / 1000f / data[5] : 0f;
                StreamConnectorCount = unchecked((int)data[7]);
            });
        }

        private void ReleaseBuffers()
        {
            ReleaseBuffer(ref _outflowBuffer);
            ReleaseBuffer(ref _outflowCellCounts);
            ReleaseBuffer(ref _outflowCellIndices);
            ReleaseBuffer(ref _outflowCounters);
            ReleaseBuffer(ref _indirectArgsBuffer);
            _activeOutflowCapacity = 0;
            _activeHashTableSize = 0;
            _activeMaxParticlesPerCell = 0;
            _counterReadbackPending = false;
            _kernelsResolved = false;
        }

        private static void ReleaseBuffer(ref ComputeBuffer buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Release();
            buffer = null;
        }

        private void OnValidate()
        {
            holeDiameter = Mathf.Max(0f, holeDiameter);
            outflowLifetime = Mathf.Max(0.1f, outflowLifetime);
            maxFallingSpeed = Mathf.Max(0.1f, maxFallingSpeed);
            particleAmount = Mathf.Max(0.0001f, particleAmount);
            drainProbeDepth = Mathf.Clamp(drainProbeDepth, 0f, 0.02f);
            drainCaptureRadius = Mathf.Clamp(drainCaptureRadius, 0f, 0.12f);
            minimumOutflowSpeed = Mathf.Max(0f, minimumOutflowSpeed);
            maxExtractionsPerSubstep = Mathf.Clamp(maxExtractionsPerSubstep, 1, 64);
            minimumContinuousStreamExtractions = Mathf.Clamp(minimumContinuousStreamExtractions, 1, 64);
            dischargeCoefficient = Mathf.Clamp(dischargeCoefficient, 0.05f, 1f);
            viscosityFlowDamping = Mathf.Max(0f, viscosityFlowDamping);
            paintQuantityUnitToCubicMeters = Mathf.Max(0.000001f, paintQuantityUnitToCubicMeters);
            particleVolumeMultiplier = Mathf.Max(0.000001f, particleVolumeMultiplier);
            maxPhysicalParticlesPerStep = Mathf.Max(1, maxPhysicalParticlesPerStep);
            physicalEmissionTurbulence = Mathf.Clamp01(physicalEmissionTurbulence);
            fallingAirTurbulence = Mathf.Clamp(fallingAirTurbulence, 0f, 2f);
            outflowRadiusFromHole = Mathf.Clamp(outflowRadiusFromHole, 0.1f, 1f);
            maxOutflowRadiusFromBucketParticle = Mathf.Clamp(maxOutflowRadiusFromBucketParticle, 0.1f, 1f);
            outflowSpawnSpacing = Mathf.Max(0f, outflowSpawnSpacing);
            streamBreakDistance = Mathf.Max(0.001f, streamBreakDistance);
            maxAdaptiveStreamBreakDistance = Mathf.Max(streamBreakDistance, maxAdaptiveStreamBreakDistance);
            streamRadiusMultiplier = Mathf.Max(0.1f, streamRadiusMultiplier);
            developmentOutflowCapacity = Mathf.Max(64, developmentOutflowCapacity);
            presentationOutflowCapacity = Mathf.Max(64, presentationOutflowCapacity);
            hashTableSize = Mathf.Max(64, hashTableSize);
            maxParticlesPerCell = Mathf.Max(1, maxParticlesPerCell);
            counterReadbackInterval = Mathf.Max(0.05f, counterReadbackInterval);
        }
    }
}

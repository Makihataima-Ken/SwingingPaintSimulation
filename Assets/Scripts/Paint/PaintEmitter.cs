using System.Collections.Generic;
using SwingingPaint.BucketFluid;
using SwingingPaint.BucketFluid.Core;
using SwingingPaint.Core;
using SwingingPaint.Surface;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwingingPaint.Paint
{
    /// <summary>
    /// Custom paint stream emitted from PaintHole.
    ///
    /// Droplets are plain data updated with manual integration. This component does not use
    /// Rigidbody, Collider, Joint, raycasts, ParticleSystem, or Unity particle physics.
    /// </summary>
    public class PaintEmitter : MonoBehaviour
    {
        private const int InstancedBatchSize = 1023;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public enum PaintRenderMode
        {
            DropletsOnly,
            ContinuousStream,
            Hybrid
        }

        public enum FallingStreamPhysicsMode
        {
            Ballistic,
            CohesiveSPH
        }

        public struct PaintParticle
        {
            public Vector3 position;
            public Vector3 previousPosition;
            public Vector3 velocity;
            public float radius;
            public Color color;
            public float wetness;
            public float amount;
            public float lifetime;
            public float age;
            public bool active;
            public int streamId;
            public int orderInStream;
            public float density;
            public float nearDensity;
        }

        [Header("References")]
        public PhysicsSettings physicsSettings;
        public BucketFluidSettings fluidSettings;
        public BucketMotionProvider motionProvider;
        public BucketFluidBoundary boundary;
        public CanvasPaintSurface paintSurface;
        public Transform paintHoleTransform;
        public Transform bucketTransform;

        [Header("Emission")]
        public bool emitWhilePlaying = true;
        public bool resetOnEnable = true;
        [Min(0f)]
        public float holeDiameter = 0.035f;
        [Min(0f)]
        public float defaultFlowRate = 1f;
        [Min(0f)]
        public float defaultPaintViscosity = 0.5f;
        [Min(0f)]
        public float defaultPaintQuantity = 100f;
        [Min(0f)]
        public float paintMassPerQuantityUnit = 0.01f;
        public bool syncPaintMassToPhysicsSettings;

        [Header("Droplets")]
        [Min(0.0001f)]
        public float dropletAmount = 0.02f;
        [Min(0.0001f)]
        public float minDropletRadius = 0.008f;
        [Min(0.0001f)]
        public float maxDropletRadius = 0.035f;
        [Min(0f)]
        public float exitSpeedMultiplier = 1f;
        [Range(0f, 0.25f)]
        public float emissionJitter = 0.02f;
        [Min(1)]
        public int maxDroplets = 2048;
        [Min(0.1f)]
        public float dropletLifetime = 6f;

        [Header("Continuous Stream")]
        public bool continuousStreamMode = true;
        public PaintRenderMode renderMode = PaintRenderMode.Hybrid;
        [Tooltip("World-space distance between emitted stream particles. Values <= 0 use particle radius * 1.5.")]
        [Min(0f)]
        public float streamParticleSpacing = 0.012f;
        [Tooltip("Smallest allowed stream sampling spacing when the outlet moves quickly.")]
        [Min(0.0001f)]
        public float minStreamParticleSpacing = 0.008f;
        [Tooltip("Hard cap on stream particles emitted in one simulation tick.")]
        [Min(1)]
        public int maxParticlesPerTick = 96;
        [Tooltip("Below this flow rate the emitter falls back to droplet behavior.")]
        [Min(0f)]
        public float minEmissionRateForStream = 0.001f;
        [Min(0.01f)]
        public float streamRadiusMultiplier = 1.7f;
        [Tooltip("Small velocity jitter for stream particles. Keep low to preserve a connected stream.")]
        [Range(0f, 0.25f)]
        public float streamVelocityJitter = 0.001f;
        [Tooltip("Maximum gap between stream particles before the renderer starts a new visual segment. Values <= 0 use particle radius * 4.")]
        [Min(0f)]
        public float streamBreakDistance = 0.09f;
        public PaintStreamRenderer streamRenderer;

        [Header("Falling Stream Physics")]
        public FallingStreamPhysicsMode fallingStreamPhysicsMode = FallingStreamPhysicsMode.CohesiveSPH;
        [Min(1)]
        public int fallingSubsteps = 2;
        [Min(0.0001f)]
        public float streamSmoothingRadius = 0.075f;
        [Min(0f)]
        public float streamPressureStiffness = 45f;
        [Min(0f)]
        public float streamNearPressureStiffness = 90f;
        [Min(0f)]
        public float streamCohesion = 0.35f;
        [Min(0f)]
        public float streamSurfaceTension = 0.2f;
        [Min(0.1f)]
        public float maxFallingSpeed = 12f;
        public bool flushCanvasAfterPaintStep = true;

        [Header("Canvas Contact")]
        public bool useVisualStreamContactRadius = true;
        [Min(0f)]
        public float streamContactRadiusMultiplier = 1.1f;
        [Range(0f, 2f)]
        public float contactPredictionFractionOfSubstep = 0.5f;
        [Min(0f)]
        public float maxContactPredictionDistance = 0.035f;

        [Header("Manual Falling Motion")]
        [Min(0f)]
        public float gravity = 9.81f;
        [Min(0f)]
        public float airResistance = 0.08f;
        [Min(0f)]
        public float viscosityDampingMultiplier = 0.35f;

        [Header("Runtime Driving")]
        [Tooltip("Skip this component's FixedUpdate loop when SimulationManager is driving fixed-step simulation.")]
        public bool useSimulationManagerDriver = true;

        [Header("Rendering")]
        public bool drawDroplets = true;
        public Mesh dropletMesh;
        public Material dropletMaterial;
        public bool depositOnPaintSurface = true;

        public float RemainingPaintQuantity { get; private set; }
        public float EmittedPaintQuantity { get; private set; }
        public int ActiveDropletCount { get; private set; }
        public bool CanEmit => emitWhilePlaying && RemainingPaintQuantity > 0f && GetFlowRate() > 0f && holeDiameter > 0f;
        public float EmissionAccumulator => _emissionAccumulator;
        public int LastEmittedParticlesPerTick => _lastEmittedParticlesPerTick;
        public int StreamRenderSegments => streamRenderer != null ? streamRenderer.RenderedSegmentCount : 0;
        public float AverageStreamSpacing => streamRenderer != null ? streamRenderer.AverageStreamSpacing : 0f;
        public float MaxStreamSpacing => streamRenderer != null ? streamRenderer.MaxStreamSpacing : 0f;
        public int BrokenStreamSegmentCount => streamRenderer != null ? streamRenderer.BrokenStreamSegmentCount : 0;
        public float AverageFallingSpeed => streamRenderer != null ? streamRenderer.AverageFallingSpeed : 0f;
        public float CurrentAdaptiveBreakDistance => streamRenderer != null ? streamRenderer.CurrentAdaptiveBreakDistance : 0f;
        public float CurrentFlowRate => GetFlowRate();
        public Color CurrentPaintColor => GetPaintColor();
        public float EffectiveStreamViscosity => GetStreamViscosity();
        public float EffectiveStreamCohesion => streamCohesion;
        public Vector3 PreviousOutletPosition => _previousOutletPosition;
        public Vector3 CurrentOutletPosition => _currentOutletPosition;
        public Vector3 PreviousOutletVelocity => _previousOutletVelocity;
        public Vector3 CurrentOutletVelocity => _currentOutletVelocity;
        public int FallingStreamNeighborCount { get; private set; }
        public float AverageStreamDensity { get; private set; }
        public float MaxStreamDensity { get; private set; }
        public int DepositsThisTick { get; private set; }
        public bool CanvasFlushedThisTick { get; private set; }
        public bool SurfaceContactModeEnabled => paintSurface != null && paintSurface.SurfaceContactModeEnabled;
        public float LastCanvasContactRadius { get; private set; }
        public float LastCanvasContactPredictionDistance { get; private set; }
        public int PredictedCanvasContactsThisTick { get; private set; }
        public bool CanvasTextureDirtyBeforeRender { get; private set; }
        public bool ShouldRenderStreamMesh =>
            continuousStreamMode &&
            renderMode != PaintRenderMode.DropletsOnly &&
            CurrentFlowRate >= minEmissionRateForStream;
        public bool ShouldRenderDropletInstances =>
            renderMode == PaintRenderMode.DropletsOnly ||
            renderMode == PaintRenderMode.Hybrid ||
            !ShouldRenderStreamMesh;
        public float EffectiveStreamBreakDistance
        {
            get
            {
                float fallbackRadius = Mathf.Max(minDropletRadius, holeDiameter * 0.5f * 0.65f);
                return streamBreakDistance > 0f
                    ? streamBreakDistance
                    : fallbackRadius * 4f;
            }
        }

        public float GetStreamVisualHalfWidth(float particleRadius)
        {
            float flowScale = Mathf.Lerp(1f, 1.45f, Mathf.Clamp01(CurrentFlowRate / 5f));
            float viscosityScale = Mathf.Lerp(1f, 1.2f, Mathf.Clamp01(EffectiveStreamViscosity / 2f));
            float cohesionScale = Mathf.Lerp(1f, 1.15f, Mathf.Clamp01(streamCohesion));
            return Mathf.Max(0.0001f, particleRadius * streamRadiusMultiplier * flowScale * viscosityScale * cohesionScale);
        }

        public float GetDesiredStreamSpacing()
        {
            float fallbackRadius = Mathf.Max(minDropletRadius, holeDiameter * 0.5f * 0.65f);
            float spacing = streamParticleSpacing > 0f
                ? streamParticleSpacing
                : fallbackRadius * 1.5f;

            return Mathf.Max(minStreamParticleSpacing, spacing);
        }

        private PaintParticle[] _droplets;
        private Matrix4x4[] _matrices;
        private MaterialPropertyBlock _propertyBlock;
        private Mesh _runtimeDropletMesh;
        private Material _runtimeDropletMaterial;
        private float _emissionAccumulator;
        private int _nextDropletIndex;
        private int _spawnSerial;
        private int _currentStreamId;
        private int _nextOrderInStream;
        private int _lastEmittedParticlesPerTick;
        private bool _wasEmittingLastStep;
        private bool _hasOutletSample;
        private Vector3 _previousOutletPosition;
        private Vector3 _currentOutletPosition;
        private Vector3 _previousOutletVelocity;
        private Vector3 _currentOutletVelocity;
        private SimulationManager _subscribedSimulationManager;
        private readonly Dictionary<Vector3Int, List<int>> _streamGrid = new Dictionary<Vector3Int, List<int>>();
        private readonly List<int> _activeStreamIndices = new List<int>();
        private Vector3[] _sphVelocityDeltas;
        private float[] _streamDensities;
        private float[] _streamNearDensities;

        private void Awake()
        {
            ResolveReferences();
            EnsureStorage();
            EnsureRenderResources();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureStorage();
            EnsureRenderResources();
            SubscribeToSimulationManager();

            if (resetOnEnable)
            {
                ResetEmitter();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromSimulationManager();
        }

        private void OnDestroy()
        {
            UnsubscribeFromSimulationManager();
            DestroyRuntimeResources();
        }

        private void Reset()
        {
            ResolveReferences();
            ResetEmitter();
        }

        private void FixedUpdate()
        {
            ResolveReferences();
            SubscribeToSimulationManager();

            if (useSimulationManagerDriver &&
                SimulationManager.Instance != null &&
                SimulationManager.Instance.driveFixedStepSimulation)
            {
                return;
            }

            if (SimulationManager.Instance != null && SimulationManager.Instance.IsPaused)
            {
                return;
            }

            Step(Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            CanvasTextureDirtyBeforeRender = paintSurface != null && paintSurface.TextureDirty;

            if (CanvasTextureDirtyBeforeRender && flushCanvasAfterPaintStep && paintSurface != null)
            {
                CanvasFlushedThisTick |= paintSurface.FlushPaintTexture();
            }

            if (!drawDroplets || ActiveDropletCount <= 0)
            {
                return;
            }

            EnsureRenderResources();

            if (ShouldRenderStreamMesh && streamRenderer != null)
            {
                streamRenderer.RenderStream();
            }

            if (ShouldRenderDropletInstances)
            {
                DrawDroplets();
            }
        }

        [ContextMenu("Reset Paint Emitter")]
        public void ResetEmitter()
        {
            EnsureStorage();

            for (int i = 0; i < _droplets.Length; i++)
            {
                _droplets[i].active = false;
            }

            RemainingPaintQuantity = GetInitialPaintQuantity();
            EmittedPaintQuantity = 0f;
            ActiveDropletCount = 0;
            _emissionAccumulator = 0f;
            _nextDropletIndex = 0;
            _spawnSerial = 0;
            _currentStreamId = 0;
            _nextOrderInStream = 0;
            _lastEmittedParticlesPerTick = 0;
            _wasEmittingLastStep = false;
            ResetStreamPhysicsMetrics();
            DepositsThisTick = 0;
            CanvasFlushedThisTick = false;
            ResetCanvasContactMetrics();

            Vector3 outletPosition = GetEmissionPosition();
            _previousOutletPosition = outletPosition;
            _currentOutletPosition = outletPosition;
            _previousOutletVelocity = Vector3.zero;
            _currentOutletVelocity = Vector3.zero;
            _hasOutletSample = true;

            if (streamRenderer != null)
            {
                streamRenderer.ClearStream();
            }
        }

        public void Step(float deltaTime)
        {
            if (deltaTime <= Mathf.Epsilon)
            {
                return;
            }

            EnsureStorage();
            SampleOutletMotion(deltaTime);
            _lastEmittedParticlesPerTick = 0;
            DepositsThisTick = 0;
            CanvasFlushedThisTick = false;
            ResetCanvasContactMetrics();

            if (CanEmit && ActiveDropletCount < maxDroplets)
            {
                EmitPaint(deltaTime);
            }
            else
            {
                _wasEmittingLastStep = false;
            }

            UpdateDroplets(deltaTime);

            if (flushCanvasAfterPaintStep && DepositsThisTick > 0 && paintSurface != null)
            {
                CanvasFlushedThisTick = paintSurface.FlushPaintTexture();
            }
        }

        private void EmitPaint(float deltaTime)
        {
            float emittedAmount = CalculateEmissionAmount(deltaTime);
            if (emittedAmount <= 0f)
            {
                return;
            }

            RemainingPaintQuantity = Mathf.Max(0f, RemainingPaintQuantity - emittedAmount);
            EmittedPaintQuantity += emittedAmount;

            if (syncPaintMassToPhysicsSettings && physicsSettings != null && paintMassPerQuantityUnit > 0f)
            {
                physicsSettings.SetPaintMass(Mathf.Max(0f, physicsSettings.PaintMass - emittedAmount * paintMassPerQuantityUnit));
            }

            _emissionAccumulator += emittedAmount;

            bool streamEmission = ShouldUseContinuousEmission();
            float targetParticleAmount = streamEmission ? GetStreamParticleAmount() : Mathf.Max(0.0001f, dropletAmount);
            int requiredFromFlow = Mathf.FloorToInt(_emissionAccumulator / targetParticleAmount);
            int requiredFromMovement = 0;

            if (streamEmission)
            {
                float pathDistance = Vector3.Distance(_previousOutletPosition, _currentOutletPosition);
                requiredFromMovement = Mathf.CeilToInt(pathDistance / GetDesiredStreamSpacing());
            }

            int requestedSpawnCount = streamEmission
                ? Mathf.Max(requiredFromFlow, requiredFromMovement)
                : requiredFromFlow;

            if (requestedSpawnCount <= 0)
            {
                return;
            }

            int tickLimit = streamEmission ? Mathf.Max(1, maxParticlesPerTick) : 128;
            int spawnCount = Mathf.Min(requestedSpawnCount, maxDroplets - ActiveDropletCount, tickLimit);

            if (spawnCount <= 0)
            {
                return;
            }

            float minimumParticleAmount = streamEmission
                ? targetParticleAmount * 0.25f
                : targetParticleAmount;

            if (_emissionAccumulator < minimumParticleAmount)
            {
                return;
            }

            float availableForThisTick = Mathf.Min(_emissionAccumulator, targetParticleAmount * spawnCount);
            float amountPerParticle = availableForThisTick / spawnCount;

            if (!_wasEmittingLastStep)
            {
                _currentStreamId++;
                _nextOrderInStream = 0;
            }

            for (int i = 0; i < spawnCount; i++)
            {
                float pathT = streamEmission
                    ? GetSpawnPathT(i, spawnCount)
                    : 1f;
                Vector3 spawnPosition = streamEmission
                    ? Vector3.Lerp(_previousOutletPosition, _currentOutletPosition, pathT)
                    : _currentOutletPosition;
                Vector3 outletVelocity = streamEmission
                    ? Vector3.Lerp(_previousOutletVelocity, _currentOutletVelocity, pathT)
                    : GetBucketWorldVelocity();

                SpawnDroplet(amountPerParticle, spawnPosition, outletVelocity, streamEmission);
            }

            _emissionAccumulator = Mathf.Max(0f, _emissionAccumulator - amountPerParticle * spawnCount);

            _lastEmittedParticlesPerTick = spawnCount;
            _wasEmittingLastStep = true;
        }

        private float CalculateEmissionAmount(float deltaTime)
        {
            float referenceDiameter = Mathf.Max(0.001f, 0.035f);
            float holeFactor = Mathf.Pow(Mathf.Max(0f, holeDiameter) / referenceDiameter, 2f);
            float viscosityFactor = 1f / (1f + GetPaintViscosity());
            float quantityFactor = Mathf.Clamp01(RemainingPaintQuantity / Mathf.Max(0.0001f, GetInitialPaintQuantity()));
            float pressureHeadFactor = Mathf.Lerp(0.35f, 1f, quantityFactor);

            Vector3 emissionDirection = GetEmissionDirection();
            Vector3 effectiveAcceleration = GetEffectiveWorldAcceleration();
            float accelerationMagnitude = effectiveAcceleration.magnitude;
            float gravityAlignment = accelerationMagnitude > Mathf.Epsilon
                ? Mathf.Clamp01(Vector3.Dot(emissionDirection, effectiveAcceleration / accelerationMagnitude))
                : 0f;

            Vector3 inertialAcceleration = motionProvider != null ? -motionProvider.WorldAcceleration : Vector3.zero;
            float motionBoost = 1f + Mathf.Max(0f, Vector3.Dot(emissionDirection, inertialAcceleration)) * 0.03f;
            motionBoost = Mathf.Clamp(motionBoost, 0.25f, 3f);

            return Mathf.Min(
                RemainingPaintQuantity,
                GetFlowRate() * holeFactor * viscosityFactor * pressureHeadFactor * gravityAlignment * motionBoost * deltaTime
            );
        }

        private void SpawnDroplet(float amount, Vector3 spawnPosition, Vector3 outletVelocity, bool streamEmission)
        {
            int index = FindDropletSlot();
            if (index < 0)
            {
                return;
            }

            Vector3 emissionDirection = GetEmissionDirection();
            float viscosityFactor = 1f / (1f + GetPaintViscosity());
            float head = Mathf.Lerp(0.04f, 0.35f, Mathf.Clamp01(RemainingPaintQuantity / Mathf.Max(0.0001f, GetInitialPaintQuantity())));
            float exitSpeed = Mathf.Sqrt(2f * Mathf.Max(0f, GetGravity()) * head) * viscosityFactor * exitSpeedMultiplier;
            exitSpeed += GetFlowRate() * 0.035f;

            float jitterAmplitude = streamEmission
                ? streamVelocityJitter * viscosityFactor
                : emissionJitter;

            PaintParticle droplet = new PaintParticle
            {
                position = spawnPosition,
                previousPosition = spawnPosition,
                velocity = outletVelocity + emissionDirection * exitSpeed + GetDeterministicJitter(_spawnSerial, jitterAmplitude),
                radius = CalculateDropletRadius(amount),
                color = GetPaintColor(),
                wetness = 1f,
                amount = amount,
                lifetime = dropletLifetime,
                age = 0f,
                active = true,
                streamId = streamEmission ? _currentStreamId : -1,
                orderInStream = streamEmission ? _nextOrderInStream++ : _spawnSerial,
                density = 0f,
                nearDensity = 0f
            };

            _droplets[index] = droplet;
            ActiveDropletCount++;
            _spawnSerial++;
        }

        private void UpdateDroplets(float deltaTime)
        {
            int substeps = ShouldUseCohesiveStreamPhysics()
                ? Mathf.Max(1, fallingSubsteps)
                : 1;
            float substepDeltaTime = deltaTime / substeps;

            for (int i = 0; i < substeps; i++)
            {
                UpdateDropletsSubstep(substepDeltaTime);
            }

            RecountActiveDroplets();
        }

        private void UpdateDropletsSubstep(float deltaTime)
        {
            if (ShouldUseCohesiveStreamPhysics())
            {
                ComputeCohesiveStreamForces(deltaTime);
            }

            float viscosityDamping = GetStreamViscosity() * viscosityDampingMultiplier;
            Vector3 gravityAcceleration = Vector3.down * GetGravity();

            for (int i = 0; i < _droplets.Length; i++)
            {
                PaintParticle droplet = _droplets[i];
                if (!droplet.active)
                {
                    continue;
                }

                droplet.age += deltaTime;
                if (droplet.age >= droplet.lifetime || droplet.wetness <= 0f)
                {
                    droplet.active = false;
                    _droplets[i] = droplet;
                    continue;
                }

                if (ShouldApplyStreamPhysics(droplet) && _sphVelocityDeltas != null)
                {
                    droplet.velocity += _sphVelocityDeltas[i];
                    droplet.density = _streamDensities != null ? _streamDensities[i] : 0f;
                    droplet.nearDensity = _streamNearDensities != null ? _streamNearDensities[i] : 0f;
                }

                droplet.velocity += gravityAcceleration * deltaTime;
                droplet.velocity *= Mathf.Max(0f, 1f - airResistance * deltaTime);
                droplet.velocity *= Mathf.Max(0f, 1f - viscosityDamping * deltaTime);

                float speed = droplet.velocity.magnitude;
                if (speed > maxFallingSpeed)
                {
                    droplet.velocity = droplet.velocity / speed * maxFallingSpeed;
                }

                droplet.previousPosition = droplet.position;
                droplet.position += droplet.velocity * deltaTime;
                droplet.wetness = Mathf.Clamp01(1f - droplet.age / droplet.lifetime);

                float contactRadius = GetCanvasContactRadius(droplet);
                float contactPredictionDistance = GetCanvasContactPredictionDistance(droplet.velocity, deltaTime);
                Vector3 contactEndPosition = droplet.position;

                if (contactPredictionDistance > 0f)
                {
                    contactEndPosition += droplet.velocity.normalized * contactPredictionDistance;
                }

                LastCanvasContactRadius = contactRadius;
                LastCanvasContactPredictionDistance = contactPredictionDistance;

                if (depositOnPaintSurface &&
                    paintSurface != null &&
                    paintSurface.TryDepositSegment(
                        droplet.previousPosition,
                        contactEndPosition,
                        droplet.radius,
                        contactRadius,
                        droplet.color,
                        droplet.wetness,
                        droplet.amount,
                        GetPaintViscosity(),
                        GetFlowRate()))
                {
                    DepositsThisTick++;
                    if (contactPredictionDistance > 0f)
                    {
                        PredictedCanvasContactsThisTick++;
                    }

                    droplet.active = false;
                    _droplets[i] = droplet;
                    continue;
                }

                _droplets[i] = droplet;
            }
        }

        private void ComputeCohesiveStreamForces(float deltaTime)
        {
            ResetStreamPhysicsMetrics();

            if (_droplets == null || _sphVelocityDeltas == null || deltaTime <= Mathf.Epsilon)
            {
                return;
            }

            System.Array.Clear(_sphVelocityDeltas, 0, _sphVelocityDeltas.Length);
            System.Array.Clear(_streamDensities, 0, _streamDensities.Length);
            System.Array.Clear(_streamNearDensities, 0, _streamNearDensities.Length);

            float smoothingRadius = GetStreamSmoothingRadius();
            BuildStreamSpatialGrid(smoothingRadius);

            if (_activeStreamIndices.Count < 2)
            {
                return;
            }

            MeasureStreamDensities(smoothingRadius);
            ApplyPairwiseStreamForces(smoothingRadius, deltaTime);
        }

        private void BuildStreamSpatialGrid(float cellSize)
        {
            _activeStreamIndices.Clear();
            _streamGrid.Clear();

            for (int i = 0; i < _droplets.Length; i++)
            {
                PaintParticle droplet = _droplets[i];
                if (!ShouldApplyStreamPhysics(droplet))
                {
                    continue;
                }

                _activeStreamIndices.Add(i);
                Vector3Int cell = WorldToStreamCell(droplet.position, cellSize);

                if (!_streamGrid.TryGetValue(cell, out List<int> indices))
                {
                    indices = new List<int>();
                    _streamGrid.Add(cell, indices);
                }

                indices.Add(i);
            }
        }

        private void MeasureStreamDensities(float smoothingRadius)
        {
            float invRadius = 1f / Mathf.Max(0.0001f, smoothingRadius);
            int neighborTotal = 0;
            float densityTotal = 0f;
            float maxDensity = 0f;

            for (int activeIndex = 0; activeIndex < _activeStreamIndices.Count; activeIndex++)
            {
                int particleIndex = _activeStreamIndices[activeIndex];
                PaintParticle particle = _droplets[particleIndex];
                Vector3Int centerCell = WorldToStreamCell(particle.position, smoothingRadius);
                float density = 0f;
                float nearDensity = 0f;
                int neighborCount = 0;

                for (int z = -1; z <= 1; z++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            Vector3Int cell = new Vector3Int(centerCell.x + x, centerCell.y + y, centerCell.z + z);
                            if (!_streamGrid.TryGetValue(cell, out List<int> indices))
                            {
                                continue;
                            }

                            for (int i = 0; i < indices.Count; i++)
                            {
                                int neighborIndex = indices[i];
                                if (neighborIndex == particleIndex)
                                {
                                    continue;
                                }

                                PaintParticle neighbor = _droplets[neighborIndex];
                                float distance = Vector3.Distance(particle.position, neighbor.position);
                                if (distance >= smoothingRadius)
                                {
                                    continue;
                                }

                                float q = 1f - distance * invRadius;
                                density += q * q;
                                nearDensity += q * q * q;
                                neighborCount++;
                            }
                        }
                    }
                }

                _streamDensities[particleIndex] = density;
                _streamNearDensities[particleIndex] = nearDensity;
                neighborTotal += neighborCount;
                densityTotal += density;
                maxDensity = Mathf.Max(maxDensity, density);
            }

            FallingStreamNeighborCount = neighborTotal;
            AverageStreamDensity = _activeStreamIndices.Count > 0 ? densityTotal / _activeStreamIndices.Count : 0f;
            MaxStreamDensity = maxDensity;
        }

        private void ApplyPairwiseStreamForces(float smoothingRadius, float deltaTime)
        {
            float invRadius = 1f / Mathf.Max(0.0001f, smoothingRadius);
            float viscosity = GetStreamViscosity();
            float cohesion = streamCohesion;
            float surfaceTension = streamSurfaceTension;

            for (int activeIndex = 0; activeIndex < _activeStreamIndices.Count; activeIndex++)
            {
                int particleIndex = _activeStreamIndices[activeIndex];
                PaintParticle particle = _droplets[particleIndex];
                Vector3Int centerCell = WorldToStreamCell(particle.position, smoothingRadius);

                for (int z = -1; z <= 1; z++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            Vector3Int cell = new Vector3Int(centerCell.x + x, centerCell.y + y, centerCell.z + z);
                            if (!_streamGrid.TryGetValue(cell, out List<int> indices))
                            {
                                continue;
                            }

                            for (int i = 0; i < indices.Count; i++)
                            {
                                int neighborIndex = indices[i];
                                if (neighborIndex <= particleIndex)
                                {
                                    continue;
                                }

                                PaintParticle neighbor = _droplets[neighborIndex];
                                Vector3 delta = neighbor.position - particle.position;
                                float distance = delta.magnitude;
                                if (distance >= smoothingRadius)
                                {
                                    continue;
                                }

                                Vector3 normal = distance > 0.00001f
                                    ? delta / distance
                                    : GetDeterministicJitter(particleIndex + neighborIndex, 1f).normalized;

                                if (normal.sqrMagnitude <= 0.000001f)
                                {
                                    normal = Vector3.up;
                                }

                                float q = 1f - distance * invRadius;
                                float averageDensity = Mathf.Max(
                                    0.0001f,
                                    _streamDensities[particleIndex] + _streamDensities[neighborIndex]);
                                float pressure = (streamPressureStiffness * q * q +
                                                  streamNearPressureStiffness * q * q * q) / averageDensity;
                                float pressureImpulse = pressure * deltaTime * 0.02f;
                                float cohesionImpulse = (cohesion * q + surfaceTension * q * q * 0.5f) * deltaTime;
                                Vector3 relativeVelocity = neighbor.velocity - particle.velocity;
                                Vector3 viscosityImpulse = relativeVelocity * (viscosity * q * deltaTime * 0.18f);

                                _sphVelocityDeltas[particleIndex] -= normal * pressureImpulse;
                                _sphVelocityDeltas[neighborIndex] += normal * pressureImpulse;
                                _sphVelocityDeltas[particleIndex] += normal * cohesionImpulse;
                                _sphVelocityDeltas[neighborIndex] -= normal * cohesionImpulse;
                                _sphVelocityDeltas[particleIndex] += viscosityImpulse;
                                _sphVelocityDeltas[neighborIndex] -= viscosityImpulse;
                            }
                        }
                    }
                }
            }

            float maxDelta = Mathf.Max(0.05f, maxFallingSpeed * 0.2f);
            for (int i = 0; i < _activeStreamIndices.Count; i++)
            {
                int index = _activeStreamIndices[i];
                float magnitude = _sphVelocityDeltas[index].magnitude;
                if (magnitude > maxDelta)
                {
                    _sphVelocityDeltas[index] = _sphVelocityDeltas[index] / magnitude * maxDelta;
                }
            }
        }

        private bool ShouldUseCohesiveStreamPhysics()
        {
            return fallingStreamPhysicsMode == FallingStreamPhysicsMode.CohesiveSPH;
        }

        private static bool ShouldApplyStreamPhysics(PaintParticle particle)
        {
            return particle.active && particle.streamId >= 0 && particle.wetness > 0f;
        }

        private void RecountActiveDroplets()
        {
            int activeCount = 0;

            for (int i = 0; i < _droplets.Length; i++)
            {
                if (_droplets[i].active)
                {
                    activeCount++;
                }
            }

            ActiveDropletCount = activeCount;
        }

        private void ResetStreamPhysicsMetrics()
        {
            FallingStreamNeighborCount = 0;
            AverageStreamDensity = 0f;
            MaxStreamDensity = 0f;
        }

        private void ResetCanvasContactMetrics()
        {
            LastCanvasContactRadius = 0f;
            LastCanvasContactPredictionDistance = 0f;
            PredictedCanvasContactsThisTick = 0;
            CanvasTextureDirtyBeforeRender = false;
        }

        private float GetCanvasContactRadius(PaintParticle droplet)
        {
            float contactRadius = Mathf.Max(0f, droplet.radius);

            if (useVisualStreamContactRadius && droplet.streamId >= 0)
            {
                float visualRadius = GetStreamVisualHalfWidth(droplet.radius) * streamContactRadiusMultiplier;
                contactRadius = Mathf.Max(contactRadius, visualRadius);
            }

            return contactRadius;
        }

        private float GetCanvasContactPredictionDistance(Vector3 velocity, float deltaTime)
        {
            float speed = velocity.magnitude;
            if (speed <= Mathf.Epsilon || deltaTime <= Mathf.Epsilon)
            {
                return 0f;
            }

            float predictedDistance = speed * deltaTime * contactPredictionFractionOfSubstep;
            return Mathf.Min(predictedDistance, maxContactPredictionDistance);
        }

        private int FindDropletSlot()
        {
            for (int i = 0; i < _droplets.Length; i++)
            {
                int index = (_nextDropletIndex + i) % _droplets.Length;
                if (!_droplets[index].active)
                {
                    _nextDropletIndex = (index + 1) % _droplets.Length;
                    return index;
                }
            }

            return -1;
        }

        private void DrawDroplets()
        {
            EnsureRenderResources();

            if (dropletMesh == null || dropletMaterial == null)
            {
                return;
            }

            if (!dropletMaterial.enableInstancing)
            {
                dropletMaterial.enableInstancing = true;
            }

            int batchCount = 0;
            Color batchColor = GetPaintColor();

            for (int i = 0; i < _droplets.Length; i++)
            {
                PaintParticle droplet = _droplets[i];
                if (!droplet.active)
                {
                    continue;
                }

                float diameter = droplet.radius * 2f;
                _matrices[batchCount] = Matrix4x4.TRS(droplet.position, Quaternion.identity, Vector3.one * diameter);
                batchColor = droplet.color;
                batchCount++;

                if (batchCount == InstancedBatchSize)
                {
                    DrawBatch(batchCount, batchColor);
                    batchCount = 0;
                }
            }

            if (batchCount > 0)
            {
                DrawBatch(batchCount, batchColor);
            }
        }

        public int CopyActiveStreamParticles(List<PaintParticle> particles)
        {
            if (particles == null)
            {
                return 0;
            }

            particles.Clear();

            if (_droplets == null)
            {
                return 0;
            }

            for (int i = 0; i < _droplets.Length; i++)
            {
                PaintParticle particle = _droplets[i];
                if (particle.active && particle.streamId >= 0 && particle.wetness > 0f)
                {
                    particles.Add(particle);
                }
            }

            return particles.Count;
        }

        private void DrawBatch(int count, Color color)
        {
            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            _propertyBlock.SetColor(ColorId, color);
            _propertyBlock.SetColor(BaseColorId, color);

            Graphics.DrawMeshInstanced(
                dropletMesh,
                0,
                dropletMaterial,
                _matrices,
                count,
                _propertyBlock,
                ShadowCastingMode.On,
                true,
                gameObject.layer
            );
        }

        private void ResolveReferences()
        {
            if (paintHoleTransform == null)
            {
                paintHoleTransform = transform;
            }

            if (bucketTransform == null)
            {
                BucketMotionProvider provider = GetComponentInParent<BucketMotionProvider>();
                bucketTransform = provider != null ? provider.transform : transform.parent;
            }

            if (fluidSettings == null)
            {
                fluidSettings = GetComponentInParent<BucketFluidSettings>();
            }

            if (motionProvider == null)
            {
                motionProvider = GetComponentInParent<BucketMotionProvider>();
            }

            if (boundary == null)
            {
                boundary = GetComponentInParent<BucketFluidBoundary>();
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

            if (streamRenderer == null)
            {
                streamRenderer = GetComponent<PaintStreamRenderer>();
            }
        }

        private void SubscribeToSimulationManager()
        {
            SimulationManager manager = SimulationManager.Instance;
            if (manager == null || manager == _subscribedSimulationManager)
            {
                return;
            }

            UnsubscribeFromSimulationManager();
            _subscribedSimulationManager = manager;
            _subscribedSimulationManager.OnReset += ResetEmitter;
        }

        private void UnsubscribeFromSimulationManager()
        {
            if (_subscribedSimulationManager == null)
            {
                return;
            }

            _subscribedSimulationManager.OnReset -= ResetEmitter;
            _subscribedSimulationManager = null;
        }

        private void EnsureStorage()
        {
            int safeMaxDroplets = Mathf.Max(1, maxDroplets);
            if (_droplets == null || _droplets.Length != safeMaxDroplets)
            {
                _droplets = new PaintParticle[safeMaxDroplets];
                ActiveDropletCount = 0;
                _nextDropletIndex = 0;
            }

            if (_matrices == null || _matrices.Length != InstancedBatchSize)
            {
                _matrices = new Matrix4x4[InstancedBatchSize];
            }

            if (_sphVelocityDeltas == null || _sphVelocityDeltas.Length != safeMaxDroplets)
            {
                _sphVelocityDeltas = new Vector3[safeMaxDroplets];
                _streamDensities = new float[safeMaxDroplets];
                _streamNearDensities = new float[safeMaxDroplets];
            }
        }

        private void EnsureRenderResources()
        {
            if (dropletMesh == null)
            {
                if (_runtimeDropletMesh == null)
                {
                    _runtimeDropletMesh = CreateDropletMesh();
                }

                dropletMesh = _runtimeDropletMesh;
            }

            if (dropletMaterial == null)
            {
                if (_runtimeDropletMaterial == null)
                {
                    Shader shader = Shader.Find("Standard");
                    if (shader != null)
                    {
                        _runtimeDropletMaterial = new Material(shader)
                        {
                            name = "Runtime Paint Droplet Material",
                            enableInstancing = true
                        };
                    }
                }

                dropletMaterial = _runtimeDropletMaterial;
            }

            if (streamRenderer == null)
            {
                streamRenderer = GetComponent<PaintStreamRenderer>();
            }

            if (streamRenderer == null)
            {
                streamRenderer = gameObject.AddComponent<PaintStreamRenderer>();
            }

            if (streamRenderer != null)
            {
                streamRenderer.emitter = this;
            }
        }

        private void DestroyRuntimeResources()
        {
            if (_runtimeDropletMesh != null)
            {
                Destroy(_runtimeDropletMesh);
                _runtimeDropletMesh = null;
            }

            if (_runtimeDropletMaterial != null)
            {
                Destroy(_runtimeDropletMaterial);
                _runtimeDropletMaterial = null;
            }

            if (streamRenderer != null)
            {
                streamRenderer.ClearStream();
            }
        }

        private void SampleOutletMotion(float deltaTime)
        {
            Vector3 outletPosition = GetEmissionPosition();

            if (!_hasOutletSample)
            {
                _previousOutletPosition = outletPosition;
                _currentOutletPosition = outletPosition;
                _previousOutletVelocity = GetBucketWorldVelocity();
                _currentOutletVelocity = _previousOutletVelocity;
                _hasOutletSample = true;
                return;
            }

            _previousOutletPosition = _currentOutletPosition;
            _previousOutletVelocity = _currentOutletVelocity;
            _currentOutletPosition = outletPosition;

            Vector3 sampledVelocity = deltaTime > Mathf.Epsilon
                ? (_currentOutletPosition - _previousOutletPosition) / deltaTime
                : Vector3.zero;

            _currentOutletVelocity = sampledVelocity.sqrMagnitude > 0.000001f
                ? sampledVelocity
                : GetBucketWorldVelocity();
        }

        private bool ShouldUseContinuousEmission()
        {
            return continuousStreamMode && GetFlowRate() >= minEmissionRateForStream;
        }

        private float GetStreamParticleAmount()
        {
            float baseAmount = Mathf.Max(0.0001f, dropletAmount);
            float referenceRadius = Mathf.Max(minDropletRadius, holeDiameter * 0.5f * 0.65f);
            float spacing = GetStreamParticleSpacing(referenceRadius);
            float referenceSpacing = Mathf.Max(0.0001f, referenceRadius * 6f);
            float spacingScale = Mathf.Clamp(spacing / referenceSpacing, 0.18f, 1f);
            return Mathf.Max(0.0001f, baseAmount * spacingScale);
        }

        private float GetStreamParticleSpacing(float particleRadius)
        {
            float spacing = streamParticleSpacing > 0f
                ? streamParticleSpacing
                : Mathf.Max(0.0001f, particleRadius * 1.5f);

            return Mathf.Max(minStreamParticleSpacing, spacing);
        }

        private static float GetSpawnPathT(int index, int count)
        {
            return Mathf.Clamp01((index + 0.5f) / Mathf.Max(1, count));
        }

        private Vector3 GetEmissionPosition()
        {
            return paintHoleTransform != null ? paintHoleTransform.position : transform.position;
        }

        private Vector3 GetEmissionDirection()
        {
            Transform source = bucketTransform != null ? bucketTransform : transform;
            Vector3 direction = source.TransformDirection(Vector3.down);
            return direction.sqrMagnitude > Mathf.Epsilon ? direction.normalized : Vector3.down;
        }

        private Vector3 GetEffectiveWorldAcceleration()
        {
            Vector3 gravityAcceleration = Vector3.down * GetGravity();
            Vector3 inertialAcceleration = motionProvider != null ? -motionProvider.WorldAcceleration : Vector3.zero;
            return gravityAcceleration + inertialAcceleration;
        }

        private Vector3 GetBucketWorldVelocity()
        {
            return motionProvider != null ? motionProvider.WorldVelocity : Vector3.zero;
        }

        private float GetGravity()
        {
            if (physicsSettings != null)
            {
                return physicsSettings.Gravity;
            }

            if (fluidSettings != null)
            {
                return fluidSettings.gravity;
            }

            return gravity;
        }

        private float GetFlowRate()
        {
            return physicsSettings != null ? physicsSettings.PaintFlowRate : defaultFlowRate;
        }

        private float GetPaintViscosity()
        {
            if (physicsSettings != null)
            {
                return physicsSettings.PaintViscosity;
            }

            if (fluidSettings != null)
            {
                return fluidSettings.viscosity;
            }

            return defaultPaintViscosity;
        }

        private float GetStreamSmoothingRadius()
        {
            float fallback = fluidSettings != null ? fluidSettings.smoothingRadius : 0.075f;
            float radius = streamSmoothingRadius > 0f ? streamSmoothingRadius : fallback;
            return Mathf.Max(minDropletRadius * 2f, radius);
        }

        private float GetStreamViscosity()
        {
            return Mathf.Max(0f, GetPaintViscosity());
        }

        private float GetInitialPaintQuantity()
        {
            return physicsSettings != null ? physicsSettings.PaintQuantity : defaultPaintQuantity;
        }

        private Color GetPaintColor()
        {
            if (physicsSettings != null)
            {
                return physicsSettings.PaintColor;
            }

            if (fluidSettings != null)
            {
                Color color = fluidSettings.paintColor;
                color.a = Mathf.Clamp01(fluidSettings.opacity);
                return color;
            }

            return new Color(0.05f, 0.22f, 0.95f, 0.92f);
        }

        private float CalculateDropletRadius(float amount)
        {
            float holeRadius = Mathf.Max(0f, holeDiameter) * 0.5f;
            float amountScale = Mathf.Pow(Mathf.Max(0.0001f, amount / Mathf.Max(0.0001f, dropletAmount)), 1f / 3f);
            return Mathf.Clamp(holeRadius * 0.65f * amountScale, minDropletRadius, maxDropletRadius);
        }

        private Vector3 GetDeterministicJitter(int index, float amplitude)
        {
            if (amplitude <= 0f)
            {
                return Vector3.zero;
            }

            float x = DeterministicSigned(index, 17);
            float y = DeterministicSigned(index, 31) * 0.35f;
            float z = DeterministicSigned(index, 47);
            return new Vector3(x, y, z) * amplitude;
        }

        private static float DeterministicSigned(int index, int salt)
        {
            unchecked
            {
                int seed = index * 73856093 ^ salt * 19349663;
                return Mathf.Repeat(Mathf.Sin(seed * 12.9898f) * 43758.5453f, 1f) * 2f - 1f;
            }
        }

        private static Vector3Int WorldToStreamCell(Vector3 position, float cellSize)
        {
            float safeCellSize = Mathf.Max(0.0001f, cellSize);
            return new Vector3Int(
                Mathf.FloorToInt(position.x / safeCellSize),
                Mathf.FloorToInt(position.y / safeCellSize),
                Mathf.FloorToInt(position.z / safeCellSize)
            );
        }

        private static Mesh CreateDropletMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "Runtime Paint Droplet Octahedron"
            };

            mesh.vertices = new[]
            {
                new Vector3(0f, 0.5f, 0f),
                new Vector3(0.5f, 0f, 0f),
                new Vector3(0f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(0f, 0f, -0.5f),
                new Vector3(0f, -0.5f, 0f)
            };

            mesh.triangles = new[]
            {
                0, 1, 2,
                0, 2, 3,
                0, 3, 4,
                0, 4, 1,
                5, 2, 1,
                5, 3, 2,
                5, 4, 3,
                5, 1, 4
            };

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnValidate()
        {
            holeDiameter = Mathf.Max(0f, holeDiameter);
            defaultFlowRate = Mathf.Max(0f, defaultFlowRate);
            defaultPaintViscosity = Mathf.Max(0f, defaultPaintViscosity);
            defaultPaintQuantity = Mathf.Max(0f, defaultPaintQuantity);
            paintMassPerQuantityUnit = Mathf.Max(0f, paintMassPerQuantityUnit);
            dropletAmount = Mathf.Max(0.0001f, dropletAmount);
            minDropletRadius = Mathf.Max(0.0001f, minDropletRadius);
            maxDropletRadius = Mathf.Max(minDropletRadius, maxDropletRadius);
            exitSpeedMultiplier = Mathf.Max(0f, exitSpeedMultiplier);
            maxDroplets = Mathf.Max(1, maxDroplets);
            dropletLifetime = Mathf.Max(0.1f, dropletLifetime);
            streamParticleSpacing = Mathf.Max(0f, streamParticleSpacing);
            minStreamParticleSpacing = Mathf.Max(0.0001f, minStreamParticleSpacing);
            maxParticlesPerTick = Mathf.Max(1, maxParticlesPerTick);
            minEmissionRateForStream = Mathf.Max(0f, minEmissionRateForStream);
            streamRadiusMultiplier = Mathf.Max(0.01f, streamRadiusMultiplier);
            streamVelocityJitter = Mathf.Clamp(streamVelocityJitter, 0f, 0.25f);
            streamBreakDistance = Mathf.Max(0f, streamBreakDistance);
            fallingSubsteps = Mathf.Max(1, fallingSubsteps);
            streamSmoothingRadius = Mathf.Max(0.0001f, streamSmoothingRadius);
            streamPressureStiffness = Mathf.Max(0f, streamPressureStiffness);
            streamNearPressureStiffness = Mathf.Max(0f, streamNearPressureStiffness);
            streamCohesion = Mathf.Max(0f, streamCohesion);
            streamSurfaceTension = Mathf.Max(0f, streamSurfaceTension);
            maxFallingSpeed = Mathf.Max(0.1f, maxFallingSpeed);
            streamContactRadiusMultiplier = Mathf.Max(0f, streamContactRadiusMultiplier);
            contactPredictionFractionOfSubstep = Mathf.Clamp(contactPredictionFractionOfSubstep, 0f, 2f);
            maxContactPredictionDistance = Mathf.Max(0f, maxContactPredictionDistance);
            gravity = Mathf.Max(0f, gravity);
            airResistance = Mathf.Max(0f, airResistance);
            viscosityDampingMultiplier = Mathf.Max(0f, viscosityDampingMultiplier);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 origin = GetEmissionPosition();
            Vector3 direction = GetEmissionDirection();
            Gizmos.color = GetPaintColor();
            Gizmos.DrawWireSphere(origin, Mathf.Max(0.005f, holeDiameter * 0.5f));
            Gizmos.DrawLine(origin, origin + direction * 0.35f);
        }
    }
}

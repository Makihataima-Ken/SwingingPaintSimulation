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

        private struct PaintDroplet
        {
            public Vector3 position;
            public Vector3 velocity;
            public float radius;
            public Color color;
            public float wetness;
            public float amount;
            public float lifetime;
            public float age;
            public bool active;
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

        [Header("Manual Falling Motion")]
        [Min(0f)]
        public float gravity = 9.81f;
        [Min(0f)]
        public float airResistance = 0.08f;
        [Min(0f)]
        public float viscosityDampingMultiplier = 0.35f;

        [Header("Rendering")]
        public bool drawDroplets = true;
        public Mesh dropletMesh;
        public Material dropletMaterial;
        public bool depositOnPaintSurface = true;

        public float RemainingPaintQuantity { get; private set; }
        public float EmittedPaintQuantity { get; private set; }
        public int ActiveDropletCount { get; private set; }
        public bool CanEmit => emitWhilePlaying && RemainingPaintQuantity > 0f && GetFlowRate() > 0f && holeDiameter > 0f;

        private PaintDroplet[] _droplets;
        private Matrix4x4[] _matrices;
        private MaterialPropertyBlock _propertyBlock;
        private Mesh _runtimeDropletMesh;
        private Material _runtimeDropletMaterial;
        private float _emissionAccumulator;
        private int _nextDropletIndex;
        private int _spawnSerial;
        private SimulationManager _subscribedSimulationManager;

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

            if (SimulationManager.Instance != null && SimulationManager.Instance.IsPaused)
            {
                return;
            }

            Step(Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            if (!drawDroplets || ActiveDropletCount <= 0)
            {
                return;
            }

            DrawDroplets();
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
        }

        public void Step(float deltaTime)
        {
            if (deltaTime <= Mathf.Epsilon)
            {
                return;
            }

            EnsureStorage();

            if (CanEmit && ActiveDropletCount < maxDroplets)
            {
                EmitPaint(deltaTime);
            }

            UpdateDroplets(deltaTime);
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

            float safeDropletAmount = Mathf.Max(0.0001f, dropletAmount);
            int spawnCount = Mathf.FloorToInt(_emissionAccumulator / safeDropletAmount);

            if (spawnCount <= 0)
            {
                return;
            }

            spawnCount = Mathf.Min(spawnCount, maxDroplets - ActiveDropletCount);
            const int maxSpawnPerStep = 128;
            spawnCount = Mathf.Min(spawnCount, maxSpawnPerStep);

            for (int i = 0; i < spawnCount; i++)
            {
                SpawnDroplet(safeDropletAmount);
                _emissionAccumulator -= safeDropletAmount;
            }
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

        private void SpawnDroplet(float amount)
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

            PaintDroplet droplet = new PaintDroplet
            {
                position = GetEmissionPosition(),
                velocity = GetBucketWorldVelocity() + emissionDirection * exitSpeed + GetDeterministicJitter(_spawnSerial, emissionJitter),
                radius = CalculateDropletRadius(amount),
                color = GetPaintColor(),
                wetness = 1f,
                amount = amount,
                lifetime = dropletLifetime,
                age = 0f,
                active = true
            };

            _droplets[index] = droplet;
            ActiveDropletCount++;
            _spawnSerial++;
        }

        private void UpdateDroplets(float deltaTime)
        {
            int activeCount = 0;
            float viscosityDamping = GetPaintViscosity() * viscosityDampingMultiplier;
            Vector3 gravityAcceleration = Vector3.down * GetGravity();

            for (int i = 0; i < _droplets.Length; i++)
            {
                PaintDroplet droplet = _droplets[i];
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

                droplet.velocity += gravityAcceleration * deltaTime;
                droplet.velocity *= Mathf.Max(0f, 1f - airResistance * deltaTime);
                droplet.velocity *= Mathf.Max(0f, 1f - viscosityDamping * deltaTime);
                Vector3 previousPosition = droplet.position;
                droplet.position += droplet.velocity * deltaTime;
                droplet.wetness = Mathf.Clamp01(1f - droplet.age / droplet.lifetime);

                if (depositOnPaintSurface &&
                    paintSurface != null &&
                    paintSurface.TryDepositSegment(
                        previousPosition,
                        droplet.position,
                        droplet.radius,
                        droplet.color,
                        droplet.wetness,
                        droplet.amount,
                        GetPaintViscosity(),
                        GetFlowRate()))
                {
                    droplet.active = false;
                    _droplets[i] = droplet;
                    continue;
                }

                _droplets[i] = droplet;
                activeCount++;
            }

            ActiveDropletCount = activeCount;
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
                PaintDroplet droplet = _droplets[i];
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
                _droplets = new PaintDroplet[safeMaxDroplets];
                ActiveDropletCount = 0;
                _nextDropletIndex = 0;
            }

            if (_matrices == null || _matrices.Length != InstancedBatchSize)
            {
                _matrices = new Matrix4x4[InstancedBatchSize];
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

        private float GetInitialPaintQuantity()
        {
            return physicsSettings != null ? physicsSettings.PaintQuantity : defaultPaintQuantity;
        }

        private Color GetPaintColor()
        {
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

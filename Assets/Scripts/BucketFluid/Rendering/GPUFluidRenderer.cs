using System.Collections.Generic;
using SwingingPaint.BucketFluid.Core;
using SwingingPaint.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwingingPaint.BucketFluid.Rendering
{
    /// <summary>
    /// Renders the initialized bucket-local fluid particles with one indirect instanced draw.
    ///
    /// This component reads GPUFluidSimulator.ParticleBuffer and sends the BucketRig local-to-world
    /// matrix to the shader. It does not create one GameObject per particle and does not move or
    /// simulate particles.
    /// </summary>
    public class GPUFluidRenderer : MonoBehaviour
    {
        private static readonly int ParticlesId = Shader.PropertyToID("_Particles");
        private static readonly int BucketLocalToWorldId = Shader.PropertyToID("_BucketLocalToWorld");
        private static readonly int ParticleSizeId = Shader.PropertyToID("_ParticleSize");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CameraRightId = Shader.PropertyToID("_CameraRight");
        private static readonly int CameraUpId = Shader.PropertyToID("_CameraUp");

        [Header("References")]
        public GPUFluidSimulator simulator;
        public Mesh particleMesh;
        public Material particleMaterial;

        [Header("Rendering")]
        [Tooltip("Local-space render bounds relative to BucketRig. The renderer converts this to world space each frame.")]
        public Bounds renderBounds = new Bounds(new Vector3(0f, -0.35f, 0f), Vector3.one * 2f);

        [Tooltip("Turns particle rendering on or off without releasing the simulator buffer.")]
        public bool renderEnabled = true;

        [Tooltip("Fallback visual particle size when BucketFluidSettings is not available.")]
        public float renderRadius = 0.055f;

        public bool HasRenderableBuffers =>
            simulator != null &&
            simulator.ParticleBufferValid &&
            particleMesh != null &&
            particleMaterial != null;

        public bool SimulatorAssigned => simulator != null;
        public bool ParticleBufferValid => simulator != null && simulator.ParticleBufferValid;
        public bool ParticleMeshAssigned => particleMesh != null;
        public bool ParticleMaterialAssigned => particleMaterial != null;
        public int RenderedInstanceCount { get; private set; }
        public uint IndirectArgsInstanceCount => _args[1];

        private ComputeBuffer _argsBuffer;
        private readonly uint[] _args = new uint[5];
        private MaterialPropertyBlock _propertyBlock;
        private int _lastInstanceCount = -1;
        private Mesh _runtimeFallbackMesh;
        private bool _hasWarnedMissingReferences;
        private bool _hasLoggedPlayDiagnostics;

        private void Awake()
        {
            ResolveReferences();
            EnsureRuntimeFallbacks();
        }

        private void Reset()
        {
            ResolveReferences();
            EnsureRuntimeFallbacks();
        }

        private void LateUpdate()
        {
            if (simulator == null)
            {
                ResolveReferences();
            }

            EnsureRuntimeFallbacks();

            if (!renderEnabled)
            {
                return;
            }

            if (!ValidateRenderSetup())
            {
                return;
            }

            UpdateArgsBuffer();
            UpdatePropertyBlock();
            LogPlayDiagnosticsOnce();

            Graphics.DrawMeshInstancedIndirect(
                particleMesh,
                0,
                particleMaterial,
                GetWorldRenderBounds(),
                _argsBuffer,
                0,
                _propertyBlock,
                ShadowCastingMode.Off,
                false,
                gameObject.layer
            );
        }

        private void OnDisable()
        {
            ReleaseArgsBuffer();
        }

        private void OnDestroy()
        {
            ReleaseArgsBuffer();
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
        }

        private void EnsureRuntimeFallbacks()
        {
            if (particleMesh == null)
            {
                if (_runtimeFallbackMesh == null)
                {
                    _runtimeFallbackMesh = CreateQuadMesh();
                }

                particleMesh = _runtimeFallbackMesh;
            }
        }

        private bool ValidateRenderSetup()
        {
            List<string> missing = new List<string>();

            if (simulator == null)
            {
                missing.Add("Missing simulator");
            }

            if (simulator != null && simulator.ParticleBuffer == null)
            {
                missing.Add("Missing particle buffer");
            }

            if (particleMesh == null)
            {
                missing.Add("Missing particle mesh");
            }

            if (particleMaterial == null)
            {
                missing.Add("Missing particle material");
            }
            else if (!particleMaterial.enableInstancing)
            {
                particleMaterial.enableInstancing = true;
            }

            if (missing.Count == 0)
            {
                _hasWarnedMissingReferences = false;
                return true;
            }

            if (!_hasWarnedMissingReferences)
            {
                _hasWarnedMissingReferences = true;
                Debug.LogWarning("GPUFluidRenderer setup is incomplete:\n- " + string.Join("\n- ", missing), this);
            }

            return false;
        }

        private void UpdateArgsBuffer()
        {
            int instanceCount = Mathf.Max(0, simulator.RuntimeActiveParticleCount);

            if (_argsBuffer == null)
            {
                _argsBuffer = new ComputeBuffer(1, _args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            }

            if (_lastInstanceCount == instanceCount)
            {
                RenderedInstanceCount = instanceCount;
                return;
            }

            _args[0] = particleMesh != null ? particleMesh.GetIndexCount(0) : 0;
            _args[1] = (uint)instanceCount;
            _args[2] = particleMesh != null ? particleMesh.GetIndexStart(0) : 0;
            _args[3] = particleMesh != null ? particleMesh.GetBaseVertex(0) : 0;
            _args[4] = 0;
            _argsBuffer.SetData(_args);
            _lastInstanceCount = instanceCount;
            RenderedInstanceCount = instanceCount;
        }

        private void LogPlayDiagnosticsOnce()
        {
            if (!Application.isPlaying ||
                _hasLoggedPlayDiagnostics ||
                simulator == null ||
                simulator.settings == null ||
                !simulator.settings.enableDebug)
            {
                return;
            }

            _hasLoggedPlayDiagnostics = true;
            Debug.Log(
                "GPUFluidRenderer diagnostics:\n" +
                $"simulator assigned: {SimulatorAssigned}\n" +
                $"particle buffer valid: {ParticleBufferValid}\n" +
                $"particle mesh assigned: {ParticleMeshAssigned}\n" +
                $"particle material assigned: {ParticleMaterialAssigned}\n" +
                $"active particle count used for rendering: {RenderedInstanceCount}\n" +
                $"indirect args instance count: {IndirectArgsInstanceCount}\n" +
                $"simulator initialized/active/target: {(simulator != null ? simulator.InitializedParticleCount : 0)} / " +
                $"{(simulator != null ? simulator.RuntimeActiveParticleCount : 0)} / {(simulator != null ? simulator.TargetParticleCount : 0)}",
                this
            );
        }

        private void UpdatePropertyBlock()
        {
            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            _propertyBlock.SetBuffer(ParticlesId, simulator.ParticleBuffer);
            _propertyBlock.SetMatrix(BucketLocalToWorldId, simulator.BucketLocalToWorldMatrix);
            _propertyBlock.SetFloat(ParticleSizeId, GetParticleVisualSize());
            _propertyBlock.SetColor(BaseColorId, GetParticleColor());

            Camera camera = Camera.main;
            _propertyBlock.SetVector(CameraRightId, camera != null ? camera.transform.right : Vector3.right);
            _propertyBlock.SetVector(CameraUpId, camera != null ? camera.transform.up : Vector3.up);
        }

        private Bounds GetWorldRenderBounds()
        {
            Matrix4x4 localToWorld = simulator != null ? simulator.BucketLocalToWorldMatrix : transform.localToWorldMatrix;
            Vector3 worldCenter = localToWorld.MultiplyPoint3x4(renderBounds.center);
            Vector3 scale = localToWorld.lossyScale;
            Vector3 worldSize = Vector3.Scale(renderBounds.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            worldSize = Vector3.Max(worldSize, Vector3.one * 0.01f);
            return new Bounds(worldCenter, worldSize);
        }

        private float GetParticleVisualSize()
        {
            if (simulator != null && simulator.settings != null)
            {
                return simulator.settings.particleVisualSize;
            }

            return renderRadius;
        }

        private Color GetParticleColor()
        {
            if (SimulationManager.Instance != null && SimulationManager.Instance.physicsSettings != null)
            {
                return SimulationManager.Instance.physicsSettings.PaintColor;
            }

            if (simulator != null && simulator.settings != null)
            {
                Color color = simulator.settings.paintColor;
                color.a = simulator.settings.opacity;
                return color;
            }

            return new Color(0.05f, 0.22f, 0.95f, 0.92f);
        }

        private void ReleaseArgsBuffer()
        {
            if (_argsBuffer != null)
            {
                _argsBuffer.Release();
                _argsBuffer = null;
            }

            _lastInstanceCount = -1;
            RenderedInstanceCount = 0;
            _args[1] = 0;
        }

        private void OnValidate()
        {
            renderRadius = Mathf.Max(0.001f, renderRadius);

            Vector3 size = renderBounds.size;
            size.x = Mathf.Max(0.01f, size.x);
            size.y = Mathf.Max(0.01f, size.y);
            size.z = Mathf.Max(0.01f, size.z);
            renderBounds.size = size;
        }

        private static Mesh CreateQuadMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "Runtime Bucket Fluid Particle Quad"
            };

            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

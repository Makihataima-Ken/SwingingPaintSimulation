using System.Collections.Generic;
using SwingingPaint.BucketFluid.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwingingPaint.BucketFluid.Rendering
{
    /// <summary>
    /// Indirect renderer for GPU outflow particles. Visual only; simulation and deposition stay on GPU buffers.
    /// </summary>
    public class GPUOutflowRenderer : MonoBehaviour
    {
        private static readonly int OutflowParticlesId = Shader.PropertyToID("_OutflowParticles");
        private static readonly int ParticleSizeId = Shader.PropertyToID("_ParticleSize");
        private static readonly int StreamRadiusMultiplierId = Shader.PropertyToID("_StreamRadiusMultiplier");
        private static readonly int CameraRightId = Shader.PropertyToID("_CameraRight");
        private static readonly int CameraUpId = Shader.PropertyToID("_CameraUp");
        private static readonly int CameraForwardId = Shader.PropertyToID("_CameraForward");

        [Header("References")]
        public GPUFluidOutflowController outflowController;
        public Mesh particleMesh;
        public Material particleMaterial;
        public Material connectorMaterial;

        [Header("Rendering")]
        public bool renderEnabled = true;
        public bool renderConnectors = true;
        public Bounds renderBounds = new Bounds(Vector3.zero, Vector3.one * 12f);
        [Min(0.001f)]
        public float fallbackParticleSize = 0.018f;
        [Min(0.1f)]
        public float particleDiameterMultiplier = 1.35f;

        public int RenderedInstanceCapacity { get; private set; }

        private Mesh _runtimeFallbackMesh;
        private Material _runtimeFallbackMaterial;
        private Material _runtimeConnectorMaterial;
        private MaterialPropertyBlock _propertyBlock;
        private bool _hasWarnedMissingReferences;

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
            ResolveReferences();
            EnsureRuntimeFallbacks();

            if (!renderEnabled || !ValidateRenderSetup())
            {
                return;
            }

            outflowController.SetRenderMeshArgs(particleMesh);

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            Camera camera = Camera.main;
            Vector3 cameraRight = camera != null ? camera.transform.right : Vector3.right;
            Vector3 cameraUp = camera != null ? camera.transform.up : Vector3.up;
            Vector3 cameraForward = camera != null ? camera.transform.forward : Vector3.forward;

            _propertyBlock.SetBuffer(OutflowParticlesId, outflowController.OutflowParticleBuffer);
            _propertyBlock.SetFloat(ParticleSizeId, GetParticleSize());
            _propertyBlock.SetFloat(StreamRadiusMultiplierId, GetStreamRadiusMultiplier());
            _propertyBlock.SetVector(CameraRightId, cameraRight);
            _propertyBlock.SetVector(CameraUpId, cameraUp);
            _propertyBlock.SetVector(CameraForwardId, cameraForward);

            if (renderConnectors && connectorMaterial != null)
            {
                Graphics.DrawMeshInstancedIndirect(
                    particleMesh,
                    0,
                    connectorMaterial,
                    renderBounds,
                    outflowController.IndirectArgsBuffer,
                    0,
                    _propertyBlock,
                    ShadowCastingMode.Off,
                    false,
                    gameObject.layer);
            }

            Graphics.DrawMeshInstancedIndirect(
                particleMesh,
                0,
                particleMaterial,
                renderBounds,
                outflowController.IndirectArgsBuffer,
                0,
                _propertyBlock,
                ShadowCastingMode.Off,
                false,
                gameObject.layer);

            RenderedInstanceCapacity = outflowController.OutflowCapacity;
        }

        private void OnDisable()
        {
            RenderedInstanceCapacity = 0;
        }

        private void OnDestroy()
        {
            if (_runtimeFallbackMesh != null)
            {
                Destroy(_runtimeFallbackMesh);
                _runtimeFallbackMesh = null;
            }

            if (_runtimeFallbackMaterial != null)
            {
                Destroy(_runtimeFallbackMaterial);
                _runtimeFallbackMaterial = null;
            }

            if (_runtimeConnectorMaterial != null)
            {
                Destroy(_runtimeConnectorMaterial);
                _runtimeConnectorMaterial = null;
            }
        }

        private void ResolveReferences()
        {
            if (outflowController == null)
            {
                outflowController = GetComponent<GPUFluidOutflowController>();
            }

            if (outflowController == null)
            {
                outflowController = GetComponentInParent<GPUFluidOutflowController>();
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

            if (particleMaterial == null)
            {
                if (_runtimeFallbackMaterial == null)
                {
                    Shader shader = Shader.Find("SwingingPaint/BucketFluid/GPUOutflowParticleInstanced");
                    if (shader != null)
                    {
                        _runtimeFallbackMaterial = new Material(shader)
                        {
                            name = "Runtime GPU Outflow Material",
                            enableInstancing = true
                        };
                    }
                }

                particleMaterial = _runtimeFallbackMaterial;
            }

            if (connectorMaterial == null)
            {
                if (_runtimeConnectorMaterial == null)
                {
                    Shader shader = Shader.Find("SwingingPaint/BucketFluid/GPUOutflowStreamConnector");
                    if (shader != null)
                    {
                        _runtimeConnectorMaterial = new Material(shader)
                        {
                            name = "Runtime GPU Outflow Connector Material",
                            enableInstancing = true
                        };
                    }
                }

                connectorMaterial = _runtimeConnectorMaterial;
            }
        }

        private bool ValidateRenderSetup()
        {
            List<string> missing = new List<string>();

            if (outflowController == null)
            {
                missing.Add("Missing GPU outflow controller");
            }
            else
            {
                if (!outflowController.gpuOutflowEnabled)
                {
                    return false;
                }

                if (outflowController.OutflowParticleBuffer == null)
                {
                    missing.Add("Missing outflow particle buffer");
                }

                if (outflowController.IndirectArgsBuffer == null)
                {
                    missing.Add("Missing indirect args buffer");
                }
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

            if (renderConnectors)
            {
                if (connectorMaterial == null)
                {
                    missing.Add("Missing connector material");
                }
                else if (!connectorMaterial.enableInstancing)
                {
                    connectorMaterial.enableInstancing = true;
                }
            }

            if (missing.Count == 0)
            {
                _hasWarnedMissingReferences = false;
                return true;
            }

            if (!_hasWarnedMissingReferences)
            {
                _hasWarnedMissingReferences = true;
                Debug.LogWarning("GPUOutflowRenderer setup is incomplete:\n- " + string.Join("\n- ", missing), this);
            }

            return false;
        }

        private float GetParticleSize()
        {
            if (outflowController != null)
            {
                return Mathf.Max(0.003f, outflowController.EffectiveOutflowParticleRadius * 2f * particleDiameterMultiplier);
            }

            return fallbackParticleSize;
        }

        private float GetStreamRadiusMultiplier()
        {
            if (outflowController != null)
            {
                return outflowController.streamRadiusMultiplier;
            }

            return 1.7f;
        }

        private static Mesh CreateQuadMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "Runtime GPU Outflow Quad"
            };

            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };

            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnValidate()
        {
            fallbackParticleSize = Mathf.Max(0.001f, fallbackParticleSize);
            Vector3 size = renderBounds.size;
            size.x = Mathf.Max(0.01f, size.x);
            size.y = Mathf.Max(0.01f, size.y);
            size.z = Mathf.Max(0.01f, size.z);
            renderBounds.size = size;
            fallbackParticleSize = Mathf.Max(0.001f, fallbackParticleSize);
            particleDiameterMultiplier = Mathf.Max(0.1f, particleDiameterMultiplier);
        }
    }
}

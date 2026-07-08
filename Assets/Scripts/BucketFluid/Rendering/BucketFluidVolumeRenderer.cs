using System.Collections.Generic;
using SwingingPaint.BucketFluid.Core;
using SwingingPaint.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwingingPaint.BucketFluid.Rendering
{
    /// <summary>
    /// Presentation renderer for the paint volume inside the bucket.
    /// The GPU particle solver still owns simulation/outflow; this component draws a continuous
    /// bucket-local liquid body so the in-bucket paint does not read as separated particle bands.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class BucketFluidVolumeRenderer : MonoBehaviour
    {
        private const string VolumeShaderName = "SwingingPaint/BucketFluid/BucketFluidVolume";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TopColorId = Shader.PropertyToID("_TopColor");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        [Header("References")]
        public BucketFluidBoundary boundary;
        public BucketFluidSettings settings;
        public GPUFluidOutflowController outflowController;
        public GPUFluidRenderer particleRenderer;
        public Material volumeMaterial;

        [Header("Rendering")]
        public bool renderEnabled = true;
        public bool disableParticleCloudInPresentation = true;
        [Min(12)]
        public int segments = 64;
        [Min(2)]
        public int verticalRings = 8;
        [Min(1)]
        public int capRings = 6;
        [Min(0f)]
        public float wallInset = 0.012f;
        [Min(0f)]
        public float bottomInset = 0.01f;
        [Range(0f, 0.25f)]
        public float minVisibleFillFraction = 0.015f;
        [Min(0f)]
        public float fillSmoothing = 12f;

        public float CurrentFillFraction { get; private set; }
        public float CurrentFillLocalY { get; private set; }
        public bool MeshValid => _mesh != null && _mesh.vertexCount > 0;

        private Mesh _mesh;
        private Material _runtimeMaterial;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private MaterialPropertyBlock _propertyBlock;
        private readonly List<Vector3> _vertices = new List<Vector3>(1024);
        private readonly List<int> _triangles = new List<int>(4096);
        private readonly List<Color> _colors = new List<Color>(1024);
        private float _lastMeshFillFraction = -1f;
        private int _lastSegments;
        private int _lastVerticalRings;
        private int _lastCapRings;
        private float _lastWallInset;
        private float _lastBottomInset;
        private float _lastBoundaryBottomY;
        private float _lastBoundaryTopY;
        private float _lastBoundaryBottomRadius;
        private float _lastBoundaryTopRadius;
        private Vector3 _lastBoundaryCenterOffset;

        private void Awake()
        {
            ResolveReferences();
            EnsureMesh();
            ApplyPresentationDefaults();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureMesh();
            ApplyPresentationDefaults();
            ForceRebuild();
        }

        private void LateUpdate()
        {
            ResolveReferences();
            EnsureMesh();
            ApplyPresentationDefaults();
            UpdateFill(Time.deltaTime);
            UpdateMaterialProperties();
            UpdateRendererState();
        }

        private void OnDisable()
        {
            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_mesh);
                }
                else
                {
                    DestroyImmediate(_mesh);
                }

                _mesh = null;
            }

            if (_runtimeMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_runtimeMaterial);
                }
                else
                {
                    DestroyImmediate(_runtimeMaterial);
                }

                _runtimeMaterial = null;
            }
        }

        [ContextMenu("Rebuild Fluid Volume Mesh")]
        public void ForceRebuild()
        {
            _lastMeshFillFraction = -1f;
            UpdateFill(0f);
        }

        private void ResolveReferences()
        {
            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
            }

            if (_meshRenderer == null)
            {
                _meshRenderer = GetComponent<MeshRenderer>();
            }

            if (boundary == null)
            {
                boundary = GetComponentInParent<BucketFluidBoundary>();
            }

            if (settings == null)
            {
                settings = GetComponentInParent<BucketFluidSettings>();
            }

            if (outflowController == null)
            {
                outflowController = GetComponentInParent<GPUFluidOutflowController>();
            }

            if (outflowController == null && boundary != null)
            {
                outflowController = boundary.GetComponentInChildren<GPUFluidOutflowController>();
            }

            if (particleRenderer == null)
            {
                particleRenderer = GetComponentInParent<GPUFluidRenderer>();
            }

            if (particleRenderer == null && boundary != null)
            {
                particleRenderer = boundary.GetComponentInChildren<GPUFluidRenderer>();
            }
        }

        private void EnsureMesh()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh
                {
                    name = "Runtime Bucket Fluid Volume Mesh"
                };
                _mesh.MarkDynamic();
            }

            if (_meshFilter != null && _meshFilter.sharedMesh != _mesh)
            {
                _meshFilter.sharedMesh = _mesh;
            }

            Material activeMaterial = GetOrCreateMaterial();
            if (_meshRenderer != null && activeMaterial != null && _meshRenderer.sharedMaterial != activeMaterial)
            {
                _meshRenderer.sharedMaterial = activeMaterial;
            }
        }

        private Material GetOrCreateMaterial()
        {
            if (volumeMaterial != null)
            {
                return volumeMaterial;
            }

            if (_runtimeMaterial != null)
            {
                return _runtimeMaterial;
            }

            Shader shader = Shader.Find(VolumeShaderName);
            if (shader == null)
            {
                return null;
            }

            _runtimeMaterial = new Material(shader)
            {
                name = "Runtime Bucket Fluid Volume Material",
                renderQueue = 2995
            };
            return _runtimeMaterial;
        }

        private void ApplyPresentationDefaults()
        {
            if (!renderEnabled || !disableParticleCloudInPresentation || particleRenderer == null || !particleRenderer.renderEnabled)
            {
                return;
            }

            particleRenderer.renderEnabled = false;
        }

        private void UpdateFill(float deltaTime)
        {
            float targetFill = GetTargetFillFraction();
            if (!Application.isPlaying || deltaTime <= 0f || fillSmoothing <= 0f)
            {
                CurrentFillFraction = targetFill;
            }
            else
            {
                float blend = 1f - Mathf.Exp(-fillSmoothing * deltaTime);
                CurrentFillFraction = Mathf.Lerp(CurrentFillFraction, targetFill, blend);
            }

            if (CurrentFillFraction > 0f && CurrentFillFraction < minVisibleFillFraction)
            {
                CurrentFillFraction = minVisibleFillFraction;
            }

            CurrentFillFraction = Mathf.Clamp01(CurrentFillFraction);

            if (NeedsMeshRebuild())
            {
                RebuildMesh();
            }
        }

        private float GetTargetFillFraction()
        {
            float fill = settings != null ? settings.fillHeightPercent : 0.85f;

            if (outflowController != null && !outflowController.infinitePaintSupplyForTuning)
            {
                fill *= outflowController.RemainingPaintFraction;
            }

            if (fill <= minVisibleFillFraction)
            {
                return 0f;
            }

            return Mathf.Clamp01(fill);
        }

        private bool NeedsMeshRebuild()
        {
            if (boundary == null || _mesh == null)
            {
                return false;
            }

            if (Mathf.Abs(CurrentFillFraction - _lastMeshFillFraction) > 0.0025f)
            {
                return true;
            }

            return _lastSegments != Mathf.Max(12, segments) ||
                   _lastVerticalRings != Mathf.Max(2, verticalRings) ||
                   _lastCapRings != Mathf.Max(1, capRings) ||
                   !Mathf.Approximately(_lastWallInset, wallInset) ||
                   !Mathf.Approximately(_lastBottomInset, bottomInset) ||
                   !Mathf.Approximately(_lastBoundaryBottomY, boundary.bottomY) ||
                   !Mathf.Approximately(_lastBoundaryTopY, boundary.topY) ||
                   !Mathf.Approximately(_lastBoundaryBottomRadius, boundary.bottomRadius) ||
                   !Mathf.Approximately(_lastBoundaryTopRadius, boundary.topRadius) ||
                   _lastBoundaryCenterOffset != boundary.boundaryLocalCenterOffset;
        }

        private void RebuildMesh()
        {
            _vertices.Clear();
            _triangles.Clear();
            _colors.Clear();

            if (boundary == null || CurrentFillFraction <= 0f)
            {
                CurrentFillLocalY = boundary != null
                    ? boundary.boundaryLocalCenterOffset.y + boundary.bottomY
                    : 0f;
                _mesh.Clear();
                CacheRebuildState();
                return;
            }

            int safeSegments = Mathf.Max(12, segments);
            int safeVerticalRings = Mathf.Max(2, verticalRings);
            int safeCapRings = Mathf.Max(1, capRings);
            float bottomY = boundary.boundaryLocalCenterOffset.y + boundary.bottomY + Mathf.Max(0f, bottomInset);
            float topY = boundary.boundaryLocalCenterOffset.y + boundary.topY - Mathf.Max(0f, bottomInset * 0.35f);
            if (topY <= bottomY)
            {
                topY = bottomY + 0.001f;
            }

            CurrentFillLocalY = Mathf.Lerp(bottomY, topY, CurrentFillFraction);
            BuildSideRings(safeSegments, safeVerticalRings, bottomY, CurrentFillLocalY);
            BuildCap(safeSegments, safeCapRings, bottomY, topFacing: false);
            BuildCap(safeSegments, safeCapRings, CurrentFillLocalY, topFacing: true);

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.SetColors(_colors);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            CacheRebuildState();
        }

        private void BuildSideRings(int safeSegments, int safeVerticalRings, float bottomY, float fillY)
        {
            int firstRing = _vertices.Count;
            for (int yIndex = 0; yIndex < safeVerticalRings; yIndex++)
            {
                float t = yIndex / Mathf.Max(1f, safeVerticalRings - 1f);
                float y = Mathf.Lerp(bottomY, fillY, t);
                float radius = GetInsetRadius(y);

                for (int segment = 0; segment < safeSegments; segment++)
                {
                    _vertices.Add(ToRendererLocal(PointOnRing(y, radius, segment / (float)safeSegments)));
                    _colors.Add(new Color(1f, 1f, 1f, Mathf.Lerp(0.82f, 1f, t)));
                }
            }

            for (int yIndex = 0; yIndex < safeVerticalRings - 1; yIndex++)
            {
                int lower = firstRing + yIndex * safeSegments;
                int upper = lower + safeSegments;

                for (int segment = 0; segment < safeSegments; segment++)
                {
                    int next = (segment + 1) % safeSegments;
                    AddQuad(lower + segment, upper + segment, upper + next, lower + next);
                }
            }
        }

        private void BuildCap(int safeSegments, int safeCapRings, float y, bool topFacing)
        {
            int centerIndex = _vertices.Count;
            Vector3 center = boundary.boundaryLocalCenterOffset;
            _vertices.Add(ToRendererLocal(new Vector3(center.x, y, center.z)));
            _colors.Add(topFacing ? Color.white : new Color(1f, 1f, 1f, 0.74f));

            int firstRing = _vertices.Count;
            float outerRadius = GetInsetRadius(y);
            for (int ring = 1; ring <= safeCapRings; ring++)
            {
                float ringRadius = outerRadius * ring / safeCapRings;
                for (int segment = 0; segment < safeSegments; segment++)
                {
                    _vertices.Add(ToRendererLocal(PointOnRing(y, ringRadius, segment / (float)safeSegments)));
                    _colors.Add(topFacing ? Color.white : new Color(1f, 1f, 1f, 0.74f));
                }
            }

            for (int segment = 0; segment < safeSegments; segment++)
            {
                int next = (segment + 1) % safeSegments;
                int ringCurrent = firstRing + segment;
                int ringNext = firstRing + next;

                if (topFacing)
                {
                    AddTriangle(centerIndex, ringNext, ringCurrent);
                }
                else
                {
                    AddTriangle(centerIndex, ringCurrent, ringNext);
                }
            }

            for (int ring = 1; ring < safeCapRings; ring++)
            {
                int inner = firstRing + (ring - 1) * safeSegments;
                int outer = inner + safeSegments;
                for (int segment = 0; segment < safeSegments; segment++)
                {
                    int next = (segment + 1) % safeSegments;
                    if (topFacing)
                    {
                        AddQuad(inner + segment, inner + next, outer + next, outer + segment);
                    }
                    else
                    {
                        AddQuad(inner + segment, outer + segment, outer + next, inner + next);
                    }
                }
            }
        }

        private Vector3 PointOnRing(float y, float radius, float normalizedAngle)
        {
            float angle = normalizedAngle * Mathf.PI * 2f;
            Vector3 center = boundary.boundaryLocalCenterOffset;
            return new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                y,
                center.z + Mathf.Sin(angle) * radius);
        }

        private float GetInsetRadius(float localY)
        {
            float radius = boundary.GetRadiusAtY(localY);
            return Mathf.Max(0.001f, radius - Mathf.Max(0f, wallInset));
        }

        private Vector3 ToRendererLocal(Vector3 bucketLocalPoint)
        {
            if (boundary == null || boundary.transform == transform)
            {
                return bucketLocalPoint;
            }

            return transform.InverseTransformPoint(boundary.transform.TransformPoint(bucketLocalPoint));
        }

        private void AddTriangle(int a, int b, int c)
        {
            _triangles.Add(a);
            _triangles.Add(b);
            _triangles.Add(c);
        }

        private void AddQuad(int a, int b, int c, int d)
        {
            AddTriangle(a, b, c);
            AddTriangle(a, c, d);
        }

        private void CacheRebuildState()
        {
            _lastMeshFillFraction = CurrentFillFraction;
            _lastSegments = Mathf.Max(12, segments);
            _lastVerticalRings = Mathf.Max(2, verticalRings);
            _lastCapRings = Mathf.Max(1, capRings);
            _lastWallInset = wallInset;
            _lastBottomInset = bottomInset;

            if (boundary == null)
            {
                return;
            }

            _lastBoundaryBottomY = boundary.bottomY;
            _lastBoundaryTopY = boundary.topY;
            _lastBoundaryBottomRadius = boundary.bottomRadius;
            _lastBoundaryTopRadius = boundary.topRadius;
            _lastBoundaryCenterOffset = boundary.boundaryLocalCenterOffset;
        }

        private void UpdateMaterialProperties()
        {
            if (_meshRenderer == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            Color color = GetPaintColor();
            Color topColor = Color.Lerp(color, Color.white, 0.18f);
            topColor.a = color.a;
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(TopColorId, topColor);
            _propertyBlock.SetFloat(SmoothnessId, settings != null ? settings.smoothness : 0.75f);
            _meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        private Color GetPaintColor()
        {
            Color color;
            if (SimulationManager.Instance != null && SimulationManager.Instance.physicsSettings != null)
            {
                color = SimulationManager.Instance.physicsSettings.PaintColor;
            }
            else if (settings != null)
            {
                color = settings.paintColor;
                color.a = settings.opacity;
            }
            else
            {
                color = new Color(0.05f, 0.22f, 0.95f, 1f);
            }

            color.a = Mathf.Clamp(color.a, 0.82f, 1f);
            return color;
        }

        private void UpdateRendererState()
        {
            if (_meshRenderer == null)
            {
                return;
            }

            _meshRenderer.enabled = renderEnabled && MeshValid && CurrentFillFraction > 0f;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
        }

        private void OnValidate()
        {
            segments = Mathf.Max(12, segments);
            verticalRings = Mathf.Max(2, verticalRings);
            capRings = Mathf.Max(1, capRings);
            wallInset = Mathf.Max(0f, wallInset);
            bottomInset = Mathf.Max(0f, bottomInset);
            minVisibleFillFraction = Mathf.Clamp(minVisibleFillFraction, 0f, 0.25f);
            fillSmoothing = Mathf.Max(0f, fillSmoothing);
            _lastMeshFillFraction = -1f;
        }
    }
}

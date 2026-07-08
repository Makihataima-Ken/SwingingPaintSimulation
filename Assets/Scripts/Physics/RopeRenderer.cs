using UnityEngine;

/// <summary>
/// Draws the rope as a procedural mesh built from the custom Pendulum rope particles.
///
/// This component is visual only. It reads positions from the manual rope solver and writes a
/// tube-like mesh through MeshFilter/MeshRenderer. It does not use Unity physics, particles,
/// trails, or built-in rope helpers.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RopeRenderer : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Anchor point where the rope starts. If empty, this GameObject is used.")]
    public Transform anchorTransform;

    [Tooltip("BucketRig transform moved by the custom pendulum. Used as fallback when no attachment point is assigned.")]
    public Transform bucketTransform;

    [Tooltip("Visual bucket attachment point where the rope should end. Usually BucketRig/RopeAttachment.")]
    public Transform attachmentTransform;

    [Tooltip("Optional pendulum reference used to auto-fill anchor and bucket transforms and to read rope stretch.")]
    public Pendulum pendulum;

    [Header("Visual Settings")]
    [Tooltip("Diameter of the procedural rope mesh at rest length.")]
    public float ropeWidth = 0.12f;

    [Tooltip("Number of sides around the generated rope tube.")]
    [Range(3, 12)]
    public int tubeSides = 6;

    [Tooltip("Optional material used by the procedural rope mesh.")]
    public Material ropeMaterial;

    [Header("Stretch Feedback (optional)")]
    [Tooltip("When on, the rope thins and changes colour as it stretches beyond its rest length. Purely cosmetic.")]
    public bool reflectStretch = true;

    [Tooltip("Rope colour at or below rest length.")]
    public Color slackColor = Color.black;

    [Tooltip("Rope colour when stretched to fully extended (see fullStretchRatio).")]
    public Color stretchedColor = new Color(1f, 0.4f, 0.2f, 1f);

    [Tooltip("Extension ratio ((length - rest) / rest) treated as 'fully stretched' for colour/width blending.")]
    public float fullStretchRatio = 0.5f;

    [Tooltip("Width multiplier applied at full stretch (a real rope gets thinner as it stretches). 1 = no thinning.")]
    [Range(0.1f, 1f)]
    public float stretchedWidthScale = 0.6f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _ropeMesh;
    private Material _runtimeMaterial;
    private MaterialPropertyBlock _propertyBlock;
    private Vector3[] _pathPoints;
    private Vector3[] _vertices;
    private Vector3[] _normals;
    private Vector2[] _uvs;
    private int[] _triangles;

    private void Awake()
    {
        ResolveReferences();
        EnsureRenderResources();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureRenderResources();
        RebuildMesh();
    }

    private void Update()
    {
        ResolveReferences();
        EnsureRenderResources();
        RebuildMesh();
    }

    private void OnDestroy()
    {
        if (_ropeMesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_ropeMesh);
            }
            else
            {
                DestroyImmediate(_ropeMesh);
            }

            _ropeMesh = null;
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

    private void RebuildMesh()
    {
        Transform ropeEnd = GetRopeEndTransform();
        if (anchorTransform == null || ropeEnd == null)
        {
            ClearMesh();
            return;
        }

        int pointCount = CopyRopePath(ropeEnd);
        if (pointCount < 2)
        {
            ClearMesh();
            return;
        }

        int sides = Mathf.Clamp(tubeSides, 3, 12);
        float width = GetCurrentRopeWidth();
        float radius = width * 0.5f;
        Color color = GetCurrentRopeColor();

        EnsureMeshArrays(pointCount, sides);
        FillTubeVertices(pointCount, sides, radius);
        FillTubeTriangles(pointCount, sides);

        _ropeMesh.Clear();
        _ropeMesh.vertices = _vertices;
        _ropeMesh.normals = _normals;
        _ropeMesh.uv = _uvs;
        _ropeMesh.triangles = _triangles;
        _ropeMesh.RecalculateBounds();

        ApplyMaterialAndColor(color);

        if (_meshRenderer != null)
        {
            _meshRenderer.enabled = true;
        }
    }

    private int CopyRopePath(Transform ropeEnd)
    {
        if (pendulum != null && pendulum.RopePointCount > 1)
        {
            int pointCount = pendulum.RopePointCount;
            EnsurePathArray(pointCount);

            for (int i = 0; i < pointCount; i++)
            {
                _pathPoints[i] = pendulum.GetRopePoint(i);
            }

            return pointCount;
        }

        EnsurePathArray(2);
        _pathPoints[0] = anchorTransform.position;
        _pathPoints[1] = ropeEnd.position;
        return 2;
    }

    private void FillTubeVertices(int pointCount, int sides, float radius)
    {
        float accumulatedLength = 0f;

        for (int i = 0; i < pointCount; i++)
        {
            if (i > 0)
            {
                accumulatedLength += Vector3.Distance(_pathPoints[i - 1], _pathPoints[i]);
            }

            Vector3 tangent = GetPointTangent(i, pointCount);
            Vector3 normal = GetFrameNormal(tangent);
            Vector3 binormal = Vector3.Cross(tangent, normal).normalized;

            int vertexStart = i * sides;
            for (int side = 0; side < sides; side++)
            {
                float angle = (Mathf.PI * 2f * side) / sides;
                Vector3 offsetDirection = normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                Vector3 worldVertex = _pathPoints[i] + offsetDirection * radius;
                int vertexIndex = vertexStart + side;

                _vertices[vertexIndex] = transform.InverseTransformPoint(worldVertex);
                _normals[vertexIndex] = transform.InverseTransformDirection(offsetDirection).normalized;
                _uvs[vertexIndex] = new Vector2(side / (float)sides, accumulatedLength);
            }
        }
    }

    private void FillTubeTriangles(int pointCount, int sides)
    {
        int triangleIndex = 0;

        for (int i = 0; i < pointCount - 1; i++)
        {
            int currentRing = i * sides;
            int nextRing = (i + 1) * sides;

            for (int side = 0; side < sides; side++)
            {
                int nextSide = (side + 1) % sides;

                int a = currentRing + side;
                int b = currentRing + nextSide;
                int c = nextRing + side;
                int d = nextRing + nextSide;

                _triangles[triangleIndex++] = a;
                _triangles[triangleIndex++] = c;
                _triangles[triangleIndex++] = b;
                _triangles[triangleIndex++] = b;
                _triangles[triangleIndex++] = c;
                _triangles[triangleIndex++] = d;
            }
        }
    }

    private Vector3 GetPointTangent(int index, int pointCount)
    {
        Vector3 tangent;

        if (index <= 0)
        {
            tangent = _pathPoints[1] - _pathPoints[0];
        }
        else if (index >= pointCount - 1)
        {
            tangent = _pathPoints[pointCount - 1] - _pathPoints[pointCount - 2];
        }
        else
        {
            tangent = _pathPoints[index + 1] - _pathPoints[index - 1];
        }

        return tangent.sqrMagnitude > 0.000001f ? tangent.normalized : Vector3.down;
    }

    private static Vector3 GetFrameNormal(Vector3 tangent)
    {
        Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.92f
            ? Vector3.right
            : Vector3.up;

        Vector3 normal = Vector3.Cross(reference, tangent);
        return normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.forward;
    }

    private float GetCurrentRopeWidth()
    {
        if (!reflectStretch || pendulum == null)
        {
            return ropeWidth;
        }

        float tensionRatio = GetTensionRatio();
        return ropeWidth * Mathf.Lerp(1f, stretchedWidthScale, tensionRatio);
    }

    private Color GetCurrentRopeColor()
    {
        if (!reflectStretch || pendulum == null)
        {
            return slackColor;
        }

        return Color.Lerp(slackColor, stretchedColor, GetTensionRatio());
    }

    private float GetTensionRatio()
    {
        if (pendulum == null)
        {
            return 0f;
        }

        float denominator = Mathf.Max(0.0001f, fullStretchRatio);
        float extensionRatio = pendulum.RestLength > 0f
            ? Mathf.Clamp01((pendulum.CurrentRopeLength / pendulum.RestLength - 1f) / denominator)
            : 0f;

        return Mathf.Max(extensionRatio, pendulum.NormalizedRopeTension);
    }

    private void ResolveReferences()
    {
        if (pendulum == null)
        {
            pendulum = FindObjectOfType<Pendulum>();
        }

        if (pendulum != null)
        {
            if (anchorTransform == null)
            {
                anchorTransform = pendulum.anchorTransform;
            }

            if (bucketTransform == null)
            {
                bucketTransform = pendulum.bucketTransform;
            }
        }

        if (attachmentTransform == null && bucketTransform != null)
        {
            attachmentTransform = bucketTransform.Find("RopeAttachment");
        }

        if (anchorTransform == null)
        {
            anchorTransform = transform;
        }
    }

    private Transform GetRopeEndTransform()
    {
        return attachmentTransform != null ? attachmentTransform : bucketTransform;
    }

    private void EnsureRenderResources()
    {
        if (_meshFilter == null)
        {
            _meshFilter = GetComponent<MeshFilter>();
        }

        if (_meshRenderer == null)
        {
            _meshRenderer = GetComponent<MeshRenderer>();
        }

        if (_ropeMesh == null)
        {
            _ropeMesh = new Mesh
            {
                name = "Runtime Procedural Rope Mesh"
            };
            _ropeMesh.MarkDynamic();
        }

        if (_meshFilter != null && _meshFilter.sharedMesh != _ropeMesh)
        {
            _meshFilter.sharedMesh = _ropeMesh;
        }
    }

    private void ApplyMaterialAndColor(Color color)
    {
        if (_meshRenderer == null)
        {
            return;
        }

        Material material = ropeMaterial != null ? ropeMaterial : GetOrCreateRuntimeMaterial();
        if (material != null && _meshRenderer.sharedMaterial != material)
        {
            _meshRenderer.sharedMaterial = material;
        }

        if (_propertyBlock == null)
        {
            _propertyBlock = new MaterialPropertyBlock();
        }

        _meshRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(ColorId, color);
        _propertyBlock.SetColor(BaseColorId, color);
        _meshRenderer.SetPropertyBlock(_propertyBlock);
    }

    private Material GetOrCreateRuntimeMaterial()
    {
        if (_runtimeMaterial != null)
        {
            return _runtimeMaterial;
        }

        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            return null;
        }

        _runtimeMaterial = new Material(shader)
        {
            name = "Runtime Procedural Rope Material"
        };
        return _runtimeMaterial;
    }

    private void EnsurePathArray(int pointCount)
    {
        if (_pathPoints == null || _pathPoints.Length != pointCount)
        {
            _pathPoints = new Vector3[pointCount];
        }
    }

    private void EnsureMeshArrays(int pointCount, int sides)
    {
        int vertexCount = pointCount * sides;
        int triangleCount = (pointCount - 1) * sides * 6;

        if (_vertices == null || _vertices.Length != vertexCount)
        {
            _vertices = new Vector3[vertexCount];
            _normals = new Vector3[vertexCount];
            _uvs = new Vector2[vertexCount];
        }

        if (_triangles == null || _triangles.Length != triangleCount)
        {
            _triangles = new int[triangleCount];
        }
    }

    private void ClearMesh()
    {
        if (_ropeMesh != null)
        {
            _ropeMesh.Clear();
        }

        if (_meshRenderer != null)
        {
            _meshRenderer.enabled = false;
        }
    }

    private void OnValidate()
    {
        ropeWidth = Mathf.Max(0.001f, ropeWidth);
        tubeSides = Mathf.Clamp(tubeSides, 3, 12);
        fullStretchRatio = Mathf.Max(0.01f, fullStretchRatio);
        stretchedWidthScale = Mathf.Clamp(stretchedWidthScale, 0.1f, 1f);

        if (_ropeMesh != null)
        {
            RebuildMesh();
        }
    }
}

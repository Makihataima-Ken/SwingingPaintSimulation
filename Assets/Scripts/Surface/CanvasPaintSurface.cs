using SwingingPaint.Core;
using UnityEngine;

namespace SwingingPaint.Surface
{
    /// <summary>
    /// Live paint target for the ground/canvas.
    ///
    /// Paint impacts are detected by callers with manual segment/plane checks. Deposition writes
    /// into a texture and uploads it to a RenderTexture used by the canvas material.
    /// </summary>
    public class CanvasPaintSurface : MonoBehaviour
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Header("References")]
        public PhysicsSettings physicsSettings;
        public Renderer targetRenderer;

        [Header("Surface Geometry")]
        public bool useTransformPlane = true;
        [Tooltip("Cube top face is local Y 0.5. Plane meshes usually use 0.")]
        public float surfaceLocalY = 0.5f;
        public Vector2 fallbackCanvasSize = new Vector2(5f, 3f);

        [Header("Texture")]
        [Min(32)]
        public int textureWidth = 1024;
        [Min(32)]
        public int textureHeight = 1024;
        public Color drySurfaceColor = new Color(0.94f, 0.92f, 0.86f, 1f);
        [Range(0f, 1f)]
        public float defaultAbsorption = 0.1f;

        [Header("Paint Behavior")]
        [Min(0.001f)]
        public float minImpactRadius = 0.025f;
        [Min(0.001f)]
        public float maxImpactRadius = 0.45f;
        [Range(0f, 2f)]
        public float opacityMultiplier = 0.85f;
        [Range(0f, 3f)]
        public float flowSpreadBoost = 0.15f;

        public RenderTexture PaintTexture { get; private set; }
        public int DepositedImpactCount { get; private set; }
        public float TotalPaintDeposited { get; private set; }
        public float CoverageArea { get; private set; }

        private Texture2D _paintTexture2D;
        private Color[] _pixels;
        private bool[] _coveredPixels;
        private Material _runtimeMaterial;
        private bool _textureDirty;
        private int _coveredPixelCount;

        private void Awake()
        {
            ResolveReferences();
            EnsureTexture();
            ApplyTextureToRenderer();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureTexture();
            ApplyTextureToRenderer();
        }

        private void LateUpdate()
        {
            FlushPaintTexture();
        }

        private void OnDestroy()
        {
            ReleaseRuntimeResources();
        }

        [ContextMenu("Clear Canvas Paint")]
        public void ClearPaint()
        {
            EnsureTexture();

            for (int i = 0; i < _pixels.Length; i++)
            {
                _pixels[i] = drySurfaceColor;
                _coveredPixels[i] = false;
            }

            _paintTexture2D.SetPixels(_pixels);
            _paintTexture2D.Apply(false, false);
            Graphics.Blit(_paintTexture2D, PaintTexture);

            DepositedImpactCount = 0;
            TotalPaintDeposited = 0f;
            _coveredPixelCount = 0;
            CoverageArea = 0f;
            _textureDirty = false;
        }

        public bool TryDepositSegment(
            Vector3 previousWorldPosition,
            Vector3 currentWorldPosition,
            float particleRadius,
            Color color,
            float wetness,
            float amount,
            float viscosity,
            float flowRate)
        {
            EnsureTexture();

            Plane plane = GetSurfacePlane();
            float previousDistance = plane.GetDistanceToPoint(previousWorldPosition);
            float currentDistance = plane.GetDistanceToPoint(currentWorldPosition);

            if (previousDistance < 0f || currentDistance > 0f)
            {
                return false;
            }

            float denominator = previousDistance - currentDistance;
            float t = denominator > Mathf.Epsilon ? previousDistance / denominator : 0f;
            t = Mathf.Clamp01(t);
            Vector3 impactPoint = Vector3.Lerp(previousWorldPosition, currentWorldPosition, t);

            if (!TryWorldToUV(impactPoint, out Vector2 uv))
            {
                return true;
            }

            DepositAtUV(uv, particleRadius, color, wetness, amount, viscosity, flowRate);
            return true;
        }

        public void DepositAtUV(
            Vector2 uv,
            float particleRadius,
            Color color,
            float wetness,
            float amount,
            float viscosity,
            float flowRate)
        {
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
            {
                return;
            }

            EnsureTexture();

            float absorption = GetAbsorption();
            Vector2 canvasSize = GetCanvasSize();
            float viscosityFactor = 1f / (1f + Mathf.Max(0f, viscosity));
            float flowFactor = 1f + Mathf.Max(0f, flowRate) * flowSpreadBoost;
            float absorptionFactor = Mathf.Clamp01(1f - absorption * 0.75f);
            float spreadWorldRadius = particleRadius * Mathf.Lerp(1.2f, 3.2f, viscosityFactor) * flowFactor * absorptionFactor;
            spreadWorldRadius = Mathf.Clamp(spreadWorldRadius, minImpactRadius, maxImpactRadius);

            int centerX = Mathf.RoundToInt(uv.x * (textureWidth - 1));
            int centerY = Mathf.RoundToInt(uv.y * (textureHeight - 1));
            int radiusX = Mathf.Max(1, Mathf.CeilToInt(spreadWorldRadius / Mathf.Max(0.001f, canvasSize.x) * textureWidth));
            int radiusY = Mathf.Max(1, Mathf.CeilToInt(spreadWorldRadius / Mathf.Max(0.001f, canvasSize.y) * textureHeight));

            int minX = Mathf.Max(0, centerX - radiusX);
            int maxX = Mathf.Min(textureWidth - 1, centerX + radiusX);
            int minY = Mathf.Max(0, centerY - radiusY);
            int maxY = Mathf.Min(textureHeight - 1, centerY + radiusY);
            float opacity = Mathf.Clamp01(color.a * wetness * Mathf.Max(0.05f, amount) * opacityMultiplier);
            Color paintColor = color;
            paintColor.a = 1f;

            for (int y = minY; y <= maxY; y++)
            {
                float normalizedY = (y - centerY) / Mathf.Max(1f, radiusY);

                for (int x = minX; x <= maxX; x++)
                {
                    float normalizedX = (x - centerX) / Mathf.Max(1f, radiusX);
                    float distance = Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
                    if (distance > 1f)
                    {
                        continue;
                    }

                    float falloff = 1f - distance;
                    falloff *= falloff;
                    float alpha = Mathf.Clamp01(opacity * falloff * (1f - absorption * 0.4f));
                    int index = y * textureWidth + x;
                    Color existing = _pixels[index];

                    existing.r = Mathf.Lerp(existing.r, paintColor.r, alpha);
                    existing.g = Mathf.Lerp(existing.g, paintColor.g, alpha);
                    existing.b = Mathf.Lerp(existing.b, paintColor.b, alpha);
                    existing.a = Mathf.Clamp01(existing.a + alpha * 0.75f);
                    _pixels[index] = existing;

                    if (!_coveredPixels[index] && IsCovered(existing))
                    {
                        _coveredPixels[index] = true;
                        _coveredPixelCount++;
                    }
                }
            }

            DepositedImpactCount++;
            TotalPaintDeposited += Mathf.Max(0f, amount);
            _textureDirty = true;
            UpdateCoverageArea();
        }

        public void FlushPaintTexture()
        {
            if (!_textureDirty)
            {
                return;
            }

            _paintTexture2D.SetPixels(_pixels);
            _paintTexture2D.Apply(false, false);
            Graphics.Blit(_paintTexture2D, PaintTexture);
            _textureDirty = false;
        }

        private bool TryWorldToUV(Vector3 worldPosition, out Vector2 uv)
        {
            if (useTransformPlane)
            {
                Vector3 local = transform.InverseTransformPoint(worldPosition);
                uv = new Vector2(local.x + 0.5f, local.z + 0.5f);
            }
            else
            {
                uv = SimulationCoordinateSystem.WorldToCanvasUV(worldPosition, transform.position, GetCanvasSize());
            }

            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        private Plane GetSurfacePlane()
        {
            if (useTransformPlane)
            {
                Vector3 point = transform.TransformPoint(new Vector3(0f, surfaceLocalY, 0f));
                Vector3 normal = transform.up;
                return new Plane(normal.sqrMagnitude > Mathf.Epsilon ? normal.normalized : Vector3.up, point);
            }

            return new Plane(Vector3.up, new Vector3(0f, SimulationCoordinateSystem.CanvasY, 0f));
        }

        private Vector2 GetCanvasSize()
        {
            Vector3 scale = transform.lossyScale;
            Vector2 size = new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.z));

            if (size.x <= Mathf.Epsilon || size.y <= Mathf.Epsilon)
            {
                size = fallbackCanvasSize;
            }

            size.x = Mathf.Max(0.001f, size.x);
            size.y = Mathf.Max(0.001f, size.y);
            return size;
        }

        private float GetAbsorption()
        {
            return physicsSettings != null ? physicsSettings.SurfaceAbsorption : defaultAbsorption;
        }

        private void UpdateCoverageArea()
        {
            Vector2 canvasSize = GetCanvasSize();
            CoverageArea = _coveredPixelCount / (float)_pixels.Length * canvasSize.x * canvasSize.y;
        }

        private bool IsCovered(Color pixel)
        {
            float colorDelta =
                Mathf.Abs(pixel.r - drySurfaceColor.r) +
                Mathf.Abs(pixel.g - drySurfaceColor.g) +
                Mathf.Abs(pixel.b - drySurfaceColor.b);

            return colorDelta > 0.03f;
        }

        private void ResolveReferences()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
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

        private void EnsureTexture()
        {
            int safeWidth = Mathf.Max(32, textureWidth);
            int safeHeight = Mathf.Max(32, textureHeight);
            bool needsTexture =
                _paintTexture2D == null ||
                _paintTexture2D.width != safeWidth ||
                _paintTexture2D.height != safeHeight ||
                PaintTexture == null ||
                PaintTexture.width != safeWidth ||
                PaintTexture.height != safeHeight;

            if (!needsTexture)
            {
                return;
            }

            ReleaseTextureResources();

            textureWidth = safeWidth;
            textureHeight = safeHeight;
            _paintTexture2D = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false, false)
            {
                name = "Runtime Canvas Paint Backing"
            };
            _pixels = new Color[textureWidth * textureHeight];
            _coveredPixels = new bool[textureWidth * textureHeight];

            PaintTexture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "Runtime Canvas Paint Texture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            PaintTexture.Create();

            ClearPaint();
        }

        private void ApplyTextureToRenderer()
        {
            if (targetRenderer == null || PaintTexture == null)
            {
                return;
            }

            if (_runtimeMaterial == null)
            {
                Material sourceMaterial = targetRenderer.sharedMaterial;
                _runtimeMaterial = sourceMaterial != null
                    ? new Material(sourceMaterial)
                    : new Material(Shader.Find("Standard"));
                _runtimeMaterial.name = "Runtime Painted Canvas Material";
            }

            _runtimeMaterial.SetTexture(MainTexId, PaintTexture);
            _runtimeMaterial.SetTexture(BaseMapId, PaintTexture);
            _runtimeMaterial.SetColor(ColorId, Color.white);
            targetRenderer.sharedMaterial = _runtimeMaterial;
        }

        private void ReleaseRuntimeResources()
        {
            ReleaseTextureResources();

            if (_runtimeMaterial != null)
            {
                Destroy(_runtimeMaterial);
                _runtimeMaterial = null;
            }
        }

        private void ReleaseTextureResources()
        {
            if (_paintTexture2D != null)
            {
                Destroy(_paintTexture2D);
                _paintTexture2D = null;
            }

            if (PaintTexture != null)
            {
                PaintTexture.Release();
                Destroy(PaintTexture);
                PaintTexture = null;
            }

            _pixels = null;
            _coveredPixels = null;
            _coveredPixelCount = 0;
        }

        private void OnValidate()
        {
            textureWidth = Mathf.Max(32, textureWidth);
            textureHeight = Mathf.Max(32, textureHeight);
            surfaceLocalY = Mathf.Clamp(surfaceLocalY, -1f, 1f);
            fallbackCanvasSize.x = Mathf.Max(0.001f, fallbackCanvasSize.x);
            fallbackCanvasSize.y = Mathf.Max(0.001f, fallbackCanvasSize.y);
            defaultAbsorption = Mathf.Clamp01(defaultAbsorption);
            minImpactRadius = Mathf.Max(0.001f, minImpactRadius);
            maxImpactRadius = Mathf.Max(minImpactRadius, maxImpactRadius);
            opacityMultiplier = Mathf.Clamp(opacityMultiplier, 0f, 2f);
            flowSpreadBoost = Mathf.Clamp(flowSpreadBoost, 0f, 3f);
        }
    }
}

using System.Collections.Generic;
using SwingingPaint.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwingingPaint.Paint
{
    /// <summary>
    /// Draws PaintEmitter particles as a connected camera-facing stream mesh.
    ///
    /// This is visual only. Paint deposition still comes from PaintEmitter's manually simulated
    /// particles crossing CanvasPaintSurface.
    /// </summary>
    [RequireComponent(typeof(PaintEmitter))]
    public class PaintStreamRenderer : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("References")]
        public PaintEmitter emitter;
        public Material streamMaterial;

        [Header("Ribbon")]
        public bool faceCamera = true;
        public bool castShadows = false;
        public bool receiveShadows = false;

        [Header("Adaptive Connection")]
        [Min(0f)]
        public float adaptiveBreakSpeedScale = 1.75f;
        [Min(0.0001f)]
        public float maxAdaptiveStreamBreakDistance = 0.30f;

        public int RenderedSegmentCount { get; private set; }
        public int BrokenStreamSegmentCount { get; private set; }
        public float AverageStreamSpacing { get; private set; }
        public float MaxStreamSpacing { get; private set; }
        public float AverageFallingSpeed { get; private set; }
        public float CurrentAdaptiveBreakDistance { get; private set; }

        private readonly List<PaintEmitter.PaintParticle> _particles = new List<PaintEmitter.PaintParticle>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<int> _triangles = new List<int>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<PaintEmitter.PaintParticle> _segmentParticles = new List<PaintEmitter.PaintParticle>();
        private Mesh _streamMesh;
        private Material _runtimeMaterial;
        private MaterialPropertyBlock _propertyBlock;

        private void Awake()
        {
            ResolveReferences();
            EnsureMesh();
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void OnDestroy()
        {
            ClearStream();

            if (_streamMesh != null)
            {
                Destroy(_streamMesh);
                _streamMesh = null;
            }

            if (_runtimeMaterial != null)
            {
                Destroy(_runtimeMaterial);
                _runtimeMaterial = null;
            }
        }

        public void RenderStream()
        {
            ResolveReferences();

            if (emitter == null || !emitter.ShouldRenderStreamMesh)
            {
                ClearStream();
                return;
            }

            emitter.CopyActiveStreamParticles(_particles);

            if (_particles.Count < 2)
            {
                ClearStream();
                return;
            }

            _particles.Sort(CompareParticles);
            BuildRibbonMesh();

            if (RenderedSegmentCount <= 0)
            {
                return;
            }

            Material material = GetStreamMaterial();
            if (material == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            Color color = emitter != null ? emitter.CurrentPaintColor : Color.white;
            _propertyBlock.SetColor(ColorId, color);
            _propertyBlock.SetColor(BaseColorId, color);

            Graphics.DrawMesh(
                _streamMesh,
                Matrix4x4.identity,
                material,
                gameObject.layer,
                null,
                0,
                _propertyBlock,
                castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows
            );
        }

        public void ClearStream()
        {
            RenderedSegmentCount = 0;
            ResetMetrics();

            if (_streamMesh != null)
            {
                _streamMesh.Clear();
            }
        }

        private void ResolveReferences()
        {
            if (emitter == null)
            {
                emitter = GetComponent<PaintEmitter>();
            }
        }

        private void EnsureMesh()
        {
            if (_streamMesh != null)
            {
                return;
            }

            _streamMesh = new Mesh
            {
                name = "Runtime Paint Stream Ribbon"
            };
            _streamMesh.MarkDynamic();
        }

        private void BuildRibbonMesh()
        {
            EnsureMesh();

            _vertices.Clear();
            _triangles.Clear();
            _colors.Clear();
            _segmentParticles.Clear();
            RenderedSegmentCount = 0;
            ResetMetrics();

            CurrentAdaptiveBreakDistance = Mathf.Max(0.0001f, emitter.EffectiveStreamBreakDistance);
            float spacingSum = 0f;
            float speedSum = 0f;
            int measuredPairCount = 0;

            for (int i = 0; i < _particles.Count; i++)
            {
                PaintEmitter.PaintParticle current = _particles[i];

                if (_segmentParticles.Count == 0)
                {
                    _segmentParticles.Add(current);
                    continue;
                }

                PaintEmitter.PaintParticle previous = _segmentParticles[_segmentParticles.Count - 1];

                if (previous.streamId != current.streamId)
                {
                    FlushSegmentToRibbon();
                    _segmentParticles.Add(current);
                    continue;
                }

                float distance = Vector3.Distance(previous.position, current.position);
                if (distance <= 0.0001f)
                {
                    continue;
                }

                float averageSpeed = (previous.velocity.magnitude + current.velocity.magnitude) * 0.5f;
                float adaptiveBreakDistance = GetAdaptiveBreakDistance(previous, current, averageSpeed);

                spacingSum += distance;
                speedSum += averageSpeed;
                measuredPairCount++;
                MaxStreamSpacing = Mathf.Max(MaxStreamSpacing, distance);
                CurrentAdaptiveBreakDistance = Mathf.Max(CurrentAdaptiveBreakDistance, adaptiveBreakDistance);

                if (distance > adaptiveBreakDistance)
                {
                    BrokenStreamSegmentCount++;
                    FlushSegmentToRibbon();
                    _segmentParticles.Add(current);
                    continue;
                }

                _segmentParticles.Add(current);
            }

            FlushSegmentToRibbon();

            if (measuredPairCount > 0)
            {
                AverageStreamSpacing = spacingSum / measuredPairCount;
                AverageFallingSpeed = speedSum / measuredPairCount;
            }

            _streamMesh.Clear();

            if (RenderedSegmentCount <= 0)
            {
                return;
            }

            _streamMesh.SetVertices(_vertices);
            _streamMesh.SetTriangles(_triangles, 0);
            _streamMesh.SetColors(_colors);
            _streamMesh.RecalculateBounds();
        }

        private void FlushSegmentToRibbon()
        {
            if (_segmentParticles.Count < 2)
            {
                _segmentParticles.Clear();
                return;
            }

            int firstVertex = _vertices.Count;

            for (int i = 0; i < _segmentParticles.Count; i++)
            {
                PaintEmitter.PaintParticle particle = _segmentParticles[i];
                Vector3 center = GetSmoothedSegmentPoint(i);
                Vector3 tangent = GetSegmentTangent(i);
                Vector3 side = GetRibbonSide(tangent, center);
                float halfWidth = emitter.GetStreamVisualHalfWidth(particle.radius);

                _vertices.Add(center - side * halfWidth);
                _vertices.Add(center + side * halfWidth);
                _colors.Add(particle.color);
                _colors.Add(particle.color);

                if (i <= 0)
                {
                    continue;
                }

                int previousLeft = firstVertex + (i - 1) * 2;
                int currentLeft = firstVertex + i * 2;

                _triangles.Add(previousLeft);
                _triangles.Add(currentLeft);
                _triangles.Add(previousLeft + 1);
                _triangles.Add(previousLeft + 1);
                _triangles.Add(currentLeft);
                _triangles.Add(currentLeft + 1);

                RenderedSegmentCount++;
            }

            _segmentParticles.Clear();
        }

        private Vector3 GetSmoothedSegmentPoint(int index)
        {
            PaintEmitter.PaintParticle current = _segmentParticles[index];

            if (index <= 0 || index >= _segmentParticles.Count - 1)
            {
                return current.position;
            }

            Vector3 previous = _segmentParticles[index - 1].position;
            Vector3 next = _segmentParticles[index + 1].position;
            return (previous + current.position * 2f + next) * 0.25f;
        }

        private Vector3 GetSegmentTangent(int index)
        {
            Vector3 tangent;

            if (_segmentParticles.Count == 2)
            {
                tangent = _segmentParticles[1].position - _segmentParticles[0].position;
            }
            else if (index <= 0)
            {
                tangent = _segmentParticles[1].position - _segmentParticles[0].position;
            }
            else if (index >= _segmentParticles.Count - 1)
            {
                tangent = _segmentParticles[index].position - _segmentParticles[index - 1].position;
            }
            else
            {
                tangent = _segmentParticles[index + 1].position - _segmentParticles[index - 1].position;
            }

            return tangent.sqrMagnitude > 0.000001f ? tangent.normalized : Vector3.down;
        }

        private float GetAdaptiveBreakDistance(
            PaintEmitter.PaintParticle from,
            PaintEmitter.PaintParticle to,
            float averageSpeed)
        {
            float fixedDeltaTime = GetSimulationFixedDeltaTime();
            float averageRadius = (from.radius + to.radius) * 0.5f;
            float baseBreakDistance = Mathf.Max(0.0001f, emitter.EffectiveStreamBreakDistance);
            float adaptiveBreakDistance = averageSpeed * fixedDeltaTime * adaptiveBreakSpeedScale + averageRadius * 3.5f;
            float connectionScale = 1f;

            if (emitter != null)
            {
                connectionScale += Mathf.Clamp01(emitter.EffectiveStreamViscosity / 2f) * 0.35f;
                connectionScale += Mathf.Clamp01(emitter.EffectiveStreamCohesion) * 0.35f;
            }

            baseBreakDistance *= connectionScale;
            adaptiveBreakDistance *= connectionScale;
            adaptiveBreakDistance = Mathf.Max(baseBreakDistance, adaptiveBreakDistance);

            return Mathf.Min(
                Mathf.Max(0.0001f, maxAdaptiveStreamBreakDistance),
                adaptiveBreakDistance);
        }

        private static float GetSimulationFixedDeltaTime()
        {
            SimulationManager manager = SimulationManager.Instance;
            return manager != null
                ? Mathf.Max(0.001f, manager.fixedTimestep)
                : Mathf.Max(0.001f, Time.fixedDeltaTime);
        }

        private Vector3 GetRibbonSide(Vector3 segmentDirection, Vector3 segmentCenter)
        {
            Vector3 viewDirection = Vector3.forward;

            if (faceCamera && Camera.main != null)
            {
                viewDirection = Camera.main.transform.position - segmentCenter;
                if (viewDirection.sqrMagnitude <= 0.0001f)
                {
                    viewDirection = Camera.main.transform.forward;
                }
            }

            viewDirection.Normalize();
            Vector3 side = Vector3.Cross(segmentDirection, viewDirection);

            if (side.sqrMagnitude <= 0.0001f)
            {
                side = Camera.main != null ? Camera.main.transform.right : Vector3.right;
            }

            return side.normalized;
        }

        private void ResetMetrics()
        {
            BrokenStreamSegmentCount = 0;
            AverageStreamSpacing = 0f;
            MaxStreamSpacing = 0f;
            AverageFallingSpeed = 0f;
            CurrentAdaptiveBreakDistance = 0f;
        }

        private Material GetStreamMaterial()
        {
            if (streamMaterial != null)
            {
                return streamMaterial;
            }

            if (_runtimeMaterial != null)
            {
                return _runtimeMaterial;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

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
                name = "Runtime Paint Stream Material"
            };

            return _runtimeMaterial;
        }

        private static int CompareParticles(PaintEmitter.PaintParticle a, PaintEmitter.PaintParticle b)
        {
            int streamCompare = a.streamId.CompareTo(b.streamId);
            return streamCompare != 0
                ? streamCompare
                : a.orderInStream.CompareTo(b.orderInStream);
        }

        private void OnValidate()
        {
            adaptiveBreakSpeedScale = Mathf.Max(0f, adaptiveBreakSpeedScale);
            maxAdaptiveStreamBreakDistance = Mathf.Max(0.0001f, maxAdaptiveStreamBreakDistance);
        }
    }
}

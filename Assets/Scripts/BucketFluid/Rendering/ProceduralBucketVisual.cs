using System.Collections.Generic;
using SwingingPaint.BucketFluid.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwingingPaint.BucketFluid.Rendering
{
    /// <summary>
    /// Fallback visual bucket generated from BucketFluidBoundary values.
    /// Used when the imported FBX model is missing, for example when Git LFS assets
    /// have not been pulled. This is visual-only and uses no Unity physics.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ProceduralBucketVisual : MonoBehaviour
    {
        public BucketFluidBoundary boundary;
        public int segments = 64;
        public float wallThickness = 0.025f;
        public Color bucketColor = new Color(0.55f, 0.58f, 0.62f, 0.62f);

        private Mesh _mesh;
        private Material _material;

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnValidate()
        {
            segments = Mathf.Max(12, segments);
            wallThickness = Mathf.Max(0.001f, wallThickness);
            Rebuild();
        }

        public void Rebuild()
        {
            if (boundary == null)
            {
                boundary = GetComponentInParent<BucketFluidBoundary>();
            }

            if (boundary == null)
            {
                return;
            }

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();

            if (_mesh == null)
            {
                _mesh = new Mesh
                {
                    name = "Procedural Bucket Fallback Mesh"
                };
            }

            BuildMesh(_mesh);
            meshFilter.sharedMesh = _mesh;
            meshRenderer.sharedMaterial = GetOrCreateMaterial();
            meshRenderer.enabled = true;
        }

        private void BuildMesh(Mesh mesh)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            Vector3 center = boundary.boundaryLocalCenterOffset;
            float bottomY = center.y + boundary.bottomY;
            float topY = center.y + boundary.topY;
            float innerBottomRadius = Mathf.Max(0.001f, boundary.bottomRadius);
            float innerTopRadius = Mathf.Max(0.001f, boundary.topRadius);
            float outerBottomRadius = innerBottomRadius + wallThickness;
            float outerTopRadius = innerTopRadius + wallThickness;

            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;

                Vector3 innerBottom0 = Point(center, innerBottomRadius, bottomY, a0);
                Vector3 innerBottom1 = Point(center, innerBottomRadius, bottomY, a1);
                Vector3 innerTop0 = Point(center, innerTopRadius, topY, a0);
                Vector3 innerTop1 = Point(center, innerTopRadius, topY, a1);

                Vector3 outerBottom0 = Point(center, outerBottomRadius, bottomY, a0);
                Vector3 outerBottom1 = Point(center, outerBottomRadius, bottomY, a1);
                Vector3 outerTop0 = Point(center, outerTopRadius, topY, a0);
                Vector3 outerTop1 = Point(center, outerTopRadius, topY, a1);

                AddQuad(vertices, triangles, outerBottom0, outerBottom1, outerTop1, outerTop0);
                AddQuad(vertices, triangles, innerBottom1, innerBottom0, innerTop0, innerTop1);
                AddQuad(vertices, triangles, innerTop0, outerTop0, outerTop1, innerTop1);
                AddQuad(vertices, triangles, outerBottom1, outerBottom0, innerBottom0, innerBottom1);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private Material GetOrCreateMaterial()
        {
            if (_material == null)
            {
                Shader shader = Shader.Find("Standard");
                _material = shader != null ? new Material(shader) : new Material(Shader.Find("Unlit/Color"));
                _material.name = "Procedural Bucket Fallback Material";
            }

            if (_material.HasProperty("_Color"))
            {
                _material.color = bucketColor;
            }

            if (bucketColor.a < 0.99f)
            {
                _material.SetFloat("_Mode", 3f);
                _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _material.SetInt("_ZWrite", 0);
                _material.DisableKeyword("_ALPHATEST_ON");
                _material.EnableKeyword("_ALPHABLEND_ON");
                _material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                _material.renderQueue = (int)RenderQueue.Transparent;
            }

            return _material;
        }

        private static Vector3 Point(Vector3 center, float radius, float y, float angle)
        {
            return new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                y,
                center.z + Mathf.Sin(angle) * radius
            );
        }

        private static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int index = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 3);
        }
    }
}

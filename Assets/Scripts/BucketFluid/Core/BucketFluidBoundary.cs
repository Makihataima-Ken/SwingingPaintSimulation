using System.Collections.Generic;
using UnityEngine;

namespace SwingingPaint.BucketFluid.Core
{
    /// <summary>
    /// Mathematical bucket boundary in BucketRig local space.
    ///
    /// The boundary is a truncated cone/frustum aligned to BucketRig local Y. It is used by
    /// setup, particle initialization, and fluid constraints without built-in physics.
    /// </summary>
    public class BucketFluidBoundary : MonoBehaviour
    {
        private const int RingSegments = 48;

        [Header("Visual Model Helper")]
        [Tooltip("Imported visual bucket model under BucketRig. Used only by editor/setup helper functions.")]
        public Transform bucketVisualModel;

        [Header("Local-Space Boundary")]
        [Tooltip("Center-line offset of the mathematical boundary in BucketRig local space.")]
        public Vector3 boundaryLocalCenterOffset = Vector3.zero;

        [Tooltip("Local Y of the inner bucket bottom, relative to boundaryLocalCenterOffset.y.")]
        public float bottomY = -0.75f;

        [Tooltip("Local Y of the bucket rim, relative to boundaryLocalCenterOffset.y.")]
        public float topY = 0.1f;

        [Tooltip("Interior radius at bottomY.")]
        public float bottomRadius = 0.34f;

        [Tooltip("Interior radius at topY.")]
        public float topRadius = 0.47f;

        [Tooltip("When true, ClosestPointInside clamps points above the rim back below topY.")]
        public bool clampTop = true;

        [Header("Boundary Response")]
        [Tooltip("Velocity loss after particles hit the mathematical bucket wall.")]
        public float wallDamping = 0.45f;

        [Tooltip("Tangential friction when particles slide along the bucket wall.")]
        public float wallFriction = 0.25f;

        [Header("Gizmos")]
        public bool drawBoundaryGizmos = true;
        [Range(2, 24)]
        public int gizmoRingCount = 6;
        public Color gizmoColor = new Color(0.1f, 0.7f, 1f, 0.75f);

        public float Height => Mathf.Max(0.001f, topY - bottomY);

        private bool _hasWarnedMissingVisualModel;

        private void Reset()
        {
            TryAutoAssignBucketVisualModel();
            ApplySafeDefaultBoundary();
        }

        private void Awake()
        {
            TryAutoAssignBucketVisualModel();
        }

        /// <summary>
        /// Returns the cone radius at a BucketRig-local Y position.
        /// </summary>
        public float GetRadiusAtY(float y)
        {
            float relativeY = y - boundaryLocalCenterOffset.y;
            float t = Mathf.InverseLerp(bottomY, topY, relativeY);
            return Mathf.Lerp(bottomRadius, topRadius, Mathf.Clamp01(t));
        }

        /// <summary>BucketRig-local center point of the rim plane.</summary>
        public Vector3 GetTopCenterLocal()
        {
            return new Vector3(
                boundaryLocalCenterOffset.x,
                boundaryLocalCenterOffset.y + topY,
                boundaryLocalCenterOffset.z
            );
        }

        /// <summary>BucketRig-local center point of the bottom plane.</summary>
        public Vector3 GetBottomCenterLocal()
        {
            return new Vector3(
                boundaryLocalCenterOffset.x,
                boundaryLocalCenterOffset.y + bottomY,
                boundaryLocalCenterOffset.z
            );
        }

        /// <summary>BucketRig-local default rope attachment point on the bucket rim.</summary>
        public Vector3 GetRopeAttachmentLocal()
        {
            return GetTopCenterLocal();
        }

        /// <summary>BucketRig-local default paint emission point just below the bucket bottom.</summary>
        public Vector3 GetPaintHoleLocal(float offsetBelowBottom)
        {
            return GetBottomCenterLocal() + Vector3.down * Mathf.Max(0f, offsetBelowBottom);
        }

        /// <summary>
        /// True when the BucketRig-local point is within the truncated cone.
        /// </summary>
        public bool IsInside(Vector3 localPosition)
        {
            Vector3 boundarySpace = ToBoundarySpace(localPosition);

            if (boundarySpace.y < bottomY)
            {
                return false;
            }

            if (clampTop && boundarySpace.y > topY)
            {
                return false;
            }

            float radiusY = Mathf.Clamp(boundarySpace.y, bottomY, topY);
            float radius = GetRadiusAtY(radiusY + boundaryLocalCenterOffset.y);
            Vector2 horizontal = new Vector2(boundarySpace.x, boundarySpace.z);
            return horizontal.sqrMagnitude <= radius * radius;
        }

        /// <summary>
        /// Projects a BucketRig-local point into the nearest valid point inside the boundary.
        /// </summary>
        public Vector3 ClosestPointInside(Vector3 localPosition)
        {
            Vector3 boundarySpace = ToBoundarySpace(localPosition);

            boundarySpace.y = clampTop
                ? Mathf.Clamp(boundarySpace.y, bottomY, topY)
                : Mathf.Max(boundarySpace.y, bottomY);

            float radiusY = Mathf.Clamp(boundarySpace.y, bottomY, topY);
            float radius = GetRadiusAtY(radiusY + boundaryLocalCenterOffset.y);
            Vector2 horizontal = new Vector2(boundarySpace.x, boundarySpace.z);

            if (horizontal.sqrMagnitude > radius * radius)
            {
                horizontal = horizontal.sqrMagnitude <= Mathf.Epsilon
                    ? Vector2.right * radius
                    : horizontal.normalized * radius;
                boundarySpace.x = horizontal.x;
                boundarySpace.z = horizontal.y;
            }

            return FromBoundarySpace(boundarySpace);
        }

        /// <summary>
        /// Estimates the local-space outward normal of the closest boundary feature.
        /// </summary>
        public Vector3 EstimateNormal(Vector3 localPosition)
        {
            Vector3 boundarySpace = ToBoundarySpace(localPosition);
            float bottomDistance = Mathf.Abs(boundarySpace.y - bottomY);
            float topDistance = clampTop ? Mathf.Abs(boundarySpace.y - topY) : float.PositiveInfinity;

            float radiusY = Mathf.Clamp(boundarySpace.y, bottomY, topY);
            float radius = GetRadiusAtY(radiusY + boundaryLocalCenterOffset.y);
            Vector2 horizontal = new Vector2(boundarySpace.x, boundarySpace.z);
            float sideDistance = Mathf.Abs(horizontal.magnitude - radius);

            if (bottomDistance <= topDistance && bottomDistance <= sideDistance)
            {
                return Vector3.down;
            }

            if (topDistance <= sideDistance)
            {
                return Vector3.up;
            }

            float slope = (topRadius - bottomRadius) / Height;
            Vector3 radialNormal = horizontal.sqrMagnitude <= Mathf.Epsilon
                ? Vector3.right
                : new Vector3(horizontal.x, 0f, horizontal.y).normalized;

            return new Vector3(radialNormal.x, -slope, radialNormal.z).normalized;
        }

        public Vector3 ClosestPointInsideWithRadius(Vector3 localPosition, float particleRadius)
        {
            Vector3 boundarySpace = ToBoundarySpace(localPosition);
            float safeBottom = bottomY + particleRadius;
            float safeTop = topY - particleRadius;

            if (safeTop < safeBottom)
            {
                float midpoint = (bottomY + topY) * 0.5f;
                safeBottom = midpoint;
                safeTop = midpoint;
            }

            boundarySpace.y = clampTop
                ? Mathf.Clamp(boundarySpace.y, safeBottom, safeTop)
                : Mathf.Max(boundarySpace.y, safeBottom);

            float radiusY = Mathf.Clamp(boundarySpace.y, bottomY, topY);
            float radius = Mathf.Max(0.001f, GetRadiusAtY(radiusY + boundaryLocalCenterOffset.y) - particleRadius);
            Vector2 horizontal = new Vector2(boundarySpace.x, boundarySpace.z);

            if (horizontal.sqrMagnitude > radius * radius)
            {
                horizontal = horizontal.sqrMagnitude <= Mathf.Epsilon
                    ? Vector2.right * radius
                    : horizontal.normalized * radius;
                boundarySpace.x = horizontal.x;
                boundarySpace.z = horizontal.y;
            }

            return FromBoundarySpace(boundarySpace);
        }

        public bool IsInsideWithRadius(Vector3 localPosition, float particleRadius)
        {
            Vector3 closest = ClosestPointInsideWithRadius(localPosition, particleRadius);
            return (closest - localPosition).sqrMagnitude <= 0.000001f;
        }

        [ContextMenu("Apply Safe Default Boundary")]
        public void ApplySafeDefaultBoundary()
        {
            boundaryLocalCenterOffset = Vector3.zero;
            bottomY = -0.75f;
            topY = 0.1f;
            bottomRadius = 0.34f;
            topRadius = 0.47f;
            clampTop = true;
            wallDamping = 0.45f;
            wallFriction = 0.25f;
            drawBoundaryGizmos = true;
            gizmoRingCount = 6;
            OnValidate();
        }

        [ContextMenu("Fit Boundary From Visual Renderer Bounds")]
        public void FitBoundaryFromVisualRendererBounds()
        {
            TryAutoAssignBucketVisualModel();

            if (bucketVisualModel == null)
            {
                Debug.LogWarning(
                    "BucketFluidBoundary could not find a child named 'Bucket'. Drag the visual bucket model into bucketVisualModel.",
                    this
                );
                return;
            }

            Renderer[] renderers = bucketVisualModel.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                Debug.LogWarning("BucketFluidBoundary found no Renderers under bucketVisualModel.", this);
                return;
            }

            List<Vector3> localCorners = new List<Vector3>();
            foreach (Renderer renderer in renderers)
            {
                Bounds bounds = renderer.bounds;
                AddWorldBoundsCorners(localCorners, bounds);
            }

            if (localCorners.Count == 0)
            {
                return;
            }

            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < localCorners.Count; i++)
            {
                Vector3 local = transform.InverseTransformPoint(localCorners[i]);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }

            boundaryLocalCenterOffset = new Vector3((min.x + max.x) * 0.5f, 0f, (min.z + max.z) * 0.5f);

            float visualHeight = Mathf.Max(0.001f, max.y - min.y);
            bottomY = min.y + visualHeight * 0.08f;
            topY = max.y - visualHeight * 0.08f;
            boundaryLocalCenterOffset.y = 0f;

            float estimatedRadius = EstimateRadiusFromLocalPoints(localCorners) * 0.82f;
            bottomRadius = estimatedRadius;
            topRadius = estimatedRadius;

            OnValidate();
            PrintBoundaryValues();
        }

        [ContextMenu("Print Boundary Values")]
        public void PrintBoundaryValues()
        {
            Debug.Log(
                "BucketFluidBoundary values:\n" +
                $"bucketVisualModel: {(bucketVisualModel != null ? bucketVisualModel.name : "None")}\n" +
                $"boundaryLocalCenterOffset: {boundaryLocalCenterOffset}\n" +
                $"bottomY: {bottomY:F4}\n" +
                $"topY: {topY:F4}\n" +
                $"bottomRadius: {bottomRadius:F4}\n" +
                $"topRadius: {topRadius:F4}\n" +
                $"clampTop: {clampTop}\n" +
                $"wallDamping: {wallDamping:F4}\n" +
                $"wallFriction: {wallFriction:F4}",
                this
            );
        }

        private void OnValidate()
        {
            if (topY <= bottomY)
            {
                topY = bottomY + 0.001f;
            }

            bottomRadius = Mathf.Max(0.001f, bottomRadius);
            topRadius = Mathf.Max(0.001f, topRadius);
            wallDamping = Mathf.Clamp01(wallDamping);
            wallFriction = Mathf.Max(0f, wallFriction);
            gizmoRingCount = Mathf.Clamp(gizmoRingCount, 2, 24);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawBoundaryGizmos)
            {
                return;
            }

            Gizmos.color = gizmoColor;

            int rings = Mathf.Max(2, gizmoRingCount);
            for (int i = 0; i < rings; i++)
            {
                float t = rings == 1 ? 0f : i / (float)(rings - 1);
                float y = Mathf.Lerp(bottomY, topY, t) + boundaryLocalCenterOffset.y;
                DrawRing(y, GetRadiusAtY(y));
            }

            DrawSideLines();
            DrawCenterOffset();
        }

        private void TryAutoAssignBucketVisualModel()
        {
            if (bucketVisualModel != null)
            {
                return;
            }

            Transform bucket = transform.Find("Bucket");
            if (bucket == null)
            {
                bucket = FindChildRecursive(transform, "Bucket");
            }

            if (bucket != null)
            {
                bucketVisualModel = bucket;
                return;
            }

            if (!_hasWarnedMissingVisualModel)
            {
                _hasWarnedMissingVisualModel = true;
                Debug.LogWarning(
                    "BucketFluidBoundary could not auto-assign bucketVisualModel. Drag the visual Bucket child into this field.",
                    this
                );
            }
        }

        private Vector3 ToBoundarySpace(Vector3 localPosition)
        {
            return localPosition - boundaryLocalCenterOffset;
        }

        private Vector3 FromBoundarySpace(Vector3 boundarySpacePosition)
        {
            return boundarySpacePosition + boundaryLocalCenterOffset;
        }

        private void DrawRing(float localY, float radius)
        {
            Vector3 previous = TransformLocalPointOnRing(localY, radius, 0f);

            for (int i = 1; i <= RingSegments; i++)
            {
                Vector3 current = TransformLocalPointOnRing(localY, radius, i / (float)RingSegments);
                Gizmos.DrawLine(previous, current);
                previous = current;
            }
        }

        private void DrawSideLines()
        {
            const int sideCount = 8;
            float bottomLocalY = bottomY + boundaryLocalCenterOffset.y;
            float topLocalY = topY + boundaryLocalCenterOffset.y;

            for (int i = 0; i < sideCount; i++)
            {
                float t = i / (float)sideCount;
                Gizmos.DrawLine(
                    TransformLocalPointOnRing(bottomLocalY, bottomRadius, t),
                    TransformLocalPointOnRing(topLocalY, topRadius, t)
                );
            }
        }

        private void DrawCenterOffset()
        {
            Vector3 worldCenter = transform.TransformPoint(boundaryLocalCenterOffset);
            float markerSize = Mathf.Max(0.035f, Mathf.Min(bottomRadius, topRadius) * 0.08f);
            Gizmos.DrawSphere(worldCenter, markerSize);
            Gizmos.DrawLine(
                transform.TransformPoint(new Vector3(boundaryLocalCenterOffset.x, bottomY + boundaryLocalCenterOffset.y, boundaryLocalCenterOffset.z)),
                transform.TransformPoint(new Vector3(boundaryLocalCenterOffset.x, topY + boundaryLocalCenterOffset.y, boundaryLocalCenterOffset.z))
            );
        }

        private Vector3 TransformLocalPointOnRing(float localY, float radius, float normalizedAngle)
        {
            float angle = normalizedAngle * Mathf.PI * 2f;
            Vector3 localPoint = new Vector3(
                boundaryLocalCenterOffset.x + Mathf.Cos(angle) * radius,
                localY,
                boundaryLocalCenterOffset.z + Mathf.Sin(angle) * radius
            );
            return transform.TransformPoint(localPoint);
        }

        private static void AddWorldBoundsCorners(List<Vector3> corners, Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;

            corners.Add(new Vector3(min.x, min.y, min.z));
            corners.Add(new Vector3(min.x, min.y, max.z));
            corners.Add(new Vector3(min.x, max.y, min.z));
            corners.Add(new Vector3(min.x, max.y, max.z));
            corners.Add(new Vector3(max.x, min.y, min.z));
            corners.Add(new Vector3(max.x, min.y, max.z));
            corners.Add(new Vector3(max.x, max.y, min.z));
            corners.Add(new Vector3(max.x, max.y, max.z));
        }

        private float EstimateRadiusFromLocalPoints(List<Vector3> worldPoints)
        {
            float maxRadius = 0.001f;

            for (int i = 0; i < worldPoints.Count; i++)
            {
                Vector3 local = transform.InverseTransformPoint(worldPoints[i]);
                Vector2 horizontal = new Vector2(
                    local.x - boundaryLocalCenterOffset.x,
                    local.z - boundaryLocalCenterOffset.z
                );
                maxRadius = Mathf.Max(maxRadius, horizontal.magnitude);
            }

            return maxRadius;
        }

        private static Transform FindChildRecursive(Transform current, string targetName)
        {
            foreach (Transform child in current)
            {
                if (child.name == targetName)
                {
                    return child;
                }

                Transform match = FindChildRecursive(child, targetName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }
    }
}

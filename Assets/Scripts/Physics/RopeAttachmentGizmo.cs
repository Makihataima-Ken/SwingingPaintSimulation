using UnityEngine;
using SwingingPaint.BucketFluid.Core;

/// <summary>
/// Draws a Scene view marker for the bucket rope attachment point.
/// Visual-only: no Rigidbody, Colliders, joints, raycasts, or Unity physics.
/// </summary>
public class RopeAttachmentGizmo : MonoBehaviour
{
    [Header("References")]
    public Transform anchorTransform;
    public BucketFluidBoundary bucketBoundary;

    [Header("Gizmo Settings")]
    public bool drawGizmo = true;
    public float radius = 0.06f;
    public float directionLength = 0.25f;
    public bool drawLineToAnchor = true;
    public Color gizmoColor = new Color(1f, 0.85f, 0.1f, 1f);

    private void OnDrawGizmos()
    {
        if (!drawGizmo)
        {
            return;
        }

        BucketFluidBoundary boundary = bucketBoundary != null
            ? bucketBoundary
            : GetComponentInParent<BucketFluidBoundary>();

        Vector3 upDirection = boundary != null
            ? boundary.transform.TransformDirection(Vector3.up).normalized
            : transform.TransformDirection(Vector3.up).normalized;

        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(transform.position, radius);
        Gizmos.DrawWireSphere(transform.position, radius * 1.4f);
        Gizmos.DrawLine(transform.position, transform.position + upDirection * directionLength);

        if (drawLineToAnchor && anchorTransform != null)
        {
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.45f);
            Gizmos.DrawLine(transform.position, anchorTransform.position);
        }

        if (boundary != null)
        {
            Vector3 rimCenter = boundary.transform.TransformPoint(boundary.GetTopCenterLocal());
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.6f);
            Gizmos.DrawLine(rimCenter, transform.position);
        }
    }

    private void OnValidate()
    {
        radius = Mathf.Max(0.001f, radius);
        directionLength = Mathf.Max(0f, directionLength);
    }
}

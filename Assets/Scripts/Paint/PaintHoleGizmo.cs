using UnityEngine;
using SwingingPaint.BucketFluid.Core;

/// <summary>
/// Draws a Scene view marker for the PaintHole transform.
///
/// PaintHole is the active paint emission point at the bottom of BucketRig.
/// It is only a marker transform and does not use Rigidbody, Colliders, or Unity physics.
/// </summary>
public class PaintHoleGizmo : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Bucket boundary that owns this PaintHole. Used only for Scene view alignment/reference drawing.")]
    public BucketFluidBoundary bucketBoundary;

    [Header("Gizmo Settings")]
    [Tooltip("Draw the PaintHole marker in the Scene view.")]
    public bool drawGizmo = true;

    [Tooltip("Radius of the PaintHole marker.")]
    public float radius = 0.08f;

    [Tooltip("Length of the downward emission direction line.")]
    public float directionLength = 0.35f;

    [Tooltip("Draw a helper line from the mathematical bucket bottom center to this hole.")]
    public bool drawBoundaryReference = true;

    [Tooltip("Marker color used in the Scene view.")]
    public Color gizmoColor = new Color(1f, 0.25f, 0.05f, 1f);

    private void OnDrawGizmos()
    {
        if (!drawGizmo)
        {
            return;
        }

        BucketFluidBoundary boundary = bucketBoundary != null
            ? bucketBoundary
            : GetComponentInParent<BucketFluidBoundary>();

        Vector3 emissionDirection = boundary != null
            ? boundary.transform.TransformDirection(Vector3.down).normalized
            : transform.TransformDirection(Vector3.down).normalized;

        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(transform.position, radius);
        Gizmos.DrawWireSphere(transform.position, radius * 1.35f);

        Gizmos.DrawLine(transform.position, transform.position + emissionDirection * directionLength);

        if (drawBoundaryReference && boundary != null)
        {
            Vector3 bottomCenter = boundary.transform.TransformPoint(boundary.GetBottomCenterLocal());
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.45f);
            Gizmos.DrawLine(bottomCenter, transform.position);
        }
    }

    private void OnValidate()
    {
        radius = Mathf.Max(0.001f, radius);
        directionLength = Mathf.Max(0f, directionLength);
    }
}

using UnityEngine;

/// <summary>
/// Draws a Scene view marker for the PaintHole transform.
///
/// PaintHole is the future paint emission point at the bottom of BucketRig.
/// It is only a marker transform and does not use Rigidbody, Colliders, or Unity physics.
/// </summary>
public class PaintHoleGizmo : MonoBehaviour
{
    [Header("Gizmo Settings")]
    [Tooltip("Draw the PaintHole marker in the Scene view.")]
    public bool drawGizmo = true;

    [Tooltip("Radius of the PaintHole marker.")]
    public float radius = 0.08f;

    [Tooltip("Length of the downward emission direction line.")]
    public float directionLength = 0.35f;

    [Tooltip("Marker color used in the Scene view.")]
    public Color gizmoColor = new Color(1f, 0.25f, 0.05f, 1f);

    private void OnDrawGizmos()
    {
        if (!drawGizmo)
        {
            return;
        }

        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(transform.position, radius);

        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * directionLength);
    }

    private void OnValidate()
    {
        radius = Mathf.Max(0.001f, radius);
        directionLength = Mathf.Max(0f, directionLength);
    }
}

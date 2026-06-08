using UnityEngine;

/// <summary>
/// Editor-safe scene helper for assigning the Swinging Paint bucket rig references by name.
///
/// Expected hierarchy:
/// SimulationRoot
/// - PivotPoint
/// - Canvas
/// - BucketRig
///   - BucketModel
///   - PaintHole
/// - RopePlaceholder
///
/// BucketRig is the physics/motion point moved by Pendulum. BucketModel is visual only,
/// and PaintHole marks the future emission point near the bottom of the bucket.
/// This helper does not use Rigidbody, Colliders, or Unity physics.
/// </summary>
[ExecuteAlways]
public class BucketRigSceneSetup : MonoBehaviour
{
    [Tooltip("Automatically reassign references when values change in the editor.")]
    public bool autoAssignOnValidate = true;

    [ContextMenu("Assign Bucket Rig References")]
    public void AssignReferences()
    {
        AssignReferences(createMissingRopeRenderer: true);
    }

    private void AssignReferences(bool createMissingRopeRenderer)
    {
        Transform root = transform.name == "SimulationRoot" ? transform : FindChildOrSelf(transform, "SimulationRoot");
        if (root == null)
        {
            root = transform;
        }

        Transform pivotPoint = root.Find("PivotPoint");
        Transform bucketRig = root.Find("BucketRig");
        Transform ropePlaceholder = root.Find("RopePlaceholder");

        Pendulum pendulum = root.GetComponent<Pendulum>();
        if (pendulum != null)
        {
            pendulum.anchorTransform = pivotPoint;
            pendulum.bucketTransform = bucketRig;
        }

        if (ropePlaceholder != null)
        {
            RopeRenderer ropeRenderer = ropePlaceholder.GetComponent<RopeRenderer>();
            if (ropeRenderer == null && createMissingRopeRenderer)
            {
                ropeRenderer = ropePlaceholder.gameObject.AddComponent<RopeRenderer>();
            }

            if (ropeRenderer != null)
            {
                ropeRenderer.anchorTransform = pivotPoint;
                ropeRenderer.bucketTransform = bucketRig;
                ropeRenderer.pendulum = pendulum;
            }
        }
    }

    private void OnValidate()
    {
        if (autoAssignOnValidate)
        {
            AssignReferences(createMissingRopeRenderer: false);
        }
    }

    private static Transform FindChildOrSelf(Transform current, string targetName)
    {
        if (current.name == targetName)
        {
            return current;
        }

        foreach (Transform child in current)
        {
            Transform match = FindChildOrSelf(child, targetName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }
}

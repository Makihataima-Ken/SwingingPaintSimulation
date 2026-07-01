using UnityEngine;
using SwingingPaint.BucketFluid;
using SwingingPaint.BucketFluid.Core;
using SwingingPaint.BucketFluid.Rendering;
using SwingingPaint.Paint;

/// <summary>
/// Editor-safe scene helper for assigning the Swinging Paint bucket rig references by name.
///
/// Expected hierarchy:
/// SimulationRoot
/// - PivotPoint
/// - Canvas
/// - BucketRig
///   - BucketModel
///   - RopeAttachment
///   - PaintHole
/// - RopePlaceholder
///
/// BucketRig is the physics/motion point moved by Pendulum. BucketModel is visual only,
/// RopeAttachment is the visual rope endpoint, and PaintHole marks the future emission point near the bottom of the bucket.
/// This helper does not use Rigidbody, Colliders, or Unity physics.
/// </summary>
[ExecuteAlways]
public class BucketRigSceneSetup : MonoBehaviour
{
    [Tooltip("Automatically reassign references when values change in the editor.")]
    public bool autoAssignOnValidate = true;

    [Tooltip("Keep PaintHole and RopeAttachment positions aligned to BucketFluidBoundary.")]
    public bool alignMarkersToBoundary = true;

    [Tooltip("How far below the mathematical bucket bottom the PaintHole emission origin sits.")]
    public float paintHoleOffsetBelowBottom = 0.025f;

    private bool _assignQueued;

    private void OnEnable()
    {
        AssignReferences(createMissingRopeRenderer: true, createMissingMarkers: true);
    }

    [ContextMenu("Assign Bucket Rig References")]
    public void AssignReferences()
    {
        AssignReferences(createMissingRopeRenderer: true, createMissingMarkers: true);
    }

    private void AssignReferences(bool createMissingRopeRenderer, bool createMissingMarkers)
    {
        Transform root = transform.name == "SimulationRoot" ? transform : FindChildOrSelf(transform, "SimulationRoot");
        if (root == null)
        {
            root = transform;
        }

        Transform pivotPoint = root.Find("PivotPoint");
        Transform bucketRig = root.Find("BucketRig");
        Transform ropePlaceholder = root.Find("RopePlaceholder");
        Transform bucketVisual = bucketRig != null ? bucketRig.Find("Bucket") : null;
        BucketFluidBoundary boundary = bucketRig != null ? bucketRig.GetComponent<BucketFluidBoundary>() : null;
        Transform paintHole = bucketRig != null
            ? EnsurePaintHole(bucketRig, boundary, alignMarkersToBoundary, paintHoleOffsetBelowBottom, createMissingMarkers)
            : null;
        Transform ropeAttachment = bucketRig != null
            ? EnsureRopeAttachment(bucketRig, boundary, alignMarkersToBoundary, createMissingMarkers)
            : null;

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
                ropeRenderer.attachmentTransform = ropeAttachment;
                ropeRenderer.pendulum = pendulum;
            }
        }

        ConfigureMarkerGizmos(boundary, paintHole, ropeAttachment, pivotPoint, createMissingMarkers);

        if (bucketRig != null)
        {
            EnsureBucketVisual(bucketRig, bucketVisual);
            EnsureBucketOrientationController(
                bucketRig,
                pivotPoint,
                ropeAttachment,
                bucketVisual,
                paintHole,
                createMissingMarkers
            );
        }
    }

    private static void EnsureBucketVisual(Transform bucketRig, Transform importedBucketVisual)
    {
        bool importedBucketHasRenderer = importedBucketVisual != null &&
                                         importedBucketVisual.GetComponentsInChildren<Renderer>(true).Length > 0;

        Transform fallback = bucketRig.Find("ProceduralBucketFallback");

        if (importedBucketHasRenderer)
        {
            if (fallback != null)
            {
                fallback.gameObject.SetActive(false);
            }

            foreach (Renderer renderer in importedBucketVisual.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
            }

            return;
        }

        if (fallback == null)
        {
            GameObject fallbackObject = new GameObject("ProceduralBucketFallback");
            fallbackObject.transform.SetParent(bucketRig, false);
            fallback = fallbackObject.transform;
        }

        fallback.gameObject.SetActive(true);
        fallback.localPosition = Vector3.zero;
        fallback.localRotation = Quaternion.identity;
        fallback.localScale = Vector3.one;

        ProceduralBucketVisual proceduralBucket = fallback.GetComponent<ProceduralBucketVisual>();
        if (proceduralBucket == null)
        {
            proceduralBucket = fallback.gameObject.AddComponent<ProceduralBucketVisual>();
        }

        proceduralBucket.boundary = bucketRig.GetComponent<BucketFluidBoundary>();
        proceduralBucket.Rebuild();
    }

    private static Transform EnsurePaintHole(
        Transform bucketRig,
        BucketFluidBoundary boundary,
        bool alignToBoundary,
        float offsetBelowBottom,
        bool createMissing
    )
    {
        Transform paintHole = bucketRig.Find("PaintHole");
        if (paintHole == null)
        {
            if (!createMissing)
            {
                return null;
            }

            GameObject paintHoleObject = new GameObject("PaintHole");
            paintHole = paintHoleObject.transform;
            paintHole.SetParent(bucketRig, false);
        }

        paintHole.localRotation = Quaternion.identity;
        paintHole.localScale = Vector3.one;

        if (alignToBoundary && boundary != null)
        {
            paintHole.localPosition = boundary.GetPaintHoleLocal(offsetBelowBottom);
        }

        return paintHole;
    }

    private static Transform EnsureRopeAttachment(
        Transform bucketRig,
        BucketFluidBoundary boundary,
        bool alignToBoundary,
        bool createMissing
    )
    {
        Transform existing = bucketRig.Find("RopeAttachment");
        if (existing != null)
        {
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;

            if (alignToBoundary && boundary != null)
            {
                existing.localPosition = boundary.GetRopeAttachmentLocal();
            }

            return existing;
        }

        if (!createMissing)
        {
            return null;
        }

        GameObject attachmentObject = new GameObject("RopeAttachment");
        Transform attachment = attachmentObject.transform;
        attachment.SetParent(bucketRig, false);
        attachment.localRotation = Quaternion.identity;
        attachment.localScale = Vector3.one;

        if (alignToBoundary && boundary != null)
        {
            attachment.localPosition = boundary.GetRopeAttachmentLocal();
        }
        else
        {
            attachment.localPosition = Vector3.zero;
        }

        return attachment;
    }

    private static BucketOrientationController EnsureBucketOrientationController(
        Transform bucketRig,
        Transform pivotPoint,
        Transform ropeAttachment,
        Transform bucketVisual,
        Transform paintHole,
        bool createMissing
    )
    {
        if (bucketRig == null)
        {
            return null;
        }

        BucketOrientationController orientationController = bucketRig.GetComponent<BucketOrientationController>();
        if (orientationController == null)
        {
            if (!createMissing)
            {
                return null;
            }

            orientationController = bucketRig.gameObject.AddComponent<BucketOrientationController>();
        }

        if (bucketVisual == null)
        {
            bucketVisual = bucketRig.Find("Bucket");
        }

        if (bucketVisual == null)
        {
            bucketVisual = bucketRig.Find("ProceduralBucketFallback");
        }

        orientationController.pivotPoint = pivotPoint;
        orientationController.bucketRig = bucketRig;
        orientationController.ropeAttachment = ropeAttachment;
        orientationController.bucketVisualRoot = bucketVisual;
        orientationController.paintHole = paintHole;

        if (orientationController.bucketLocalUpAxis.sqrMagnitude <= 0.000001f)
        {
            orientationController.bucketLocalUpAxis = Vector3.up;
        }

        if (orientationController.bucketLocalForwardAxis.sqrMagnitude <= 0.000001f)
        {
            orientationController.bucketLocalForwardAxis = Vector3.forward;
        }

        return orientationController;
    }

    private static void ConfigureMarkerGizmos(
        BucketFluidBoundary boundary,
        Transform paintHole,
        Transform ropeAttachment,
        Transform pivotPoint,
        bool createMissing
    )
    {
        if (paintHole != null)
        {
            ConfigurePaintEmitter(boundary, paintHole, createMissing);

            PaintHoleGizmo paintHoleGizmo = paintHole.GetComponent<PaintHoleGizmo>();
            if (paintHoleGizmo == null)
            {
                if (createMissing)
                {
                    paintHoleGizmo = paintHole.gameObject.AddComponent<PaintHoleGizmo>();
                }
            }

            if (paintHoleGizmo != null)
            {
                paintHoleGizmo.bucketBoundary = boundary;
            }
        }

        if (ropeAttachment != null)
        {
            RopeAttachmentGizmo ropeAttachmentGizmo = ropeAttachment.GetComponent<RopeAttachmentGizmo>();
            if (ropeAttachmentGizmo == null)
            {
                if (createMissing)
                {
                    ropeAttachmentGizmo = ropeAttachment.gameObject.AddComponent<RopeAttachmentGizmo>();
                }
            }

            if (ropeAttachmentGizmo != null)
            {
                ropeAttachmentGizmo.bucketBoundary = boundary;
                ropeAttachmentGizmo.anchorTransform = pivotPoint;
            }
        }
    }

    private static void ConfigurePaintEmitter(
        BucketFluidBoundary boundary,
        Transform paintHole,
        bool createMissing
    )
    {
        PaintEmitter paintEmitter = paintHole.GetComponent<PaintEmitter>();
        if (paintEmitter == null)
        {
            if (!createMissing)
            {
                return;
            }

            paintEmitter = paintHole.gameObject.AddComponent<PaintEmitter>();
        }

        Transform bucketRig = boundary != null ? boundary.transform : paintHole.parent;
        paintEmitter.paintHoleTransform = paintHole;
        paintEmitter.bucketTransform = bucketRig;
        paintEmitter.boundary = boundary;
        paintEmitter.fluidSettings = bucketRig != null ? bucketRig.GetComponent<BucketFluidSettings>() : null;
        paintEmitter.motionProvider = bucketRig != null ? bucketRig.GetComponent<BucketMotionProvider>() : null;

        GPUFluidOutflowController outflowController = paintHole.GetComponent<GPUFluidOutflowController>();
        if (outflowController == null && createMissing)
        {
            outflowController = paintHole.gameObject.AddComponent<GPUFluidOutflowController>();
        }

        if (outflowController != null)
        {
            outflowController.paintHoleTransform = paintHole;
            outflowController.simulator = bucketRig != null ? bucketRig.GetComponent<GPUFluidSimulator>() : null;
            outflowController.settings = bucketRig != null ? bucketRig.GetComponent<BucketFluidSettings>() : null;
            outflowController.motionProvider = bucketRig != null ? bucketRig.GetComponent<BucketMotionProvider>() : null;
            outflowController.boundary = boundary;
        }

        GPUOutflowRenderer outflowRenderer = paintHole.GetComponent<GPUOutflowRenderer>();
        if (outflowRenderer == null && createMissing)
        {
            outflowRenderer = paintHole.gameObject.AddComponent<GPUOutflowRenderer>();
        }

        if (outflowRenderer != null)
        {
            outflowRenderer.outflowController = outflowController;
        }
    }

    private void OnValidate()
    {
        paintHoleOffsetBelowBottom = Mathf.Max(0f, paintHoleOffsetBelowBottom);

        if (autoAssignOnValidate)
        {
            QueueEditorAssignReferences();
        }
    }

    private void QueueEditorAssignReferences()
    {
#if UNITY_EDITOR
        if (_assignQueued)
        {
            return;
        }

        _assignQueued = true;
        UnityEditor.EditorApplication.delayCall += AssignReferencesAfterValidation;
        return;
#endif

        AssignReferences(createMissingRopeRenderer: false, createMissingMarkers: false);
    }

#if UNITY_EDITOR
    private void AssignReferencesAfterValidation()
    {
        if (this == null)
        {
            return;
        }

        _assignQueued = false;
        AssignReferences(createMissingRopeRenderer: false, createMissingMarkers: false);
    }
#endif

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

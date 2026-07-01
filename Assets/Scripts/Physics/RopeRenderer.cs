using UnityEngine;

/// <summary>
/// Draws a visual rope between an anchor point and a bucket attachment point using a LineRenderer.
///
/// This script is visual only. It does not use Rigidbody, Colliders, joints,
/// raycasts, or Unity's built-in physics engine.
/// BucketRig is the pendulum motion point; RopeAttachment is the visual endpoint on the bucket.
///
/// Because the Pendulum now moves BucketRig to the dynamic (stretched) rope length, the line
/// between the anchor and the bucket already grows and shrinks on its own. This component is
/// extended (not replaced) with optional stretch feedback: as the rope extends beyond its rest
/// length it can thin out and tint toward a "stretched" colour, making the elasticity visible.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(LineRenderer))]
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
    [Tooltip("Width of the rope line at rest length.")]
    public float ropeWidth = 0.12f;

    [Tooltip("Optional material used by the LineRenderer.")]
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

    private LineRenderer _lineRenderer;

    private void Awake()
    {
        _lineRenderer = GetOrCreateLineRenderer();
        ResolveReferences();
        ConfigureLineRenderer();
    }

    private void Update()
    {
        ResolveReferences();

        if (_lineRenderer == null)
        {
            _lineRenderer = GetOrCreateLineRenderer();
            ConfigureLineRenderer();
        }

        Transform ropeEnd = GetRopeEndTransform();
        if (anchorTransform == null || ropeEnd == null)
        {
            return;
        }

        if (pendulum != null && pendulum.RopePointCount > 1)
        {
            int pointCount = pendulum.RopePointCount;
            if (_lineRenderer.positionCount != pointCount)
            {
                _lineRenderer.positionCount = pointCount;
            }

            for (int i = 0; i < pointCount; i++)
            {
                _lineRenderer.SetPosition(i, pendulum.GetRopePoint(i));
            }
        }
        else
        {
            // Fallback for edit mode or scenes that still use only anchor/bucket transforms.
            if (_lineRenderer.positionCount != 2)
            {
                _lineRenderer.positionCount = 2;
            }

            _lineRenderer.SetPosition(0, anchorTransform.position);
            _lineRenderer.SetPosition(1, bucketTransform.position);
        }

        ApplyStretchFeedback();
    }

    /// <summary>
    /// Drives line width and colour from how far the rope is currently stretched beyond rest length.
    /// No-op (and resets to the slack look) when stretch feedback is disabled or no pendulum is known.
    /// </summary>
    private void ApplyStretchFeedback()
    {
        if (!reflectStretch || pendulum == null)
        {
            _lineRenderer.startWidth = ropeWidth;
            _lineRenderer.endWidth = ropeWidth;
            return;
        }

        // Tension ratio comes from the custom pendulum spring force. Fall back to extension ratio
        // if an older pendulum state is present before the first simulation step.
        float denominator = Mathf.Max(0.0001f, fullStretchRatio);
        float extensionRatio = pendulum.RestLength > 0f
            ? Mathf.Clamp01((pendulum.CurrentRopeLength / pendulum.RestLength - 1f) / denominator)
            : 0f;
        float tensionRatio = Mathf.Max(extensionRatio, pendulum.NormalizedRopeTension);

        float width = ropeWidth * Mathf.Lerp(1f, stretchedWidthScale, tensionRatio);
        _lineRenderer.startWidth = width;
        _lineRenderer.endWidth = width;

        Color color = Color.Lerp(slackColor, stretchedColor, tensionRatio);
        _lineRenderer.startColor = color;
        _lineRenderer.endColor = color;
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

    private void ConfigureLineRenderer()
    {
        if (_lineRenderer == null)
        {
            return;
        }

        _lineRenderer.positionCount = 2;
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.startWidth = ropeWidth;
        _lineRenderer.endWidth = ropeWidth;

        if (ropeMaterial != null)
        {
            _lineRenderer.material = ropeMaterial;
        }
    }

    private LineRenderer GetOrCreateLineRenderer()
    {
        LineRenderer lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
        }

        return lineRenderer;
    }

    private void OnValidate()
    {
        ropeWidth = Mathf.Max(0.001f, ropeWidth);
        fullStretchRatio = Mathf.Max(0.01f, fullStretchRatio);
        stretchedWidthScale = Mathf.Clamp(stretchedWidthScale, 0.1f, 1f);

        LineRenderer lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            lineRenderer.startWidth = ropeWidth;
            lineRenderer.endWidth = ropeWidth;
        }
    }
}

using UnityEngine;

/// <summary>
/// Draws a visual rope between an anchor point and BucketRig using a LineRenderer.
///
/// This script is visual only. It does not use Rigidbody, Colliders, joints,
/// raycasts, or Unity's built-in physics engine.
/// BucketRig is the pendulum motion point; BucketModel is only the visual child.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(LineRenderer))]
public class RopeRenderer : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Anchor point where the rope starts. If empty, this GameObject is used.")]
    public Transform anchorTransform;

    [Tooltip("BucketRig transform where the rope ends. Do not assign the visual BucketModel child.")]
    public Transform bucketTransform;

    [Tooltip("Optional pendulum reference used to auto-fill anchor and bucket transforms.")]
    public Pendulum pendulum;

    [Header("Visual Settings")]
    [Tooltip("Width of the rope line.")]
    public float ropeWidth = 0.04f;

    [Tooltip("Optional material used by the LineRenderer.")]
    public Material ropeMaterial;

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

        if (anchorTransform == null || bucketTransform == null)
        {
            return;
        }

        // Draw the rope visually by connecting the anchor and bucket positions.
        _lineRenderer.SetPosition(0, anchorTransform.position);
        _lineRenderer.SetPosition(1, bucketTransform.position);
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

        if (anchorTransform == null)
        {
            anchorTransform = transform;
        }
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

        LineRenderer lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            lineRenderer.startWidth = ropeWidth;
            lineRenderer.endWidth = ropeWidth;
        }
    }
}

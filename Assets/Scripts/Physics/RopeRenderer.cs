using UnityEngine;

/// <summary>
/// Renders a rope between a pivot point and a target transform using a LineRenderer.
/// This script visualizes the connection between the pivot (where this component is attached)
/// and the moving bucket controlled by the pendulum simulation.
///
/// Design decisions:
/// - Dynamically adjusts the rope width to visually convey rope tension when elasticity is enabled.
/// - Reads from Pendulum to get the current stretched length, enabling visual feedback of stretch.
/// - Keeps the rope rendering independent from the physics logic.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class RopeRenderer : MonoBehaviour
{
    [Header("Target Settings")]
    [Tooltip("The transform of the bucket at the end of the rope. If not set, will attempt to find a child named 'Bucket'.")]
    public Transform bucket;

    [Header("Visual Settings")]
    [Tooltip("Width of the rope.")]
    public float ropeWidth = 0.05f;

    [Tooltip("Material for the rope.")]
    public Material ropeMaterial;

    [Header("Elastic Visuals")]
    [Tooltip("Reference to the Pendulum component to read current rope length.")]
    public Pendulum pendulum;

    [Tooltip("Scale factor for how much the rope thins when stretched. Higher = more noticeable.")]
    public float stretchVisualScale = 0.5f;

    [Tooltip("Minimum rope width allowed to prevent invisibility.")]
    public float minRopeWidth = 0.005f;

    private LineRenderer _lineRenderer;
    private float _baseRopeWidth;

    void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();

        if (_lineRenderer == null)
        {
            Debug.LogError("RopeRenderer requires a LineRenderer component.", this);
            enabled = false;
            return;
        }

        if (bucket == null)
        {
            bucket = transform.Find("Bucket");
            if (bucket == null)
            {
                Debug.LogWarning("RopeRenderer: No bucket assigned and no child named 'Bucket' found.", this);
            }
        }

        if (pendulum == null)
        {
            pendulum = FindObjectOfType<Pendulum>();
        }

        _baseRopeWidth = ropeWidth;
    }

    void Start()
    {
        ConfigureLineRenderer();
    }

    void Update()
    {
        if (bucket == null || _lineRenderer == null)
            return;

        UpdateRopePositions();
        UpdateElasticVisuals();
    }

    /// <summary>
    /// Configures the LineRenderer with the correct number of points and appearance settings.
    /// </summary>
    private void ConfigureLineRenderer()
    {
        _lineRenderer.positionCount = 2;
        _lineRenderer.startWidth = ropeWidth;
        _lineRenderer.endWidth = ropeWidth;
        _lineRenderer.useWorldSpace = true;

        if (ropeMaterial != null)
        {
            _lineRenderer.material = ropeMaterial;
        }
    }

    /// <summary>
    /// Updates the LineRenderer positions to connect the pivot (this transform) to the bucket.
    /// </summary>
    private void UpdateRopePositions()
    {
        _lineRenderer.SetPosition(0, transform.position);
        _lineRenderer.SetPosition(1, bucket.position);
    }

    /// <summary>
    /// Adjusts the rope width based on elastic stretch for visual feedback.
    /// As the rope stretches, it becomes thinner to simulate tension.
    /// </summary>
    private void UpdateElasticVisuals()
    {
        if (pendulum == null)
            return;

        // Calculate a thinness factor based on stretch ratio
        float restLength = 1f; // Default if pendulum settings are unavailable
        float currentLength = pendulum.CurrentRopeLength;

        if (pendulum.settings != null)
        {
            restLength = pendulum.settings.RestLength;
        }

        if (restLength > 0f)
        {
            float stretchRatio = currentLength / restLength;
            float widthFactor = 1f - ((stretchRatio - 1f) * stretchVisualScale);
            float newWidth = Mathf.Max(minRopeWidth, _baseRopeWidth * widthFactor);

            _lineRenderer.startWidth = newWidth;
            _lineRenderer.endWidth = newWidth;
        }
    }

    /// <summary>
    /// Updates the rope width at runtime (useful for inspector changes or dynamic effects).
    /// </summary>
    public void SetRopeWidth(float width)
    {
        ropeWidth = width;
        _baseRopeWidth = width;
        if (_lineRenderer != null)
        {
            _lineRenderer.startWidth = width;
            _lineRenderer.endWidth = width;
        }
    }
}

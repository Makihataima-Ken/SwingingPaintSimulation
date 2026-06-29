using UnityEngine;

/// <summary>
/// Optional helper for a PhysicsTest scene that applies simple pendulum test presets.
///
/// This script does not use Rigidbody, Colliders, or Unity physics. It only writes values
/// into Pendulum and resets the manual pendulum state so bucket motion can be observed.
/// </summary>
public class PendulumPhysicsTestController : MonoBehaviour
{
    [System.Serializable]
    public struct PendulumTestPreset
    {
        public string name;
        public float ropeLength;
        public float initialAngleDegrees;
        public float initialAngularVelocity;
        public float directionAngleDegrees;
        public float damping;
        public float gravity;
    }

    [Header("References")]
    public Pendulum pendulum;

    [Header("Test Controls")]
    [Tooltip("Apply this preset index on Start. Use -1 to leave the Pendulum unchanged.")]
    public int applyPresetOnStart = 0;

    [Tooltip("Press number keys 1-6 in Play Mode to apply the matching preset.")]
    public bool enableNumberKeyShortcuts = true;

    [Header("Presets")]
    public PendulumTestPreset[] presets =
    {
        new PendulumTestPreset
        {
            name = "Short Rope",
            ropeLength = 1.5f,
            initialAngleDegrees = 30f,
            initialAngularVelocity = 0f,
            directionAngleDegrees = 0f,
            damping = 0.05f,
            gravity = 9.81f
        },
        new PendulumTestPreset
        {
            name = "Long Rope",
            ropeLength = 4f,
            initialAngleDegrees = 30f,
            initialAngularVelocity = 0f,
            directionAngleDegrees = 0f,
            damping = 0.05f,
            gravity = 9.81f
        },
        new PendulumTestPreset
        {
            name = "15 Degree Angle",
            ropeLength = 2.5f,
            initialAngleDegrees = 15f,
            initialAngularVelocity = 0f,
            directionAngleDegrees = 0f,
            damping = 0.05f,
            gravity = 9.81f
        },
        new PendulumTestPreset
        {
            name = "45 Degree Angle",
            ropeLength = 2.5f,
            initialAngleDegrees = 45f,
            initialAngularVelocity = 0f,
            directionAngleDegrees = 0f,
            damping = 0.05f,
            gravity = 9.81f
        },
        new PendulumTestPreset
        {
            name = "Low Damping",
            ropeLength = 2.5f,
            initialAngleDegrees = 30f,
            initialAngularVelocity = 0f,
            directionAngleDegrees = 0f,
            damping = 0.01f,
            gravity = 9.81f
        },
        new PendulumTestPreset
        {
            name = "High Damping",
            ropeLength = 2.5f,
            initialAngleDegrees = 30f,
            initialAngularVelocity = 0f,
            directionAngleDegrees = 0f,
            damping = 0.35f,
            gravity = 9.81f
        }
    };

    private void Awake()
    {
        if (pendulum == null)
        {
            pendulum = FindObjectOfType<Pendulum>();
        }
    }

    private void Start()
    {
        if (applyPresetOnStart >= 0)
        {
            ApplyPreset(applyPresetOnStart);
        }
    }

    private void Update()
    {
        if (!enableNumberKeyShortcuts)
        {
            return;
        }

        for (int i = 0; i < presets.Length && i < 9; i++)
        {
            KeyCode key = (KeyCode)((int)KeyCode.Alpha1 + i);
            if (Input.GetKeyDown(key))
            {
                ApplyPreset(i);
            }
        }
    }

    public void ApplyPreset(int presetIndex)
    {
        if (pendulum == null || presets == null || presetIndex < 0 || presetIndex >= presets.Length)
        {
            return;
        }

        PendulumTestPreset preset = presets[presetIndex];

        pendulum.ropeLength = Mathf.Max(0.01f, preset.ropeLength);
        pendulum.motionMode = Pendulum.MotionMode.PlanarPendulum;
        pendulum.initialAngleDegrees = preset.initialAngleDegrees;
        pendulum.initialAngularVelocity = preset.initialAngularVelocity;
        pendulum.initialLateralAngularVelocityDegrees = 0f;
        pendulum.directionAngleDegrees = preset.directionAngleDegrees;
        pendulum.swingDirectionDegrees = preset.directionAngleDegrees;
        pendulum.damping = Mathf.Max(0f, preset.damping);
        pendulum.gravity = Mathf.Max(0f, preset.gravity);
        pendulum.ResetState(preset.initialAngleDegrees, preset.initialAngularVelocity);

        Debug.Log($"Applied pendulum test preset: {preset.name}", this);
    }
}

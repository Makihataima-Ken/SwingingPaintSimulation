using UnityEngine;

namespace SwingingPaint.Core
{
    /// <summary>
    /// Keeps a camera aimed at BucketRig so the Game view follows the swinging bucket and fluid.
    ///
    /// This is a visual tracking helper only. It reads and writes Transforms and does not use
    /// Rigidbody, Colliders, or Unity physics.
    /// </summary>
    [ExecuteAlways]
    public class BucketTrackingCamera : MonoBehaviour
    {
        [Header("Tracking")]
        [Tooltip("Usually BucketRig. The camera follows and looks at this transform.")]
        public Transform target;

        [Tooltip("World-space offset from the target.")]
        public Vector3 offset = new Vector3(0f, 6f, 0f);

        [Tooltip("Local visual offset from the target point the camera looks toward.")]
        public Vector3 lookAtOffset = new Vector3(0f, -0.25f, 0f);

        [Tooltip("World direction used as the top of the screen. Use +Z for a straight top-down view.")]
        public Vector3 cameraUpHint = Vector3.forward;

        [Header("Smoothing")]
        public bool followInEditMode = true;
        public float positionSharpness = 10f;
        public float rotationSharpness = 12f;

        private void LateUpdate()
        {
            if (target == null || (!Application.isPlaying && !followInEditMode))
            {
                return;
            }

            Vector3 desiredPosition = target.position + offset;
            Vector3 lookTarget = target.position + lookAtOffset;
            Quaternion desiredRotation = GetLookRotation(desiredPosition, lookTarget);

            if (Application.isPlaying)
            {
                float positionBlend = 1f - Mathf.Exp(-positionSharpness * Time.deltaTime);
                float rotationBlend = 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, desiredPosition, positionBlend);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationBlend);
            }
            else
            {
                transform.position = desiredPosition;
                transform.rotation = desiredRotation;
            }
        }

        private void OnValidate()
        {
            positionSharpness = Mathf.Max(0f, positionSharpness);
            rotationSharpness = Mathf.Max(0f, rotationSharpness);

            if (cameraUpHint.sqrMagnitude <= 0.0001f)
            {
                cameraUpHint = Vector3.forward;
            }
        }

        private Quaternion GetLookRotation(Vector3 desiredPosition, Vector3 lookTarget)
        {
            Vector3 forward = lookTarget - desiredPosition;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                return transform.rotation;
            }

            forward.Normalize();

            Vector3 up = cameraUpHint.sqrMagnitude > 0.0001f ? cameraUpHint.normalized : Vector3.forward;
            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.98f)
            {
                up = Vector3.forward;
            }

            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.98f)
            {
                up = Vector3.right;
            }

            return Quaternion.LookRotation(forward, up);
        }
    }
}

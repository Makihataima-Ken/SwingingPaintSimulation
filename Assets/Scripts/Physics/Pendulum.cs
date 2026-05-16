using UnityEngine;

public class Pendulum : MonoBehaviour
{
    public Transform bucket;

    public float length = 2f;
    public float gravity = 9.81f;
    public float damping = 0.05f;

    public float angle = 30f;
    public float angularVelocity = 0f;

    public float direction = 0f;

    void Update()
    {
        float dt = Time.deltaTime;

        float theta = angle * Mathf.Deg2Rad;

        float angularAccel =
            -(gravity / length) * Mathf.Sin(theta)
            - damping * angularVelocity;

        angularVelocity += angularAccel * dt;
        theta += angularVelocity * dt;

        angle = theta * Mathf.Rad2Deg;

        float x =
            length * Mathf.Sin(theta) * Mathf.Cos(direction);

        float z =
            length * Mathf.Sin(theta) * Mathf.Sin(direction);

        float y =
            -length * Mathf.Cos(theta);

        bucket.localPosition =
            new Vector3(x, y, z);
    }
}
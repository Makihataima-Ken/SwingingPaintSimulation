using System.Collections.Generic;
using UnityEngine;

namespace SwingingPaint.Physics
{
    /// <summary>
    /// A fully manual 3D mass-spring rope: a chain of point-mass particles connected by
    /// damped Hooke springs (F = -k * x - c * v). No Rigidbody / Joint / Unity physics is used.
    ///
    /// The rope is "built from tiny springs" the same way the fluid is built from tiny particles:
    /// each segment between two neighbouring particles is an individual damped spring, and an
    /// optional second set of "bending" springs (particle i to i+2) gives the rope stiffness so
    /// it behaves like a real rope instead of a loose string of beads.
    ///
    /// Everything is integrated in full 3D, so the pendulum can swing in ANY direction; gravity
    /// is an arbitrary 3D vector and there is no constrained 2D swing plane.
    /// </summary>
    public class SpringRope
    {
        public struct Particle
        {
            public Vector3 Position;
            public Vector3 PrevPosition;
            public Vector3 Velocity;
            public float Mass;
            public float InvMass; // 0 => pinned (immovable)
        }

        // ---- Configuration (refreshed by the owner before each Step) ----
        public Vector3 GravityAccel = new Vector3(0f, -9.81f, 0f);
        public float SegmentRestLength = 0.2f;
        public float SegmentStiffness = 200f;            // per-segment Hooke k
        public float SegmentDamping = 0.5f;              // per-segment relative-velocity damping c
        public float BendingStiffness = 0f;              // i -> i+2 springs (rope rigidity)
        public float CompressionResistance = 0.1f;       // 0 = pure rope (sags), 1 = stiff rod
        public float AirDrag = 0.05f;                    // linear velocity damping per second
        public float MaxSpeed = 50f;                     // hard per-particle speed cap
        public float MaxSegmentStretchMultiplier = 1.5f; // safety clamp vs SegmentRestLength
        public int ConstraintIterations = 2;             // position relaxation passes (safety only)

        private readonly List<Particle> _particles = new List<Particle>();

        public int Count => _particles.Count;
        public IReadOnlyList<Particle> Particles => _particles;

        public Vector3 AnchorPosition => _particles.Count > 0 ? _particles[0].Position : Vector3.zero;
        public Vector3 EndPosition => _particles.Count > 0 ? _particles[_particles.Count - 1].Position : Vector3.zero;
        public Vector3 EndVelocity => _particles.Count > 0 ? _particles[_particles.Count - 1].Velocity : Vector3.zero;

        /// <summary>Straight-line distance from the anchor to the bucket end.</summary>
        public float EndToAnchorDistance => (EndPosition - AnchorPosition).magnitude;

        /// <summary>Summed length of every segment (the rope's true contour length).</summary>
        public float ContourLength
        {
            get
            {
                float total = 0f;
                for (int i = 1; i < _particles.Count; i++)
                {
                    total += (_particles[i].Position - _particles[i - 1].Position).magnitude;
                }

                return total;
            }
        }

        /// <summary>
        /// (Re)builds the rope as a straight line of particleCount particles from anchor to end.
        /// Particle 0 is pinned to the anchor; extraEndMass is added to the final (bucket) particle.
        /// </summary>
        public void Build(Vector3 anchor, Vector3 end, int particleCount, float ropeMass, float extraEndMass, Vector3 initialVelocityAtEnd)
        {
            particleCount = Mathf.Max(2, particleCount);
            _particles.Clear();

            float perParticleMass = ropeMass / particleCount;
            for (int i = 0; i < particleCount; i++)
            {
                float t = (float)i / (particleCount - 1);
                Vector3 pos = Vector3.Lerp(anchor, end, t);

                float mass = perParticleMass;
                if (i == particleCount - 1)
                {
                    mass += extraEndMass;
                }

                bool pinned = (i == 0);
                // Initial velocity ramps from 0 at the anchor to the full value at the bucket,
                // approximating a rigid initial swing before the springs take over.
                Vector3 vel = pinned ? Vector3.zero : initialVelocityAtEnd * t;

                _particles.Add(new Particle
                {
                    Position = pos,
                    PrevPosition = pos,
                    Velocity = vel,
                    Mass = mass,
                    InvMass = pinned ? 0f : 1f / Mathf.Max(1e-4f, mass)
                });
            }
        }

        /// <summary>Pins particle 0 to the live anchor position (the anchor may move/be dragged).</summary>
        public void SetAnchor(Vector3 anchor)
        {
            if (_particles.Count == 0)
            {
                return;
            }

            var p = _particles[0];
            p.Position = anchor;
            p.PrevPosition = anchor;
            p.Velocity = Vector3.zero;
            p.InvMass = 0f;
            _particles[0] = p;
        }

        /// <summary>
        /// Moves the final rope particle. When pinned is true it behaves like a kinematic grab point;
        /// when false it is released back to the spring simulation with the supplied velocity.
        /// </summary>
        public void SetEnd(Vector3 position, Vector3 velocity, bool pinned)
        {
            if (_particles.Count == 0)
            {
                return;
            }

            int index = _particles.Count - 1;
            var p = _particles[index];
            p.Position = position;
            p.PrevPosition = position;
            p.Velocity = velocity;
            p.InvMass = pinned ? 0f : 1f / Mathf.Max(1e-4f, p.Mass);
            _particles[index] = p;
        }

        /// <summary>Advances the rope by dt, split into substeps for stiff-spring stability.</summary>
        public void Step(float dt, int substeps)
        {
            substeps = Mathf.Max(1, substeps);
            float h = dt / substeps;
            for (int s = 0; s < substeps; s++)
            {
                Substep(h);
            }
        }

        private void Substep(float h)
        {
            int n = _particles.Count;
            if (n < 2)
            {
                return;
            }

            // 1) Gravity + air drag on every free particle.
            var force = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var p = _particles[i];
                if (p.InvMass == 0f)
                {
                    force[i] = Vector3.zero;
                    continue;
                }

                force[i] = GravityAccel * p.Mass;            // F = m * g
                force[i] += -AirDrag * p.Mass * p.Velocity;  // simple linear drag
            }

            // 2) Structural springs: every neighbouring pair is one tiny damped spring.
            for (int i = 0; i < n - 1; i++)
            {
                AddSpring(force, i, i + 1, SegmentRestLength, SegmentStiffness, SegmentDamping, CompressionResistance);
            }

            // 3) Bending springs (i -> i+2) give the rope real stiffness / shape memory.
            if (BendingStiffness > 0f)
            {
                for (int i = 0; i < n - 2; i++)
                {
                    AddSpring(force, i, i + 2, SegmentRestLength * 2f, BendingStiffness, SegmentDamping, CompressionResistance);
                }
            }

            // 4) Symplectic (semi-implicit) Euler integration.
            for (int i = 0; i < n; i++)
            {
                var p = _particles[i];
                if (p.InvMass == 0f)
                {
                    continue;
                }

                Vector3 accel = force[i] * p.InvMass;
                p.Velocity += accel * h;

                float speed = p.Velocity.magnitude;
                if (speed > MaxSpeed)
                {
                    p.Velocity *= MaxSpeed / speed;
                }

                p.PrevPosition = p.Position;
                p.Position += p.Velocity * h;
                _particles[i] = p;
            }

            // 5) Safety clamp: hard cap on per-segment stretch via position projection
            //    (anchor stays fixed). Prevents a spike from ever exploding the rope.
            float maxLen = SegmentRestLength * Mathf.Max(1f, MaxSegmentStretchMultiplier);
            for (int iter = 0; iter < ConstraintIterations; iter++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    var a = _particles[i];
                    var b = _particles[i + 1];
                    Vector3 delta = b.Position - a.Position;
                    float len = delta.magnitude;
                    if (len <= maxLen || len < 1e-6f)
                    {
                        continue;
                    }

                    Vector3 dir = delta / len;
                    float excess = len - maxLen;
                    float wA = a.InvMass;
                    float wB = b.InvMass;
                    float wSum = wA + wB;
                    if (wSum <= 0f)
                    {
                        continue;
                    }

                    a.Position += dir * (excess * (wA / wSum));
                    b.Position -= dir * (excess * (wB / wSum));
                    _particles[i] = a;
                    _particles[i + 1] = b;
                }
            }

            // 6) Re-derive velocity from corrected positions so the clamp does not add energy.
            if (h > 1e-6f)
            {
                for (int i = 0; i < n; i++)
                {
                    var p = _particles[i];
                    if (p.InvMass == 0f)
                    {
                        continue;
                    }

                    p.Velocity = (p.Position - p.PrevPosition) / h;
                    _particles[i] = p;
                }
            }
        }

        private void AddSpring(Vector3[] force, int i, int j, float rest, float k, float c, float compressionResistance)
        {
            var pi = _particles[i];
            var pj = _particles[j];

            Vector3 delta = pj.Position - pi.Position;
            float len = delta.magnitude;
            if (len < 1e-6f)
            {
                return;
            }

            Vector3 dir = delta / len;

            float stretch = len - rest;
            // A rope resists stretching but barely resists compression (it just sags).
            float effK = stretch >= 0f ? k : k * Mathf.Clamp01(compressionResistance);

            // Damping along the spring axis (relative velocity projected on dir).
            float relVelAlongDir = Vector3.Dot(pj.Velocity - pi.Velocity, dir);

            float scalarForce = effK * stretch + c * relVelAlongDir;
            Vector3 f = scalarForce * dir;

            force[i] += f;   // pulls i toward j when stretched
            force[j] -= f;
        }
    }
}

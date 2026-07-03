# PhysicsTest Scene Instructions

Use this scene to test only the manual bucket pendulum motion before adding paint particles.

## Scene Setup

1. Create a new Unity scene named `PhysicsTest.unity` in `Assets/Scenes/`.
2. Add an empty GameObject named `PhysicsTestRoot`.
3. Add an empty child named `Anchor` at position `(0, 5, 0)`.
4. Add a simple visible bucket placeholder, such as a Cube, named `Bucket`.
5. Do not add Rigidbody or Collider components to any test object.
6. Add `Pendulum` to `PhysicsTestRoot`.
7. Assign `Anchor` to `Pendulum.anchorTransform`.
8. Assign `Bucket` to `Pendulum.bucketTransform`.
9. Optional: add `RopeRenderer` to `PhysicsTestRoot`; it will render a procedural mesh rope through MeshFilter/MeshRenderer. Assign the same `Anchor` and `Bucket` transforms.
10. Optional: add `PendulumPhysicsTestController` to `PhysicsTestRoot` and assign the `Pendulum`.

## Inspector Values To Test

Use these values on the `Pendulum` component, or use the matching presets in `PendulumPhysicsTestController`.

| Test | ropeLength | initialAngleDegrees | initialAngularVelocity | directionAngleDegrees | damping | gravity |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Short rope | 1.5 | 30 | 0 | 0 | 0.05 | 9.81 |
| Long rope | 4.0 | 30 | 0 | 0 | 0.05 | 9.81 |
| 15 degree angle | 2.5 | 15 | 0 | 0 | 0.05 | 9.81 |
| 45 degree angle | 2.5 | 45 | 0 | 0 | 0.05 | 9.81 |
| Low damping | 2.5 | 30 | 0 | 0 | 0.01 | 9.81 |
| High damping | 2.5 | 30 | 0 | 0 | 0.35 | 9.81 |

## What To Look For

- Short rope should swing faster than long rope.
- Long rope should swing more slowly and cover a wider arc.
- 15 degrees should produce a small, stable swing.
- 45 degrees should produce a wider swing.
- Low damping should preserve motion for longer.
- High damping should settle much faster.

## Optional Test Controller

Add `PendulumPhysicsTestController` to the scene to switch presets during Play Mode.

- Press `1` for Short Rope.
- Press `2` for Long Rope.
- Press `3` for 15 Degree Angle.
- Press `4` for 45 Degree Angle.
- Press `5` for Low Damping.
- Press `6` for High Damping.

This test setup uses only transforms, manual pendulum math, and optional procedural mesh rope visuals.

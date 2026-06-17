# Elastic Rope Pendulum — Implementation Notes

This documents the upgrade from a fixed-length damped pendulum to an **elastic (spring)
pendulum**. Only `Pendulum.cs` and `RopeRenderer.cs` were modified. No scene, GameObject,
hierarchy, or Inspector reference was changed or recreated. No Rigidbody, Collider, SpringJoint,
or any Unity joint is used — the behaviour is integrated by hand.

## What changed and why

### Pendulum.cs
| Change | Why | Relation to the research model |
| --- | --- | --- |
| Added a second degree of freedom: the rope length `r` is now dynamic, not constant. | A fixed `L` cannot stretch. Making `r` a state variable is what turns the rigid rod into a rope. | The model is the standard **elastic pendulum** in polar coordinates `(r, θ)`. |
| Hooke's Law restoring force `F = -k·x`, `x = r − restLength`, applied only when `x > 0`. | The rope must pull the bucket back when stretched but must **not push** when slack (a rope has no compression strength). | `springAccel = k_eff·x` enters the radial equation as `-springAccel`. |
| `k_eff = ropeStiffness / (1 + ropeElasticity)`. | Gives `ropeElasticity` a clear meaning: higher elasticity softens the spring → more stretch under the same load. | Elasticity ↔ compliance of the spring. |
| Separate `ropeDamping` (radial) and `damping` (angular). | Stretch bounce and swing decay are physically independent and should be tunable separately. | `-ropeDamping·r'` damps `r'`; `-damping·θ'` damps `θ'`. |
| Position mapping now uses dynamic `r` instead of constant `L`. | So the bucket visibly moves out/in as the rope stretches/contracts. | `x = a.x + r·sinθ·cosφ`, `y = a.y − r·cosθ`, `z = a.z + r·sinθ·sinφ`. |
| `restLength` carries the old `ropeLength` field via `[FormerlySerializedAs("ropeLength")]`; `ropeLength` kept as a property alias. | Preserves the scene's serialized value (`1.98`) **and** keeps `PendulumPhysicsTestController` compiling unchanged. | — |

Equations of motion (unit mass, integrated every sub-step):

```
x         = r - restLength                              // signed extension
springAcc = (x > 0) ? k_eff * x : 0                     // Hooke, pull-only
r''       = r*θ'^2 + g*cosθ - springAcc - ropeDamping*r'
θ''       = (-g*sinθ - 2*r'*θ') / r - damping*θ'
```

### RopeRenderer.cs
- The existing anchor→bucket `LineRenderer` already reflects stretch, because the bucket is moved
  to the dynamic length. **The component was extended, not replaced.**
- Added optional, purely cosmetic *stretch feedback*: the line thins and tints toward
  `stretchedColor` as `CurrentRopeLength` exceeds `RestLength`. Disable with `reflectStretch`.

## Numerical stability measures (Pendulum.cs)
1. **Sub-stepping** — each frame's `dt` is split into fixed steps no larger than `maxSubStep`
   (default 5 ms). Stiff springs need a small step (`dt < 2/√k`); sub-stepping gives
   FixedUpdate-grade stability while keeping the existing `Update` + `TimeScale` + pause pipeline.
2. **Symplectic (semi-implicit) Euler** — velocities are integrated before positions. This is
   energy-stable for oscillators; plain explicit Euler would inject energy and diverge.
3. **Frame-time clamp** (`maxFrameTime`, default 0.1 s) — a lag/pause spike can never feed a huge
   `dt` into the stiff spring.
4. **Length clamps with anti-windup** — `r` is held in `[0.05, restLength·maxStretchMultiplier]`;
   on hitting a bound the outward/inward velocity is zeroed so energy cannot accumulate at the wall.
5. **`1/r` singularity guard** — the angular equation divides by `max(r, 0.05)`.
6. **Velocity caps** — `maxRadialSpeed`, `maxAngularSpeed` bound any transient spike per sub-step.
7. **Equilibrium-seeded reset** — `ResetState` pre-loads `r` to the static gravity equilibrium at
   the start angle, so the rope does not "drop and bounce" on the first frame.

## Inspector configuration (Pivot → Pendulum component)
Recommended starting values (all live-tunable in Play Mode):

| Field | Value |
| --- | --- |
| Rest Length | `1.98` (migrated from old Rope Length) |
| Rope Stiffness | `50` |
| Rope Damping | `0.5` |
| Rope Elasticity | `0.5` |
| Damping (angular) | `0.05` |
| Gravity | `9.81` |
| Max Sub Step | `0.005` |
| Max Frame Time | `0.1` |
| Max Stretch Multiplier | `4` |

`RopeRenderer`: leave `reflectStretch` on for visible elasticity, or off to keep the original look.

## Setup steps
No setup required. After Unity recompiles:
1. The `Pendulum` on **Pivot** gains the **Elastic Rope** and **Stability** sections; the old
   `Rope Length` value appears as **Rest Length** automatically.
2. Press Play — the bucket swings *and* the rope visibly stretches/contracts.
3. Tune `Rope Stiffness` / `Rope Elasticity` / `Rope Damping` live to taste.

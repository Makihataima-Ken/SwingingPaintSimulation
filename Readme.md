# Swinging Paint Simulation

## Project Overview

This project simulates a swinging paint bucket using custom mathematical and physical models implemented in Unity without relying heavily on built-in physics systems.

The simulation combines:
- Damped pendulum motion
- Simplified fluid flow
- Projectile motion
- Surface paint spreading

**Goal:** Create an interactive, physics-based artistic simulation where paint emitted from a swinging bucket creates dynamic patterns on a surface.

---

## Project Architecture
```
Assets/
│
├── Scenes/
│   ├── Main.unity
│   ├── PhysicsTest.unity
│   ├── PaintTest.unity
│
├── Scripts/
│   ├── Core/
│   │   └── SimulationManager.cs
│   │
│   ├── Physics/
│   │   └── Pendulum.cs
│   │
│   ├── Paint/
│   │   ├── PaintEmitter.cs
│   │   ├── PaintDrop.cs
│   │   └── CanvasDrawer.cs
│   │
│   └── UI/
│       └── UIController.cs
```
## 👥 TEAM ASSIGNMENT (5 MEMBERS)
### 👤 MEMBER 1 — Physics Lead
- Responsibility: """Core pendulum simulation"""
- Tasks:
  1. Implement damped pendulum
  2. Implement 3D position conversion
  Using:
  ```
  x = L sinθ cosφ
  y = -L cosθ
  z = L sinθ sinφ
  ```
  3. Add:
  damping
  variable length
  initial angle
  direction angle
  Deliverables
  Pendulum.cs
- Git Branch
  ```
  feature/pendulum-system
  ```

### 👤 MEMBER 2 — Paint Emission & Fluid Logic
- Responsibility: """Paint flow from bucket"""
- Tasks
  1. Paint emission rate
  2. Create emitter system : PaintEmitter.cs
  that handles:
      spawn paint drops
      control frequency
      reduce paint amount over time
  3. Add parameters:
    viscosity
    hole radius
    paint quantity
    Deliverables
    PaintEmitter.cs
    PaintSettings.cs
- Git Branch
  ```
  feature/paint-emission
  ```
### 👤 MEMBER 3 — Projectile & Paint Drop Physics
- Responsibility: Paint drop movement
- Tasks
  1. Implement projectile motion
  2. Create:
    PaintDrop.cs that handles:
        initial velocity inheritance
        gravity simulation
        trajectory updates
  3. Collision detection
       Detect collision with:
        Canvas plane
        Deliverables
        PaintDrop.cs
- Git Branch
  ```
  feature/projectile-system
  ```
### 👤 MEMBER 4 — Surface Interaction & Drawing
- Responsibility: Actual paint visualization
- Tasks
  1. Implement surface painting
      Simplified formula from report: R_final = R_base × Flow × Surface × (1 - Absorption)
  2. Implement:
  CanvasDrawer.cs
  Recommended Approach is to use: Texture2D
  3. Add:
    spread radius
    absorption
    surface type
    Deliverables
    CanvasDrawer.cs
    SurfaceSettings.cs
- Git Branch
  ```
  feature/surface-system
  ```
### 👤 MEMBER 5 — Integration + UI + Git Manager
- Responsibility: System integration
- Tasks
  1. Build UI:
      Sliders:
            gravity
            rope length
            damping
            paint flow
            viscosity
  2. Scene management: Main.unity

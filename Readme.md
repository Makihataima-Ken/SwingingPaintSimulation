# Swinging Paint Simulation - Setup & Documentation

## Table of Contents
1. [Scene Setup Instructions](#scene-setup-instructions)
2. [UI Hierarchy Setup](#ui-hierarchy-setup)
3. [Elastic Rope Physics Model](#elastic-rope-physics-model)
4. [ numerical Stability Considerations](#numerical-stability-considerations)
5. [Architecture Overview](#architecture-overview)
6. [Design Decisions](#design-decisions)

---

## 1. Scene Setup Instructions

### Step 1: Create the PhysicsSettings Asset
1. In the Unity Editor, go to **Assets > Create > SwingingPaint > Physics Settings**
2. Alternatively, right-click in the Project window: **Create > SwingingPaint > Physics Settings**
3. Name it `PhysicsSettings`
4. Place it in a `Resources` folder (Create folder: `Assets/Resources/`) so it can be auto-loaded at runtime

### Step 2: Create the Scene Hierarchy
Create the following GameObjects in your scene:

```
Scene Root
├── SimulationManager (Empty Game行政复议)
│   ├── SimulationManager.cs
│   └── PhysicsSettings: [drag your asset here]
│   └── Pendulum: [drag Pivot here]
│
├── Pivot (Empty GameObject - the pendulum attachment point)
│   ├── Pendulum.cs
│   │   └── Settings: [drag PhysicsSettings asset]
│   │   └── Bucket: [drag Bucket here]
│   ├── LineRenderer (Component)
│   │   └── RopeRenderer.cs
│   │       └── Bucket: [drag Bucket here]
│       └── Pendulum: [drag Pendulum component here]
│
└── Bucket (Cube or Sphere - the moving weight)
```

### Step 3: Configure the Components
- **SimulationManager**:
  - Assign `PhysicsSettings` asset
  - Assign `Pendulum` component (from Pivot GameObject)

- **Pendulum (on Pivot)**:
  - Assign `PhysicsSettings` asset
  - Assign `Bucket` Transform

- **RopeRenderer (on Pivot)**:
  - Assign `Bucket` Transform
  - Assign `Pendulum` component (auto-finds if not set)
  - Assign a Material for the rope visual

### Step 4: Create the UI Canvas
Follow the UI Hierarchy instructions below.

---

## 2. UI Hierarchy Setup

### Scene UI Structure
```
Canvas (Screen Space - Overlay)
├── PhysicsPanel (Panel)
│   ├── Title (TextMeshProUGUI): "Physics Parameters"
│   │
│   ├── GravityRow (Horizontal Layout Group)
│   │   ├── Label (TextMeshProUGUI): "Gravity"
│   │   ├── Slider (Slider): 0 - 20
│   │   └── Value (TextMeshProUGUI): "9.810"
│   │
│   ├── DampingRow (Horizontal Layout Group)
│   │   ├── Label (TextMeshProUGUI): "Damping"
│   │   ├── Slider (Slider): 0 - 1
│   │   └── Value (TextMeshProUGUI): "0.050"
│   │
│   ├── InitialAngleRow (Horizontal Layout Group)
│   │   ├── Label (TextMeshProUGUI): "Initial Angle"
│   │   ├── Slider (Slider): -90 - 90
│   │   └── Value (TextMeshProUGUI): "30.000"
│   │
│   └── ... (additional rows)
│
├── RopePanel (Panel)
│   ├── Title: "Rope Parameters"
│   ├── RestLengthRow
│   ├── RopeStiffnessRow
│   └── RopeElasticityRow
│
├── PaintPanel (Panel)
│   ├── Title: "Paint Parameters"
│   ├── PaintFlowRow
│   ├── PaintViscosityRow
│   └── SurfaceAbsorptionRow
│
├── SimulationPanel (Panel)
│   ├── Title: "Simulation Control"
│   ├── ResetParamsButton (Button): "Reset Parameters"
│   ├── PauseButton (Button): "Pause"
│   ├── ResumeButton (Button): "Resume"
│   └── RestartButton (Button): "Restart"
│
└── DebugPanel (Panel)
    ├── Title: "Debug Info"
    ├── AngleText (TextMeshProUGUI)
    ├── AngularVelocityText (TextMeshProUGUI)
    ├── RopeLengthText (TextMeshProUGUI)
    ├── MaxExtensionText (TextMeshProUGUI)
    └── BucketPositionText (TextMeshProUGUI)
```

### UIController Inspector Assignment
Drag the corresponding UI elements into the `UIController` script's public fields in the Inspector.

---

## 3. Elastic Rope Physics Model

### Hooke's Law Foundation
The elasticity is modeled using **Hooke's Law**:
```
F_spring = -k * x
```
Where:
- `k` = ropeStiffness (spring constant)
- `x` = extension beyond rest length (`L - restLength`)

### Full Equations of Motion

#### Angular Motion (Pendulum Equation)
```
alpha = - (g / L) * sin(theta) - damping * omega
```
Where:
- `alpha` = angular acceleration (rad/s²)
- `g` = gravity
- `L` = instantaneous rope length
- `theta` = angle from vertical
- `omega` = angular velocity
- `damping` = damping coefficient

#### Elastic Length Motion
The rope length `L(t)` is not constant. Its second derivative is governed by:
```
d²L/dt² = centripetal + gravitational + elastic + damping
        = L * omega² - g * cos(theta) - k_eff * (L - restLength) - damping * vL
```
Where:
- `L * omega²` = Centripetal force (outward)
- `-g * cos(theta)` = Gravitational component along rope
- `-k_eff * (L - restLength)` = Elastic restoring force (Hooke's Law)
- `k_eff = stiffness * (1 + elasticity)` = Effective stiffness
- `-damping * vL` = Damping on length change

### Numerical Integration
We use **semi-implicit Euler** integration for stability:
1. Calculate accelerations based on current state
2. Update velocities: `v += a * dt`
3. Update positions: `x += v * dt`

---

## 4. Numerical Stability Considerations

| Issue | Mitigation Strategy |
|-------|---------------------|
| Large time steps (low FPS) | Clamp `dt` to `MAX_DT = 0.05s` |
| Negative rope length | Clamp `L` to `[restLength * 0.1, restLength * 3]` |
| Runaway length velocity | Clamp `_lengthVelocity` to `[-10, 10]` m/s |
| Angle drift | Normalize theta to `[-pi, pi]` after each step |
| Stiff spring instability | Use semi-implicit Euler instead of explicit |
| Zero-length division | Ensure `restLength >= 0.01` via validation |

---

## 5. Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│                    UIController                       │
│  (Unity UI - Sliders, Inputs, Buttons, Text)        │
└──────────────────┬──────────────────────────────────┘
                   │ User Input
                   ▼
┌─────────────────────────────────────────────────────┐
│                 PhysicsSettings                       │
│  (ScriptableObject - Single Source of Truth)        │
│  - Gravity, Damping, Angle, etc.                    │
│  - Fires OnSettingsChanged event                      │
└────────────┬───────────────────────┬────────────────┘
             │                       │
             ▼                       ▼
┌─────────────────────┐    ┌─────────────────────────┐
│      Pendulum       │    │    SimulationManager    │
│  (Custom Physics)   │◄───┤  (State & Coordination)│
│  - Integrates       │    │  - Pause/Resume         │
│    angular motion   │    │  - Reset/Restart        │
│  - Integrates       │    │  - Default values       │
│    elastic length   │    └──────────────┬───────────┘
│  - Updates bucket   │                   │
│    position         │                   │
└──────────┬──────────┘                   │
           │                              │
           ▼                              ▼
┌─────────────────────┐         ┌─────────────────────┐
│   RopeRenderer      │         │   Debug Panel       │
│  (LineRenderer)     │         │  (UIController)     │
│  - Visualizes rope  │         │  - Angle            │
│  - Stretches        │         │  - Angular Velocity │
│    visually         │         │  - Rope Length      │
│                     │         │  - Max Extension    │
└─────────────────────┘         │  - Bucket Position  │
                                 └─────────────────────┘
```

---

## 6. Design Decisions

### 1. **Centralized Settings (PhysicsSettings)**
- **Why**: Avoids magic numbers scattered across scripts
- **How**: `ScriptableObject` with `[SerializeField]` backing fields and public properties
- **Benefit**: Easy to save as an asset, persistent across play sessions

### 2. **ScriptableObject over Singleton for Data**
- **Why**: Data should be an asset, not a runtime MonoBehaviour
- **Benefit**: Can be version-controlled, shared between scenes, and edited in the Inspector

### 3. **Singleton for SimulationManager**
- **Why**: Need global access to pause/resume/reset from UI
- **Implementation**: `static Instance` with null-check and warning on duplicates

### 4. **No Rigidbody / No SpringJoint**
- **Requirement**: Custom mathematical simulation per project specs
- **Benefit**: Full control over integration, no hidden physics engine interactions

### 5. **Event-Driven Updates**
- **Why**: `PhysicsSettings` fires `OnSettingsChanged` when any value changes
- **Benefit**: Systems can react to changes without polling
- **Trade-off**: Slightly more complex than direct field access, but much cleaner

### 6. **Semi-Implicit Euler Integration**
- **Why**: More stable than explicit Euler for oscillatory systems
- **Benefit**: Allows larger time steps without blowing up

### 7. **Separate Visuals from Physics**
- **Why**: `RopeRenderer` should not know about Hooke's Law
- **How**: `RopeRenderer` reads `CurrentRopeLength` from `Pendulum`
- **Benefit**: Easy to swap rendering (e.g., 3D mesh vs LineRenderer)

### 8. **Debug Panel in Same UIController**
- **Why**: Debug info is read-only and doesn't need separate logic
- **Benefit**: Reuses the same `_pendulum` reference, minimal overhead

---

## Quick Reference: Default Parameters

| Parameter | Default Value | Range |
|-----------|---------------|-------|
| Gravity | 9.81 m/s² | 0 - 20 |
| Damping | 0.05 | 0 - 1 |
| Initial Angle | 30° | -90 - 90 |
| Rest Length | 2.0 m | 0.5 - 5 |
| Rope Stiffness | 50 | 1 - 200 |
| Rope Elasticity | 0.5 | 0 - 2 |
| Paint Flow Rate | 1.0 | 0 - 10 |
| Paint Viscosity | 0.5 | 0 - 5 |
| Surface Absorption | 0.1 | 0 - 1 |

---

## File Checklist

- [x] `Assets/Scripts/Core/PhysicsSettings.cs`
- [x] `Assets/Scripts/Core/SimulationManager.cs`
- [x] `Assets/Scripts/UI/UIController.cs`
- [x] `Assets/Scripts/Physics/Pendulum.cs`
- [x] `Assets/Scripts/Physics/RopeRenderer.cs`
- [x] `Assets/Scripts/SwingingPaint.asmdef`

# Swinging Paint Simulation

A Unity project for simulating a swinging paint bucket with a custom rope-based pendulum, dynamic paint outflow, and a canvas-like paint deposition surface. The project combines physics-driven motion, fluid-like paint behavior, and runtime tuning controls for experimentation and presentation.

## Overview

This repository contains a playable simulation scene and the supporting Unity scripts for:

- a custom 3D pendulum and spring-rope solver for the bucket motion
- mouse-based grabbing and throwing of the bucket
- paint flow from the bucket and visual rendering of the stream
- a GPU-assisted fluid/outflow system for particle-based paint behavior
- a configurable runtime control panel for pausing, resetting, and tuning parameters

The main scene is located at [Assets/Scenes/Main.unity](Assets/Scenes/Main.unity).

## Key Features

- Custom pendulum simulation without relying on standard Unity joints or rigidbody-based rope physics
- Interactive drag-and-throw behavior for the hanging bucket
- Adjustable physics parameters through a centralized settings asset
- Fluid-like outflow and paint rendering components
- Runtime simulation controls for testing and demos
- Scene-based setup for a self-contained VR/desktop-friendly experiment

## Project Structure

- [Assets/Scenes](Assets/Scenes) - Unity scene files
- [Assets/Scripts/Core](Assets/Scripts/Core) - simulation coordination, settings, camera, and scene setup
- [Assets/Scripts/Physics](Assets/Scripts/Physics) - pendulum, rope rendering, and spring rope logic
- [Assets/Scripts/Paint](Assets/Scripts/Paint) - paint emission and paint hole visualization
- [Assets/Scripts/Surface](Assets/Scripts/Surface) - paint surface and related rendering logic
- [Assets/Scripts/BucketFluid](Assets/Scripts/BucketFluid) - fluid simulation, outflow control, and rendering
- [Assets/Scripts/UI](Assets/Scripts/UI) - runtime controls and presentation tools
- [Assets/Shaders](Assets/Shaders) - shader files used by the fluid and paint rendering systems
- [Assets/Settings](Assets/Settings) - serialized settings assets such as physics configuration

## Requirements

- Unity 2022.3 LTS (the project is currently configured for Unity 2022.3.62f3)
- A working Unity installation with the standard editor tools

## Getting Started

1. Clone the repository to your local machine.
2. Open the project folder in Unity Hub.
3. Open the main scene at [Assets/Scenes/Main.unity](Assets/Scenes/Main.unity).
4. Press Play in the Unity Editor to start the simulation.

## Usage

- Left mouse button: grab and drag the bucket to influence its motion
- Release: throw the bucket with the sampled drag velocity
- F1: toggle the runtime simulation control panel
- The control panel provides buttons for pause/resume, restart, reset, canvas clearing, and tuning various simulation parameters

## Main Systems

### Simulation Manager
The central controller for the project is implemented in [Assets/Scripts/Core/SimulationManager.cs](Assets/Scripts/Core/SimulationManager.cs). It coordinates the simulation state, reset logic, and fixed-step execution.

### Pendulum and Rope Physics
The swinging motion is handled by [Assets/Scripts/Physics/Pendulum.cs](Assets/Scripts/Physics/Pendulum.cs) and [Assets/Scripts/Physics/SpringRope.cs](Assets/Scripts/Physics/SpringRope.cs). These scripts implement the bucket’s custom rope-driven motion and support interactive dragging.

### Physics Settings
Core simulation values are centralized in [Assets/Scripts/Core/PhysicsSettings.cs](Assets/Scripts/Core/PhysicsSettings.cs) as a ScriptableObject asset, making it easier to tune the simulation without changing code.

### Fluid and Paint Systems
The paint-related systems are organized under the BucketFluid and Paint folders and are responsible for simulation of outflow, particle behavior, and paint deposition on the target surface.

## Development Notes

- The project is organized around Unity GameObjects and MonoBehaviours.
- Most gameplay and simulation logic is contained in the scripts under [Assets/Scripts](Assets/Scripts).
- The runtime control panel is designed for live tuning during play mode.

## Troubleshooting

If a parameter change does not immediately affect the current motion, the runtime UI may suggest restarting or resetting the simulation. This is expected because some changes are applied on reset for stability.

## License

No explicit license file is currently included in this repository.

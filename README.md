# Audio Visualizer — Unity 6 Debug Tool

A runtime audio debugging and spatial-audio simulation tool built in Unity 6 (URP). It layers physics-based occlusion, Doppler displacement, reverb, and low-pass filtering on top of Unity's built-in audio engine, and exposes everything through an in-editor gizmo system and an in-game debug overlay.

---

## Project Structure

```
Assets/
├── Scripts/
│   ├── AudioVisualizerScripts/
│   │   ├── AudioManager.cs          — singleton hub; clip library, emitter registry, F1 toggle
│   │   ├── AudioEmitter.cs          — partner script; randomised volume/pitch playback (not modified)
│   │   ├── DebugEmitter.cs          — all spatial-audio systems (see below)
│   │   ├── AudioDebugUI.cs          — IMGUI overlay table (F1)
│   │   ├── AudioDirectionVisualizer.cs — screen-space directional ring (V)
│   │   ├── AmbientZoneSpline.cs     — spline-bounded ambient zones
│   │   ├── DopplerProxy.cs          — marker component for displaced proxy AudioSource
│   │   ├── MinimapController.cs     — top-down minimap camera (M)
│   │   └── MinimapEmitterRing.cs    — per-emitter pulse rings on minimap
│   ├── PlayerController.cs          — first-person movement + jump audio
│   └── Figure8Mover.cs              — figure-8 motion helper for moving-emitter tests
├── Scenes/
│   ├── WallEnvironmentScene         — occlusion test (walls with colliders)
│   ├── RegionEnvironmentScene       — ambient zone test
│   └── EnvironmentScene             — general sandbox
└── Sounds/  BirdChirp.wav · Jump.wav · Leaves.wav
```

---

## Systems Overview

### AudioManager
Singleton that owns the clip library (label → `AudioClip`), maintains a list of all active `DebugEmitter` and `AmbientZoneSpline` instances, caches the scene `AudioListener`, and gates global debug rendering via `AudioManager.DebugEnabled` (toggled with **F1**).

### AudioEmitter *(partner script — read-only)*
Handles timed/looping playback with per-play volume and pitch randomisation. `DebugEmitter` sits alongside it and intercepts the `AudioSource` each frame to apply spatial corrections.

---

### DebugEmitter — Spatial Audio Pipeline

`LateUpdate` runs five stages in order each frame:

| Stage | What it does |
|-------|-------------|
| 1. Listener resolve | Finds the `AudioListener` via `AudioManager` or scene search |
| 2. Distance | `DistanceToListener` — scalar used by every downstream stage |
| 3. Doppler displacement | Positions a child proxy `AudioSource` behind the emitter along its velocity vector; mutes the main source while active |
| 4. Volume + occlusion | Computes `EffectiveVolume`; raycasts for walls and applies continuous attenuation and low-pass filter |
| 4b. Reverb | Runs 10 outward probe rays; applies Sabine's formula to drive `AudioReverbFilter` parameters |
| 5. Doppler pitch | Closing-rate scalar shifts pitch up (approaching) or down (receding) |

#### Occlusion
Two rays are cast per frame — **listener → emitter** (entry faces) and **emitter → listener** (exit faces). Each matched pair gives the wall's chord length. Attenuation uses `occlusionVolumeMultiplier ^ (totalThickness / referenceThickness)`, making thin corners produce mild occlusion and thick walls produce strong occlusion. A low-pass filter (`AudioLowPassFilter`) is blended in proportion to wall thickness × indoor ratio. The gizmo draws **two spheres per wall** (entry + exit) coloured green / orange / red by wall count.

#### Reverb
10 probe rays (8 horizontal at 45° + up + down) measure average room radius and surface hit ratio. Sabine's RT60 formula (`RT60 ≈ 0.054 × r_avg / α`) drives `AudioReverbFilter` decay time, reflection delay, HF ratio, and diffusion — all smoothed over time. The indoor ratio (hits / 10) blends outdoor (dry) against indoor (wet) character automatically.

#### Doppler Displacement
A `_DopplerProxy` child `GameObject` carries a mirrored `AudioSource`. Its position is offset behind the emitter: `displacedPos = position − velocityNorm × (speed × distance / soundSpeed × exaggeration)`, clamped to `maxDisplacementDistance`. Occlusion raycasts target the proxy position so the muffling matches where the sound appears to originate.

#### Doppler Pitch
`closingSpeed = (prevDistance − currentDistance) / deltaTime` captures both emitter and listener motion with a single scalar. `pitchShift = 1 + (closingSpeed / soundSpeed) × strength`, clamped to [0.5, 2.0]. An optional distance-based pitch curve (`pitchAtMinDistance` → `pitchAtMaxDistance`) can be layered on top.

---

### AudioDebugUI
IMGUI table rendered at runtime (**F1**). One row per registered `DebugEmitter`:

`Name · Dist · Vol · Blend · Muted · Occl · Disp · Pitch · RT60 · LPF Hz`

Neutral values (`Occl = 1.00`, `Disp = 0.0m`, `LPF = -`) display when systems are disabled, so the table is always safe to read.

### AudioDirectionVisualizer
Screen-space crescents centred on the HUD indicate each emitter's direction relative to the listener. Amplitude is modulated by live `AudioSource.GetOutputData` samples. Toggle with **V**.

### AmbientZoneSpline
Spline-defined ambient regions. Uses a 64-sample polygon approximation for point-in-polygon testing. Registers with `AudioManager`; zone name, inside/outside state, and edge distance appear in the debug overlay.

---

## Controls

| Key | Action |
|-----|--------|
| F1 | Toggle debug overlay + gizmos |
| M | Toggle minimap / overview mode |
| V | Toggle direction visualizer |
| WASD / Mouse | Player movement and look |
| Space | Jump |

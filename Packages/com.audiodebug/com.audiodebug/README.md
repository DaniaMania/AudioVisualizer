# Audio Debug System
**Unity 6 · URP · Input System**

Runtime audio debugging toolkit with per-emitter occlusion, Doppler displacement, a minimap ring visualizer, a screen-space direction visualizer, and a live debug HUD.

---

## Installation

1. Copy the `com.audiodebug` folder into your project's `Packages/` directory (alongside the built-in packages).
2. Unity will detect it automatically — no Package Manager import needed.

---

## First-Time Setup (any new scene)

1. Open **Tools → Audio Debug System → Setup Wizard**
2. Click **Run Full Setup** — this will:
   - Create the required layers (`Minimap`, `Ground`, `MinimapOnly`)
   - Instantiate `AudioSystem` and `MinimapRig` prefabs into the scene
   - Auto-find your player by the `"Player"` tag and wire it
   - Wire `AudioDebugUI` references
3. Check the wizard checklist — all rows should be green

---

## Per-Emitter Setup

Add these components to any GameObject that produces sound:

| Component | Required | Purpose |
|---|---|---|
| `AudioSource` | Yes | Unity audio playback |
| `AudioEmitter` | Yes | Clip lookup, intervals, pitch/volume randomization |
| `DebugEmitter` | Yes | Occlusion, Doppler, scene gizmos, HUD data |
| `MinimapEmitterRing` | Optional | Animated pulse ring on the minimap |

Register your `AudioClip` assets in the `AudioManager` Sound Library with unique string labels. `AudioEmitter` references clips by that label.

---

## Ambient Zone Splines *(requires com.unity.splines)*

1. Create an empty GameObject → add **Spline Container** → draw your zone path
2. Create another empty GameObject → add **AmbientZoneSpline** + **AudioSource**
3. Assign the Spline Container and Player transform in the inspector

If `com.unity.splines` is not installed, `AmbientZoneSpline` is compiled out automatically.

---

## Layers

| Layer | Purpose |
|---|---|
| `Minimap` | Assigned to ring `LineRenderer` children by `MinimapEmitterRing` |
| `Ground` | Assign to terrain/floor — included in minimap base camera culling |
| `MinimapOnly` | Reserved for objects that should only appear on the minimap |

---

## Runtime Keys

| Key | Action |
|---|---|
| `F1` | Toggle debug HUD + minimap |
| `V` | Toggle direction visualizer |
| `G` | Toggle minimap overview mode |
| `=` / `-` | Zoom minimap in / out |

---

## Package Structure

```
com.audiodebug/
├── package.json
├── README.md
├── Prefabs/
│   ├── AudioSystem.prefab     ← AudioManager + AudioDebugUI + Visualizer
│   └── MinimapRig.prefab      ← Minimap UI + camera + MinimapController
├── Runtime/
│   ├── AudioManager.cs
│   ├── AudioEmitter.cs
│   ├── DebugEmitter.cs
│   ├── DopplerProxy.cs
│   ├── MinimapEmitterRing.cs
│   ├── MinimapController.cs
│   ├── AudioDebugUI.cs
│   ├── AudioDirectionVisualizer.cs
│   └── AmbientZoneSpline.cs   ← compiled only if com.unity.splines present
├── Editor/
│   └── AudioDebugSetupWizard.cs
└── Samples~/
    └── Demo/
        ├── PlayerController.cs
        └── Figure8Mover.cs
```

================================================================
  AUDIO DEBUG SYSTEM  -  Setup Guide
================================================================


BEFORE YOU START
----------------------------------------------------------------
Install these packages via Window > Package Manager > Unity Registry:

   - Cinemachine
   - Splines
   - ProBuilder
   - Input System

When prompted to enable the new Input System and restart, click Yes.


----------------------------------------------------------------
STEP 1  -  Import TextMesh Pro Essentials
----------------------------------------------------------------
After importing this package, open the example scene.
A popup will appear asking to import TMP Essentials.
Click "Import TMP Essentials" and wait for it to finish.


----------------------------------------------------------------
STEP 2  -  Create Required Layers
----------------------------------------------------------------
Go to:  Edit > Project Settings > Tags and Layers

Add these three layers in any free slot (User Layer 8 or above):

   - Minimap
   - Ground
   - MinimapOnly

The minimap will not display correctly without these.


----------------------------------------------------------------
STEP 3  -  Set Up URP
----------------------------------------------------------------
This project requires the Universal Render Pipeline.
If your project does not already use URP:

   1. Install "Universal RP" from the Package Manager
   2. Go to Edit > Project Settings > Graphics
   3. Assign a URP Render Pipeline Asset


----------------------------------------------------------------
STEP 4  -  Tag Your Player
----------------------------------------------------------------
Select your Player GameObject in the Hierarchy.
Set its Tag dropdown to "Player".

The minimap and audio system use this tag to find the player.


----------------------------------------------------------------
STEP 5  -  Open the Example Scene
----------------------------------------------------------------
Open:  AudioDebugSystem / Scenes / RegionEnvironmentScene

Press Play and use these controls:

   W A S D       Move
   Mouse         Look
   Space         Jump
   F             Toggle audio debug HUD
   V             Toggle direction visualizer
   G             Toggle minimap overview
   = and -       Zoom minimap in and out


================================================================
  ADDING THE SYSTEM TO YOUR OWN SCENE
================================================================


MINIMUM SETUP
----------------------------------------------------------------
   1. Drag  AudioDebugSystem / Prefabs / AudioManager  into your scene
   2. Add your AudioClips to the Sound Library on the AudioManager
   3. On each sound object, add AudioEmitter + DebugEmitter
   4. Set the clip label in AudioEmitter to match a Sound Library label


MINIMAP SETUP
----------------------------------------------------------------
   1. Drag  AudioDebugSystem / Prefabs / AudioMinimap  into your Canvas
   2. On MinimapController, assign your Player transform
   3. On each sound object, also add MinimapEmitterRing for pulse rings


AMBIENT ZONE SETUP  (requires Splines package)
----------------------------------------------------------------
   1. Create an empty GameObject, add Spline Container, draw your zone
   2. Create another empty GameObject, add AmbientZoneSpline + AudioSource
   3. Assign the Spline Container and Player in the inspector


================================================================
  COMPONENT REFERENCE
================================================================

   AudioManager              Singleton. Holds the clip library. Required.

   AudioEmitter              Plays clips by label.
                             Controls interval and randomization.

   DebugEmitter              Occlusion, Doppler displacement,
                             scene gizmos, and HUD data.

   MinimapEmitterRing        Animated pulse ring on the minimap.
                             Optional.

   MinimapController         Manages minimap camera follow,
                             zoom, and overview mode.

   AudioDebugUI              The runtime HUD. Toggle with F key.

   AudioDirectionVisualizer  Screen-space direction indicators.
                             Toggle with V key.

   AmbientZoneSpline         Spline-based ambient audio zone.
                             Requires Unity Splines package.

================================================================

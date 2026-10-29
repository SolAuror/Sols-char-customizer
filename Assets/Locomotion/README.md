# Optional human locomotion demo

This folder contains the human locomotion slice extracted from the Synty-game
Malbers Steve test, including the corrected head look and body-only Humanoid
retargeting. It uses an adapted Malbers motor and human animation state machine
as a starting point for an immersive RPG controller. Fishbox's movement,
swimming, and custom clip animator are design references; its traversal and
gameplay systems are not dependencies. See [CONTROLLER_DIRECTION.md](CONTROLLER_DIRECTION.md)
for the intended feel, source audit, and remaining feature work. It does not
require importing the entire Malbers asset pack.

## Try it

- Open `Assets/CharCustomization/Scenes/SC_CustomizerDemo.unity`, customize either
  body, enter a name, and select **Finalize**. The camera blends into gameplay.
- Open `Assets/Locomotion/Scenes/SC_HumanLocomotionTest.unity` for immediate
  locomotion testing on the ported test floor, without the customization UI.
- The reusable player is `Resources/Locomotion/HumanPlayer.prefab` in this folder.

| Input | Action |
| --- | --- |
| WASD / left stick | Move relative to the camera |
| Mouse / right stick | Orbit camera |
| Shift / configured Sprint action | Sprint |
| Space / configured Jump action | Jump |
| C / gamepad east button | Toggle crouch (standing requires headroom) |
| Tab | Toggle strafe |
| 1 / 2 | Lower / raise movement speed |
| K | Test death |
| R | Respawn and restore movement/collision |
| Escape / left click | Release / recapture cursor |

The motor enables Idle, Locomotion, Jump, Fall, and test Death. Crouch is stance
7; state 10 is Death and must never be used for crouch. The broader
human animator and its referenced clips are retained for later extension.
Combat, weapons, swimming, climbing, riding, and AI are not wired into this demo.

The active `Animations/HumanRpg.controller` uses 45 selected Fishbox clips for
idle, walking, running, sprinting, turns, directional strafe/crouch, jump, fall,
landing, and test death.
Free movement follows the clips' horizontal root travel at natural playback
speed. Grounded strafe uses motor travel with directional poses, avoiding a
second root-motion contribution. Copied clips have no legacy animation events
or facial muscle curves. `Animations/Fishbox/SOURCE_MAP.json` records each source.

Jump uses Fishbox's 25 m/s² gravity with a configurable 1.25 m height: immediate
takeoff, a predictable ballistic arc, retained horizontal momentum, 0.08 s coyote
grace, and 0.12 s landing input buffering. The motor owns airborne translation;
copied takeoff clips have their long authored falling trajectory removed.
Height and gravity are exposed on `HumanLocomotionController`.

## Module boundary

`CharCustomization` owns appearance, proportions, preview controls, UI, saving,
and the small `ICharacterGameplayHandoff` contract. It has no source or serialized
asset dependencies on this folder. Its preview animator is idle-only.

`CustomizerLocomotionHandoff` registers from this folder when a demo scene loads.
It instantiates the optional player, applies the finalized recipe to its own
appearance components, rebuilds the proportioned Humanoid Avatar, and binds the
active body's native motor. The preview remains visible during the camera blend;
the gameplay model replaces it when control begins. The editor is not converted
into a player or made responsible for movement.

Removing `Assets/Locomotion` leaves the character editor functional. Finalize
still saves the character and keeps the UI active. Also remove the optional test
scene from the build scene list when removing the module. Unity Splines 2.8.2 is
declared explicitly for the extracted shared runtime; it is not required by the
customizer's scripts.

## Layout and dependencies

- `Scripts/Runtime`: player input/camera adapter and optional customizer handoff.
- `Scripts/Editor`: animation authoring and repeatable validation tools.
- `Resources/Locomotion`: reusable player with both customizable bodies.
- `Avatars`: per-body Humanoid definitions excluding eyes and jaw retargeting.
- `Animations`: adapted RPG controller and selected Fishbox animation copies.
- `Input`: independent gameplay actions. The customizer retains its own UI map.
- `Scenes`: the ported test level and its ground material.
- `ThirdParty/Malbers Animations`: the referenced human assets plus the shared
  Malbers code assemblies needed to compile them. The shared assembly includes
  systems beyond the five enabled states; it is not the complete asset pack.

The player reuses the character editor's models, materials, hair, and appearance
code. This dependency goes from locomotion to customization, never the reverse.
The imported vendor assets retain their existing ownership and licence terms.

## Integration details

Each body has its own Animator, MAnimal, Rigidbody, capsule, Aim, and head-look
IK. Motors stay disabled during customization. Runtime states are cloned per
body. Capsule size and camera height are fitted after applying proportions.
The free-movement rotator is omitted so Malbers does not reparent the skeleton
under an extra transform and invalidate the customizer's Avatar rebuilds.

The imported facial controls are not Humanoid eyes/jaw. Excluding those mappings
preserves authored face poses while body/head animation continues. Head/neck IK
offsets are calibrated to each body's axes. The existing customizer twist-bone
and proportion/foot-IK systems remain in use.

Adaptations in the vendor copy:

- Use `AdvancedDropdownItem.children` for Unity 6000.3 compatibility.
- Restrict the editor assembly to the Editor platform.
- Expose capsule-default recaching after proportion-dependent sizing.
- Feed player intent as world-space direction without the AI movement path's
  turn braking; preserve analog magnitude in strafe.
- Use exponential response for movement/turn smoothing and convert strafe's
  authored 60 Hz rotation fraction to the current timestep.
- Make full-package tag/layer/legacy-input setup an explicit advanced menu action.
  This demo only adds `Animal` for native component authoring and `Stair` for ground checks.

Some retained optional combat/weapon FBX clips report the source pack's copied
Avatar bone-length mismatch warnings on a fresh import. Those optional actions
are not enabled in this slice; the enabled locomotion is covered by runtime checks.

## Validation

Use **Tools > Sol > Locomotion > Validate Assets and Optional Boundary** to
check both motors, state caches, facial mappings, missing scripts, and reverse
asset dependencies. **Run Runtime Checks** exercises both sexes through actual
Finalize/camera handoff, edited height and skin, walk, sprint, jump, crouch, and
facial stability. It also checks immediate test-scene playback and saving with
no gameplay provider. Test saves go to the outer project's `work` directory.

**Run Movement Feel Checks** exercises turns, reversals, stopping, analog strafe,
single jumps, landing input buffering, crouch/stand, live rig and proportion
changes, death/respawn, and immediate respawn before delayed death cleanup.
The 30/60/120 Hz menu variants use a controlled simulation timestep; these are
behavior checks, not performance benchmarks or a substitute for playing the
controller. The test restores the previously saved scene and input settings.

The customizer was additionally compiled with this entire folder physically
removed, and its nine avatar-rebuild edit-mode tests passed.

**Apply Grounded Animation Set** (Ctrl+Shift+F6) reapplies the curated clips and
blend trees to the 24 selected base-layer states and assigns both player bodies.
It intentionally replaces edits to those states; it leaves the Fishbox source
project untouched.

# Immersive RPG controller direction

The character editor remains the primary product. Locomotion is an optional,
polished runtime rig showcase that can develop into an immersive RPG controller.
Runtime changes to sex, body proportions, and appearance must remain supported.
The dependency direction is locomotion -> character editor, through the existing
handoff contract; the editor must work without this folder.

## Feel target

User references: Elder Scrolls + The Witcher 3, with Crimson Desert the strongest
influence. The interpretation for this project is immediate, predictable control
with convincing body weight, connected animation, and an unobtrusive camera.
These are design targets, not claims about those games' internal implementations.

- Exploration: precise slow walking, continuous analog speed, relaxed posture,
  and free camera orbit. Moving forward must never rotate the camera by itself.
- Running: short acceleration, readable weight transfer on turns, and a clean
  stop. A reversal should show the body changing direction without a long input
  lock or an instantaneous 180-degree spin. Avoid smoothing input, velocity,
  animation, and camera so heavily that their delays accumulate.
- Facing: free movement turns toward travel; strafe explicitly separates facing
  and travel. Camera input has one owner and steering uses the intended orbit
  heading, independent of camera follow lag.
- Grounding: controlled jumps, preserved airborne momentum, stable landings,
  sensible slope/step response, and feet that match travel speed. Weight should
  come from animation and bounded motion response, not lost button presses.
- Crouch: a real stance with matching clearance, directional motion, and safe
  standing checks. Its movement and transitions must still work on changed rigs.
- Swimming: coherent entry, surface locomotion, directional motion, and exit,
  using an explicit water query. Additional underwater behavior follows only
  when it can be tested with suitable clips and collision.
- One-shot actions: reusable custom clip playback with deliberate blending,
  explicit interruption rules, and reliable return to locomotion.
- Test death remains available separately from locomotion/stance selection.

This scope does not include Fishbox traversal, a combat framework, or wholesale
imports of either source project's systems.

## Source audit: what to retain and what to change

Inspected source: `B:/Projects/Unity/03-oldwork/Fishbox/Assets`.
Fishbox files were read only. The implementation now copies 45 selected clips
into this module with new asset GUIDs. No Fishbox code, Magic Animation Blend,
Kinemation, or traversal dependencies were imported.

| Source | Useful learning | Adaptation for this module |
| --- | --- | --- |
| Adapted Malbers motor/states | Ground probing, root-motion integration, state lifecycle, stances, existing human animator | Keep one motion authority; adapt its player path and replace clips/tuning as needed. Imported behavior is not the design specification. |
| `Scripts/Locomotion/LocomotionController.cs`, `HandleLateralMovement` | Different acceleration by gait and active braking after release | Use a clear target speed and a single velocity response. Fishbox compounds curve-driven speed limiting, acceleration, and drag; small stick input still chooses the full gait target speed. Do not copy that response chain. |
| `LocomotionController.cs`, `HandleVerticalMovement` | Coyote allowance and crouch-jump headroom consideration | Keep bounded grace/buffering. Physical grounding must be authoritative; Fishbox derives grounding here from its movement enum. |
| `LocomotionController.SwimFly.cs`, `HandleSwimMovement` | Camera-directed swimming, explicit up/down, timestep-corrected velocity smoothing, chest-relative surface limit | Keep the intent and collision-aware surface correction. Supply water data through an optional adapter; do not depend on Fishbox water managers or silently invent water at the character's current height. |
| `LocomotionAnimation.cs` and `Animations/PCAnimator.controller` | Directional blend trees, gait selection, action-state tagging | Animation should consume intent plus measured motion. Fishbox clears raw gameplay input during interaction and knows about fishing/combat/NPC systems; those responsibilities must stay out of the new animation driver. Refresh animator caches when a runtime rig/controller changes. |
| `Scripts/Interactables/InteractionPoint.Animator.cs` | `CustomClipPlaceholder` overridden and played through `Base Layer.Interactions.CustomClipState.CustomClipPlay` | Keep a dedicated replaceable clip entry. Give each player ownership of playback, cancellation, locks, and override lifetime; do not let multiple interaction objects replace/restore its controller independently. |
| `SolMagicBlendBridge.cs` | Explicit priority between locomotion and transient actions | Use one action owner and a bounded completion path. In the old bridge the timeout is bypassed while the requested profile remains assigned; profile identity alone cannot prove that a one-shot is still playing. |

The last row is a code-level lifecycle risk, not a reproduced Fishbox runtime
failure. Likewise, source inspection cannot establish which animation looks
better on the current customized bodies.

## Clip candidates verified in the Fishbox animator

The following are referenced by `PCAnimator.controller`, resolved through their
asset GUIDs. They are more useful candidates than importing every animation in
the old project. Paths below are relative to Fishbox's `Assets/Animations`.

| Group | Referenced candidates |
| --- | --- |
| Walk/run/sprint | `Movement/Walk/WalkFwdLoop 1.anim`, `WalkBwdLoop.anim`; `Movement/Run/RunFwdLoop.anim`, `RunBwdLoop.anim`, `RunLtLoop.anim`, `RunRtLoop.anim`; `Movement/SprintFwdLoop.anim` |
| Directional motion | `Movement/Strafe/StrafeLeftLoop.anim`, `StrafeRightLoop.anim`, the left/right 45/135 loops; corresponding `Movement/Run/RunStrafe*` clips |
| Crouch | `Crouch/CrouchLoop_new.anim`, `Crouch_WalkFwd_new.anim`, `Crouch_WalkBwd_new.anim`, left/right/45/135 variants, `Crouch_Turn90L_new.anim`, `Crouch_Turn90R_new.anim` |
| Swim | `Swimming/Swim_IdleWade.anim`, `Swim_Fwd_A.anim`, `Swim_Fwd_A_Left.anim`, `Swim_Fwd_A_Right.anim`, `Swim_Left.anim`, `Swim_Right.anim`, `SwimBackwards_A.anim` |
| One-shot route | `CustomClipPlaceholder.anim`; the interaction component supplies the actual clip |

The swim tree uses two-dimensional directional blending on `inputX/inputY`.
The gait tree uses `inputMagnitude` thresholds 0 / 0.75 / 1 / 1.5 for
idle/walk/run/sprint. Those thresholds belong to Fishbox's animator contract and
must not be copied into the current animator without remapping its parameters.
The user identifies the movement/crouch source as Kubold animations for UE;
the asset names and references alone do not independently establish provenance.

Selected gait/crouch/jump/landing/death clips now drive `HumanRpg.controller`.
Both bodies were reviewed with sampled pose sheets; runtime checks exercise rig
switching and proportion changes. Idle crouch uses `Crouch/Crouch_Idle.anim` for
a balanced squat. The exact copied paths are in `Animations/Fishbox/SOURCE_MAP.json`.
Legacy animation events and facial curves were removed. The motor owns yaw and
vertical travel; free movement retains horizontal root motion, while directional
strafe uses motor travel only. Takeoff clips contained downward body curves of
tens of metres; these are flattened in the copies so they cannot displace the
rig independently of the ballistic jump.

## Integration order and acceptance

1. Stabilize camera, motion response, crouch, jump/landing, death/respawn, and rig
   rebinding. Run behavioral checks at 30/60/120 Hz and the real customizer handoff.
2. Compare the verified Fishbox gait/crouch candidates against the imported human
   clips in the test scene. Choose a coherent set and tune measured travel speed
   to the stride before adding starts/stops/turns. Keep gait transitions interruptible.
3. Add a small one-shot API with clip, fade time, movement/root-motion policy,
   and interruption policy. Death, disable, respawn, and rig rebinding must cancel
   safely; completion must release only the locks owned by that action. Test an
   interrupted action, consecutive actions, and a rig change during playback.
4. Add swimming through a locomotion-owned state and water adapter. Test surface
   entry/exit hysteresis, shore collision, loss of water, changed proportions,
   and death/respawn while swimming. No traversal system is needed.

The curated clip replacement in step 2 is implemented; authored start/stop
transitions and steps 3-4 remain future work. The current executable
slice remains ground locomotion with crouch and test death. Numeric checks catch
regressions but do not certify the desired feel: final tuning requires playing
slow walk, sprint-release, tight turns, reversals, slopes/steps, and transitions.

## Verified foundation (2026-10-27)

Unity 6000.3.9f1 play-mode checks passed at controlled 30/60/120 Hz timesteps:
turns, reversal, settling, analog strafe, one airborne jump, landing buffer,
crouch/stand, runtime rig and proportion changes, death/respawn, and immediate
respawn. Forward speed was 3.776 / 3.787 / 3.789 m/s; jump peaks were
0.901 / 0.872 / 0.858 m respectively. These are behavior measurements, not
performance results or final artistic approval.

The real customizer handoff passed for both sexes with changed proportions and
skin. Facial drift measured zero. Forward-input camera yaw travel measured
0.15 degrees at the positive orbit test and 0.12 degrees in the negative-orbit
standalone test. The optional dependency audit passed, and character saving
remained functional with the gameplay provider removed. The original test scene
was restored after checks. Detailed logs are outside the repository at
`../work/movement-validation-2026-10-27.log`.

## Fishbox clip and jump pass (2026-10-27)

The active RPG controller replaces 24 base-layer states using 45 copied clips.
Natural gait playback removes the old clip speedups; directional strafe uses
actual backward/diagonal clips rather than reversing a forward animation.
Both body pose sheets are under `../work/animation-review`.

Jump uses a 1.25 m target with Fishbox's 25 m/s² gravity, starts without waiting
for a clip event, and preserves the existing buffer, coyote grace, and horizontal
momentum. The previous quadratic-in-time gravity remains available to unrelated
vendor users; this demo opts into constant acceleration. Jump height is integrated
at the timestep midpoint, and Fall inherits the same gravity clock and velocity
offset. Legacy jump animation messages cannot restart the impulse.

Updated movement checks passed at 30/60/120 Hz: forward speed was
3.238 / 3.257 / 3.267 m/s; jump peaks were 1.247 / 1.250 / 1.250 m.
These runs include takeoff latency, repeated airborne input, landing buffering,
crouch, rig/proportion changes, and both delayed/immediate death-respawn paths.

Both real customizer handoffs passed with edited proportions and skin, a 1.25 m
jump, and zero facial drift. Forward-only camera yaw travel was 0.52 / 0.11
degrees for the two bodies and 0.14 degrees in the standalone negative-orbit
test. The dependency audit and saving without a gameplay provider also passed.
Detailed results: `../work/rpg-animation-validation-2026-10-27.log`.

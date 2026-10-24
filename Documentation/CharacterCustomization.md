# Character Customization System

## Purpose
This project explores a reusable character customization system for Unity 6.3. The system should remain suitable for integration into varied Unity projects and, when sufficiently mature, for distribution through the Unity Asset Store.

The current architecture diagram and presentation pseudocode are available in [SystemPresentationArchitecture.md](SystemPresentationArchitecture.md). Third-party attribution for adapted BodyMorphLite behavior is recorded in [ThirdPartyNotices.md](ThirdPartyNotices.md).

## Current State
The demo scene contains male and female MPFB characters with a shared morph and skin interface. One character is displayed at a time and each retains independent in-memory morph values while switching.

The creator UI exposes body and head controls through stable logical identifiers. Bipolar controls drive imported positive and negative blendshape pairs, while positive-only controls use a zero-to-one range. Bindings are resolved across all skinned renderers beneath the selected character so dependent face and accessory meshes remain aligned.

Morph definitions now come from `CharacterMorphCatalogAsset` when assigned, with `CharacterMorphCatalog` preserved as a static fallback. This lets the demo keep its current rows and recipe IDs while moving toward authored, swappable character catalogs.

`CharacterMorphDefinition` is the shared abstract definition type. `BipolarMorphDefinition` and `PositiveOnlyMorphDefinition` inherit its common identifier, label, group, and shape-name mapping, then provide their own valid range, binding requirements, and weight calculation. Each definition also declares a driver type. Blendshape definitions drive mesh weights; skeletal definitions route through `CharacterRigProportionDriver`.

The rig path separates responsibility from surface morphs. Breast, glutes, face, belly, lower waist, and similar detail controls remain blendshape-driven. Height, shoulder width, hips, and the BML-style proportion channels are skeletal. The Body tab now consolidates surface body controls with upper body, lower body, upper waist, chest, head, neck, arms, hands, fingers, legs, and feet as individual constrained sliders while preserving hidden BML-compatible `rig.waist`, `rig.shoulders`, and `rig.foot_radius` recipe IDs for compatibility and advanced testing.

`CharacterRigProportionDriver` captures bind pose, reapplies skeletal changes from that baseline, and exposes optional foot grounding through `CharacterRigAnimatorIkBridge`. Once skeletal edits settle, it rebuilds the humanoid Avatar from the morphed skeleton so Unity's retargeting and IK use the edited limb lengths. Unmapped limb twist bones from the Auto-Rig Pro export are driven by `CharacterTwistBoneDriver`, which the controller adds to each character Animator in Play Mode. Forearm and shin twist bones take a share of the hand or foot roll; upper-arm and thigh twist bones counter part of their segment's own roll. Proportions are reapplied only after an edit rather than every frame, because humanoid animation does not write bone scale or the edited shoulder and upper-leg positions. `ReapplyPoseEveryFrame` restores per-frame application for setups where something else does, and translation DoF Avatars enable it automatically. Foot grounding probes from the current frame's animated IK goals and tilts the animated foot rotation to the ground slope, so heel lift and toe-off are kept at full IK weight. The scale formulas and grounding strategy are adapted from BodyMorphLite by Serhat Dikel under the MIT license, but are integrated into CharacterEditor's catalog, recipe, and active-root architecture rather than copied as a drop-in component.

Gameplay systems can drive the original muscle and body-fat vision through `SetStatGrowth(statId, normalizedValue)`. `StatGrowthDefinition` maps a normalized host-game stat to an existing stable morph ID and output range. Muscle maps `0..1` to a `body.muscle` offset of `0..1`; body fat maps `0..1` to a `body.weight` offset of `-1..1`, with `0.5` representing no change.

Stat growth writes into a gameplay morph layer rather than the creator recipe. Each morph's applied value is the base recipe value plus the sum of every gameplay layer offset, clamped to the definition range. The player's authored appearance is therefore preserved when strength or body fat changes, and finalized saves and presets only ever capture the base recipe. Host games can add their own layers, such as ageing or injury, through `SetMorphLayerOffset(layerId, morphId, offset)`.

```csharp
controller.SetStatGrowth("muscle", normalizedStrength);
controller.SetStatGrowth("body_fat", normalizedBodyFat);
```

The menu is authored in `Resources/Prefabs/CharacterMorphMenuDemoUI.prefab`. Eight morph tabs, Skin, and Presets share the left rail. The Skin tab offers authored tones, saved custom skin colours, a collapsed HSV custom-colour panel, roughness control, and eye material swatches. Skin is applied only to explicitly assigned body renderers through `MaterialPropertyBlock`, so shared materials and non-skin meshes are not modified. Eye material choices are assigned from `CharacterEyeMaterialPalette` and are stored in recipes by stable material ID.

Each morph row explicitly stores its logical identifier and UI references. Runtime code binds these existing controls without rebuilding the menu; a missing row is cloned from the prefab's inactive slider template, placed in catalogue order, and shown only when its group is selected.

The character preview uses left-drag to rotate the visible character, the mouse wheel to zoom, and right-drag to move the focus point vertically. `CharacterPreviewControls` continually frames the active renderer bounds and exposes native position/rotation damping. It can blend its transform and field of view into an optional gameplay camera without a Cinemachine dependency.

`CharacterRecipe` is the versioned shared appearance payload: sex, skin selection or custom colour, eye material ID, and all morph values in stable ID order. `CharacterPreset` stores read-only authored starter recipes as ScriptableObjects, while runtime-created presets are saved to JSON. `CharacterProfile` applies presets to NPCs or accepts a runtime recipe for players and other spawned characters.

The fixed footer accepts a character name, randomizes the current sex within restrained morph ranges, and finalizes the visible recipe. Finalized players are stored in `Application.persistentDataPath/SolCharacterCustomization/players.json`. The file supports multiple records with stable IDs; replacing a case-insensitive name requires an explicit second click.

Runtime presets are stored in `Application.persistentDataPath/SolCharacterCustomization/presets.json` through `CharacterPresetSaveRepository`. They use the same explicit overwrite pattern as finalized players. The menu merges authored presets and saved presets in one dropdown, labelling the source as Authored or Saved.

Saved custom skin colours are stored in `Application.persistentDataPath/SolCharacterCustomization/skin-colors.json` through `CharacterSkinColorSaveRepository`. Duplicate colours are detected within one 8-bit colour step and reuse the existing saved colour rather than creating another swatch.

```csharp
CharacterRecipe npcRecipe = npcProfile.CaptureRecipe();
playerProfile.ApplyRecipe(savedPlayer.Recipe);
npcProfile.ApplyPreset(authoredNpcPreset);
```

Host games can keep the default JSON files, subscribe to the menu and finalization events, or replace persistence with game-owned repositories:

```csharp
menu.SetPresetRepository(customPresetRepository);
menu.RuntimePresetSaved += savedPreset => AppendToGameSave(savedPreset.Recipe);
menu.PresetLoaded += (name, recipe) => TrackPresetUse(name);

finalization.SetPlayerSaveRepository(customPlayerRepository);
finalization.Finalized += player => AttachCharacterToPlayer(player.Recipe);
```

## Design Principles
- Keep the system independent of any single game's requirements.
- Add only the functionality required by the current iteration.
- Record accepted decisions, rejected approaches, and unresolved questions as the design develops.

## Decisions
- The default character sex is centralized in `CharacterCustomizationUiConfig`.
- Male and female recipes remain independent during the current session.
- Runtime code addresses morphs by stable identifiers rather than FBX blendshape names.
- `CharacterMorphCatalogAsset` is the preferred authored source of morph definitions; the static catalog remains as a migration fallback.
- Morph behaviour uses inheritance and polymorphism: the controller delegates range, shape, and weight rules to the concrete morph definition.
- Driver type separates blendshape surface controls from skeletal proportion controls.
- BML-style rig channels are curated deliberately into the Body tab, while hidden compatibility channels and inspector controls remain available for direct driver testing.
- Rig edits are reapplied from captured bind pose to avoid cumulative bone drift.
- Skeletal edits are baked into a rebuilt humanoid Avatar after a short debounce rather than on every slider tick, because assigning an Avatar rebinds the Animator. Animator state and parameters are carried across the rebind. The BML leg-length ground offset is only used while the Avatar is stale or rebuilding is disabled; the feet offset remains.
- Joint offsets are applied by moving bones and bind poses together on a per-character mesh copy, then sharing that copy among characters with identical offsets once an Avatar rebuild commits. Editing in place stays cheap (bind poses only), and crowds keep mesh sharing.
- The sidecar's axis mapping is solved at bind time from rest bone heads rather than configured, because Blender and Unity FBX settings vary per export. Only signed axis permutations and uniform scale are tried, which covers every FBX round trip and rejects mismatched rigs.
- Skeletal morphs switch to their mesh shapes only when that sex has a joint offset table, so projects without sidecars keep bone scaling and nothing tears.
- Blender tooling lives in `Tools/Blender`, outside `Assets`. `sol_character_tools` reads the authored `.blend` (or an exported FBX) without modifying weights or shapes, computes per-shape joint offsets from the voxel-heat joint bands, runs pre-export checks, and writes a `.solrig` sidecar to `Assets/CharCustomization/RigData`. The sidecar uses the armature's own axes in metres plus rest bone heads, so Unity can derive the axis mapping instead of depending on FBX export settings. See `Tools/Blender/README.md`.
- Twist bones are driven by swing-twist decomposition in a late `LateUpdate`, not by the Animation Rigging package, to avoid a package dependency and keep setup automatic. Rest rotations come from skin bind poses so initialization order does not matter. This fixes the main linear-blend-skinning artifact at wrists and ankles without dual quaternion skinning, which would also need scale-aware blending because the proportion system scales bones.
- Rig proportions use a dirty flag instead of a per-frame reapply. Idle characters and baked NPCs cost nothing in the rig driver until edited.
- NPC baking folds catalog blendshapes into reference-counted shared meshes instead of per-character copies, so crowds built from presets share memory. Bone scales stay live on the rig with the rebuilt Avatar; they are not baked into bind poses, so attachments parented to scaled bones still inherit that scale.
- A baked mesh's lifetime follows its renderer through `CharacterBakedMeshHandle`, not the customization manager, so host games can destroy the manager after finalizing.
- Foot IK smooths only the terrain slope and layers it over the animated foot rotation. The earlier BML-style target replaced the animated rotation with the root rotation, which flattened feet during walk cycles.
- `CharacterRigAnimatorIkBridge` is the only `OnAnimatorIK` entry point, because the active Animator lives on the male/female body prefab rather than on the manager.
- Stat growth uses composition rather than another morph subclass because one input system can drive different morph behaviours. The host game owns progression rules and sends normalized muscle or body-fat values.
- Gameplay changes are additive layers over the creator recipe, never writes into it. Earlier stat growth overwrote the player's chosen value and leaked gameplay state into finalized saves. Layers are runtime-only; the host game re-sends stats after loading a save.
- The authored Canvas prefab is the source of truth for menu hierarchy and presentation.
- Runtime UI creation is limited to cloning a missing morph row from the assigned template.
- Switching tabs preserves the selected category across character changes and returns the slider list to the top.
- Reset clears only the visible character's recipe through `ResetCurrentCharacter()` and immediately refreshes the current sliders. The other character's recipe is unchanged.
- Authored and runtime presets store the complete versioned recipe. Missing known morphs load as zero, while unknown IDs are ignored with a warning.
- Character names belong to player records rather than reusable appearance recipes.
- Randomize preserves name and sex, uses 65 percent of each morph's legal range, and selects authored skin tones and eye materials.
- Authored skin swatches use stable IDs. A custom HSV colour is stored as an RGBA recipe override.
- Skin colour is parameterised as melanin, hemoglobin, and undertone instead of HSV, so every value is a plausible skin and regions such as palms, lips, and blush can respond physically. The human colour table comes from an absorption model fitted to the Monk Skin Tone Scale, with the melanin axis remapped so the ten swatches are evenly spaced. A neutral surface-reflection term keeps very deep skin from turning orange.
- The colour model is a generated table rather than shader maths so fantasy races can supply painted tables in the same layout through `CharacterSkinProfile`, and so CPU colour matching reads exactly what the shader renders.
- The skin shader is hand-written HLSL rather than Shader Graph because Shader Graph cannot replace URP's lighting model. Scattering is approximated with per-channel wrapped diffuse scaled by curvature instead of screen-space subsurface scattering, which URP does not provide.
- Recipes moved to version 3 with stored skin parameters. Version 2 recipes migrate on apply by matching their tone or custom colour, so no save conversion step is needed.
- Skin values reach Sol/Character Skin materials through per-character material copies made in Play Mode, which keeps skin renderers in the SRP Batcher. Other materials still use `MaterialPropertyBlock`. Copies are not made in Edit Mode, so scene materials are never replaced while authoring.
- The skin picker replaces HSV with a melanin by hemoglobin plane and three parameter sliders whose backgrounds show the reachable colours. It is built into the authored prefab by an explicit editor tool that reuses the existing slider objects, consistent with the prefab-first menu. The HSV path remains as a fallback for menus without a picker.
- Eye material swatches use stable IDs from `CharacterEyeMaterialPalette` and persist through `CharacterRecipe.EyeMaterialId`.
- `CharacterProfile` is the common application boundary for authored NPC presets and player-save recipes.
- Finalization stores multiple players in one JSON file and preserves record IDs when explicitly overwriting a duplicate name.
- Runtime presets store multiple saved appearances in one JSON file and preserve record IDs when explicitly overwriting a duplicate name.
- Saved custom skin colours store multiple RGBA swatches in one JSON file and de-duplicate near-identical colours.
- Gameplay-camera and controller references are optional. When assigned, finalization performs a native smooth handoff; otherwise the saved demo remains interactive.
- Reset All is contained in the Presets tab and clears every morph on the visible character. Each morph tab exposes a fixed Reset control that clears only that tab's group.
- `Tools > Character Customization > Validate Morph Demo` checks the centralized tab list, skin palette and renderer bindings, profile/finalization wiring, character morphs, scene wiring, and UI input references without rebuilding assets.
- `Tools > Sol > Character Customization > Validate Selected Character Setup` checks catalog validity, active roots, humanoid Animator, blendshape bindings, required rig bones, IK bridge, and Animator Controller IK Pass.
- ScriptableObject presets are authoring assets rather than player save files. User-saved presets persist through `CharacterPresetSaveRepository`.
- Editor setup tools are explicit menu actions. They no longer auto-run on editor reload, so designer prefab edits are not silently rewritten.
- Game-specific progression, hair, and clothing remain outside the current iteration.

## Validation Checkpoint
The earlier tabbed-menu iteration was validated in Unity 6000.3.9f1 on 24 June 2026. The finalization and runtime-preset iterations add Edit Mode coverage for recipe JSON, multiple-player persistence, runtime-preset persistence, duplicate overwrite handling, malformed files, and restrained randomization, plus validator and Play Mode checks for the authored profile, skin, footer, and camera wiring.

The BodyMorphLite merge has catalog/visibility edit-mode coverage. Avatar rebuilding has edit-mode coverage on a synthetic humanoid: bone scales and side offsets are baked into the rebuilt skeleton, neutral edits restore the source Avatar, unchanged values do not queue rebuilds, and unbinding restores the source Avatar and bind pose. The rig dirty flag is covered for clean poses, the per-frame opt-in, and edits made before binding. Twist bones are covered on a synthetic humanoid with Auto-Rig Pro style twist bones: detection and default weights, helper bones ignored, forearm and leg twist following hand and foot roll, swing rejected, twist separated from combined swing, upper-arm counter-roll, bind-pose rest rotations on an already-posed character, and signed angle wrapping. Joint offsets are covered for sidecar parsing (including both exported characters), rejecting old versions and malformed offsets, calibration through a mirrored, swapped, and scaled armature, a shape moving its joint and bind pose while children follow, the rebuilt Avatar and shared mesh tracking the offsets and releasing at zero, unbinding restoring mesh and bones, and skeletal morphs taking the mesh-shape route only when a table is assigned. The skin picker is covered for binding to profile values, slider edits updating the profile, plane, and change event, and slider gradients running in the right direction. The skin colour model is covered for Monk swatch accuracy, monotonic melanin, hemoglobin and undertone direction, generated-table agreement with the model, inverse colour matching, recipe storage and version 2 migration, and parameter round trips through `CharacterProfile`. Mesh baking is covered for folding, multi-frame interpolation, unreadable meshes, shared-mesh reuse and release, expression weights surviving a bake, stale detection and rebake, unbake, recipe sharing between characters, and releasing the hidden sex's root.

Play Mode proof is still required on the MPFB characters. Run each case with grounding enabled, then with it disabled, and with Grounding Debug drawing on:

| Case | Setup | Pass condition |
| --- | --- | --- |
| Flat idle | `rig.legs` at -1, 0, 1; `rig.feet` at -1 and 1 | Soles rest on the floor with no visible float or sink after the rebuild settles. |
| Flat walk | Same values, demo walk | No foot sliding beyond the unmorphed baseline; heels lift and toes roll off. |
| Steps | 10 cm and 18 cm steps | Pelvis lowers to the lower foot; neither foot penetrates the step. |
| Sex switch | Morph female, switch to male, switch back | Each body keeps its own proportions; the inactive body returns to its source Avatar. |
| Finalize handoff | Morph, then finalize immediately | Gameplay starts on the rebuilt Avatar with no one-frame pop or T-pose. |
| Drag stress | Drag `rig.legs` continuously for several seconds | No rebuild while dragging; one rebuild after release; no animation restart. |
| Wrist and ankle twist | Pose or animate a 90° forearm roll and a foot roll; compare with the twist driver disabled | No candy-wrap pinch at the wrist or ankle; forearm skin twists progressively from elbow to wrist; elbows and knees do not roll. |

## Next Steps
1. Run the Play Mode grounding checklist above on the MPFB characters and record the results.
2. Assign `SK_Human_F` and `SK_Human_M` joint offset tables on the morph controller, confirm the calibration in the Play Mode checklist (height, shoulders, and hips now use mesh shapes), and fix the sidecar check findings in Blender: female teeth and tongue missing mouth and jaw shapes, the `Body_Stat_Muscle` and ear-rotation naming differences between sexes, the stray `Key 1` on the male teeth, and more than four bone influences per vertex.
3. Convert the body materials to Sol/Character Skin, build the skin picker into the demo menu, and paint the region mask (lips, palms/soles/nails, blush, areolae) and a transmission channel in the shared MakeHuman UV layout.
4. Add age: wrinkle normal blend, roughness and pigmentation shifts, age spots, and MPFB age targets driven through the `age` morph layer.
5. Profile NPC baking in a crowd scene: GPU skinning time and mesh memory for 50 characters from three presets, baked and unbaked.
6. Assign and validate catalog/profile assets in the demo prefab, then test setup on a brand-new humanoid root.
7. Separate reusable runtime, UGUI, gameplay-demo, editor, and tests into assembly boundaries once the current demo is stable.
8. Move demo-only FBX, materials, fonts, sprites, lighting, and scenes into a sample boundary during package extraction.
9. Expand automated coverage for value clamping, bipolar weights, stat-growth mapping, recipe isolation, missing bindings, skeletal channels, skin rendering, and gameplay-camera handoff.
10. Expand installation and packaging documentation, declare dependencies, audit imported asset redistribution rights, and add version and changelog files.
11. Add appearance option providers for hair and clothing only when those selectors become active scope.

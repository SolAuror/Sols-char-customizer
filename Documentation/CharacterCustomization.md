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

`CharacterRigProportionDriver` captures bind pose, reapplies skeletal changes from that baseline, and exposes optional foot grounding through `CharacterRigAnimatorIkBridge`. The scale formulas and grounding strategy are adapted from BodyMorphLite by Serhat Dikel under the MIT license, but are integrated into CharacterEditor's catalog, recipe, and active-root architecture rather than copied as a drop-in component.

Gameplay systems can drive the original muscle and body-fat vision through `SetStatGrowth(statId, normalizedValue)`. `StatGrowthDefinition` maps a normalized host-game stat to an existing stable morph ID and output range. Muscle maps `0..1` to `body.muscle` at `0..1`; body fat maps `0..1` to `body.weight` at `-1..1`, with `0.5` representing the neutral body.

```csharp
controller.SetStatGrowth("muscle", normalizedStrength);
controller.SetStatGrowth("body_fat", normalizedBodyFat);
```

The menu is authored in `Prefabs/CharacterMorphMenuDemoUI.prefab`. Eight morph tabs, Skin, and Presets share the left rail. The Skin tab offers authored tones, saved custom skin colours, a collapsed HSV custom-colour panel, roughness control, and eye material swatches. Skin is applied only to explicitly assigned body renderers through `MaterialPropertyBlock`, so shared materials and non-skin meshes are not modified. Eye material choices are assigned from `CharacterEyeMaterialPalette` and are stored in recipes by stable material ID.

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
- `CharacterRigAnimatorIkBridge` is the only `OnAnimatorIK` entry point, because the active Animator lives on the male/female body prefab rather than on the manager.
- Stat growth uses composition rather than another morph subclass because one input system can drive different morph behaviours. The host game owns progression rules and sends normalized muscle or body-fat values.
- The authored Canvas prefab is the source of truth for menu hierarchy and presentation.
- Runtime UI creation is limited to cloning a missing morph row from the assigned template.
- Switching tabs preserves the selected category across character changes and returns the slider list to the top.
- Reset clears only the visible character's recipe through `ResetCurrentCharacter()` and immediately refreshes the current sliders. The other character's recipe is unchanged.
- Authored and runtime presets store the complete versioned recipe. Missing known morphs load as zero, while unknown IDs are ignored with a warning.
- Character names belong to player records rather than reusable appearance recipes.
- Randomize preserves name and sex, uses 65 percent of each morph's legal range, and selects authored skin tones and eye materials.
- Authored skin swatches use stable IDs. A custom HSV colour is stored as an RGBA recipe override.
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

The BodyMorphLite merge currently passes generated runtime/editor builds and has catalog/visibility edit-mode coverage. The remaining validation gap is visual Play Mode proof: height, leg, and foot edits should be tested on flat ground, small steps, male/female switching, and finalize handoff with grounding enabled.

## Next Steps
1. Prove BML-style grounding in Play Mode across flat ground, small steps, male/female switching, and finalize handoff.
2. Assign and validate catalog/profile assets in the demo prefab, then test setup on a brand-new humanoid root.
3. Separate reusable runtime, UGUI, gameplay-demo, editor, and tests into assembly boundaries once the current demo is stable.
4. Move demo-only FBX, materials, fonts, sprites, lighting, and scenes into a sample boundary during package extraction.
5. Expand automated coverage for value clamping, bipolar weights, stat-growth mapping, recipe isolation, missing bindings, skeletal channels, skin rendering, and gameplay-camera handoff.
6. Expand installation and packaging documentation, declare dependencies, audit imported asset redistribution rights, and add licence, version, and changelog files.
7. Add appearance option providers for hair and clothing only when those selectors become active scope.

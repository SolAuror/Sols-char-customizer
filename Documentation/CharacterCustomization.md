# Character Customization System

## Purpose
This project explores a reusable character customization system for Unity 6.3. The system should remain suitable for integration into varied Unity projects and, when sufficiently mature, for distribution through the Unity Asset Store.

The current architecture diagram and presentation pseudocode are available in [SystemPresentationArchitecture.md](SystemPresentationArchitecture.md).

## Current State
The demo scene contains male and female MPFB characters with a shared morph interface. One character is displayed at a time and each retains an independent in-memory recipe while switching.

The initial implementation exposes 38 body and head controls through stable logical identifiers. Bipolar controls drive the imported positive and negative blendshape pair, while muscle and breast use a zero-to-one range. Bindings are resolved across all skinned renderers beneath the selected character so dependent face and accessory meshes remain aligned.

`CharacterMorphDefinition` is the shared abstract definition type. `BipolarMorphDefinition` and `PositiveOnlyMorphDefinition` inherit its common identifier, label, group, and shape-name mapping, then provide their own valid range, binding requirements, and weight calculation. The controller works through the base type, so it does not need type-specific conditionals when applying a morph.

Gameplay systems can drive the original muscle and body-fat vision through `SetStatGrowth(statId, normalizedValue)`. `StatGrowthDefinition` maps a normalized host-game stat to an existing stable morph ID and output range. Muscle maps `0..1` to `body.muscle` at `0..1`; body fat maps `0..1` to `body.weight` at `-1..1`, with `0.5` representing the neutral body.

```csharp
controller.SetStatGrowth("muscle", normalizedStrength);
controller.SetStatGrowth("body_fat", normalizedBodyFat);
```

The menu is authored in `Prefabs/CharacterMorphMenu.prefab`. Eight boxed tabs filter the single slider area into Body, Jaw / Chin, Mouth, Nose, Cheeks, Eyes, Brows, and Neck / Ears groups. The tab rail and slider panel scroll independently, with Body selected initially.

Each morph row explicitly stores its logical identifier and UI references. Runtime code binds these existing controls without rebuilding the menu; a missing row is cloned from the prefab's inactive slider template, placed in catalogue order, and shown only when its group is selected.

The character preview uses left-drag to rotate the visible character, the mouse wheel to zoom, and right-drag to pan the camera vertically. `CharacterPreviewControls` is authored on `CharacterMorphManager.prefab`, with sensitivities and movement limits exposed in the Inspector.

`CharacterMorphPreset` is an authored ScriptableObject snapshot containing one character sex and all catalogue morph values in stable ID order. The Presets tab accepts a preset name, saves or overwrites that named entry in the shared preset library, and loads saved entries from a dropdown. Loading switches to the stored sex and restores the complete recipe.

## Design Principles
- Keep the system independent of any single game's requirements.
- Add only the functionality required by the current iteration.
- Record accepted decisions, rejected approaches, and unresolved questions as the design develops.

## Decisions
- Female is the default character in the demo.
- Male and female recipes remain independent during the current session.
- Runtime code addresses morphs by stable identifiers rather than FBX blendshape names.
- Morph behaviour uses inheritance and polymorphism: the controller delegates range, shape, and weight rules to the concrete morph definition.
- Stat growth uses composition rather than another morph subclass because one input system can drive different morph behaviours. The host game owns progression rules and sends normalized muscle or body-fat values.
- The authored Canvas prefab is the source of truth for menu hierarchy and presentation.
- Runtime UI creation is limited to cloning a missing morph row from the assigned template.
- Switching tabs preserves the selected category across character changes and returns the slider list to the top.
- Reset clears only the visible character's recipe through `ResetCurrentCharacter()` and immediately refreshes the current sliders. The other character's recipe is unchanged.
- Presets store a complete recipe and sex. Missing known values load as zero, while unknown IDs are ignored with a warning.
- Reset All is contained in the Presets tab and clears every morph on the visible character. Each morph tab exposes a fixed Reset control that clears only that tab's group.
- `Tools > Character Customization > Validate Morph Demo` checks the menu, character morphs, scene wiring, and UI input references without rebuilding assets.
- ScriptableObject presets are authoring assets rather than player save files. A build can load them, but Save changes remain in memory only for that player session.
- Durable player save data, colour customization, and game-specific progression remain outside the current iteration.

## Validation Checkpoint
The tabbed menu and reset iteration was validated in Unity 6000.3.9f1 on 24 June 2026. The editor validator, Play Mode acceptance checks, and runtime and editor builds passed. The checks covered all 38 authored rows, tab filtering, independent scroll areas, character switching, recipe isolation, current-character reset, and blendshape application across every matching renderer.

## Next Steps
1. Replace the hardcoded MPFB catalogue and male/female assumptions with serialized morph profiles and character-variant bindings while preserving stable logical identifiers.
2. Separate reusable runtime and editor code from the MPFB demo, then add package metadata, assembly definitions, and a `Samples~` demo.
3. Add Edit Mode tests for value clamping, bipolar weights, stat-growth mapping, recipe isolation, missing bindings, and reset, followed by Play Mode coverage for switching and prefab UI binding.
4. Document installation and public APIs, declare dependencies, audit MPFB redistribution rights, and add licence, version, and changelog files.
5. Add a writable player serialization format only when durable persistence becomes an active iteration. Colour customization and game progression remain deferred until separately approved.

## Unresolved Questions
- What writable recipe format should complement the authored ScriptableObject presets when player persistence is introduced?

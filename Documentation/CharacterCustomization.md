# Character Customization System

## Purpose
This project explores a reusable character customization system for Unity 6.3. The system should remain suitable for integration into varied Unity projects and, when sufficiently mature, for distribution through the Unity Asset Store.

The current architecture diagram and presentation pseudocode are available in [SystemPresentationArchitecture.md](SystemPresentationArchitecture.md).

## Current State
The demo scene contains male and female MPFB characters with a shared morph and skin interface. One character is displayed at a time and each retains independent in-memory morph values while switching.

The initial implementation exposes 38 body and head controls through stable logical identifiers. Bipolar controls drive the imported positive and negative blendshape pair, while muscle and breast use a zero-to-one range. Bindings are resolved across all skinned renderers beneath the selected character so dependent face and accessory meshes remain aligned.

`CharacterMorphDefinition` is the shared abstract definition type. `BipolarMorphDefinition` and `PositiveOnlyMorphDefinition` inherit its common identifier, label, group, and shape-name mapping, then provide their own valid range, binding requirements, and weight calculation. The controller works through the base type, so it does not need type-specific conditionals when applying a morph.

Gameplay systems can drive the original muscle and body-fat vision through `SetStatGrowth(statId, normalizedValue)`. `StatGrowthDefinition` maps a normalized host-game stat to an existing stable morph ID and output range. Muscle maps `0..1` to `body.muscle` at `0..1`; body fat maps `0..1` to `body.weight` at `-1..1`, with `0.5` representing the neutral body.

```csharp
controller.SetStatGrowth("muscle", normalizedStrength);
controller.SetStatGrowth("body_fat", normalizedBodyFat);
```

The menu is authored in `Prefabs/CharacterMorphMenu.prefab`. Eight morph tabs, Skin, and Presets share the left rail. The Skin tab offers eight authored tones and a collapsed HSV custom-colour panel. Skin is applied only to explicitly assigned body renderers through `MaterialPropertyBlock`, so shared materials and non-skin meshes are not modified.

Each morph row explicitly stores its logical identifier and UI references. Runtime code binds these existing controls without rebuilding the menu; a missing row is cloned from the prefab's inactive slider template, placed in catalogue order, and shown only when its group is selected.

The character preview uses left-drag to rotate the visible character, the mouse wheel to zoom, and right-drag to move the focus point vertically. `CharacterPreviewControls` continually frames the active renderer bounds and exposes native position/rotation damping. It can blend its transform and field of view into an optional gameplay camera without a Cinemachine dependency.

`CharacterRecipe` is the versioned shared appearance payload: sex, skin selection or custom colour, and all morph values in stable ID order. `CharacterPreset` stores that recipe as an authored ScriptableObject. `CharacterProfile` applies presets to NPCs or accepts a runtime recipe for players and other spawned characters.

The fixed footer accepts a character name, randomizes the current sex within restrained morph ranges, and finalizes the visible recipe. Finalized players are stored in `Application.persistentDataPath/SolCharacterCustomization/players.json`. The file supports multiple records with stable IDs; replacing a case-insensitive name requires an explicit second click.

```csharp
CharacterRecipe npcRecipe = npcProfile.CaptureRecipe();
playerProfile.ApplyRecipe(savedPlayer.Recipe);
npcProfile.ApplyPreset(authoredNpcPreset);
```

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
- Presets store the complete versioned recipe. Missing known morphs load as zero, while unknown IDs are ignored with a warning.
- Character names belong to player records rather than reusable appearance recipes.
- Randomize preserves name and sex, uses 65 percent of each morph's legal range, and selects only authored skin tones.
- Authored skin swatches use stable IDs. A custom HSV colour is stored as an RGBA recipe override.
- `CharacterProfile` is the common application boundary for authored NPC presets and player-save recipes.
- Finalization stores multiple players in one JSON file and preserves record IDs when explicitly overwriting a duplicate name.
- Gameplay-camera and controller references are optional. When assigned, finalization performs a native smooth handoff; otherwise the saved demo remains interactive.
- Reset All is contained in the Presets tab and clears every morph on the visible character. Each morph tab exposes a fixed Reset control that clears only that tab's group.
- `Tools > Character Customization > Validate Morph Demo` checks the ten tabs, skin palette and renderer bindings, profile/finalization wiring, character morphs, scene wiring, and UI input references without rebuilding assets.
- ScriptableObject presets are authoring assets rather than player save files. A build can load them, but Save changes remain in memory only for that player session.
- Game-specific progression, eye colour, hair, and clothing remain outside the current iteration.

## Validation Checkpoint
The earlier tabbed-menu iteration was validated in Unity 6000.3.9f1 on 24 June 2026. The finalization iteration adds Edit Mode coverage for recipe JSON, multiple-player persistence, duplicate overwrite handling, malformed files, and restrained randomization, plus validator and Play Mode checks for the authored profile, skin, footer, and camera wiring.

## Next Steps
1. Replace the hardcoded MPFB catalogue and male/female assumptions with serialized morph profiles and character-variant bindings while preserving stable logical identifiers.
2. Separate reusable runtime and editor code from the MPFB demo, then add package metadata, assembly definitions, and a `Samples~` demo.
3. Expand automated coverage for value clamping, bipolar weights, stat-growth mapping, recipe isolation, missing bindings, skin rendering, and the assigned gameplay-camera handoff.
4. Document installation and public APIs, declare dependencies, audit MPFB redistribution rights, and add licence, version, and changelog files.
5. Add appearance option providers for eye colour, hair, and clothing only when those selectors become active scope.

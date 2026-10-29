# Character Customization API Guide

This guide covers the runtime methods and extension points a host game should use. The public namespace is `Sol.CharacterCustomization`.

The stable API boundary is the logical ID layer. Gameplay, UI, presets, and saves should use IDs such as `body.weight`, `body.muscle`, `rig.upper_body`, `muscle`, and `body_fat`; imported FBX blendshape names belong inside the morph catalog.

## Core Types

### `CharacterMorphController`

Attach this to the character customization manager. It owns the active sex, in-memory male/female morph recipes, blendshape bindings, stat-growth mapping, and the optional rig proportion driver.

| Member | Use |
| --- | --- |
| `ActiveSex` | Current visible sex, `CharacterSex.Female` or `CharacterSex.Male`. |
| `ActiveCharacterRoot` | Transform for the visible character root. |
| `ActiveAnimator` / `TryGetActiveAnimator(out Animator)` | Access the active character Animator for gameplay handoff. |
| `Definitions` | Effective morph definitions from the assigned `CharacterMorphCatalogAsset`, or static fallback catalog. |
| `SetSex(CharacterSex sex)` | Shows the selected root, hides the other root, rebinds the rig driver, and reapplies that sex's current recipe. |
| `SetMorph(string morphId, float value)` | Sets one morph on the active sex. Values are clamped to the definition range. |
| `GetMorph(string morphId)` | Reads the active sex's creator (base recipe) value, returning `0` when unset. Gameplay layers are not included. |
| `GetEffectiveMorph(string morphId)` | Reads the value actually applied to the model: base recipe plus every gameplay layer, clamped to the definition range. |
| `TryGetDefinition(string morphId, out CharacterMorphDefinition definition)` | Resolves catalog metadata for a stable morph ID. |
| `CaptureMorphValues()` | Captures all base recipe values in catalog order for recipe storage. Gameplay layers are excluded. |
| `ApplyMorphValues(IReadOnlyList<CharacterMorphValue> values)` | Applies saved morph values, defaulting missing known IDs to `0` and warning for unknown IDs. |
| `RandomizeCurrent(float rangeScale = 0.65f, System.Random random = null)` | Randomizes creator-visible morphs on the active sex within a restrained portion of their range. |
| `SetStatGrowth(string statId, float normalizedValue)` | Maps a host-game stat to an additive offset on its target morph, stored in the `stat.<statId>` gameplay layer. `muscle` offsets `body.muscle`; `body_fat` offsets `body.weight`. The creator recipe is never modified. |
| `ClearStatGrowth(string statId)` | Removes one stat's gameplay layer. |
| `SetMorphLayerOffset(string layerId, string morphId, float offset)` | Sets an additive offset for one morph in a named gameplay layer such as `injury` or `age`. |
| `TryGetMorphLayerOffset(string layerId, string morphId, out float offset)` | Reads one layer's offset for a morph. |
| `ClearMorphLayer(string layerId)` / `ClearAllMorphLayers()` | Removes one or every gameplay layer and reapplies the base recipe. |
| `ResetCurrentCharacter()` | Resets every base recipe morph on the active sex to `0`. Gameplay layers stay applied. |
| `ResetGroup(string groupId)` | Resets only one morph group, such as `Body` or `Eyes`. Gameplay layers stay applied. |
| `IsMorphAvailable(string morphId)` | Checks whether the active model can currently apply a morph through blendshapes or rig binding. |
| `ValidateConfiguration(List<string> errors)` | Reports missing roots, invalid catalog data, missing blendshapes, or missing rig driver setup. |
| `BakeActiveCharacter(out string error)` | Folds the active character's catalog blendshapes into shared baked meshes. Calling it again rebakes from the source meshes. |
| `UnbakeActiveCharacter()` | Restores the source meshes and reapplies the current recipe and gameplay layers. |
| `IsBaked` / `IsBakeStale` | Whether baked meshes are shown, and whether recipe or layer edits since the bake are not yet visible. |
| `ReleaseInactiveSexRoot()` | Destroys the hidden sex's character root so a finished NPC carries one rig. That sex can no longer be shown. |

Example:

```csharp
using Sol.CharacterCustomization;

public sealed class StrengthMorphBridge : MonoBehaviour
{
    [SerializeField] private CharacterMorphController controller;

    public void ApplyProgression(float strength01, float bodyFat01)
    {
        controller.SetStatGrowth("muscle", strength01);
        controller.SetStatGrowth("body_fat", bodyFat01);
    }
}
```

### `CharacterProfile`

Use this as the recipe boundary for NPC presets, player saves, skin, eye materials, and spawned characters. It wraps `CharacterMorphController` with appearance data that is not just morph values.

| Member | Use |
| --- | --- |
| `CaptureRecipe()` | Captures sex, skin tone or custom skin color, eye material, and all morph values. |
| `ApplyRecipe(CharacterRecipe recipe)` | Applies a complete versioned recipe. |
| `ApplyPreset(CharacterPreset preset)` | Applies an authored ScriptableObject preset. |
| `SetSkinTone(string toneId)` | Applies a palette skin tone and clears the custom color flag. |
| `SetCustomSkinColor(Color color)` | Applies a clamped custom skin color through `MaterialPropertyBlock`. |
| `SetSkinRoughness(float roughness)` | Updates configured roughness/smoothness shader properties. |
| `SetEyeMaterial(string materialId)` | Applies a material from `CharacterEyeMaterialPalette`. |
| `RandomizeCurrent(float rangeScale = 0.65f, System.Random random = null)` | Randomizes morphs plus authored skin and eye options. |
| `RefreshSkin()` | Reapplies current skin and eye material state to assigned renderers. |
| `SetSkinParameters(CharacterSkinParameters parameters)` | Sets skin colour as melanin, hemoglobin, and undertone for Sol/Character Skin materials. Also stores the matching colour as the custom colour. |
| `SkinParameters` / `SkinProfile` | Current skin parameters, and the colour space they are read in. |
| `EvaluateSkinColor(CharacterSkinParameters parameters)` | sRGB colour of a parameter set, without regional variation, for UI swatches. |
| `BakeAppearance(bool releaseInactiveSex, out string error)` | Bakes the active character and optionally releases the hidden sex's root. `bakeOnStart` runs this automatically after the authored preset. |

Example:

```csharp
CharacterRecipe savedRecipe = playerProfile.CaptureRecipe();
npcProfile.ApplyRecipe(savedRecipe);
npcProfile.SetEyeMaterial("green");
npcProfile.SetSkinTone("deep");
```

### `CharacterRecipe`

`CharacterRecipe` is the durable appearance payload shared by authored presets, runtime presets, NPC setup, and finalized player records.

It stores:

- `Version`, currently `3`.
- `Sex`.
- `SkinToneId`, or `UsesCustomSkinColor` plus `CustomSkinColor`.
- `HasSkinParameters` plus `SkinParameters` (version 3). Older recipes have none; applying them matches parameters from the tone or custom colour.
- `EyeMaterialId`.
- `MorphValues`, as stable `CharacterMorphValue` entries.

Use `Copy()` when storing a recipe beyond the immediate call path. Use `HasValidIdentifiers(out string error)` before writing custom JSON or external save data. Use `TryGetValue(string morphId, out float value)` for targeted reads.

## UI and Finalization

### `CharacterMorphDemoUI`

This is the authored UGUI menu coordinator. Host games usually keep the prefab wiring and use its repository/event hooks instead of directly rebuilding menu rows.

| Member | Use |
| --- | --- |
| `SetPresetRepository(ICharacterPresetSaveRepository repository)` | Replaces the default JSON preset repository. |
| `SetCustomSkinRepository(ICharacterSkinColorSaveRepository repository)` | Replaces the default saved custom skin color repository. |
| `TrySaveCurrentPreset(...)` | Captures the current profile and writes a runtime preset. |
| `TryApplyPresetRecipe(string presetName, CharacterRecipe recipe, out string error)` | Applies an externally supplied recipe through the menu and raises `PresetLoaded`. |
| `CreateSliderForMorph(string morphId)` | Runtime fallback for missing authored rows. Prefer authored rows in the prefab. |
| `RefreshPanel()` / `RefreshSkinPanel()` | Re-syncs visible UI state after external changes. |
| `RuntimePresetSaved` | Event raised after a runtime preset is saved. |
| `PresetLoaded` | Event raised after a preset recipe is applied. |
| `RuntimePresetDeleted` | Event raised after a runtime preset is deleted. |

Example:

```csharp
menu.SetPresetRepository(customPresetRepository);
menu.SetCustomSkinRepository(customSkinRepository);

menu.RuntimePresetSaved += preset => SaveToCampaignSlot(preset.Recipe);
menu.PresetLoaded += (name, recipe) => Analytics.TrackPreset(name);
menu.RuntimePresetDeleted += presetName => Debug.Log($"Deleted {presetName}");
```

### `CharacterFinalizationFlow`

Use this when the creator should write a named player record and optionally hand off from preview mode to gameplay.

| Member | Use |
| --- | --- |
| `SetPlayerSaveRepository(ICharacterPlayerSaveRepository saveRepository)` | Replaces the default JSON player repository. Passing `null` restores the default repository. |
| `FinalizeCharacter()` | Captures the profile, saves a named player, handles duplicate-name confirmation, raises `Finalized`, and optionally starts camera handoff. |
| `Randomize()` / `RandomizeName()` | Runtime commands used by the footer buttons. |
| `Finalized` | Event raised with the saved `PlayerCharacterRecord`. |

Example:

```csharp
finalization.SetPlayerSaveRepository(customPlayerRepository);
finalization.Finalized += record =>
{
    currentCampaign.CharacterId = record.Id;
    currentCampaign.Appearance = record.Recipe.Copy();
};
```

## Persistence Contracts

The default repositories write JSON under:

```text
Application.persistentDataPath/SolCharacterCustomization/
```

Default files:

- `presets.json` for runtime-created presets.
- `players.json` for finalized named characters.
- `skin-colors.json` for saved custom skin colours.

Replace these interfaces when a host game needs cloud saves, profile slots, encrypted saves, or a central save file.

| Interface | Default implementation | Responsibility |
| --- | --- | --- |
| `ICharacterPresetSaveRepository` | `CharacterPresetSaveRepository` | Load, find, save, overwrite, and delete runtime presets. |
| `ICharacterPlayerSaveRepository` | `CharacterPlayerSaveRepository` | Load, find, save, and overwrite finalized player records. |
| `ICharacterSkinColorSaveRepository` | `CharacterSkinColorSaveRepository` | Load, save, de-duplicate, and delete saved custom skin colors. |

Repository methods return `bool` and an `out string error` instead of throwing for normal validation failures. Duplicate preset/player names are reported through `duplicateName`; the caller should ask for explicit confirmation before retrying with `overwriteExisting: true`.

## Catalog and Definition API

### Stable morph IDs

The static fallback catalog currently includes these integration-facing IDs:

- Body surface and proportion IDs: `body.muscle`, `body.weight`, `body.height`, `body.breast`, `body.glutes`, `body.shoulder_width`, `body.chest_width`, `body.waist`, `body.hips`, `head.weight`.
- Face IDs: `head.jaw.*`, `head.chin.*`, `head.mouth.*`, `head.nose.*`, `head.cheekbone.width`, `head.cheek.fullness`, `head.eyes.*`, `head.eyebrows.*`, `head.neck.width`, `head.ears.*`.
- Rig IDs: `rig.upper_body`, `rig.lower_body`, `rig.spine`, `rig.chest`, `rig.waist`, `rig.head`, `rig.neck`, `rig.shoulders`, `rig.upper_arms`, `rig.lower_arms`, `rig.hands`, `rig.fingers`, `rig.legs`, `rig.feet`, `rig.foot_radius`.

`rig.waist`, `rig.shoulders`, and `rig.foot_radius` remain available for recipes and advanced tests even when hidden from the creator UI.

### `CharacterMorphDefinition`

Definitions describe one logical morph:

- `Id`, `Label`, `Group`.
- `MinimumValue` and implicit maximum `1`.
- `DriverType`, either blendshape or skeletal path.
- `VisibleInCreator`.
- `RigChannel` for skeletal definitions.
- `GetPositiveShape(CharacterSex)` and `GetNegativeShape(CharacterSex)` for blendshape definitions.
- `CalculateWeights(float value, out float positiveWeight, out float negativeWeight)`.

Use `CharacterMorphCatalogAsset` as the authored catalog source on prefabs. If no asset is assigned, `CharacterMorphController` falls back to `CharacterMorphCatalog.Definitions`.

### Stat growth IDs

`CharacterStatGrowthCatalog` currently exposes:

| Stat ID | Target morph | Mapping |
| --- | --- | --- |
| `muscle` | `body.muscle` | `0..1` to an offset of `0..1`. |
| `body_fat` | `body.weight` | `0..1` to an offset of `-1..1`, with `0.5` as neutral. |

### Morph layers

The applied value of each morph is resolved as:

```text
effective = clamp(base recipe value + sum of gameplay layer offsets, definition minimum, 1)
```

The base recipe is what the player authored in the creator. It is the only data captured by `CaptureMorphValues()`, `CharacterProfile.CaptureRecipe()`, presets, and finalized player saves. Gameplay layers are runtime state owned by the host game: stat growth, ageing, injury, curses, or any other system that should reshape the body without erasing the player's chosen appearance. Layers are character-wide, so they also apply after `SetSex`.

Layers are not persisted by this package. After loading a save, the host game re-sends its current stats:

```csharp
playerProfile.ApplyRecipe(savedPlayer.Recipe);
controller.SetStatGrowth("muscle", stats.Strength01);
controller.SetStatGrowth("body_fat", stats.BodyFat01);
controller.SetMorphLayerOffset("injury", "body.weight", -0.15f);
```

### Skin colour and the skin shader

`Sol/Character Skin` (`Shaders/SolCharacterSkin.shader`) replaces the tint-over-albedo approach. Skin colour comes from three parameters looked up in a colour table:

| Parameter | Range | Meaning |
| --- | --- | --- |
| `Melanin` | 0..1 | Very fair to very deep. The ten Monk Skin Tone Scale swatches sit at 0.1 to 1.0. |
| `Hemoglobin` | 0..1 | Sallow or pale to flushed or ruddy. About 0.13 matches average skin. |
| `Undertone` | 0..1 | Olive or neutral to golden or warm (the pheomelanin share). |

`SkinColorModel` generates the human colour table from a melanin and hemoglobin absorption model fitted to the Monk scale, and provides `FindClosest` to convert any colour into parameters. `CharacterSkinProfile` pairs a colour table with region behaviour; a fantasy race uses a different profile with a painted table in the same layout (three undertone slices side by side, melanin on x, hemoglobin on y).

The shader's base map supplies detail only: it is divided by `_BaseMapMean` to remove the skin tone baked into the texture, and `_DetailChroma` keeps some of its colour variation. Other inputs:

| Map | Channels |
| --- | --- |
| `_SkinRegionMap` | R lips, G palms, soles, and nails, B blush zones, A areolae. Each region scales melanin or adds hemoglobin through the profile's region settings. Black (no regions) by default. |
| `_SkinUtilityMap` | R occlusion, G transmission (1 where thin), B curvature. White by default. |
| `_BumpMap` | Tangent-space normal map. |

Lighting uses wrapped per-channel diffuse scaled by curvature (pre-integrated scattering), two GGX specular lobes, and back-lit transmission through thin regions. The shader supports Forward, Forward+, and Deferred (as a forward-only material), main and additional light shadows, soft shadows, SSAO, reflection probes, and fog. It does not support per-vertex additional lights, light cookies, light layers, decals, mixed-lighting shadow masks, LOD cross-fade, or Adaptive Probe Volumes; it uses spherical-harmonic probes.

Setup:

1. `Tools > Sol > Character Customization > Skin > Create or Update Human Skin Profile` generates `HumanSkinColorTable.asset` and `HumanSkinProfile.asset` in `CustomizationPresets`.
2. Select the body skin materials and run `Tools > Sol > Character Customization > Skin > Convert Selected Materials To Sol Skin`. This keeps the base map, normal map, and smoothness, measures the base map's mean colour, and applies the profile.
3. Assign the profile to `CharacterProfile.skinProfile`. Without one, the built-in human model is used for colour matching.
4. Run `Tools > Sol > Character Customization > Skin > Build Skin Picker In Demo Menu` to replace the demo menu's HSV sliders with the skin picker.

In Play Mode, `CharacterProfile` gives each character its own copy of any Sol/Character Skin material (`instanceSkinMaterials`, on by default) and writes skin values to it, so skin renderers stay in the SRP Batcher. Renderers with other materials still receive values through `MaterialPropertyBlock`. If the character outlives its profile, it keeps its material copies.

### Skin picker UI

`CharacterSkinParameterPicker` edits `CharacterProfile.SkinParameters` directly:

- A `CharacterSkinPlaneArea` colour plane: melanin across, hemoglobin up, drawn at the current undertone. Click or drag to pick.
- Melanin, hemoglobin, and undertone sliders. Each slider's background shows the colours it would produce with the other two values held, so every reachable value is a plausible skin.
- `Bind(CharacterProfile)`, `Refresh()`, and a `ParametersChanged` event.

`CharacterMorphDemoUI` uses the picker when `skinParameterPicker` is assigned, or finds one under the menu, and falls back to the HSV sliders otherwise. The build tool reuses the authored HSV slider objects, renaming them and their rows and labels, and adds a vertical layout so the preview strip, the plane, and the three rows fit the panel's existing height.

Saved custom swatches store the picked skin's colour. Reselecting one matches it back to parameters, which reproduces the same colour.

Existing skin tones and the HSV custom colour keep working: they are converted to the nearest parameters. A tone can store exact parameters instead with `hasParameters`.

### NPC baking

Live blendshapes cost GPU work every frame for every non-zero weight. Baking folds the catalog's body and face shapes into the base vertices of a mesh copy and removes them; every other shape, such as expressions or visemes, is kept with its current weight. Skin and eye materials are unaffected.

```csharp
npcProfile.ApplyPreset(guardPreset);
npcProfile.BakeAppearance(releaseInactiveSex: true, out string error);

// Later, after stat growth or another recipe change on a baked character:
if (npcController.IsBakeStale)
{
    npcController.BakeActiveCharacter(out error);
}
```

- Baked meshes are shared through `CharacterMeshBaker`. Characters on the same source mesh with the same weights, such as NPCs spawned from one preset, use one mesh. Weights are compared to a hundredth of a weight point.
- Each baked mesh is reference-counted by a `CharacterBakedMeshHandle` on its renderer and destroyed when its last renderer is destroyed or unbaked, whether or not the customization manager still exists.
- While baked, blendshape morph edits update the recipe but not the visible mesh. Skeletal morphs still apply live.
- Baking requires Read/Write enabled on the source meshes. This keeps one CPU copy of each source mesh; baked results are GPU-only.
- Baking rebinds the active Animator, because animated blendshape curves bind to shape indices. Animator state and parameters are carried across.
- `CharacterMeshBaker.TryBake(...)` is also available directly for tools that own the resulting mesh.

## Rig Proportion API

`CharacterRigProportionDriver` is used by `CharacterMorphController` for skeletal morph definitions. Host code usually does not need to call it directly unless building a custom controller.

| Member | Use |
| --- | --- |
| `Bind(Transform root, Animator animator)` | Binds the active humanoid root and captures bind pose. |
| `SetMorph(CharacterMorphDefinition definition, float value)` | Applies one skeletal morph contribution. |
| `ResetMorph(CharacterMorphDefinition definition)` | Clears one skeletal contribution. |
| `ApplyNow()` | Restores bind pose and reapplies current skeletal contributions. |
| `ApplyIfDirty()` | Reapplies only when an edit is pending, or every frame when `ReapplyPoseEveryFrame` is set or the Avatar uses translation DoF. Returns `true` if the pose was applied. |
| `IsPoseDirty` / `ReapplyPoseEveryFrame` | Pending-edit state, and the opt-in for setups where something else writes bone scales or the edited bone positions. |
| `RebuildAvatarNow()` | Rebuilds the humanoid Avatar from the morphed skeleton immediately, or restores the source Avatar when the skeleton is neutral. Returns `false` if the rebuild could not run. |
| `RebuildAvatarOnSkeletalChange` | Enables the automatic, debounced Avatar rebuild after skeletal edits. On by default. |
| `IsAvatarRebuildPending` | `true` while skeletal edits are waiting for the rebuild delay. |
| `SourceAvatar` / `RuntimeAvatar` | The imported Avatar captured at bind time, and the current rebuilt Avatar or `null`. |
| `ValidateBinding(out string error)` | Checks active root, humanoid Animator, required bones, and optional IK bridge. |
| `EnableFootIkGrounding` | Enables optional foot IK grounding when a `CharacterRigAnimatorIkBridge` is present on the active Animator object. |

Unity's humanoid retargeting and IK use the limb lengths stored in the Avatar, not the live bone scales. After skeletal edits settle (`avatarRebuildDelay`, 0.25 seconds by default), the driver bakes its bone scales and side offsets into a copy of the source Avatar's `HumanDescription` and assigns the result with `AvatarBuilder.BuildHumanAvatar`. Animated hip height and IK reach then match the morphed body. The Animator's current states, layer weights, and parameters are carried across the rebind; in-progress transitions restart. `CharacterFinalizationFlow` flushes a pending rebuild before raising `Finalized`. Host code that finalizes characters another way should call `RebuildAvatarNow()` when `IsAvatarRebuildPending` is `true`.

Rebuilt Avatars are owned by the driver and destroyed when replaced or when the driver rebinds. If the driver is destroyed while its character is still alive, the character keeps its morphed bones and Avatar.

`CharacterRigProportionProfile` controls scale ranges per `CharacterRigProportionChannel`. Use `EvaluateScale(...)` only when implementing custom rig behaviour.

## Joint Offsets

Joint offsets move bones with the mesh shapes, so a taller or broader body is also re-jointed rather than scaled. The data comes from `Tools/Blender` as a `.solrig` sidecar (see `Tools/Blender/README.md`), imported by `CharacterRigSidecarImporter` into a `CharacterJointOffsetTable` asset. Sidecar check findings appear as import warnings.

Setup: assign the tables to `CharacterMorphController.femaleJointOffsets` and `maleJointOffsets` (the demo's are in `Assets/CharCustomization/RigData`).

At runtime:

- `CharacterJointOffsetBinding` matches the exported rest bone heads to the imported bind poses to find the axis mapping (any signed axis permutation, mirrors included, plus uniform scale). If the joints differ by more than 2% of the skeleton's size, the table is rejected with a warning and `ValidateBinding` reports why.
- Each blendshape's offsets are weighted by its current weight. Bones move by the result, and their bind poses move with them on a per-character mesh copy, so the reshaped skin is not displaced twice. Bones the Animator positions (the hips) move through the rebuilt Avatar instead.
- Committing an Avatar rebuild swaps that private copy for a copy shared by every character with the same offsets, so NPCs from one preset still share meshes, and the NPC baker shares their baked meshes. Shared copies are reference-counted by `CharacterJointMeshHandle` on the renderer.
- Skeletal morphs whose shapes also exist on the mesh (`body.height`, `body.shoulder_width`, and `body.hips` on the demo characters) use the mesh shapes instead of bone scaling, but only for a sex with a joint offset table. Without one they keep bone scaling, because moving the mesh without moving the joints would tear skinning. `CharacterMorphController.UsesMeshShapes(morphId)` reports which route a morph takes.

| Member | Use |
| --- | --- |
| `CharacterRigProportionDriver.Bind(root, animator, table)` | Binds a character with optional joint offsets. The controller does this from its per-sex tables. |
| `CharacterRigProportionDriver.MarkJointOffsetsDirty()` | Re-reads blendshape weights on the next pose application. The controller calls it whenever it sets weights. |
| `CharacterRigProportionDriver.JointOffsets` | The active binding, with its `Calibration` and the current `LocalOffsets`. |
| `CharacterJointOffsetTable.TryCreate(json, ...)` | Builds a table from sidecar JSON without the importer. |

## Twist Bones

`CharacterTwistBoneDriver` drives limb twist bones that humanoid animation never animates because they are not mapped in the Avatar. The MPFB/Auto-Rig Pro characters skin part of each forearm, upper arm, thigh, and shin to `forearm_twist`, `arm_twist`, `thigh_twist`, and `leg_twist`; left at bind pose they candy-wrap the wrists and ankles.

`CharacterMorphController` adds the driver to each character Animator in Play Mode when `ensureTwistBoneDrivers` is on (the default). It can also be added by hand.

| Member | Use |
| --- | --- |
| `Initialize()` | Detects or validates bindings and captures rest rotations. Returns `false` when nothing is driven. |
| `ApplyNow()` | Applies twist for the current pose. Runs automatically in `LateUpdate`, after the Animator, IK, and the rig proportion driver. |
| `Bindings` / `ActiveTwistBoneCount` | Authored or detected bindings, and how many are being driven. |
| `FindHumanoidTwistBones(Animator, List<TwistBoneBinding>)` | Static detection used by the driver, the controller, and the setup validator. |
| `TwistAngle(Quaternion, Vector3)` | Swing-twist decomposition: the signed twist angle about an axis, in degrees. |

Detection adds every unmapped child of a humanoid limb segment whose name contains "twist":

| Segment | Source | Default weight |
| --- | --- | --- |
| Lower arm, lower leg | Hand or foot roll relative to the segment (`DistalBone`) | The bone's position along the segment, 0 at the elbow or knee to 1 at the wrist or ankle. Mid-segment bones take 0.5. |
| Upper arm, thigh | The segment's own roll (`SegmentBone`) | `-0.5` at the shoulder or hip, fading to 0 at the far end. Counter-rotates so the skin near the joint twists less. |

Rest rotations come from the skin bind poses, so the driver works even when it initializes on a character that is already animating. Author `bindings` in the inspector to override detection or tune weights. The Avatar's twist settings (Upper/Lower Arm and Leg Twist) also distribute roll onto the mapped limb bones, so tune them alongside these weights.

## Preview and Gameplay Handoff

`CharacterPreviewControls` controls the dependency-free preview camera rig and supports native handoff to a gameplay camera.

| Member | Use |
| --- | --- |
| `RotateCharacter(float degrees)` | Rotates the visible character root. |
| `Zoom(float amount)` | Adjusts preview framing distance. |
| `PanVertical(float amount)` | Moves the preview focus up or down. |
| `ResetCamera()` / `SnapToFocus()` | Restores preview framing. |
| `SetCustomizationInputEnabled(bool enabled)` | Disables creator input during handoff. |
| `BlendTo(Camera gameplayCamera, float duration, Action onComplete = null)` | Blends preview transform/FOV into the gameplay camera. |

For the included demo controller, call `DemoPlayerController.BindAnimator(activeAnimator)` before enabling gameplay input.

## Validation

Before relying on a new prefab or character root, run:

```text
Tools > Character Customization > Validate Morph Demo
Tools > Sol > Character Customization > Validate Selected Character Setup
```

For compile validation:

```powershell
dotnet build Assembly-CSharp.csproj -nologo
dotnet build Assembly-CSharp-Editor.csproj -nologo
```

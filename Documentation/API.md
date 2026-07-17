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
| `GetMorph(string morphId)` | Reads the active sex's current morph value, returning `0` when unset. |
| `TryGetDefinition(string morphId, out CharacterMorphDefinition definition)` | Resolves catalog metadata for a stable morph ID. |
| `CaptureMorphValues()` | Captures all catalog morph values in catalog order for recipe storage. |
| `ApplyMorphValues(IReadOnlyList<CharacterMorphValue> values)` | Applies saved morph values, defaulting missing known IDs to `0` and warning for unknown IDs. |
| `RandomizeCurrent(float rangeScale = 0.65f, System.Random random = null)` | Randomizes creator-visible morphs on the active sex within a restrained portion of their range. |
| `SetStatGrowth(string statId, float normalizedValue)` | Maps a host-game stat to its target morph. `muscle` maps to `body.muscle`; `body_fat` maps to `body.weight`. |
| `ResetCurrentCharacter()` | Resets every morph on the active sex to `0`. |
| `ResetGroup(string groupId)` | Resets only one morph group, such as `Body` or `Eyes`. |
| `IsMorphAvailable(string morphId)` | Checks whether the active model can currently apply a morph through blendshapes or rig binding. |
| `ValidateConfiguration(List<string> errors)` | Reports missing roots, invalid catalog data, missing blendshapes, or missing rig driver setup. |

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

- `Version`, currently `2`.
- `Sex`.
- `SkinToneId`, or `UsesCustomSkinColor` plus `CustomSkinColor`.
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
| `muscle` | `body.muscle` | `0..1` to `0..1`. |
| `body_fat` | `body.weight` | `0..1` to `-1..1`, with `0.5` as neutral. |

## Rig Proportion API

`CharacterRigProportionDriver` is used by `CharacterMorphController` for skeletal morph definitions. Host code usually does not need to call it directly unless building a custom controller.

| Member | Use |
| --- | --- |
| `Bind(Transform root, Animator animator)` | Binds the active humanoid root and captures bind pose. |
| `SetMorph(CharacterMorphDefinition definition, float value)` | Applies one skeletal morph contribution. |
| `ResetMorph(CharacterMorphDefinition definition)` | Clears one skeletal contribution. |
| `ApplyNow()` | Restores bind pose and reapplies current skeletal contributions. |
| `ValidateBinding(out string error)` | Checks active root, humanoid Animator, required bones, and optional IK bridge. |
| `EnableFootIkGrounding` | Enables optional foot IK grounding when a `CharacterRigAnimatorIkBridge` is present on the active Animator object. |

`CharacterRigProportionProfile` controls scale ranges per `CharacterRigProportionChannel`. Use `EvaluateScale(...)` only when implementing custom rig behaviour.

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

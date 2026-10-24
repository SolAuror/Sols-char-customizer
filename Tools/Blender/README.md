# Blender tools

Blender-side tooling for the character customizer. Nothing here is imported by Unity; it lives outside `Assets` on purpose.

## Sol Character Tools

Reads a rigged, shape-keyed character and writes a `.solrig` sidecar for Unity. It never changes skin weights, shape keys, or bones, so hand-authored `+`/`-` shapes and Auto-Rig Pro voxel heat skinning stay exactly as they are.

It does two things:

1. **Joint offsets per shape key.** For every deform bone it finds the joint as a weighted set of vertices, usually the band where voxel heat skinning shares weight between the bone and its neighbour (the wrist band is shared by `hand` and `forearm_twist`). A shape key's offset for that joint is the weighted average of the shape's vertex deltas, so offsets combine linearly like blendshapes. Unity can then move joints with the body instead of scaling bones.
2. **Pre-export checks.** Shape key pairing and naming, near-empty or non-zero shapes, keys relative to something other than the basis, left/right symmetry, proxies (eyebrows, eyelashes, eyes, teeth, tongue) missing shapes that move the body next to them, skin weights Unity will truncate or drop, and shape names that differ from the other character.

### Use from Blender

1. Zip the `sol_character_tools` folder and install it with *Edit > Preferences > Add-ons > Install from Disk*. Blender 4.2 or later.
2. Open the character `.blend`. In the 3D Viewport sidebar, open the **Sol** tab.
3. Set **Sidecar** to `Assets/CharCustomization/RigData/<character>.solrig` in the Unity project, optionally **Check Config** to `Tools/Blender/character_checks.json` and **Compare With** to the other character's `.solrig`.
4. **Run Checks** reports without writing; **Export Sidecar** writes the file. Results appear in the Info bar and in full in the *Sol Character Checks* text block.

### Use headless

From a `.blend` (always pass `--` before the tool's own arguments):

```
blender -b Human_F.blend --python Tools/Blender/sol_export_cli.py -- \
    --out Assets/CharCustomization/RigData/SK_Human_F.solrig \
    --compare-with Assets/CharCustomization/RigData/SK_Human_M.solrig
```

From an exported FBX, with Blender or the `bpy` Python module:

```
python Tools/Blender/sol_export_cli.py --fbx Assets/CharCustomization/Resources/SK_Human_F.fbx \
    --out Assets/CharCustomization/RigData/SK_Human_F.solrig
```

Exit code 0 means no errors, 1 means checks found errors, 2 means no character was found. `--armature` and `--body` pick objects by name when the scene has several.

### Check exceptions

`character_checks.json` records intentional exceptions:

| Key | Meaning |
| --- | --- |
| `allowUnpaired` | One-sided morphs, such as `Body_Breast+`. |
| `allowUnsuffixed` | Shape keys that are not catalog morphs. |
| `allowAsymmetric` | Shapes that are meant to differ left to right. |
| `ignoreProxyShapes` | Per proxy (matched by a case-insensitive part of its name), body shapes that proxy should not follow. The defaults keep eyeballs still for eyelid and brow shapes, and teeth and tongue still for lip fullness. |

### Sidecar format

JSON, `format: "sol-character-sidecar"`, `version: 2`. Arrays stay one level deep so Unity's `JsonUtility` can read the file without a JSON package:

- `space`: `armature-metres`. Positions and offsets use the armature's own axes, scaled to metres. Unity matches the exported rest `bones[].head` positions against its imported bones to find the axis mapping, so the exporter does not depend on FBX axis settings.
- `bones`: deform bones in hierarchy order, each with `parent`, rest `head`, the joint `method` (`band`, `region`, `colocated`, `children`, `inherit`), its `bandPartner`, and how far the joint centre sits from the head.
- `shapes`: every shape key, the `meshes` that carry it, `boneIndices` of the joints it moves at least 0.05 mm at full weight, and `offsets` as the matching flat `x, y, z` triples.
- `checks`: findings with `severity`, `code`, `message`, and `details`.

### Tests

The tests run against the `bpy` module, Blender as a Python package. `bpy` 5.2.3 needs Python 3.13:

```
python3.13 -m venv .venv
.venv/bin/pip install bpy==5.2.3 numpy pytest
.venv/bin/python -m pytest Tools/Blender/tests
```

They build small synthetic rigs with known answers, and also run on the exported characters in `Assets/CharCustomization/Resources` when present.

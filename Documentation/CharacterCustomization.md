# Character Customization System

## Purpose
This project explores a reusable character customization system for Unity 6.3. The system should remain suitable for integration into varied Unity projects and, when sufficiently mature, for distribution through the Unity Asset Store.

## Current State
The demo scene contains male and female MPFB characters with a shared morph interface. One character is displayed at a time and each retains an independent in-memory recipe while switching.

The initial implementation exposes 38 body and head controls through stable logical identifiers. Bipolar controls drive the imported positive and negative blendshape pair, while muscle and breast use a zero-to-one range. Bindings are resolved across all skinned renderers beneath the selected character so dependent face and accessory meshes remain aligned.

The menu is authored in `Prefabs/CharacterMorphMenu.prefab`. Each morph row explicitly stores its logical identifier and UI references. Runtime code binds these existing controls without rebuilding the menu; a missing row is cloned from the prefab's inactive slider template.

## Design Principles
- Keep the system independent of any single game's requirements.
- Add only the functionality required by the current iteration.
- Record accepted decisions, rejected approaches, and unresolved questions as the design develops.

## Decisions
- Female is the default character in the demo.
- Male and female recipes remain independent during the current session.
- Runtime code addresses morphs by stable identifiers rather than FBX blendshape names.
- The authored Canvas prefab is the source of truth for menu hierarchy and presentation.
- Runtime UI creation is limited to cloning a missing morph row from the assigned template.
- `Tools > Character Customization > Validate Morph Demo` checks the menu, character morphs, scene wiring, and UI input references without rebuilding assets.
- Persistent save data, colour customization, and game-specific progression remain outside the current iteration.

## Unresolved Questions
- What recipe serialization format should be used when persistence is introduced?

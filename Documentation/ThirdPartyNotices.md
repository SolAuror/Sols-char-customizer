# Third-Party Notices

This project keeps third-party reference-code attribution explicit so the character editor can be distributed without guesswork.

## BodyMorphLite

- Project: BodyMorphLite
- Author: Serhat Dikel
- License: MIT License
- Usage in CharacterEditor: behavioral reference for humanoid proportion formulas, foot grounding, pelvis compensation, and animated foot IK blending.
- Imported assets: none. No BodyMorphLite demo scenes, prefabs, materials, or plugin files are included in CharacterEditor.
- Adapted code location: `Assets/CharCustomization/Scripts/Core/CharacterRigProportionDriver.cs`

CharacterEditor does not copy BodyMorphLite as a drop-in component. It adapts the relevant behavior into CharacterEditor's stable morph catalog, `CharacterRigProportionProfile`, bind-pose restore path, and `CharacterRigAnimatorIkBridge`.

```text
MIT License

Copyright (c) Serhat Dikel

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

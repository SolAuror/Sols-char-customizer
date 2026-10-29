using NUnit.Framework;
using Sol.CharacterCustomization.EditorTools;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTests
{
    public sealed class CharacterTattooEditModeTests
    {
        [Test]
        public void RecipeStoresTattoosAndOlderRecipesHaveNone()
        {
            var placement = new CharacterTattooPlacement(
                " wolf ", new Vector3(0.1f, 1.2f, 0.05f), Quaternion.Euler(10f, 20f, 30f),
                new Vector3(-0.11f, 0.11f, 0.12f), new Color(0.2f, 0.1f, 0.4f, 0.3f), 1.5f);
            var recipe = new CharacterRecipe();
            recipe.SetTattoos(new[] { placement });

            CharacterRecipe loaded = JsonUtility.FromJson<CharacterRecipe>(JsonUtility.ToJson(recipe));
            Assert.That(loaded.Version, Is.EqualTo(5));
            Assert.That(loaded.HasTattoos, Is.True);
            Assert.That(loaded.Tattoos.Count, Is.EqualTo(1));
            CharacterTattooPlacement tattoo = loaded.Tattoos[0];
            Assert.That(tattoo.DesignId, Is.EqualTo("wolf"));
            Assert.That(Vector3.Distance(tattoo.Position, placement.Position), Is.LessThan(1e-6f));
            Assert.That(Quaternion.Angle(tattoo.Rotation, placement.Rotation), Is.LessThan(0.01f));
            Assert.That(tattoo.IsMirrored, Is.True, "a negative width is kept as mirroring");
            Assert.That(tattoo.Ink.a, Is.EqualTo(1f), "ink is opaque; opacity is separate");
            Assert.That(tattoo.Opacity, Is.EqualTo(1f), "opacity is clamped to 0-1");
            Assert.That(recipe.Copy().Tattoos.Count, Is.EqualTo(1));

            var none = new CharacterRecipe();
            none.SetTattoos(new CharacterTattooPlacement[0]);
            Assert.That(none.HasTattoos, Is.True, "an empty list clears tattoos when applied");

            CharacterRecipe legacy = JsonUtility.FromJson<CharacterRecipe>(
                "{\"version\":4,\"hasHair\":true,\"hairStyleId\":\"M14\"}");
            Assert.That(legacy.HasTattoos, Is.False, "older recipes carry no tattoos");
            Assert.That(legacy.Tattoos.Count, Is.EqualTo(0));
        }

        [Test]
        public void LoadingALookOrRandomizingClearsTattoos()
        {
            GameObject character = BuildCharacter("Customizer", null);
            try
            {
                var tattoos = character.AddComponent<CharacterTattoos>();
                var profile = character.GetComponent<CharacterProfile>();
                SetField(profile, "tattoos", tattoos);
                var placement = new CharacterTattooPlacement("wolf", Vector3.zero, Quaternion.identity, new Vector3(0.1f, 0.1f, 0.1f), Color.black);

                tattoos.SetPlacements(new[] { placement });
                CharacterRecipe legacy = JsonUtility.FromJson<CharacterRecipe>("{\"version\":4,\"morphValues\":[]}");
                Assert.That(profile.ApplyRecipe(legacy), Is.True);
                Assert.That(tattoos.Placements.Count, Is.EqualTo(0), "a look saved without tattoos has none");

                var inked = new CharacterRecipe();
                inked.SetTattoos(new[] { placement, placement });
                Assert.That(profile.ApplyRecipe(inked), Is.True);
                Assert.That(tattoos.Placements.Count, Is.EqualTo(2), "a look's own tattoos replace the current ones");

                profile.RandomizeCurrent(0.65f, new System.Random(3));
                Assert.That(tattoos.Placements.Count, Is.EqualTo(0), "randomizing clears tattoos");
            }
            finally
            {
                Object.DestroyImmediate(character);
            }
        }

        [Test]
        public void RecipeRejectsTattooWithoutDesign()
        {
            var recipe = new CharacterRecipe();
            recipe.SetTattoos(new[] { new CharacterTattooPlacement(" ", Vector3.zero, Quaternion.identity, Vector3.one, Color.black) });

            Assert.That(recipe.HasValidIdentifiers(out string error), Is.False);
            Assert.That(error, Does.Contain("design"));
        }

        [Test]
        public void PlacementRoundTripsThroughMeshSpace()
        {
            foreach (Vector3 size in new[] { new Vector3(0.11f, 0.09f, 0.12f), new Vector3(-0.11f, 0.09f, 0.12f) })
            {
                Matrix4x4 meshFromBox = Matrix4x4.TRS(new Vector3(0.3f, 1.1f, -0.05f), Quaternion.Euler(-35f, 70f, 12f), size);
                CharacterTattooPlacement placement = CharacterTattooPlacement.FromMeshSpace("wolf", meshFromBox, Color.black);

                AssertMatrix(placement.MeshFromBox, meshFromBox);
                Assert.That(placement.IsMirrored, Is.EqualTo(size.x < 0f));
            }
        }

        [Test]
        public void CatalogFindsDesignsByIdAndTexture()
        {
            var catalog = ScriptableObject.CreateInstance<CharacterTattooCatalog>();
            var texture = new Texture2D(4, 4);
            try
            {
                catalog.ConfigureDesigns(new[]
                {
                    new CharacterTattooDesign("rune-tentacles", "Rune Tentacles", texture),
                    new CharacterTattooDesign("wolf", null, null),
                });

                Assert.That(catalog.TryGet(" rune-tentacles ", out CharacterTattooDesign design), Is.True);
                Assert.That(design.Label, Is.EqualTo("Rune Tentacles"));
                Assert.That(catalog.TryFind(texture, out CharacterTattooDesign byTexture), Is.True);
                Assert.That(byTexture.Id, Is.EqualTo("rune-tentacles"));
                Assert.That(catalog.TryGet("wolf", out CharacterTattooDesign wolf) && wolf.Label == "wolf", Is.True,
                    "a design without a label shows its ID");
                Assert.That(catalog.TryGet("missing", out _), Is.False);
                Assert.That(SolTattooSetup.DesignId("RuneTentacles"), Is.EqualTo("rune-tentacles"));
                Assert.That(SolTattooSetup.DesignId("Wolf"), Is.EqualTo("wolf"));
            }
            finally
            {
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void ProjectorIsCapturedInBindPoseMeshSpace()
        {
            var character = new GameObject("Character");
            var catalog = ScriptableObject.CreateInstance<CharacterTattooCatalog>();
            var design = new Texture2D(4, 4);
            var material = new Material(Shader.Find(SolSkinShaderIds.ShaderName));
            var mesh = new Mesh();
            try
            {
                var bone = new GameObject("forearm_stretch.l").transform;
                bone.SetParent(character.transform, false);
                bone.SetLocalPositionAndRotation(new Vector3(0.3f, 1.1f, 0f), Quaternion.Euler(0f, 0f, -40f));

                // Bind pose taken with the bone elsewhere, so a bone-relative pose alone
                // would land in the wrong place: the capture has to go through it.
                Matrix4x4 bindpose = Matrix4x4.TRS(new Vector3(0.25f, 1.2f, 0.02f), Quaternion.Euler(5f, 0f, -30f), Vector3.one).inverse;
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.boneWeights = new[] { new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f } };
                mesh.bindposes = new[] { bindpose };

                var body = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
                body.transform.SetParent(character.transform, false);
                body.sharedMesh = mesh;
                body.bones = new[] { bone };
                body.sharedMaterial = material;

                var projector = new GameObject("Tattoo").AddComponent<CharacterTattooProjector>();
                projector.transform.SetParent(bone, false);
                projector.transform.SetLocalPositionAndRotation(new Vector3(0.05f, 0.02f, -0.03f), Quaternion.Euler(0f, 90f, 15f));
                projector.transform.localScale = new Vector3(0.11f, 0.1f, 0.12f);
                projector.Configure(design, Color.red, 0.7f);

                catalog.ConfigureDesigns(new[] { new CharacterTattooDesign("wolf", "Wolf", design) });
                var tattoos = character.AddComponent<CharacterTattoos>();
                typeof(CharacterTattoos)
                    .GetField("catalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .SetValue(tattoos, catalog);

                Assert.That(tattoos.TryCapture(projector, out CharacterTattooPlacement placement, out string error), Is.True, error);
                Matrix4x4 expected = bindpose.inverse * Matrix4x4.TRS(
                    projector.transform.localPosition, projector.transform.localRotation, projector.transform.localScale);
                AssertMatrix(placement.MeshFromBox, expected);
                Assert.That(placement.DesignId, Is.EqualTo("wolf"));
                Assert.That(placement.Opacity, Is.EqualTo(0.7f).Within(1e-6f));

                int changes = 0;
                tattoos.Changed += () => changes++;
                tattoos.AddPlacement(placement);
                tattoos.SetPlacements(new[] { placement, placement });
                tattoos.RemovePlacement(0);
                Assert.That(changes, Is.EqualTo(3));
                Assert.That(tattoos.Placements.Count, Is.EqualTo(1));

                // The projector still marks the skin too; only a placement is outlined.
                tattoos.Rebuild();
                Assert.That(tattoos.TattooCount, Is.EqualTo(2));
                Assert.That(tattoos.Layer, Is.Not.Null);
                Assert.That(tattoos.Highlight, Is.Null, "nothing is outlined until a placement is highlighted");
                tattoos.Highlighted = 0;
                tattoos.Rebuild();
                Assert.That(tattoos.Highlight, Is.Not.Null);
                Assert.That(tattoos.Highlight.width, Is.EqualTo(tattoos.Layer.width / 2));
                tattoos.Highlighted = 3;
                tattoos.Rebuild();
                Assert.That(tattoos.Highlight, Is.Null, "a placement that does not exist is not outlined");
            }
            finally
            {
                Object.DestroyImmediate(character);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(design);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PresetsAndPlayerSavesKeepHairAndTattoos()
        {
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SolTattooSaveTest_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var recipe = new CharacterRecipe();
                recipe.SetHair("F55", new Color(0.3f, 0.2f, 0.1f), 0.6f);
                recipe.SetTattoos(new[]
                {
                    new CharacterTattooPlacement("wolf", new Vector3(-0.35f, -0.1f, 1.07f), Quaternion.Euler(0f, 90f, 0f), new Vector3(0.11f, 0.11f, 0.12f), Color.black),
                });

                var presets = new CharacterPresetSaveRepository(System.IO.Path.Combine(folder, "presets.json"));
                Assert.That(presets.TrySavePreset("Inked", recipe, false, out _, out _, out string presetError), Is.True, presetError);
                Assert.That(presets.TryFindByName("Inked", out RuntimeCharacterPresetRecord preset, out presetError), Is.True, presetError);
                AssertKeepsLook(preset.Recipe);

                var players = new CharacterPlayerSaveRepository(System.IO.Path.Combine(folder, "players.json"));
                Assert.That(players.TrySavePlayer("Ink", recipe, false, out PlayerCharacterRecord player, out _, out string playerError), Is.True, playerError);
                AssertKeepsLook(player.Recipe);
            }
            finally
            {
                if (System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.Delete(folder, true);
                }
            }
        }

        [Test]
        public void GameplayCopyGetsTheCustomizerHairAndTattoos()
        {
            var catalog = ScriptableObject.CreateInstance<CharacterTattooCatalog>();
            GameObject source = BuildCharacter("Customizer", "F55");
            GameObject copy = BuildCharacter("Gameplay", null);
            copy.SetActive(false);
            try
            {
                var sourceTattoos = source.AddComponent<CharacterTattoos>();
                typeof(CharacterTattoos)
                    .GetField("catalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .SetValue(sourceTattoos, catalog);
                SetField(source.GetComponent<CharacterProfile>(), "tattoos", sourceTattoos);

                bool copied = CharacterAppearanceTransfer.TryCopyParts(
                    source.GetComponent<CharacterProfile>(), copy.GetComponent<CharacterProfile>(), out string report);

                Assert.That(copied, Is.True, report);
                Transform copyRoot = copy.transform.Find("Female");
                CharacterHairStyle style = copyRoot.GetComponentInChildren<CharacterHairStyle>(true);
                Assert.That(style, Is.Not.Null, report);
                Assert.That(style.StyleId, Is.EqualTo("F55"));
                Assert.That(style.transform.parent, Is.EqualTo(copyRoot.Find("Hair")), "keeps its place in the hierarchy");
                var hair = style.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Transform head = copyRoot.Find("Rig/head.x");
                Assert.That(hair.bones, Is.EqualTo(new[] { head }), "rebound to the copy's own head bone");
                Assert.That(hair.rootBone, Is.EqualTo(head));

                var tattoos = copy.GetComponent<CharacterTattoos>();
                Assert.That(tattoos, Is.Not.Null);
                Assert.That(tattoos.Catalog, Is.EqualTo(catalog));

                Assert.That(CharacterAppearanceTransfer.TryCopyParts(
                    source.GetComponent<CharacterProfile>(), copy.GetComponent<CharacterProfile>(), out _), Is.True);
                Assert.That(copyRoot.GetComponentsInChildren<CharacterHairStyle>(true).Length, Is.EqualTo(1), "copying again adds nothing");
                Assert.That(copy.GetComponents<CharacterTattoos>().Length, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(copy);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void PlacerDescribesWhatItPlacedAndKeepsItWhenMoved()
        {
            Vector3 up = Vector3.forward;
            Vector3 point = new(0.3f, -0.08f, 1.05f);
            Vector3 normal = new Vector3(0.6f, -0.8f, 0.1f).normalized;
            CharacterTattooPlacement placement = CharacterTattooPlacer.Place(
                "wolf", point, normal, up, 35f, new Vector2(0.1f, 0.14f), true, Color.red, 0.6f);

            CharacterTattooPlacer.Describe(placement, up, out Vector3 describedPoint, out Vector3 describedNormal,
                out float angle, out Vector2 size, out bool mirrored);
            Assert.That(Vector3.Distance(describedPoint, point), Is.LessThan(1e-5f));
            Assert.That(Vector3.Angle(describedNormal, normal), Is.LessThan(0.01f));
            Assert.That(angle, Is.EqualTo(35f).Within(0.01f));
            Assert.That(size.x, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(size.y, Is.EqualTo(0.14f).Within(1e-5f));
            Assert.That(mirrored, Is.True);

            // The box reaches out of the skin and inward in proportion to the design.
            Matrix4x4 boxFromMesh = placement.MeshFromBox.inverse;
            float inward = CharacterTattooPlacer.InwardReachPerSize * 0.14f;
            float outward = CharacterTattooPlacer.OutwardReachPerSize * 0.14f;
            Assert.That(Mathf.Abs(boxFromMesh.MultiplyPoint3x4(point + normal * outward * 0.99f).z), Is.LessThan(0.5f));
            Assert.That(Mathf.Abs(boxFromMesh.MultiplyPoint3x4(point + normal * outward * 1.01f).z), Is.GreaterThan(0.5f));
            Assert.That(Mathf.Abs(boxFromMesh.MultiplyPoint3x4(point - normal * inward * 0.99f).z), Is.LessThan(0.5f));
            Assert.That(Mathf.Abs(boxFromMesh.MultiplyPoint3x4(point - normal * inward * 1.01f).z), Is.GreaterThan(0.5f));

            Vector3 elsewhere = new(-0.2f, 0.05f, 0.6f);
            Vector3 otherNormal = new Vector3(-0.3f, -0.9f, 0.2f).normalized;
            CharacterTattooPlacement moved = CharacterTattooPlacer.Move(placement, elsewhere, otherNormal, up);
            CharacterTattooPlacer.Describe(moved, up, out describedPoint, out _, out angle, out size, out mirrored);
            Assert.That(Vector3.Distance(describedPoint, elsewhere), Is.LessThan(1e-5f));
            Assert.That(angle, Is.EqualTo(35f).Within(0.01f), "moving keeps the turn");
            Assert.That(size.y, Is.EqualTo(0.14f).Within(1e-5f));
            Assert.That(mirrored, Is.True);
            Assert.That(moved.Ink, Is.EqualTo(placement.Ink));
            Assert.That(moved.Opacity, Is.EqualTo(0.6f).Within(1e-6f));

            CharacterTattooPlacement turned = CharacterTattooPlacer.Reshape(moved, up, -90f, size * 2f, false);
            CharacterTattooPlacer.Describe(turned, up, out describedPoint, out _, out angle, out size, out mirrored);
            Assert.That(Vector3.Distance(describedPoint, elsewhere), Is.LessThan(1e-5f), "reshaping keeps the point");
            Assert.That(angle, Is.EqualTo(-90f).Within(0.01f));
            Assert.That(size.y, Is.EqualTo(0.28f).Within(1e-5f));
            Assert.That(mirrored, Is.False);
        }

        [Test]
        public void TattoosSavedWithAShallowBoxAreRefittedInPlace()
        {
            // Saved when boxes reached only 2 cm out of the skin.
            Vector3 point = new(0f, 0.06f, 1.3f);
            Vector3 normal = Vector3.up;
            var size = new Vector3(-0.3f, 0.25f, CharacterTattooPlacer.OutwardReach + CharacterTattooPlacer.InwardReachPerSize * 0.3f);
            Quaternion rotation = Quaternion.LookRotation(-normal, Vector3.forward) * Quaternion.AngleAxis(20f, Vector3.forward);
            Vector3 centre = point + normal * ((size.z - 2f * CharacterTattooPlacer.InwardReachPerSize * 0.3f) * 0.5f);
            var saved = new CharacterTattooPlacement("wolf", centre, rotation, size, Color.black, 0.8f);

            CharacterTattooPlacement refitted = CharacterTattooPlacer.Refit(saved);
            Assert.That(refitted.Size.z, Is.GreaterThan(saved.Size.z), "the box reaches further out");
            CharacterTattooPlacer.Describe(refitted, Vector3.forward, out Vector3 refittedPoint, out _, out float angle, out Vector2 refittedSize, out bool mirrored);
            Assert.That(Vector3.Distance(refittedPoint, point), Is.LessThan(1e-5f), "on the same point of skin");
            Assert.That(angle, Is.EqualTo(20f).Within(0.01f));
            Assert.That(refittedSize.x, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(mirrored, Is.True);
            Assert.That(refitted.Opacity, Is.EqualTo(0.8f).Within(1e-6f));

            Assert.That(CharacterTattooPlacer.Refit(refitted).MeshFromBox, Is.EqualTo(refitted.MeshFromBox), "a current box is kept");
        }

        [Test]
        public void CurvedTattoosKeepTheirCurveWhenSavedAndReshaped()
        {
            Vector3 axis = new Vector3(0.2f, 0f, -1f);
            CharacterTattooPlacement placement = CharacterTattooPlacer.Place(
                "wolf", new Vector3(0.3f, -0.08f, 1.1f), Vector3.right, Vector3.forward, 0f,
                new Vector2(0.1f, 0.1f), false, Color.black, 0.9f, axis, 0.05f);
            Assert.That(placement.IsCurved, Is.True);
            Assert.That(placement.CurveAxis, Is.EqualTo(axis.normalized));

            var recipe = new CharacterRecipe();
            recipe.SetTattoos(new[] { placement });
            CharacterTattooPlacement loaded = JsonUtility.FromJson<CharacterRecipe>(JsonUtility.ToJson(recipe)).Tattoos[0];
            Assert.That(loaded.CurveRadius, Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(Vector3.Angle(loaded.CurveAxis, axis), Is.LessThan(0.01f));

            CharacterTattooPlacement reshaped = CharacterTattooPlacer.Reshape(placement, Vector3.forward, 20f, new Vector2(0.2f, 0.2f), true);
            Assert.That(reshaped.CurveRadius, Is.EqualTo(0.05f).Within(1e-6f), "reshaping keeps the curve");
            Assert.That(reshaped.WithInk(Color.red, 0.5f).IsCurved, Is.True, "changing ink keeps the curve");

            CharacterTattooPlacement flattened = CharacterTattooPlacer.Move(placement, Vector3.zero, Vector3.back, Vector3.forward);
            Assert.That(flattened.IsCurved, Is.False, "moved onto skin with no limb, it projects flat");

            CharacterRecipe older = JsonUtility.FromJson<CharacterRecipe>(
                "{\"version\":5,\"hasTattoos\":true,\"tattoos\":[{\"designId\":\"wolf\",\"size\":{\"x\":0.1,\"y\":0.1,\"z\":0.1}}]}");
            Assert.That(older.Tattoos[0].IsCurved, Is.False, "tattoos saved before curves stay flat");
        }

        [Test]
        public void CurveFollowsTheNearestLimbBone()
        {
            var character = new GameObject("Character");
            var material = new Material(Shader.Find(SolSkinShaderIds.ShaderName));
            var mesh = new Mesh();
            try
            {
                // An upper arm along x from the shoulder (0.2) to the elbow (0.5), 5 cm
                // round; a short finger bone that is too small to wrap around; and a spine
                // up the middle of a torso, 6 cm under the back and 16 cm from the front.
                Transform shoulder = AddBone("upperarm", character.transform, new Vector3(0.2f, 0f, 1.4f));
                Transform elbow = AddBone("forearm", shoulder, new Vector3(0.3f, 0f, 0f));
                Transform finger = AddBone("finger", elbow, new Vector3(0.03f, 0f, 0f));
                Transform hips = AddBone("hips", character.transform, new Vector3(0f, 0f, 1f));
                Transform chest = AddBone("chest", hips, new Vector3(0f, 0f, 0.3f));
                Transform[] bones = { shoulder, elbow, finger, hips, chest };
                var vertices = new System.Collections.Generic.List<Vector3>();
                for (int step = 0; step < 5; step++)
                {
                    for (int turn = 0; turn < 16; turn++)
                    {
                        float angle = turn * Mathf.PI / 8f;
                        vertices.Add(new Vector3(0.25f + step * 0.05f, 0.05f * Mathf.Cos(angle), 1.4f + 0.05f * Mathf.Sin(angle)));
                        vertices.Add(new Vector3(0.16f * Mathf.Cos(angle), -0.05f + 0.11f * Mathf.Sin(angle), 1.05f + step * 0.05f));
                    }
                }

                mesh.SetVertices(vertices);
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.boneWeights = vertices.ConvertAll(_ => new BoneWeight { weight0 = 1f }).ToArray();
                mesh.bindposes = System.Array.ConvertAll(bones, bone => bone.worldToLocalMatrix * character.transform.localToWorldMatrix);

                var body = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
                body.transform.SetParent(character.transform, false);
                body.sharedMesh = mesh;
                body.bones = bones;
                body.sharedMaterial = material;
                var tattoos = character.AddComponent<CharacterTattoos>();

                Assert.That(tattoos.TryGetCurve(new Vector3(0.35f, 0f, 1.45f), out Vector3 axis, out float radius), Is.True);
                Assert.That(Vector3.Angle(axis, Vector3.right) < 0.01f || Vector3.Angle(axis, Vector3.left) < 0.01f, Is.True, axis.ToString());
                Assert.That(radius, Is.EqualTo(0.05f).Within(1e-4f));

                Assert.That(tattoos.TryGetCurve(new Vector3(0.35f, 0f, 1.6f), out _, out _), Is.False,
                    "skin further than a limb's radius from any bone projects flat");
                Assert.That(tattoos.TryGetCurve(new Vector3(0f, 0.06f, 1.15f), out _, out _), Is.False,
                    "the back is not wrapped round the spine just under it");
            }
            finally
            {
                Object.DestroyImmediate(character);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
            }
        }

        private static Transform AddBone(string name, Transform parent, Vector3 localPosition)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;
            return bone;
        }

        [Test]
        public void TattooTabBuildsIntoTheDemoMenu()
        {
            const string menuPath = "Assets/CharCustomization/Resources/Prefabs/CharacterMorphMenuDemoUI.prefab";
            GameObject menuRoot = UnityEditor.PrefabUtility.LoadPrefabContents(menuPath);
            try
            {
                Assert.That(SolTattooMenuSetup.TryBuildTab(menuRoot, out string message), Is.True, message);
                var menu = menuRoot.GetComponentInChildren<CharacterMorphDemoUI>(true);
                var serializedMenu = new UnityEditor.SerializedObject(menu);
                var picker = serializedMenu.FindProperty("tattooPicker").objectReferenceValue as CharacterTattooPicker;
                Assert.That(picker, Is.Not.Null);
                Assert.That(serializedMenu.FindProperty("tattooPanel").objectReferenceValue, Is.EqualTo(picker.gameObject));
                var serializedPicker = new UnityEditor.SerializedObject(picker);
                foreach (string field in new[]
                         {
                             "designContent", "designTileTemplate", "placedContent", "placedTileTemplate", "inkContent",
                             "inkSwatchTemplate", "sizeSlider", "rotationSlider", "opacitySlider", "mirrorButton", "removeButton", "hint"
                         })
                {
                    Assert.That(serializedPicker.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
                }

                var designTemplate = (UnityEngine.UI.Button)serializedPicker.FindProperty("designTileTemplate").objectReferenceValue;
                Assert.That(designTemplate.transform.parent.GetComponent<UnityEngine.UI.GridLayoutGroup>().cellSize.x,
                    Is.GreaterThan(SolHairMenuSetup.TileSize), "design previews are larger than hair style tiles");

                var groups = new System.Collections.Generic.List<string>();
                SerializedPropertyGroups(serializedMenu, groups);
                Assert.That(groups, Is.EqualTo(CharacterCustomizationUiConfig.ExpectedTabs(groups)), "tabs are in the expected order");
                Assert.That(groups, Does.Contain(CharacterCustomizationUiConfig.TattooGroupId));

                Assert.That(SolTattooMenuSetup.TryBuildTab(menuRoot, out message), Is.True, message);
                SerializedPropertyGroups(new UnityEditor.SerializedObject(menu), groups);
                Assert.That(groups.FindAll(group => group == CharacterCustomizationUiConfig.TattooGroupId).Count, Is.EqualTo(1),
                    "building again rebuilds the panel without a second tab");
            }
            finally
            {
                UnityEditor.PrefabUtility.UnloadPrefabContents(menuRoot);
            }
        }

        private static void SerializedPropertyGroups(UnityEditor.SerializedObject serializedMenu, System.Collections.Generic.List<string> groups)
        {
            groups.Clear();
            UnityEditor.SerializedProperty tabs = serializedMenu.FindProperty("tabButtons");
            for (int index = 0; index < tabs.arraySize; index++)
            {
                if (tabs.GetArrayElementAtIndex(index).objectReferenceValue is CharacterMorphTabButton tab)
                {
                    groups.Add(tab.GroupId);
                }
            }
        }

        private static void AssertKeepsLook(CharacterRecipe recipe)
        {
            Assert.That(recipe.HasHair, Is.True);
            Assert.That(recipe.HairStyleId, Is.EqualTo("F55"));
            Assert.That(recipe.HairSheen, Is.EqualTo(0.6f).Within(1e-6f));
            Assert.That(recipe.HasTattoos, Is.True);
            Assert.That(recipe.Tattoos.Count, Is.EqualTo(1));
            Assert.That(recipe.Tattoos[0].DesignId, Is.EqualTo("wolf"));
        }

        // A profile and controller with a female root holding a rig (head.x) and, when a
        // style is given, a hairstyle skinned to that head under a Hair group.
        private static GameObject BuildCharacter(string name, string hairStyle)
        {
            var character = new GameObject(name);
            var female = new GameObject("Female").transform;
            female.SetParent(character.transform, false);
            var rig = new GameObject("Rig").transform;
            rig.SetParent(female, false);
            var head = new GameObject("head.x").transform;
            head.SetParent(rig, false);
            var group = new GameObject("Hair").transform;
            group.SetParent(female, false);

            var controller = character.AddComponent<CharacterMorphController>();
            SetField(controller, "femaleRoot", female.gameObject);
            var profile = character.AddComponent<CharacterProfile>();
            SetField(profile, "controller", controller);

            if (hairStyle != null)
            {
                var style = new GameObject("Sol_" + hairStyle).AddComponent<CharacterHairStyle>();
                style.transform.SetParent(group, false);
                style.Configure(hairStyle, hairStyle);
                var cards = new GameObject("Cards").AddComponent<SkinnedMeshRenderer>();
                cards.transform.SetParent(style.transform, false);
                cards.bones = new[] { head };
                cards.rootBone = head;
            }

            return character;
        }

        private static void SetField(Object target, string field, Object value)
        {
            var serialized = new UnityEditor.SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssertMatrix(Matrix4x4 actual, Matrix4x4 expected)
        {
            for (int element = 0; element < 16; element++)
            {
                Assert.That(actual[element], Is.EqualTo(expected[element]).Within(1e-4f), $"element {element}");
            }
        }
    }
}

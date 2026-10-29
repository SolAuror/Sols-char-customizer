using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTests
{
    public sealed class CharacterJointOffsetEditModeTests
    {
        private const float Tolerance = 0.0001f;

        // The synthetic exporter armature: Unity -X, Z, Y in centimetres. A mirror,
        // a swap, and a scale, like a Blender to Unity FBX round trip.
        private static Vector3 ToArmature(Vector3 unity)
        {
            return new Vector3(-unity.x, unity.z, unity.y) * 100f;
        }

        private readonly List<Object> created = new();
        private readonly List<CharacterRigProportionDriver> drivers = new();
        private SyntheticHumanoid humanoid;
        private SkinnedMeshRenderer body;
        private Mesh originalMesh;

        [TearDown]
        public void TearDown()
        {
            // Edit Mode skips OnDestroy, so unbinding is what releases mesh copies.
            foreach (CharacterRigProportionDriver driver in drivers)
            {
                if (driver != null)
                {
                    driver.Bind(null, null);
                }
            }

            drivers.Clear();
            foreach (Object target in created)
            {
                if (target != null)
                {
                    Object.DestroyImmediate(target);
                }
            }

            created.Clear();
            humanoid?.Destroy();
            humanoid = null;
            if (originalMesh != null)
            {
                Object.DestroyImmediate(originalMesh);
            }
        }

        [Test]
        public void TableReadsTheSidecarFormat()
        {
            string json = "{\"format\":\"sol-character-sidecar\",\"version\":2,\"body\":\"Body\",\"bodyVertexCount\":4," +
                          "\"bones\":[{\"name\":\"a\",\"parent\":null,\"head\":[0,1,2],\"method\":\"region\"}," +
                          "{\"name\":\"b\",\"parent\":\"a\",\"head\":[0,2,2],\"method\":\"band\"}]," +
                          "\"shapes\":[{\"name\":\"S+\",\"meshes\":[\"Body\"],\"boneIndices\":[1],\"offsets\":[0.5,0,-0.25]}]," +
                          "\"checks\":[{\"severity\":\"warning\",\"code\":\"x\",\"message\":\"m\",\"details\":{\"k\":1}}]}";

            Assert.That(CharacterJointOffsetTable.TryCreate(json, out CharacterJointOffsetTable table, out string error), Is.True, error);
            created.Add(table);

            Assert.That(table.Bones.Count, Is.EqualTo(2));
            Assert.That(table.Bones[1].Parent, Is.EqualTo("a"));
            Assert.That(table.Bones[0].RestHead, Is.EqualTo(new Vector3(0f, 1f, 2f)));
            Assert.That(table.TryGetShape("S+", out CharacterJointOffsetTable.JointShape shape), Is.True);
            Assert.That(shape.BoneIndices[0], Is.EqualTo(1));
            Assert.That(shape.Offsets[0], Is.EqualTo(new Vector3(0.5f, 0f, -0.25f)));
            Assert.That(table.Checks[0].Code, Is.EqualTo("x"));
        }

        [Test]
        public void TableRejectsOldVersionsAndBrokenOffsets()
        {
            Assert.That(CharacterJointOffsetTable.TryCreate(
                "{\"format\":\"sol-character-sidecar\",\"version\":1}", out _, out string versionError), Is.False);
            StringAssert.Contains("version 1", versionError);

            Assert.That(CharacterJointOffsetTable.TryCreate(
                "{\"format\":\"sol-character-sidecar\",\"version\":2,\"bones\":[{\"name\":\"a\",\"head\":[0,0,0]}]," +
                "\"shapes\":[{\"name\":\"S\",\"boneIndices\":[0],\"offsets\":[1,2]}]}", out _, out string offsetError), Is.False);
            StringAssert.Contains("offset values", offsetError);
        }

        [TestCase("SK_Human_F.solrig")]
        [TestCase("SK_Human_M.solrig")]
        public void ExportedCharacterSidecarsLoad(string fileName)
        {
            string path = Path.Combine(Application.dataPath, "CharCustomization", "RigData", fileName);
            if (!File.Exists(path))
            {
                Assert.Ignore($"{fileName} is not in this project.");
            }

            Assert.That(CharacterJointOffsetTable.TryCreate(File.ReadAllText(path), out CharacterJointOffsetTable table, out string error),
                Is.True, error);
            created.Add(table);
            Assert.That(table.BodyVertexCount, Is.EqualTo(13380));
            Assert.That(table.Bones.Count, Is.EqualTo(132));
            Assert.That(table.TryGetShape("Body_Height+", out CharacterJointOffsetTable.JointShape height), Is.True);
            Assert.That(height.BoneIndices.Count, Is.GreaterThan(100));
        }

        [Test]
        public void CalibrationRecoversMirroredScaledAxes()
        {
            var unity = new List<Vector3>
            {
                new(0f, 1f, 0f), new(0.3f, 1.4f, 0.1f), new(-0.3f, 1.4f, 0.1f), new(0.1f, 0.5f, -0.05f), new(0f, 1.7f, 0.2f)
            };
            var exported = new List<Vector3>();
            foreach (Vector3 point in unity)
            {
                exported.Add(ToArmature(point) + new Vector3(5f, -2f, 1f));
            }

            Assert.That(JointSpaceCalibration.TrySolve(exported, unity, out JointSpaceCalibration calibration), Is.True);
            Assert.That(calibration.Scale, Is.EqualTo(0.01f).Within(1e-5f));
            Assert.That(calibration.RelativeError, Is.LessThan(1e-4f));
            Assert.That((calibration.TransformVector(ToArmature(new Vector3(0.02f, -0.01f, 0.03f))) -
                         new Vector3(0.02f, -0.01f, 0.03f)).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void ShapeWeightMovesJointAndItsBindPose()
        {
            BuildCharacter(out CharacterRigProportionDriver driver, ("Reach+", new Dictionary<string, Vector3>
            {
                { "LeftLowerArm", new Vector3(-0.02f, 0f, 0f) },
                { "LeftHand", new Vector3(-0.02f, 0f, 0f) }
            }));
            Transform lowerArm = humanoid.Find("LeftLowerArm");
            Transform hand = humanoid.Find("LeftHand");
            Vector3 lowerArmBind = lowerArm.localPosition;
            Vector3 handBind = hand.localPosition;
            int lowerArmIndex = System.Array.IndexOf(body.bones, lowerArm);
            Matrix4x4 originalBindpose = originalMesh.bindposes[lowerArmIndex];

            Assert.That(driver.JointOffsets, Is.Not.Null);
            Assert.That(driver.JointOffsets.Calibration.Scale, Is.EqualTo(0.01f).Within(1e-5f));

            SetWeight("Reach+", 100f);
            driver.MarkJointOffsetsDirty();
            driver.ApplyNow();

            Assert.That((lowerArm.localPosition - (lowerArmBind + new Vector3(-0.02f, 0f, 0f))).magnitude, Is.LessThan(Tolerance));
            Assert.That((hand.localPosition - handBind).magnitude, Is.LessThan(Tolerance), "The hand moves with its parent.");
            Assert.That(body.sharedMesh, Is.Not.SameAs(originalMesh));
            var probe = new Vector3(-0.4f, 1.4f, 0.05f);
            Vector3 expected = originalBindpose.MultiplyPoint(probe - new Vector3(-0.02f, 0f, 0f));
            Assert.That((body.sharedMesh.bindposes[lowerArmIndex].MultiplyPoint(probe) - expected).magnitude, Is.LessThan(Tolerance));
        }

        [Test]
        public void RebuiltAvatarAndSharedMeshFollowTheOffsets()
        {
            BuildCharacter(out CharacterRigProportionDriver driver, ("Reach+", new Dictionary<string, Vector3>
            {
                { "LeftLowerArm", new Vector3(-0.02f, 0f, 0f) }
            }));

            SetWeight("Reach+", 100f);
            driver.MarkJointOffsetsDirty();
            driver.ApplyNow();
            Assert.That(driver.RebuildAvatarNow(), Is.True);

            Assert.That(SkeletonPosition(humanoid.Animator.avatar, "LeftLowerArm").x, Is.EqualTo(-0.30f).Within(Tolerance));
            Assert.That(CharacterBindPoseMeshCache.GetReferenceCount(body.sharedMesh), Is.EqualTo(1),
                "A committed rebuild swaps the private copy for a shared one.");

            Mesh shared = body.sharedMesh;
            SetWeight("Reach+", 0f);
            driver.MarkJointOffsetsDirty();
            driver.ApplyNow();

            Assert.That(body.sharedMesh, Is.SameAs(originalMesh));
            Assert.That(shared == null, Is.True, "The last user releases the shared copy.");
        }

        // Prefabs rename the body object (SK_Human_F becomes M_Human_F), and a hair
        // groom skinned to the head can have more vertices than the body.
        [Test]
        public void RenamedBodyIsFoundBesideLargerHair()
        {
            humanoid = SyntheticHumanoid.Create();
            CharacterJointOffsetTable table = BuildBody(("Reach+", new Dictionary<string, Vector3>
            {
                { "LeftLowerArm", new Vector3(-0.02f, 0f, 0f) }
            }));
            body.gameObject.name = "M_Body";

            var hairObject = new GameObject("Hair");
            hairObject.transform.SetParent(humanoid.Root.transform, false);
            var hair = hairObject.AddComponent<SkinnedMeshRenderer>();
            Transform head = humanoid.Find("Head");
            var hairMesh = new Mesh { name = "Hair" };
            created.Add(hairMesh);
            hairMesh.vertices = new Vector3[8];
            hairMesh.triangles = new[] { 0, 1, 2, 3, 4, 5, 5, 6, 7 };
            hairMesh.bindposes = new[] { head.worldToLocalMatrix * hairObject.transform.localToWorldMatrix };
            hair.bones = new[] { head };
            hair.sharedMesh = hairMesh;

            var manager = new GameObject("Joint Offset Driver");
            created.Add(manager);
            var driver = manager.AddComponent<CharacterRigProportionDriver>();
            drivers.Add(driver);
            driver.Bind(humanoid.Root.transform, humanoid.Animator, table);

            Assert.That(driver.JointOffsets, Is.Not.Null, "The binding chose the hair as the body.");
            Transform lowerArm = humanoid.Find("LeftLowerArm");
            Vector3 bind = lowerArm.localPosition;
            SetWeight("Reach+", 100f);
            driver.MarkJointOffsetsDirty();
            driver.ApplyNow();
            Assert.That((lowerArm.localPosition - (bind + new Vector3(-0.02f, 0f, 0f))).magnitude, Is.LessThan(Tolerance));
        }

        [Test]
        public void UnbindingRestoresMeshAndBones()
        {
            BuildCharacter(out CharacterRigProportionDriver driver, ("Reach+", new Dictionary<string, Vector3>
            {
                { "LeftLowerArm", new Vector3(-0.02f, 0f, 0f) }
            }));
            Transform lowerArm = humanoid.Find("LeftLowerArm");
            Vector3 bind = lowerArm.localPosition;

            SetWeight("Reach+", 100f);
            driver.MarkJointOffsetsDirty();
            driver.ApplyNow();
            driver.Bind(null, null);

            Assert.That(body.sharedMesh, Is.SameAs(originalMesh));
            Assert.That((lowerArm.localPosition - bind).magnitude, Is.LessThan(Tolerance));
        }

        [Test]
        public void SkeletalMorphUsesMeshShapeOnlyWithJointOffsets()
        {
            CharacterMorphController withTable = BuildController(assignTable: true);
            withTable.SetMorph("body.height", 1f);
            Assert.That(withTable.UsesMeshShapes("body.height"), Is.True);
            Assert.That(Weight("Body_Height+"), Is.EqualTo(100f).Within(Tolerance));
            Assert.That(humanoid.Root.transform.localScale.y, Is.EqualTo(1f).Within(Tolerance), "No bone scaling.");

            Transform head = humanoid.Find("Head");
            Vector3 headBind = head.localPosition;
            withTable.RigProportionDriver.ApplyNow();
            Assert.That((head.localPosition - (headBind + new Vector3(0f, 0.05f, 0f))).magnitude, Is.LessThan(Tolerance));
        }

        [Test]
        public void SkeletalMorphKeepsBoneScalingWithoutJointOffsets()
        {
            CharacterMorphController withoutTable = BuildController(assignTable: false);
            withoutTable.SetMorph("body.height", 1f);

            Assert.That(withoutTable.UsesMeshShapes("body.height"), Is.False);
            Assert.That(Weight("Body_Height+"), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(humanoid.Root.transform.localScale.y, Is.GreaterThan(1f));
        }

        private CharacterMorphController BuildController(bool assignTable)
        {
            humanoid = SyntheticHumanoid.Create();
            CharacterJointOffsetTable table = BuildBody(("Body_Height+", new Dictionary<string, Vector3>
            {
                { "Head", new Vector3(0f, 0.05f, 0f) }
            }), ("Body_Height-", new Dictionary<string, Vector3>()));

            var manager = new GameObject("Joint Offset Manager");
            created.Add(manager);
            manager.AddComponent<CharacterRigProportionDriver>();
            var controller = manager.AddComponent<CharacterMorphController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("femaleRoot").objectReferenceValue = humanoid.Root;
            serialized.FindProperty("activeSex").enumValueIndex = (int)CharacterSex.Female;
            if (assignTable)
            {
                serialized.FindProperty("femaleJointOffsets").objectReferenceValue = table;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            controller.SetSex(CharacterSex.Female);
            drivers.Add(controller.RigProportionDriver);
            return controller;
        }

        private void BuildCharacter(
            out CharacterRigProportionDriver driver,
            params (string shape, Dictionary<string, Vector3> unityOffsets)[] shapes)
        {
            humanoid = SyntheticHumanoid.Create();
            CharacterJointOffsetTable table = BuildBody(shapes);
            var manager = new GameObject("Joint Offset Driver");
            created.Add(manager);
            driver = manager.AddComponent<CharacterRigProportionDriver>();
            drivers.Add(driver);
            driver.Bind(humanoid.Root.transform, humanoid.Animator, table);
        }

        // A tiny skinned body over the synthetic skeleton, with one blendshape per
        // shape, and the matching table in the mirrored centimetre armature space.
        private CharacterJointOffsetTable BuildBody(params (string shape, Dictionary<string, Vector3> unityOffsets)[] shapes)
        {
            var bodyObject = new GameObject("Body");
            bodyObject.transform.SetParent(humanoid.Root.transform, false);
            body = bodyObject.AddComponent<SkinnedMeshRenderer>();

            var bones = new List<Transform>();
            foreach (Transform bone in humanoid.Root.GetComponentsInChildren<Transform>())
            {
                if (bone != humanoid.Root.transform && bone != bodyObject.transform)
                {
                    bones.Add(bone);
                }
            }

            var bindposes = new Matrix4x4[bones.Count];
            for (int index = 0; index < bones.Count; index++)
            {
                bindposes[index] = bones[index].worldToLocalMatrix * bodyObject.transform.localToWorldMatrix;
            }

            originalMesh = new Mesh { name = "Joint Test Body" };
            originalMesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right, Vector3.forward };
            originalMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            originalMesh.bindposes = bindposes;
            var deltas = new[] { Vector3.up * 0.01f, Vector3.zero, Vector3.zero, Vector3.zero };
            foreach ((string shape, _) in shapes)
            {
                originalMesh.AddBlendShapeFrame(shape, 100f, deltas, null, null);
            }

            body.bones = bones.ToArray();
            body.sharedMesh = originalMesh;

            var json = new StringBuilder("{\"format\":\"sol-character-sidecar\",\"version\":2,\"body\":\"Body\",\"bones\":[");
            var indexOf = new Dictionary<string, int>();
            for (int index = 0; index < bones.Count; index++)
            {
                Vector3 head = ToArmature(bones[index].position);
                string parent = bones[index].parent == humanoid.Root.transform ? "null" : $"\"{bones[index].parent.name}\"";
                json.Append(index > 0 ? "," : "")
                    .Append($"{{\"name\":\"{bones[index].name}\",\"parent\":{parent},\"head\":[{Number(head.x)},{Number(head.y)},{Number(head.z)}]}}");
                indexOf[bones[index].name] = index;
            }

            json.Append("],\"shapes\":[");
            for (int shapeIndex = 0; shapeIndex < shapes.Length; shapeIndex++)
            {
                var indices = new List<string>();
                var values = new List<string>();
                foreach (KeyValuePair<string, Vector3> offset in shapes[shapeIndex].unityOffsets)
                {
                    Vector3 exported = ToArmature(offset.Value);
                    indices.Add(indexOf[offset.Key].ToString(CultureInfo.InvariantCulture));
                    values.Add($"{Number(exported.x)},{Number(exported.y)},{Number(exported.z)}");
                }

                json.Append(shapeIndex > 0 ? "," : "")
                    .Append($"{{\"name\":\"{shapes[shapeIndex].shape}\",\"meshes\":[\"Body\"],")
                    .Append($"\"boneIndices\":[{string.Join(",", indices)}],\"offsets\":[{string.Join(",", values)}]}}");
            }

            json.Append("],\"checks\":[]}");
            Assert.That(CharacterJointOffsetTable.TryCreate(json.ToString(), out CharacterJointOffsetTable table, out string error), Is.True, error);
            created.Add(table);
            return table;
        }

        private void SetWeight(string shape, float weight)
        {
            body.SetBlendShapeWeight(body.sharedMesh.GetBlendShapeIndex(shape), weight);
        }

        private float Weight(string shape)
        {
            return body.GetBlendShapeWeight(body.sharedMesh.GetBlendShapeIndex(shape));
        }

        private static Vector3 SkeletonPosition(Avatar avatar, string boneName)
        {
            foreach (SkeletonBone bone in avatar.humanDescription.skeleton)
            {
                if (bone.name == boneName)
                {
                    return bone.position;
                }
            }

            Assert.Fail($"Avatar skeleton has no bone named '{boneName}'.");
            return default;
        }

        private static string Number(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}

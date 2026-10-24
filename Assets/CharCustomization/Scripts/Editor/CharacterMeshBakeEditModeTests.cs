using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Sol.CharacterCustomization.EditorTests
{
    public sealed class CharacterMeshBakeEditModeTests
    {
        private const float Tolerance = 0.0001f;
        private const string WeightPositive = "Body_Stat_Weight+";
        private const string WeightNegative = "Body_Stat_Weight-";
        private const string Smile = "Expression_Smile";
        private const string MultiFrame = "Expression_MultiFrame";

        private readonly List<Object> createdObjects = new();
        private readonly List<CharacterMorphController> controllers = new();

        [TearDown]
        public void TearDown()
        {
            foreach (CharacterMorphController controller in controllers)
            {
                if (controller != null)
                {
                    controller.UnbakeActiveCharacter();
                }
            }

            foreach (Object created in createdObjects)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            controllers.Clear();
            createdObjects.Clear();
        }

        [Test]
        public void BakeFoldsWeightedShapesAndKeepsOthers()
        {
            Mesh source = CreateMesh();
            var weights = new Dictionary<string, float> { { WeightPositive, 50f }, { WeightNegative, 0f } };

            Assert.That(CharacterMeshBaker.TryBake(source, weights, out Mesh baked, out string error), Is.True, error);
            createdObjects.Add(baked);

            Assert.That(baked.vertices[0].y, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(baked.vertices[2].y, Is.EqualTo(1.5f).Within(Tolerance));
            Assert.That(baked.GetBlendShapeIndex(WeightPositive), Is.EqualTo(-1));
            Assert.That(baked.GetBlendShapeIndex(WeightNegative), Is.EqualTo(-1));
            Assert.That(baked.blendShapeCount, Is.EqualTo(2));

            int smileIndex = baked.GetBlendShapeIndex(Smile);
            var deltas = new Vector3[baked.vertexCount];
            baked.GetBlendShapeFrameVertices(smileIndex, 0, deltas, null, null);
            Assert.That(deltas[0].z, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(baked.normals[0].z, Is.EqualTo(-1f).Within(Tolerance));
        }

        [TestCase(25f, 0.5f)]
        [TestCase(75f, 2f)]
        [TestCase(100f, 3f)]
        public void BakeInterpolatesMultiFrameShapes(float weight, float expectedX)
        {
            Mesh source = CreateMesh();
            var weights = new Dictionary<string, float> { { MultiFrame, weight } };

            Assert.That(CharacterMeshBaker.TryBake(source, weights, out Mesh baked, out string error), Is.True, error);
            createdObjects.Add(baked);

            Assert.That(baked.vertices[0].x, Is.EqualTo(expectedX).Within(Tolerance));
        }

        [Test]
        public void BakeRefusesMeshWithoutReadWrite()
        {
            Mesh source = CreateMesh();
            source.UploadMeshData(true);

            Assert.That(CharacterMeshBaker.TryBake(source, new Dictionary<string, float>(), out _, out string error), Is.False);
            StringAssert.Contains("Read/Write", error);
        }

        [Test]
        public void SharedBakesAreReusedAndDestroyedOnLastRelease()
        {
            Mesh source = CreateMesh();
            var heavy = new Dictionary<string, float> { { WeightPositive, 80f } };
            var slim = new Dictionary<string, float> { { WeightNegative, 80f } };

            Assert.That(CharacterMeshBaker.TryAcquireShared(source, heavy, out Mesh first, out _), Is.True);
            Assert.That(CharacterMeshBaker.TryAcquireShared(source, heavy, out Mesh second, out _), Is.True);
            Assert.That(CharacterMeshBaker.TryAcquireShared(source, slim, out Mesh other, out _), Is.True);

            Assert.That(second, Is.SameAs(first));
            Assert.That(other, Is.Not.SameAs(first));
            Assert.That(CharacterMeshBaker.GetSharedReferenceCount(first), Is.EqualTo(2));

            CharacterMeshBaker.ReleaseShared(first);
            Assert.That(first != null, Is.True);
            CharacterMeshBaker.ReleaseShared(second);
            Assert.That(first == null, Is.True);

            CharacterMeshBaker.ReleaseShared(other);
            Assert.That(other == null, Is.True);
        }

        [Test]
        public void ControllerBakeSwapsMeshAndKeepsExpressionWeights()
        {
            CharacterMorphController controller = CreateCharacter(out SkinnedMeshRenderer renderer, out Mesh source);
            controller.SetMorph("body.weight", 0.5f);
            renderer.SetBlendShapeWeight(source.GetBlendShapeIndex(Smile), 30f);

            Assert.That(controller.BakeActiveCharacter(out string error), Is.True, error);

            Mesh baked = renderer.sharedMesh;
            Assert.That(baked, Is.Not.SameAs(source));
            Assert.That(baked.GetBlendShapeIndex(WeightPositive), Is.EqualTo(-1));
            Assert.That(renderer.GetBlendShapeWeight(baked.GetBlendShapeIndex(Smile)), Is.EqualTo(30f).Within(Tolerance));
            Assert.That(renderer.GetComponent<CharacterBakedMeshHandle>().BakedMesh, Is.SameAs(baked));
            Assert.That(controller.IsBaked, Is.True);
            Assert.That(controller.IsBakeStale, Is.False);
        }

        [Test]
        public void EditsAfterBakeMarkItStaleUntilRebaked()
        {
            CharacterMorphController controller = CreateCharacter(out SkinnedMeshRenderer renderer, out _);
            controller.SetMorph("body.weight", 0.5f);
            Assert.That(controller.BakeActiveCharacter(out string error), Is.True, error);
            Mesh firstBake = renderer.sharedMesh;

            controller.SetStatGrowth("body_fat", 0.25f);
            Assert.That(renderer.sharedMesh, Is.SameAs(firstBake));
            Assert.That(controller.IsBakeStale, Is.True);

            Assert.That(controller.BakeActiveCharacter(out error), Is.True, error);
            Assert.That(renderer.sharedMesh, Is.Not.SameAs(firstBake));
            Assert.That(firstBake == null, Is.True, "The superseded bake should be released.");
            Assert.That(controller.IsBakeStale, Is.False);
        }

        [Test]
        public void UnbakeRestoresSourceMeshAndCurrentWeights()
        {
            CharacterMorphController controller = CreateCharacter(out SkinnedMeshRenderer renderer, out Mesh source);
            controller.SetMorph("body.weight", 0.5f);
            renderer.SetBlendShapeWeight(source.GetBlendShapeIndex(Smile), 30f);
            Assert.That(controller.BakeActiveCharacter(out string error), Is.True, error);

            controller.SetMorph("body.weight", -0.4f);
            controller.UnbakeActiveCharacter();

            Assert.That(controller.IsBaked, Is.False);
            Assert.That(renderer.sharedMesh, Is.SameAs(source));
            Assert.That(renderer.GetBlendShapeWeight(source.GetBlendShapeIndex(WeightPositive)), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(renderer.GetBlendShapeWeight(source.GetBlendShapeIndex(WeightNegative)), Is.EqualTo(40f).Within(Tolerance));
            Assert.That(renderer.GetBlendShapeWeight(source.GetBlendShapeIndex(Smile)), Is.EqualTo(30f).Within(Tolerance));
        }

        [Test]
        public void CharactersWithMatchingRecipesShareOneBakedMesh()
        {
            Mesh source = CreateMesh();
            CharacterMorphController first = CreateCharacter(source, out SkinnedMeshRenderer firstRenderer);
            CharacterMorphController second = CreateCharacter(source, out SkinnedMeshRenderer secondRenderer);
            first.SetMorph("body.weight", 0.3f);
            second.SetMorph("body.weight", 0.3f);

            Assert.That(first.BakeActiveCharacter(out string error), Is.True, error);
            Assert.That(second.BakeActiveCharacter(out error), Is.True, error);

            Assert.That(secondRenderer.sharedMesh, Is.SameAs(firstRenderer.sharedMesh));
            Assert.That(CharacterMeshBaker.GetSharedReferenceCount(firstRenderer.sharedMesh), Is.EqualTo(2));
        }

        [Test]
        public void ReleasedInactiveRootCannotBeShownAgain()
        {
            CharacterMorphController controller = CreateCharacter(out _, out _);
            var otherRoot = new GameObject("Other Sex Root");
            createdObjects.Add(otherRoot);
            CharacterSex otherSex = controller.ActiveSex == CharacterSex.Female ? CharacterSex.Male : CharacterSex.Female;
            AssignRoot(controller, otherSex, otherRoot);

            Assert.That(controller.ReleaseInactiveSexRoot(), Is.True);
            Assert.That(otherRoot == null, Is.True);

            CharacterSex activeSex = controller.ActiveSex;
            controller.SetSex(otherSex);
            Assert.That(controller.ActiveSex, Is.EqualTo(activeSex));

            var errors = new List<string>();
            controller.ValidateConfiguration(errors);
            Assert.That(errors.Exists(message => message.Contains("character root is assigned")), Is.False);
        }

        private CharacterMorphController CreateCharacter(out SkinnedMeshRenderer renderer, out Mesh source)
        {
            source = CreateMesh();
            return CreateCharacter(source, out renderer);
        }

        private CharacterMorphController CreateCharacter(Mesh source, out SkinnedMeshRenderer renderer)
        {
            var manager = new GameObject("Bake Test Manager");
            var root = new GameObject("Bake Test Body Root");
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            createdObjects.Add(manager);
            createdObjects.Add(root);

            renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = source;

            var controller = manager.AddComponent<CharacterMorphController>();
            AssignRoot(controller, controller.ActiveSex, root);
            controllers.Add(controller);
            return controller;
        }

        private static void AssignRoot(CharacterMorphController controller, CharacterSex sex, GameObject root)
        {
            var serialized = new SerializedObject(controller);
            serialized.FindProperty(sex == CharacterSex.Female ? "femaleRoot" : "maleRoot").objectReferenceValue = root;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private Mesh CreateMesh()
        {
            var mesh = new Mesh { name = "Bake Test Mesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back };
            mesh.tangents = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f) };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.AddBlendShapeFrame(WeightPositive, 100f, Fill(Vector3.up), null, null);
            mesh.AddBlendShapeFrame(WeightNegative, 100f, Fill(Vector3.down), null, null);
            mesh.AddBlendShapeFrame(Smile, 100f, Fill(Vector3.forward), null, null);
            mesh.AddBlendShapeFrame(MultiFrame, 50f, Fill(Vector3.right), null, null);
            mesh.AddBlendShapeFrame(MultiFrame, 100f, Fill(Vector3.right * 3f), null, null);
            createdObjects.Add(mesh);
            return mesh;
        }

        private static Vector3[] Fill(Vector3 delta)
        {
            return new[] { delta, delta, delta };
        }
    }
}

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Sol.CharacterCustomization.EditorTools
{
    // Sets up hair exported by Tools/Blender (Export Hair). Creates M_Sol_<style>_Cards
    // and M_Sol_<style>_Cap with Sol/Character Hair from the T_Sol_<style>_* textures
    // beside the FBX, remaps the FBX onto them, and attaches the hair to a character
    // inside a prefab as one hairstyle (CharacterHairStyle).
    //
    // The hair FBX holds meshes in the character FBX's space, carrying the body's head
    // shape keys. Attaching saves skinned copies bound fully to the bone, with the bind
    // pose taken from the body's so the hair lines up even though the hair FBX has no
    // matching skeleton. Under the character root they act like the other proxies: the
    // morph controller drives their blendshapes by name, and joint offsets move them.
    public sealed class SolHairSetupWindow : EditorWindow
    {
        private const string ShaderName = "Sol/Character Hair";
        private const string HairFolder = "Assets/CharCustomization/Resources/Hair";
        private const string DefaultPrefabPath = "Assets/CharCustomization/Resources/Prefabs/DemoPlayer.prefab";
        private const string DefaultCharacter = "";
        private const string DefaultBone = "head.x";
        private const string CharacterPreference = "Sol.HairSetup.Character";
        // sRGB medium-dark brown; matches the shader default.
        private static readonly Color DefaultHairColour = new Color(0.27f, 0.19f, 0.12f, 1f);

        [SerializeField] private GameObject hair;
        [SerializeField] private GameObject characterPrefab;
        [SerializeField] private string characterName = DefaultCharacter;
        [SerializeField] private string boneName = DefaultBone;

        [MenuItem("Tools/Sol/Character Customization/Hair/Hair Setup")]
        private static void Open()
        {
            GetWindow<SolHairSetupWindow>("Sol Hair Setup");
        }

        private void OnEnable()
        {
            if (hair == null)
            {
                hair = AssetDatabase.FindAssets("Sol_ t:Model", new[] { HairFolder })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(path => Path.GetFileName(path).StartsWith("Sol_"))
                    .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                    .FirstOrDefault(model => model != null);
            }

            if (characterPrefab == null)
            {
                characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
            }

            characterName = EditorPrefs.GetString(CharacterPreference, characterName);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Creates the hair materials and their soft-fringe variants, then adds the hair under the bone of the " +
                "chosen character in the prefab. Running it again replaces the hair and keeps material changes you made.",
                MessageType.None);
            hair = (GameObject)EditorGUILayout.ObjectField("Hair (Sol_*.fbx)", hair, typeof(GameObject), false);
            characterPrefab = (GameObject)EditorGUILayout.ObjectField(
                "Character Prefab", characterPrefab, typeof(GameObject), false);

            // No silent default: a character not in this prefab shows blank until one is
            // picked, so hair never lands on the wrong character.
            string[] characters = CharacterNames(characterPrefab);
            bool characterChosen;
            if (characters.Length > 0)
            {
                int current = Array.IndexOf(characters, characterName);
                int chosen = EditorGUILayout.Popup("Character", current, characters);
                if (chosen >= 0 && chosen != current)
                {
                    characterName = characters[chosen];
                    EditorPrefs.SetString(CharacterPreference, characterName);
                }

                characterChosen = chosen >= 0;
            }
            else
            {
                characterName = EditorGUILayout.TextField("Character", characterName);
                characterChosen = !string.IsNullOrWhiteSpace(characterName);
            }

            boneName = EditorGUILayout.TextField("Bone", boneName);

            using (new EditorGUI.DisabledScope(hair == null))
            {
                if (GUILayout.Button("Create Materials"))
                {
                    Run(() => CreateMaterials(hair));
                }

                using (new EditorGUI.DisabledScope(characterPrefab == null || !characterChosen))
                {
                    if (GUILayout.Button($"Create Materials and Attach to {(characterChosen ? characterName : "…")}"))
                    {
                        Run(() =>
                        {
                            CreateMaterials(hair);
                            Attach(hair, AssetDatabase.GetAssetPath(characterPrefab), characterName, boneName);
                        });
                    }

                    if (GUILayout.Button($"Detach from {(characterChosen ? characterName : "…")}"))
                    {
                        Run(() => Detach(hair, AssetDatabase.GetAssetPath(characterPrefab), characterName));
                    }
                }
            }
        }

        private static void Run(Action action)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException error)
            {
                EditorUtility.DisplayDialog("Sol Hair Setup", error.Message, "OK");
            }
        }

        private static string[] CharacterNames(GameObject prefab)
        {
            if (prefab == null)
            {
                return Array.Empty<string>();
            }

            return prefab.GetComponentsInChildren<Animator>(true)
                .Where(animator => animator.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                .Select(animator => animator.name)
                .Distinct()
                .ToArray();
        }

        public static void CreateMaterials(GameObject hairModel)
        {
            string modelPath = AssetDatabase.GetAssetPath(hairModel);
            if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter importer))
            {
                throw new InvalidOperationException($"{hairModel.name} is not an imported model.");
            }

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Shader '{ShaderName}' was not found.");
            }

            string folder = Path.GetDirectoryName(modelPath).Replace('\\', '/');
            Material cards = CreateOrUpdateMaterial(folder, hairModel.name, shader, cap: false);
            Material cap = CreateOrUpdateMaterial(folder, hairModel.name, shader, cap: true);
            CreateOrUpdateFringe(cards);
            CreateOrUpdateFringe(cap);

            // The exporter names the FBX's materials after these, so remap by name.
            foreach (Material material in new[] { cards, cap })
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
            }

            importer.SaveAndReimport();
            AssetDatabase.SaveAssets();
            Debug.Log($"Hair materials ready for {hairModel.name}: {cards.name}, {cap.name}.", hairModel);
        }

        private static Material CreateOrUpdateMaterial(string folder, string label, Shader shader, bool cap)
        {
            string part = cap ? "Cap" : "Cards";
            string path = $"{folder}/M_{label}_{part}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created)
            {
                material = new Material(shader) { name = $"M_{label}_{part}" };
                material.SetColor("_BaseColor", DefaultHairColour);
            }
            else
            {
                material.shader = shader;
            }

            string prefix = cap ? $"{folder}/T_{label}_Cap_" : $"{folder}/T_{label}_";
            material.SetTexture("_HairData", LoadTexture(prefix + "Data.png"));
            material.SetTexture("_BumpMap", LoadTexture(prefix + "Normal.png"));
            material.SetTexture("_FlowMap", cap ? LoadTexture(prefix + "Flow.png") : null);
            material.SetFloat("_IsCap", cap ? 1f : 0f);
            if (cap)
            {
                material.EnableKeyword("_SOL_HAIR_CAP");
            }
            else
            {
                material.DisableKeyword("_SOL_HAIR_CAP");
            }

            // Cards are seen from both sides; the cap only from outside.
            material.SetFloat("_Cull", (float)(cap ? CullMode.Back : CullMode.Off));

            if (created)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        // A material variant of the hair material that draws only the soft coverage below
        // the cutoff, blended in the transparent queue. It inherits everything else, so
        // colour and highlight changes on the base material carry over.
        private static Material CreateOrUpdateFringe(Material baseMaterial)
        {
            string path = FringePath(baseMaterial);
            Material fringe = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = fringe == null;
            if (created)
            {
                fringe = new Material(baseMaterial) { name = Path.GetFileNameWithoutExtension(path) };
            }

            fringe.parent = baseMaterial;
            fringe.SetFloat("_Fringe", 1f);
            fringe.EnableKeyword("_SOL_HAIR_FRINGE");
            fringe.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            fringe.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            fringe.SetFloat("_ZWrite", 0f);
            fringe.renderQueue = (int)RenderQueue.Transparent;
            // The alpha-tested base material already casts shadows and writes depth.
            foreach (string pass in new[] { "ShadowCaster", "DepthOnly", "DepthNormals", "DepthNormalsOnly" })
            {
                fringe.SetShaderPassEnabled(pass, false);
            }

            if (created)
            {
                AssetDatabase.CreateAsset(fringe, path);
            }
            else
            {
                EditorUtility.SetDirty(fringe);
            }

            return fringe;
        }

        private static string FringePath(Material baseMaterial)
        {
            string path = AssetDatabase.GetAssetPath(baseMaterial);
            return $"{Path.GetDirectoryName(path).Replace('\\', '/')}/{Path.GetFileNameWithoutExtension(path)}_Fringe.mat";
        }

        private static Texture2D LoadTexture(string path)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                Debug.LogWarning($"Hair texture missing: {path}. Export the hair again from Blender.");
            }

            return texture;
        }

        public static void Attach(GameObject hairModel, string prefabPath, string characterName, string boneName)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform character = contents.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(candidate => candidate.name == characterName)
                    ?? throw new InvalidOperationException($"'{characterName}' is not in {Path.GetFileName(prefabPath)}.");
                Transform bone = character.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(candidate => candidate.name == boneName)
                    ?? throw new InvalidOperationException($"'{characterName}' has no bone named '{boneName}'.");
                SkinnedMeshRenderer body = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(renderer => renderer.sharedMesh != null && Array.IndexOf(renderer.bones, bone) >= 0)
                    .OrderByDescending(renderer => renderer.sharedMesh.vertexCount)
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException($"No skinned mesh under '{characterName}' uses '{boneName}'.");

                // The hair FBX root is the character FBX root's space, so at rest each hair mesh
                // sits on the character root at its node's placement in the hair model. The
                // bone's rest placement comes from the body's bind pose.
                int index = Array.IndexOf(body.bones, bone);
                Matrix4x4 boneRest = body.transform.localToWorldMatrix * body.sharedMesh.bindposes[index].inverse;

                // Replace earlier attachments, including the first ones parented to the bone.
                foreach (Transform old in character.GetComponentsInChildren<Transform>(true)
                             .Where(candidate => candidate.name == hairModel.name)
                             .ToArray())
                {
                    Object.DestroyImmediate(old.gameObject);
                }

                var root = new GameObject(hairModel.name) { layer = bone.gameObject.layer };
                root.transform.SetParent(character, false);
                string styleId = StyleId(hairModel.name);
                string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(hairModel)).Replace('\\', '/');
                var preview = AssetDatabase.LoadAssetAtPath<Sprite>($"{folder}/T_{hairModel.name}_Preview.png");
                root.AddComponent<CharacterHairStyle>().Configure(styleId, styleId, preview);
                Matrix4x4 modelToRoot = hairModel.transform.worldToLocalMatrix;
                int attached = 0;
                foreach (Renderer source in hairModel.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = source is SkinnedMeshRenderer skinnedSource
                        ? skinnedSource.sharedMesh
                        : source.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    Matrix4x4 nodeLocal = modelToRoot * source.transform.localToWorldMatrix;
                    Matrix4x4 bindpose = boneRest.inverse * character.localToWorldMatrix * nodeLocal;
                    Mesh skinned = SaveSkinnedMesh(mesh, bindpose, $"{folder}/{source.name}_Skinned.asset");

                    var part = new GameObject(source.name) { layer = bone.gameObject.layer };
                    part.transform.SetParent(root.transform, false);
                    part.transform.localPosition = nodeLocal.GetPosition();
                    part.transform.localRotation = nodeLocal.rotation;
                    part.transform.localScale = nodeLocal.lossyScale;
                    var renderer = part.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = skinned;
                    renderer.bones = new[] { bone };
                    renderer.rootBone = bone;
                    renderer.localBounds = BoundsInBone(skinned, bindpose);
                    renderer.sharedMaterials = WithFringe(source.sharedMaterial);
                    attached++;
                }

                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Attached {hairModel.name} ({attached} skinned meshes, style '{styleId}') to " +
                          $"{characterName}, following {boneName}, in {Path.GetFileName(prefabPath)}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // Removes the hair (skinned or the older bone-parented version) from one character.
        public static void Detach(GameObject hairModel, string prefabPath, string characterName)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform character = contents.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(candidate => candidate.name == characterName)
                    ?? throw new InvalidOperationException($"'{characterName}' is not in {Path.GetFileName(prefabPath)}.");
                Transform[] attached = character.GetComponentsInChildren<Transform>(true)
                    .Where(candidate => candidate.name == hairModel.name)
                    .ToArray();
                foreach (Transform old in attached)
                {
                    Object.DestroyImmediate(old.gameObject);
                }

                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                Debug.Log($"Removed {attached.Length} {hairModel.name} attachment(s) from {characterName} in {Path.GetFileName(prefabPath)}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // 'Sol_M14' is style 'M14'.
        public static string StyleId(string hairName)
        {
            return hairName.StartsWith("Sol_") ? hairName.Substring("Sol_".Length) : hairName;
        }

        // A copy of the hair mesh skinned fully to one bone, keeping its blendshapes (the
        // body's head shapes) so the morph controller drives it like any proxy. Saved as an
        // asset beside the FBX; an existing copy is overwritten in place so references hold.
        private static Mesh SaveSkinnedMesh(Mesh source, Matrix4x4 bindpose, string path)
        {
            Mesh mesh = Object.Instantiate(source);
            mesh.name = Path.GetFileNameWithoutExtension(path);
            var weights = new BoneWeight[mesh.vertexCount];
            for (int index = 0; index < weights.Length; index++)
            {
                weights[index] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            }

            mesh.boneWeights = weights;
            mesh.bindposes = new[] { bindpose };

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            existing.name = mesh.name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // Bounds in the root bone's space, with room for the blendshapes.
        private static Bounds BoundsInBone(Mesh mesh, Matrix4x4 bindpose)
        {
            Vector3[] vertices = mesh.vertices;
            var bounds = new Bounds(bindpose.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            foreach (Vector3 vertex in vertices)
            {
                bounds.Encapsulate(bindpose.MultiplyPoint3x4(vertex));
            }

            bounds.Expand(bounds.size.magnitude * 0.15f);
            return bounds;
        }

        // A second material on a single-submesh renderer draws the mesh again: the
        // fringe goes over the alpha-tested hair.
        private static Material[] WithFringe(Material hairMaterial)
        {
            Material fringe = hairMaterial == null
                ? null
                : AssetDatabase.LoadAssetAtPath<Material>(FringePath(hairMaterial));
            return fringe != null ? new[] { hairMaterial, fringe } : new[] { hairMaterial };
        }
    }
}

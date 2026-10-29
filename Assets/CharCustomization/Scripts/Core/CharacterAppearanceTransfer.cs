using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sol.CharacterCustomization
{
    // Gives a second character built from the same rigs, such as a gameplay copy of the
    // customizer character, the parts a recipe refers to, so applying the recipe there
    // shows the same hair and tattoos:
    // - Hairstyles the source has and the copy lacks are cloned under the copy's
    //   matching character root, their skinned meshes rebound to its bones by name.
    // - The copy gets a CharacterTattoos with the source's catalog and bake settings.
    // Run it while the copy is still inactive, before its components wake: the morph
    // controller binds hair blendshapes, and joint offsets adjust hair bind poses, only
    // when the character wakes, so parts added later would not follow morphs.
    public static class CharacterAppearanceTransfer
    {
        public static bool TryCopyParts(CharacterProfile source, CharacterProfile destination, out string report)
        {
            var log = new StringBuilder();
            if (source == null || destination == null || source.Controller == null || destination.Controller == null)
            {
                report = "Both characters need a CharacterProfile with a morph controller.";
                return false;
            }

            if (destination.gameObject.activeInHierarchy)
            {
                log.Append("The copy is already active, so cloned hair will not follow morphs until it is re-initialized. ");
            }

            bool copied = CopyHair(source.Controller.FemaleRoot, destination.Controller.FemaleRoot, log)
                          & CopyHair(source.Controller.MaleRoot, destination.Controller.MaleRoot, log);
            CopyTattoos(source, destination, log);
            report = log.ToString().Trim();
            return copied;
        }

        private static bool CopyHair(GameObject sourceRoot, GameObject destinationRoot, StringBuilder log)
        {
            if (sourceRoot == null || destinationRoot == null)
            {
                return true;
            }

            var existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterHairStyle style in destinationRoot.GetComponentsInChildren<CharacterHairStyle>(true))
            {
                existing.Add(style.StyleId);
            }

            Dictionary<string, Transform> bones = TransformsByName(destinationRoot.transform);
            bool copiedAll = true;
            foreach (CharacterHairStyle style in sourceRoot.GetComponentsInChildren<CharacterHairStyle>(true))
            {
                if (!existing.Add(style.StyleId))
                {
                    continue;
                }

                Transform parent = FindByPath(destinationRoot.transform, RelativePath(sourceRoot.transform, style.transform.parent))
                                   ?? destinationRoot.transform;
                GameObject clone = Object.Instantiate(style.gameObject, parent, false);
                clone.name = style.gameObject.name;
                if (TryRebind(clone, bones, out string missing))
                {
                    log.Append($"Copied hairstyle '{style.StyleId}' to '{destinationRoot.name}'. ");
                    continue;
                }

                if (Application.isPlaying)
                {
                    Object.Destroy(clone);
                }
                else
                {
                    Object.DestroyImmediate(clone);
                }

                existing.Remove(style.StyleId);
                copiedAll = false;
                log.Append($"Hairstyle '{style.StyleId}' was not copied: '{destinationRoot.name}' has no bone '{missing}'. ");
            }

            return copiedAll;
        }

        // Points every skinned mesh in the clone at the same-named bones of the copy.
        private static bool TryRebind(GameObject clone, Dictionary<string, Transform> bones, out string missing)
        {
            foreach (SkinnedMeshRenderer renderer in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Transform[] sourceBones = renderer.bones;
                var rebound = new Transform[sourceBones.Length];
                for (int index = 0; index < sourceBones.Length; index++)
                {
                    if (sourceBones[index] == null)
                    {
                        continue;
                    }

                    if (!bones.TryGetValue(sourceBones[index].name, out rebound[index]))
                    {
                        missing = sourceBones[index].name;
                        return false;
                    }
                }

                renderer.bones = rebound;
                if (renderer.rootBone != null)
                {
                    if (!bones.TryGetValue(renderer.rootBone.name, out Transform rootBone))
                    {
                        missing = renderer.rootBone.name;
                        return false;
                    }

                    renderer.rootBone = rootBone;
                }
            }

            missing = null;
            return true;
        }

        private static void CopyTattoos(CharacterProfile source, CharacterProfile destination, StringBuilder log)
        {
            CharacterTattoos sourceTattoos = source.Tattoos != null ? source.Tattoos : source.GetComponent<CharacterTattoos>();
            if (sourceTattoos == null
                || destination.Tattoos != null
                || destination.GetComponent<CharacterTattoos>() != null)
            {
                return;
            }

            // On the profile's object, where CharacterProfile looks for it when it wakes.
            var tattoos = destination.gameObject.AddComponent<CharacterTattoos>();
            tattoos.CopySettings(sourceTattoos, destination.Controller);
            log.Append($"Added tattoos to '{destination.name}'. ");
        }

        private static Dictionary<string, Transform> TransformsByName(Transform root)
        {
            var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                result.TryAdd(candidate.name, candidate);
            }

            return result;
        }

        private static string RelativePath(Transform root, Transform target)
        {
            var names = new List<string>();
            for (Transform current = target; current != null && current != root; current = current.parent)
            {
                names.Insert(0, current.name);
            }

            return string.Join("/", names);
        }

        private static Transform FindByPath(Transform root, string path)
        {
            return string.IsNullOrEmpty(path) ? root : root.Find(path);
        }
    }
}

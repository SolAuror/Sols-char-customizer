// Last Change: 9/17/2026 5:14pm
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Utilities
{
    /// <summary>Static lookup from a Collider to the TriggerTarget living on its GameObject, plus every live TriggerTarget</summary>
    public static class TriggerRegistry
    {
        private static readonly HashSet<TriggerTarget> AllTargets = new();
        private static readonly Dictionary<Collider, TriggerTarget> ColliderMap = new();
        private static readonly List<Collider> KeysToRemove = new(32);

        //MWC: opt-in logging. These fired on every scene load/cleanup which spammed the Console in shipped builds.
        public static bool DebugLogs = false;

        /// <summary>Every enabled TriggerTarget in the loaded scenes (read-only view)</summary>
        public static IReadOnlyCollection<TriggerTarget> Targets => AllTargets;

        /// <summary>How many colliders are currently mapped to a TriggerTarget</summary>
        public static int ColliderCount => ColliderMap.Count;

        static TriggerRegistry()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        /// <summary>Adds a TriggerTarget to the live set</summary>
        public static void RegisterTarget(TriggerTarget target)
        {
            if (target)
                AllTargets.Add(target);
        }

        /// <summary>Removes a TriggerTarget from the live set (works for a target that is being destroyed)</summary>
        public static void UnregisterTarget(TriggerTarget target)
        {
            if (target is not null)
                AllTargets.Remove(target);
        }

        /// <summary>Maps a collider to the TriggerTarget on its GameObject</summary>
        public static void RegisterCollider(Collider collider, TriggerTarget target)
        {
            if (collider && target)
                ColliderMap[collider] = target;
        }

        /// <summary>Removes the collider entry whoever owns it. A destroyed collider is still removed: the key hashes by instance ID</summary>
        public static void UnregisterCollider(Collider collider)
        {
            //Reference-null only. Unity's fake-null check skipped destroyed colliders, so their entries leaked for the whole session
            if (collider is not null)
                ColliderMap.Remove(collider);
        }

        /// <summary>Removes the collider entry only if [owner] is the target it maps to, so a condemned target cannot unmap its replacement</summary>
        public static void UnregisterCollider(Collider collider, TriggerTarget owner)
        {
            if (collider is null) return;

            if (ColliderMap.TryGetValue(collider, out var mapped) && ReferenceEquals(mapped, owner))
                ColliderMap.Remove(collider);
        }

        /// <summary>The TriggerTarget on the collider's GameObject, or null when it has none registered</summary>
        public static TriggerTarget GetTargetForCollider(Collider collider)
        {
            TriggerTarget target = null;
            if (collider)
                ColliderMap.TryGetValue(collider, out target);
            return target;
        }

        /// <summary>Drops every entry whose collider or target was destroyed (runs on scene load/unload; call it after bulk destroys)</summary>
        public static void RemoveInvalidReferences()
        {
            KeysToRemove.Clear();

            foreach (var entry in ColliderMap)
            {
                if (entry.Key == null || entry.Value == null ||
                    !entry.Key.gameObject || !entry.Value.gameObject)
                {
                    KeysToRemove.Add(entry.Key);
                }
            }

            foreach (var key in KeysToRemove)
            {
                ColliderMap.Remove(key);
            }

            AllTargets.RemoveWhere(target => target == null || !target.gameObject);

            if (DebugLogs && KeysToRemove.Count > 0)
                Debug.Log($"[TriggerRegistry] Removed {KeysToRemove.Count} invalid references");
        }

        /// <summary>Sweeps dead entries on every scene load (additive too: an additively loaded game never sees a Single load)</summary>
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RemoveInvalidReferences();
            if (DebugLogs) Debug.Log($"[TriggerRegistry] Cleanup performed on scene load: {scene.name}");
        }

        /// <summary>Sweeps dead entries when a scene is unloaded, the moment most registered colliders die</summary>
        private static void OnSceneUnloaded(Scene scene)
        {
            RemoveInvalidReferences();
            if (DebugLogs) Debug.Log($"[TriggerRegistry] Cleanup performed on scene unload: {scene.name}");
        }

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                AllTargets.Clear();
                ColliderMap.Clear();
                //Debug.Log("[TriggerRegistry] Registry cleared on exit play mode");
            }
        }
#endif
    }
}
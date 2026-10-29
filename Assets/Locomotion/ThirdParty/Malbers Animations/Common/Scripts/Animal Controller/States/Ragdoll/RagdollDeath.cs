using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Controller
{
    [AddTypeMenu("Death/Ragdoll Replace")]
    public class RagdollDeath : State
    {
        [Header("Ragdoll")]
        [Tooltip("Ragdoll prefab that will replace the current animal controller")]
        public GameObject ragdollPrefab;

        public float Drag = 0.1f;
        public float AngularDrag = 0.1f;

        public bool EnablePreProcessing = true;
        public CollisionDetectionMode collision = CollisionDetectionMode.ContinuousSpeculative;

        [Header("Stability")] // MWC: grouped stability settings
        [Tooltip("Solver iterations per physics step — higher values reduce joint stretching and explosions (Unity default: 6)")]
        public int SolverIterations = 10; // MWC: exposed for stability tuning
        [Tooltip("Velocity solver iterations per physics step — higher values improve joint damping (Unity default: 1)")]
        public int SolverVelocityIterations = 5; // MWC: exposed for stability tuning
        [Tooltip("Caps angular velocity on each ragdoll bone to prevent spinning explosions")]
        public float MaxAngularVelocity = 7f; // MWC: cap to prevent joint blow-up
        [Tooltip("Multiplier applied to the incoming hit force transferred to the ragdoll")]
        public float HitForceMultiplier = 1f; // MWC: allow force scaling without editing the state

        [Header("Timing")]
        [Tooltip("Number of update frames to wait before triggering the ragdoll replacement")]
        public int DelayFrames = 3; // MWC: replaces hardcoded 3; matches Death.cs pattern

        public override string StateIDName => "Death";

        [Tooltip("Destroy the Animal after the Ragdoll is created. If is set to false then it will only Hide the GameObject, and the meshes are returned to it when it is enabled again (E.g. by a Respawner)")]
        public bool DestroyAnimal = true;

        /// <summary>Renderer moved into the Ragdoll, with everything needed to hand it back to the Animal</summary>
        private struct MovedRenderer
        {
            public Transform transform;
            public Transform parent;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public SkinnedMeshRenderer skinned;
            public Transform[] bones;
            public Transform rootBone;
        }

        /// <summary>Renderers taken by the Ragdoll (only tracked when Destroy Animal is off)</summary>
        private readonly List<MovedRenderer> movedRenderers = new();

        /// <summary>Ragdoll spawned by the last Death (only tracked when Destroy Animal is off)</summary>
        private GameObject ragdoll;

        public override void Activate()
        {
            animal.Delay_Action(DelayFrames, () => // MWC: use configurable DelayFrames
            {
                animal.Mode_Stop();
                animal.Mode_Interrupt();
                base.Activate();
                Replace();
            });
        }

        public void Replace()
        {
            //Instantiate the new Ragdoll model
            GameObject ragdollInstance = Instantiate(ragdollPrefab, transform.position, transform.rotation);

            movedRenderers.Clear();
            ragdoll = DestroyAnimal ? null : ragdollInstance; //Keep the Ragdoll only when the Animal will be reused

            //Prepare the ragdoll
            var AllJoints = ragdollInstance.GetComponentsInChildren<CharacterJoint>();
            foreach (var joint in AllJoints)
            {
                joint.enablePreprocessing = EnablePreProcessing; // MWC: was commented out; now applies the exposed field
                joint.enableProjection = true;
            }

            //need to disable it, otherwise when we copy over the hierarchy objects position/rotation, the ragdoll will try each time to
            //"correct" the attached joint, leading to a deformed/glitched instance
            ragdollInstance.SetActive(false);

            //Match the Root Bones
            ragdollInstance.transform.SetPositionAndRotation(transform.position, transform.rotation);

            // MWC: guard against missing RootBone to avoid NullReferenceException
            if (animal.RootBone == null)
            {
                Debug.LogError($"[RagdollDeath] {animal.name} has no RootBone assigned. Bone matching skipped.", this);
            }
            else
            {
                //Map all the Animal Bones in the Dictionary
                var animalBones = animal.RootBone.GetComponentsInChildren<Transform>();
                var AnimalBoneMap = new Dictionary<string, Transform>();
                foreach (Transform bone in animalBones) AnimalBoneMap[bone.name] = bone;

                //Map all the Bones in the Ragdoll in a Dictionary
                var ragdollBones = ragdollInstance.GetComponentsInChildren<Transform>();

                foreach (var bn in ragdollBones)
                {
                    //Match the Position and Rotation of Animal Bones to the Ragdoll Bone
                    if (AnimalBoneMap.TryGetValue(bn.name, out Transform root))
                    {
                        bn.SetPositionAndRotation(root.position, root.rotation);
                    }
                }
            }

            animal.Anim.enabled = false; //Disable Animator (?)

            // MWC: pass true to include inactive objects — ragdollInstance is SetActive(false) above,
            //      so the default (includeInactive=false) would find nothing and leave phantom renderers
            var allSkinnedMeshRendererRagdoll = ragdollInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var allMeshRendererRagdoll = ragdollInstance.GetComponentsInChildren<MeshRenderer>(true);

            foreach (var rdoll in allSkinnedMeshRendererRagdoll)
            {
                Destroy(rdoll.gameObject);
            }

            foreach (var rdoll in allMeshRendererRagdoll)
            {
                Destroy(rdoll.gameObject);
            }



            var allSkinnedMeshRendererAnimal = animal.GetComponentsInChildren<SkinnedMeshRenderer>(false);
            var allMeshRendererAnimal = animal.GetComponentsInChildren<MeshRenderer>(false);

            var allLODs = animal.GetComponentsInChildren<LODGroup>();

            //change the LODs of the cameras
            foreach (var lod in allLODs)
            {
                Track(lod.transform);
                lod.transform.parent = ragdollInstance.transform;
            }

            //Move all Skinned mesh renderers to the Ragdoll
            foreach (var rdoll in allSkinnedMeshRendererAnimal)
            {
                Track(rdoll.transform); //Before the bones are remapped

                if (rdoll.gameObject.activeInHierarchy)
                {
                    if (rdoll.GetComponentInParent<LODGroup>() == null)
                    {
                        rdoll.transform.parent = ragdollInstance.transform;
                    }
                }
                RemapSkinToNewBones(rdoll, ragdollInstance.transform);
            }

            //Move all Mesh renderers to the Ragdoll
            foreach (var rdoll in allMeshRendererAnimal)
            {
                if (rdoll.gameObject.activeInHierarchy)
                {
                    if (rdoll.GetComponentInParent<LODGroup>() == null)
                    {
                        // MWC: default to ragdoll root so foundParent is never null (was: unguarded null assign)
                        Transform foundParent = ragdollInstance.transform;

                        Transform possibleParentTransform = ragdollInstance.transform.FindGrandChild(rdoll.transform.parent.name);
                        if (possibleParentTransform != null)
                        {
                            foundParent = possibleParentTransform;
                        }
                        else if (rdoll.transform.parent?.parent != null) // MWC: null-safe grandparent check
                        {
                            Transform grandParentMatch = ragdollInstance.transform.FindGrandChild(rdoll.transform.parent.parent.name);
                            if (grandParentMatch != null) foundParent = grandParentMatch; // MWC: only assign if found
                        }

                        Track(rdoll.transform);
                        rdoll.transform.parent = foundParent;
                    }
                }
            }

            Vector3 HitDirection = Vector3.zero;
            Vector3 HitPoint = Vector3.zero;
            Collider HitCollider = null;
            ForceMode ForceMod = ForceMode.VelocityChange;

            if (animal.TryGetComponent<IMDamage>(out var IMDamage))
            {
                HitDirection = IMDamage.HitDirection;
                HitPoint = IMDamage.HitPosition;
                HitCollider = IMDamage.HitCollider;
                ForceMod = IMDamage.LastForceMode;
            }

            MDebug.Draw_Arrow(HitPoint, HitDirection.normalized * 3, Color.yellow, 5);

            // MWC: cache animal velocity before any destruction; guard against missing RB
            Vector3 animalVelocity = (animal.RB != null) ? animal.RB.linearVelocity : Vector3.zero;

            var ragdollRB = ragdollInstance.GetComponentsInChildren<Rigidbody>();
            foreach (var rb in ragdollRB)
            {
                rb.collisionDetectionMode = collision;
                rb.isKinematic = false;
                rb.linearVelocity = animalVelocity; // MWC: use cached velocity (was: direct RB access, could NPE)

                rb.linearDamping = Drag;
                rb.angularDamping = AngularDrag;

                // MWC: improve joint stability — Unity defaults are 6/1; these values prevent stretching and explosion
                rb.solverIterations = SolverIterations;
                rb.solverVelocityIterations = SolverVelocityIterations;
                rb.maxAngularVelocity = MaxAngularVelocity; // MWC: cap angular velocity to prevent spinning blow-up

                if (HitCollider != null && HitCollider.name.Contains(rb.name)) //Find the collider and the rigidbody
                {
                    rb.AddForce(HitDirection * HitForceMultiplier, ForceMod); // MWC: apply configurable force multiplier
                }
            }

            animal.OnStateChange.Invoke(ID);//Invoke the Event!!

            ragdollInstance.SetActive(true);

            animal.Delay_Action(() =>
            {
                if (DestroyAnimal)
                    Destroy(animal.gameObject);
                else
                    animal.gameObject.SetActive(false);
            });
        }


        /// <summary>Remembers where a renderer came from so it can be returned to the Animal when it is reused</summary>
        private void Track(Transform t)
        {
            if (DestroyAnimal) return; //The Animal is destroyed, nothing to return

            var skinned = t.GetComponent<SkinnedMeshRenderer>();

            movedRenderers.Add(new MovedRenderer
            {
                transform = t,
                parent = t.parent,
                localPosition = t.localPosition,
                localRotation = t.localRotation,
                localScale = t.localScale,
                skinned = skinned,
                bones = skinned ? (Transform[])skinned.bones.Clone() : null,
                rootBone = skinned ? skinned.rootBone : null,
            });
        }

        /// <summary>Returns the meshes taken by the Ragdoll to the Animal, re-enables its Animator and removes the Ragdoll (used when the Animal is reused instead of destroyed)</summary>
        public void RestoreAnimal()
        {
            if (movedRenderers.Count == 0 && ragdoll == null) return;

            foreach (var m in movedRenderers)
            {
                if (m.transform == null) continue;

                m.transform.SetParent(m.parent, false);
                m.transform.SetLocalPositionAndRotation(m.localPosition, m.localRotation);
                m.transform.localScale = m.localScale;

                if (m.skinned != null)
                {
                    m.skinned.bones = m.bones;
                    m.skinned.rootBone = m.rootBone;
                }
            }
            movedRenderers.Clear();

            if (ragdoll != null) Destroy(ragdoll);
            ragdoll = null;

            if (animal != null && animal.Anim != null) animal.Anim.enabled = true;
        }

        /// <summary>When the Animal is enabled again after a Ragdoll Death, take the meshes back from the Ragdoll</summary>
        public override void OnAnimalEnabled() => RestoreAnimal();

        private void RemapSkinToNewBones(SkinnedMeshRenderer thisRenderer, Transform RootBone)
        {
            if (thisRenderer == null) return;

            var OldRootBone = thisRenderer.rootBone;

            var NewBones = RootBone.GetComponentsInChildren<Transform>();

            Dictionary<string, Transform> boneMap = new();

            foreach (Transform t in NewBones)
            {
                boneMap[t.name] = t;
            }

            Transform[] boneArray = thisRenderer.bones;

            for (int idx = 0; idx < boneArray.Length; ++idx)
            {
                string boneName = boneArray[idx].name;
                Transform original = boneArray[idx]; // MWC: preserve original in case remapping fails

                if (!boneMap.TryGetValue(boneName, out boneArray[idx]))
                {
                    boneArray[idx] = original; // MWC: fall back to original bone; was: null, which caused downstream NPE
                    Debug.LogWarning("[RagdollDeath] Failed to remap bone: " + boneName);
                }
            }
            thisRenderer.bones = boneArray;

            if (boneMap.TryGetValue(OldRootBone.name, out Transform ro))
            {
                thisRenderer.rootBone = ro; //Remap the rootbone
            }
        }

#if UNITY_EDITOR
        public override void SetSpeedSets(MAnimal animal)
        {
            //Do nothing... Death does not need a Speed Set
        }

        // MWC: added Reset() to configure sensible editor defaults on asset creation (matches Death.cs pattern)
        internal override void Reset()
        {
            base.Reset();

            ID = MTools.GetInstance<StateID>("Death");

            noModes.Value = true;

            General = new AnimalModifier()
            {
                modify = (modifier)(-1),
                Persistent = true,
                LockInput = true,
                LockMovement = true,
                AdditiveRotation = true,
            };
        }
#endif
    }
}

// Last Change: 9/24/2026 1:13pm
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;


#if UNITY_EDITOR
using UnityEditor;
#endif


namespace MalbersAnimations.Controller.AI
{
    public enum LookFor
    {
        /// <summary>Obsolete. The Main Animal singleton was removed; kept at index 0 so old assets still load</summary>
        [System.Obsolete("MAnimal.MainAnimal was removed. Use LookFor.MalbersTag with the Player Tag, or LookFor.TransformVar")]
        [InspectorName("Main Animal Player (Obsolete)")]
        MainAnimalPlayer,
        MalbersTag, UnityTag, Zones, GameObject, ClosestWayPoint, CurrentTarget, TransformVar, GameObjectVar, RuntimeGameobjectSet, LastDamager
    }


    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Look", order = -101)]
    public class LookDecision : MAIDecision
    {
        public override string DisplayName => "General/Look";

        [Range(0, 1)]
        /// <summary>Angle of Vision of the Animal</summary>
        [Tooltip("Shorten the Look Ray to not found the ground by mistake")]
        public float LookMultiplier = 0.9f;

        /// <summary>Range for Looking forward and Finding something</summary>
        [Space, Tooltip("Range for Looking forward and Finding something")]

        public bool UseBlackBoardRange = false;
        [Hide(nameof(UseBlackBoardRange), true)]
        public FloatReference LookRange = new(15);
        [Hide(nameof(UseBlackBoardRange)), Tooltip("Blackboard variable for 'Look Range'. Make sure is a Float variable")]
        public string LookRangeBlackboard = "LookRange";

        public bool UseBlackBoardAngle = false;
        /// <summary>Angle of Vision of the Animal</summary>
        [Tooltip("Angle of Vision of the Animal")]
        [Hide(nameof(UseBlackBoardAngle), true), Range(0, 360)]
        public float LookAngle = 120;

        [Hide(nameof(UseBlackBoardAngle)), Tooltip("Blackboard variable for 'Look Angle'. Make sure is a Float variable")]
        public string LookAngleBlackboard = "LookAngle";


        [Tooltip("Layers that can block the Animal Eyes")]
        public LayerReference ObstacleLayer = new(1);
        [Space]
        /// <summary>What to look for?? </summary>
        [Space, Tooltip("What to look for??")]
        public LookFor lookFor = LookFor.MalbersTag;
        [Tooltip("Look for this Unity Tag on an Object")]
        [Hide(nameof(lookFor), (int)LookFor.UnityTag)]
        public string UnityTag = string.Empty;
        [Tooltip("Look for an Specific GameObject by its name")]
        [Hide(nameof(lookFor), (int)LookFor.GameObject)]
        public string GameObjectName = string.Empty;

        [RequiredField, Tooltip("Transform Reference value. This value should be set by a Transform Hook Component")]
        [Hide(nameof(lookFor), (int)LookFor.TransformVar)]
        public TransformVar transform;
        [RequiredField, Tooltip("GameObject Reference value. This value should be set by a GameObject Hook Component")]
        [Hide(nameof(lookFor), (int)LookFor.GameObjectVar)]
        public GameObjectVar gameObject;

        [RequiredField, Tooltip("GameObjectSet. Search for all  GameObjects Set in the Set")]
        [Hide(nameof(lookFor), (int)LookFor.RuntimeGameobjectSet)]
        public RuntimeGameObjects gameObjectSet;

        /// <summary>Type of Zone we want to find</summary>
        [Tooltip("Type of Zone we want to find")]
        [Hide(nameof(lookFor), (int)LookFor.Zones)]
        public ZoneType zoneType;

        [Tooltip("Search for all zones. When off, only Zones of the Zone Type that match the Zone ID and Mode Ability are found")]
        [Hide(nameof(lookFor), (int)LookFor.Zones)]
        public bool AllZones = true;
        /// <summary>ID value of the Zone we want to find</summary>
        [Tooltip("ID value of the Zone we want to find (the State, Stance or Mode ID of the Zone). -1 = any ID. Ignored when All Zones is on")]
        [Hide(nameof(lookFor), (int)LookFor.Zones)]
        public int ZoneID = -1;

        [Tooltip("Ability index of a Mode Zone. -1 = any Ability. Only used on Mode Zones, ignored when All Zones is on")]
        [Hide(nameof(lookFor), (int)LookFor.Zones)]
        public int ZoneModeAbility = -1;

        /// <summary>Custom Tags you want to find (Old Compatability</summary>
        [Tooltip("Custom Tags you want to find")]
        [HideInInspector] public Tag[] tags;

        [Tooltip("Custom Tags you want to find")]
        [Hide(nameof(lookFor), (int)LookFor.MalbersTag)]
        public MTags m_Tags;


        [Space(20), Tooltip("If the what we are looking for is found then Assign it as a new Target")]
        public bool AssignTarget = true;
        [Tooltip("If the what we are looking for is found then also start moving")]
        public bool MoveToTarget = true;
        /// <summary>Obsolete. Replaced by the Selection method; kept only to upgrade old assets</summary>
        [Tooltip("Select randomly one of the potential targets, not the closest one found")]
        [HideInInspector] public bool ChooseRandomly = false;

        [Tooltip("How to select one of the found candidates. Closest was the old default")]
        [Hide(nameof(lookFor), (int)LookFor.MalbersTag, (int)LookFor.UnityTag, (int)LookFor.RuntimeGameobjectSet)]
        public TagFindGameObject Selection = TagFindGameObject.Closest;

        [Tooltip("Element index used when the Selection is set to Index")]
        [Hide(nameof(Selection), (int)TagFindGameObject.Index)]
        public int SelectionIndex = 0;

        [Tooltip("GameObject name used when the Selection is set to By Name")]
        [Hide(nameof(Selection), (int)TagFindGameObject.ByName)]
        public string SelectionName = string.Empty;

        /// <summary>True once the old Choose Randomly bool was converted to the Selection method</summary>
        [SerializeField, HideInInspector] private bool upgradedSelection;



        private void OnValidate()
        {
            UpgradeToMTag();
            UpgradeSelection();
        }

        /// <summary>Old assets stored the selection as a bool. Convert it once to the Selection method</summary>
        public void UpgradeSelection()
        {
            if (upgradedSelection) return;

            upgradedSelection = true;

            if (ChooseRandomly)
            {
                Selection = TagFindGameObject.Random;
                ChooseRandomly = false;
                MTools.SetDirty(this);
            }
        }


        [Tooltip("If the Look decision is achieved then do a reaction on self")]
        public Reaction2 ReactionOnSelf;

        [Tooltip("If the Look decision is achieved then do a reaction on the Target")]
        public Reaction2 ReactionOnTarget;


        public Color debugColor = new(0, 0, 0.7f, 0.3f);

        void Reset() => Description = "The Animal will look for an Object using a cone view";

        public void UpgradeToMTag()
        {
            if (m_Tags == null || m_Tags.Length == 0 && (tags != null && tags.Length > 0))
            {
                m_Tags = new MTags(tags);
                tags = null;
                MTools.SetDirty(this);
            }
        }


        //MWC: Per-agent runtime state. Lives on the brain (not this shared SO) so each AI keeps its own found objects.
        private class LookState : BrainState
        {
            public GameObject[] GameObjects;    //Candidate objects found by Malbers Tag / Unity Tag / Runtime Set
            public Component[] Colliders;       //Non-trigger colliders of the candidates, used to compute their center
        }

        public override bool Decide(MAnimalBrain brain, int index) => Look_For(brain, false, index);
        public override void FinishDecision(MAnimalBrain brain, int index)
        {
            UpgradeToMTag(); //Remove later in newer updates, this is just to upgrade the old Tags array to the new MTags Scriptable Object version 1.5.2


            Look_For(brain, AssignTarget, index); //This will assign the Target in case its true
        }
        public override void PrepareDecision(MAnimalBrain brain, int index)
        {
            //MWC: Reset the runtime state up front. The old code relied on the brain clearing DecisionsVars each state
            //entry; the typed state persists across re-entries, so we clear it here to avoid stale objects/colliders.
            var state = brain.DecisionState<LookState>(index);
            state.GameObjects = null;
            state.Colliders = null;

            switch (lookFor)
            {
                case LookFor.MalbersTag:

                    if (m_Tags == null || m_Tags.tags == null || m_Tags.tags.Count == 0)
                    {
                        Debug.LogWarning($"There's no Tags to look for in the Look Decision [{name}]: " + brain.name, this);
                        return;
                    }
                    var gTags = Tags.GameObjectbyTag(m_Tags);
                    if (gTags.Count > 0) state.GameObjects = gTags.ToArray();

                    break;

                case LookFor.UnityTag:
                    if (string.IsNullOrEmpty(UnityTag)) return;
                    state.GameObjects = GameObject.FindGameObjectsWithTag(UnityTag);
                    break;
                case LookFor.RuntimeGameobjectSet:
                    if (gameObjectSet == null || gameObjectSet.Count == 0) return;
                    state.GameObjects = gameObjectSet.Items.ToArray();
                    break;

                default:
                    break;
            }

            StoreColliders(brain, index);
        }

        /// <summary> Store all renderers found on the GameObjects  </summary>
        private void StoreColliders(MAnimalBrain brain, int index)
        {
            var state = brain.DecisionState<LookState>(index);

            if (state.GameObjects != null && state.GameObjects.Length > 0)
            {
                var colliders = new List<Collider>();

                for (int i = 0; i < state.GameObjects.Length; i++)
                {
                    var AllColliders = state.GameObjects[i].GetComponentsInChildren<Collider>();

                    foreach (var c in AllColliders)
                    {
                        if (c.isTrigger) continue;
                        if (MTools.Layer_in_LayerMask(c.gameObject.layer, ObstacleLayer.Value)) continue;
                        if (c.transform.SameHierarchy(brain.Animal.transform)) continue; //Avoid adding its own colliders

                        colliders.Add(c); //Save only good Colliders
                    }
                }
                state.Colliders = colliders.ToArray();
            }
        }


        /// <summary>  Looks for a gameobject according to the Look For type.</summary>
        private bool Look_For(MAnimalBrain brain, bool assign, int index)
        {
            UpgradeSelection();

#pragma warning disable CS0618 // MainAnimalPlayer is obsolete but old assets still carry it
            return lookFor switch
            {
                LookFor.MainAnimalPlayer => LookForObsoletePlayer(),
#pragma warning restore CS0618
                LookFor.MalbersTag => LookForMalbersTags(brain, assign, index),
                LookFor.UnityTag => LookForUnityTags(brain, assign, index),
                LookFor.Zones => LookForZones(brain, assign),
                LookFor.GameObject => LookForGameObjectByName(brain, assign),
                LookFor.ClosestWayPoint => LookForClosestWaypoint(brain, assign),
                LookFor.CurrentTarget => LookForTarget(brain, assign),
                LookFor.TransformVar => LookForTransformVar(brain, assign),
                LookFor.GameObjectVar => LookForGoVar(brain, assign),
                LookFor.RuntimeGameobjectSet => LookForGoSet(brain, assign, index),
                LookFor.LastDamager => LookForGoLastDamager(brain, assign, index),
                _ => false,
            };
        }

        private bool LookForGoLastDamager(MAnimalBrain brain, bool assign, int index)
        {
            if (brain.Animal.TryGetComponent<MDamageable>(out var damageable))
            {
                var lastDamager = damageable.LastDamage.Damager;
                if (lastDamager != null)
                {
                    AssignMoveTarget(brain, lastDamager.transform, assign);
                    var Center = brain.TargetAnimal ? brain.TargetAnimal.Center : lastDamager.transform.position;
                    return IsInFieldOfView(brain, Center, out _);
                }
            }
            return false;
        }

        public bool LookForTarget(MAnimalBrain brain, bool assign)
        {
            if (brain.Target == null) return false;

            AssignMoveTarget(brain, brain.Target, assign);
            var Center = brain.TargetAnimal ? brain.TargetAnimal.Center : brain.Target.position;
            return IsInFieldOfView(brain, Center, out _);
        }

        public bool LookForTransformVar(MAnimalBrain brain, bool assign)
        {
            if (transform == null || transform.Value == null) return false;

            AssignMoveTarget(brain, transform.Value, assign);

            var Center =
                transform.Value == brain.Target && !brain.AIControl.IsAITarget.IsUnityRefNull() ? //: corrected null check of unity object interface type
                brain.AIControl.IsAITarget.GetCenterY() :
                transform.Value.position;

            return IsInFieldOfView(brain, Center, out _);
        }

        /// <summary>Looks for the GameObject stored on the GameObject Var. False when the variable is missing, empty or holds a prefab asset</summary>
        public bool LookForGoVar(MAnimalBrain brain, bool assign)
        {
            if (gameObject == null || gameObject.Value == null || gameObject.Value.IsPrefab()) return false;

            AssignMoveTarget(brain, gameObject.Value.transform, assign);

            var Center =
               gameObject.Value.transform == brain.Target && !brain.AIControl.IsAITarget.IsUnityRefNull() ? //: corrected null check of unity object interface type
               brain.AIControl.IsAITarget.GetCenterY() :
               gameObject.Value.transform.position;

            return IsInFieldOfView(brain, Center, out _);
        }

        private bool IsInFieldOfView(MAnimalBrain brain, Vector3 Center, out float Distance)
        {
            var Direction_to_Target = (Center - brain.Eyes.position); //Put the Sight a bit higher

            //Important, otherwise it will find the ground for Objects to close to it. Also Apply the Scale
            Distance = Vector3.Distance(Center, brain.Eyes.position) * LookMultiplier; //CustomPatch: TODO: this can be optimized by using squared distance (soon...)


            var LookAngle = UseBlackBoardAngle ? brain.LocalVars.GetFloat(LookAngleBlackboard) : this.LookAngle;
            var LookRange = UseBlackBoardRange ? brain.LocalVars.GetFloat(LookRangeBlackboard) : this.LookRange.Value;


            if (LookAngle == 0 || LookRange <= 0) return true; //Means the Field of view can be ignored

            if (Distance < LookRange * brain.Animal.ScaleFactor) //Check if whe are inside the Look Radius
            {
                Vector3 EyesForward = Vector3.ProjectOnPlane(brain.Eyes.forward, brain.Animal.UpVector);

                var angle = Vector3.Angle(Direction_to_Target, EyesForward); //CustomPatch: TODO: can significantly be optimized by using dot product checking instead of complex Sqrt and ACos based angle calculations (will get back to this)

                if (angle < (LookAngle * 0.5f)) //CustomPatch: minor but easy optimization: multiply cheaper than divide (by around 4x - 10x CPU cycles)
                {
                    //Need a RayCast to see if there's no obstacle in front of the Animal OBSTACLE LAYER
                    if (Physics.Raycast(brain.Eyes.position, Direction_to_Target, out RaycastHit hit, Distance, ObstacleLayer, QueryTriggerInteraction.Ignore))
                    {
                        if (brain.debug)
                        {
                            MDebug.DrawRay(brain.Eyes.position, Direction_to_Target * LookMultiplier, Color.green, interval);
                            MDebug.DrawLine(hit.point, Center, Color.red, interval);
                            MDebug.DrawWireSphere(Center, Color.red, interval);
                            MDebug.DrawCircle(hit.point, hit.normal, 0.1f, Color.red, true, interval);
                        }

                        return false; //Meaning there's something between the Eyes of the Animal and the Target
                    }
                    else
                    {
                        if (brain.debug)
                        {
                            MDebug.DrawRay(brain.Eyes.position, Direction_to_Target, Color.green, interval);
                            MDebug.DrawWireSphere(Center, Color.green, interval);
                        }
                        return true;
                    }
                }

                return false;
            }
            //  Debug.Log($"False (NOT IN Distance {Distance} > RANGE) {LookRange.Value}" );
            return false;
        }

        private void AssignMoveTarget(MAnimalBrain brain, Transform target, bool assign)
        {
            if (assign && AssignTarget)
            {
                brain.AIControl.SetTarget(target, MoveToTarget);
            }
        }

        /// <summary>Looks for the closest Zone inside the field of view that passes the Zone filters</summary>
        public bool LookForZones(MAnimalBrain brain, bool assign)
        {
            var zones = Zone.Zones;
            var zones2 = Zone2.Zones; //MWC: Zone2 (Reaction Zones) can be found too
            if ((zones == null || zones.Count == 0) && (zones2 == null || zones2.Count == 0)) return false;  //There's no zone around here

            float minDistance = float.PositiveInfinity;

            Component FoundZone = null;

            if (zones != null)
            {
                foreach (var zone in zones)
                {
                    if (zone == null || zone.ZoneCollider == null) continue;
                    if (!ZonePassesFilter(zone)) continue;

                    if (IsInFieldOfView(brain, zone.ZoneCollider.bounds.center, out float Distance) && Distance < minDistance)
                    {
                        minDistance = Distance;
                        FoundZone = zone;
                    }
                }
            }

            if (zones2 != null)
            {
                foreach (var zone in zones2)
                {
                    if (zone == null || zone.ZoneCollider == null) continue;
                    if (!ZonePassesFilter(zone)) continue;

                    if (IsInFieldOfView(brain, zone.ZoneCollider.bounds.center, out float Distance) && Distance < minDistance)
                    {
                        minDistance = Distance;
                        FoundZone = zone;
                    }
                }
            }

            if (FoundZone)
            {
                AssignMoveTarget(brain, FoundZone.transform, assign);

                ReactionOnSelf.React(brain.Animal); //React to the Look Decision Achieved
                ReactionOnTarget.React(FoundZone);

                return true;
            }
            return false;
        }

        /// <summary>True when the Zone matches All Zones, or the Zone Type AND the Zone ID (-1 = any) AND, on Mode Zones, the Ability (-1 = any)</summary>
        private bool ZonePassesFilter(Zone zone)
        {
            if (AllZones) return true;
            if (zone.zoneType != zoneType) return false;
            if (ZoneID != -1 && zone.ZoneID != ZoneID) return false;
            if (zone.zoneType == ZoneType.Mode && ZoneModeAbility != -1 &&
                (zone.modeID == null || zone.ModeAbilityIndex != ZoneModeAbility)) return false; //modeID null would throw in ModeAbilityIndex
            return true;
        }

        /// <summary>Same filter for Zone2. The Zone Type comes from its Input Mode / first Activation Reaction</summary>
        private bool ZonePassesFilter(Zone2 zone)
        {
            if (AllZones) return true;
            if (zone.Type != zoneType) return false;
            if (ZoneID != -1 && zone.ZoneID != ZoneID) return false;
            if (zone.Type == ZoneType.Mode && ZoneModeAbility != -1 && zone.ModeAbilityIndex != ZoneModeAbility) return false;
            return true;
        }


        public bool LookForMalbersTags(MAnimalBrain brain, bool assign, int index)
        {
            if (m_Tags == null || m_Tags.Length == 0) return false;

            var filteredTags = Tags.GameObjectbyTag(m_Tags);

            //Tagged objects use their pivot as center, same as before the Selection method was added
            return SelectCandidate(brain, assign, index, filteredTags, Selection, false);
        }

        public bool LookForUnityTags(MAnimalBrain brain, bool assign, int index)
        {
            if (string.IsNullOrEmpty(UnityTag)) return false;

            return SelectFromState(brain, assign, index, Selection);
        }

        public bool LookForGoSet(MAnimalBrain brain, bool assign, int index)
        {
            if (gameObjectSet == null || gameObjectSet.Count == 0) return false;

            return SelectFromState(brain, assign, index, Selection);
        }

        /// <summary>Selects one of the GameObjects stored on Prepare Decision using the given method</summary>
        private bool SelectFromState(MAnimalBrain brain, bool assign, int index, TagFindGameObject mode)
        {
            var state = brain.DecisionState<LookState>(index);

            if (state.GameObjects == null || state.GameObjects.Length == 0) return false;

            return SelectCandidate(brain, assign, index, new List<GameObject>(state.GameObjects), mode, true);
        }

        /// <summary>Picks one candidate inside the field of view using the given selection method</summary>
        private bool SelectCandidate(MAnimalBrain brain, bool assign, int index, List<GameObject> candidates,
            TagFindGameObject mode, bool useColliderCenter)
        {
            if (candidates == null || candidates.Count == 0) return false;

            var state = brain.DecisionState<LookState>(index);

            switch (mode)
            {
                case TagFindGameObject.Random:
                    while (candidates.Count != 0)
                    {
                        int random = Random.Range(0, candidates.Count);

                        if (CandidateInView(brain, state, candidates[random], useColliderCenter, out _))
                            return FoundCandidate(brain, candidates[random], assign);

                        candidates.RemoveAt(random);
                    }
                    return false;

                case TagFindGameObject.First:
                    foreach (var go in candidates)
                    {
                        if (CandidateInView(brain, state, go, useColliderCenter, out _))
                            return FoundCandidate(brain, go, assign);
                    }
                    return false;

                case TagFindGameObject.Index:
                    var byIndex = SelectionIndex >= 0 && SelectionIndex < candidates.Count ? candidates[SelectionIndex] : null;

                    if (CandidateInView(brain, state, byIndex, useColliderCenter, out _))
                        return FoundCandidate(brain, byIndex, assign);
                    return false;

                case TagFindGameObject.ByName:
                    var byName = candidates.Find(x => x != null && x.name == SelectionName);

                    if (CandidateInView(brain, state, byName, useColliderCenter, out _))
                        return FoundCandidate(brain, byName, assign);
                    return false;

                default:
                    GameObject closest = null;
                    float minDistance = float.MaxValue;

                    foreach (var go in candidates)
                    {
                        if (CandidateInView(brain, state, go, useColliderCenter, out float distance) && distance < minDistance)
                        {
                            minDistance = distance;
                            closest = go;
                        }
                    }

                    if (closest != null) return FoundCandidate(brain, closest, assign);
                    return false;
            }
        }

        /// <summary>Assigns the selected GameObject as the new Target</summary>
        private bool FoundCandidate(MAnimalBrain brain, GameObject go, bool assign)
        {
            AssignMoveTarget(brain, go.transform, assign);
            return true;
        }

        /// <summary>True when the candidate center is inside the field of view. Also returns its distance</summary>
        private bool CandidateInView(MAnimalBrain brain, LookState state, GameObject go, bool useColliderCenter, out float distance)
        {
            distance = float.MaxValue;

            if (go == null) return false;

            var center = useColliderCenter ? GetCenter(state, go) : go.transform.position;

            return IsInFieldOfView(brain, center, out distance);
        }

        /// <summary>Center of a GameObject using its stored non trigger colliders, or its pivot when it has none</summary>
        private Vector3 GetCenter(LookState state, GameObject go)
        {
            var center = go.transform.position;

            if (state != null && state.Colliders != null && state.Colliders.Length > 0)
            {
                var bounds = Vector3.zero;
                int total = 0;

                foreach (var c in state.Colliders)
                {
                    if (c != null && c is Collider collider && c.transform.SameHierarchy(go.transform))
                    {
                        bounds += collider.bounds.center;
                        total++;
                    }
                }

                if (total > 0)
                {
                    bounds /= total;
                    if (bounds != Vector3.zero) center = bounds;
                }
            }

            return center;
        }

        /// <summary>Finds the closest stored GameObject inside the field of view</summary>
        public bool ClosestGameObject(MAnimalBrain brain, bool assign, int index)
            => SelectFromState(brain, assign, index, TagFindGameObject.Closest);

        /// <summary>Finds a random stored GameObject inside the field of view</summary>
        public bool ChooseRandomObject(MAnimalBrain brain, bool assign, int index)
            => SelectFromState(brain, assign, index, TagFindGameObject.Random);



        public bool LookForGameObjectByName(MAnimalBrain brain, bool assign)
        {
            if (string.IsNullOrEmpty(GameObjectName)) return false;

            var gameObject = GameObject.Find(GameObjectName);

            if (gameObject)
            {
                AssignMoveTarget(brain, gameObject.transform, assign);
                return IsInFieldOfView(brain, gameObject.transform.position, out _);   //Find if is inside the Field of view
            }
            return false;
        }

        public bool LookForClosestWaypoint(MAnimalBrain brain, bool assign)
        {
            var allWaypoints = MWayPoint.WayPoints;
            if (allWaypoints == null || allWaypoints.Count == 0) return false;  //There's no waypoints  around here

            float minDistance = float.MaxValue;

            MWayPoint closestWayPoint = null;

            foreach (var way in allWaypoints)
            {
                var center = way.GetCenterY();
                if (IsInFieldOfView(brain, center, out float Distance))
                {
                    if (Distance < minDistance)
                    {
                        minDistance = Distance;
                        closestWayPoint = way;
                    }
                }
            }

            if (closestWayPoint)
            {
                AssignMoveTarget(brain, closestWayPoint.transform, assign);
                return true; //Find if is inside the Field of view
            }
            return false;
        }

        /// <summary>True once the obsolete Main Animal Player warning was logged for this asset (runtime only)</summary>
        [System.NonSerialized] private bool warnedObsoletePlayer;

        /// <summary>Obsolete Main Animal Player mode: the singleton is gone, so it never finds anything. Warns once in the Editor</summary>
        private bool LookForObsoletePlayer()
        {
            WarnObsoletePlayer();
            return false;
        }

        /// <summary>Logs the obsolete Main Animal Player warning once per asset. Stripped from player builds</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void WarnObsoletePlayer()
        {
            if (warnedObsoletePlayer) return;
            warnedObsoletePlayer = true;
            Debug.LogWarning($"[{name}] Look Decision: <B>Main Animal Player</B> is obsolete and no longer finds anything. " +
                "Change <B>Look For</B> to [Malbers Tag] with the Player Tag, or to [Transform Var].", this);
        }


#if UNITY_EDITOR

        public override void DrawGizmos(MAnimalBrain brain)
        {
            var Eyes = brain.Eyes;

            var scale = brain.Animal ? brain.Animal.ScaleFactor : brain.transform.root.localScale.y;

            if (Eyes != null)
            {
                Color c = debugColor;
                c.a = 1f;

                var LookAngle = this.LookAngle;
                var LookRange = this.LookRange.Value;

                if (UseBlackBoardAngle || UseBlackBoardRange)
                {
                    var ILocalVars = brain.LocalVars;

                    if (ILocalVars != null)
                    {
                        if (UseBlackBoardAngle) LookAngle = ILocalVars.GetVarEditor<float>(LookAngleBlackboard);
                        if (UseBlackBoardRange) LookRange = ILocalVars.GetVarEditor<float>(LookRangeBlackboard);
                    }
                }

                if (LookAngle == 0f || LookRange == 0f) return; //Means the Field of view can be ignored

                Vector3 EyesForward = Vector3.ProjectOnPlane(brain.Eyes.forward, Vector3.up);

                Vector3 rotatedForward = Quaternion.Euler(0, -LookAngle * 0.5f, 0) * EyesForward;
                UnityEditor.Handles.color = c;
                UnityEditor.Handles.DrawWireArc(Eyes.position, Vector3.up, rotatedForward, LookAngle, LookRange * scale);
                UnityEditor.Handles.color = debugColor;
                UnityEditor.Handles.DrawSolidArc(Eyes.position, Vector3.up, rotatedForward, LookAngle, LookRange * scale);
            }
        }

        /// <summary>Converts a decision left on the obsolete Main Animal Player mode to Malbers Tag [Player]. Returns true when the asset was changed</summary>
        public static bool ConvertObsoletePlayerToTag(LookDecision decision, Tag playerTag)
        {
            if (decision == null) return false;

            var so = new SerializedObject(decision);
            var lookFor = so.FindProperty(nameof(LookDecision.lookFor));

#pragma warning disable CS0618
            if (lookFor.intValue != (int)LookFor.MainAnimalPlayer) return false;
#pragma warning restore CS0618

            lookFor.intValue = (int)LookFor.MalbersTag;

            if (playerTag != null)
            {
                var tags = so.FindProperty(nameof(m_Tags)).FindPropertyRelative(nameof(MTags.tags));
                bool hasPlayer = false;

                for (int i = 0; i < tags.arraySize; i++)
                {
                    if (tags.GetArrayElementAtIndex(i).objectReferenceValue == playerTag) { hasPlayer = true; break; }
                }

                if (!hasPlayer)
                {
                    tags.InsertArrayElementAtIndex(tags.arraySize);
                    tags.GetArrayElementAtIndex(tags.arraySize - 1).objectReferenceValue = playerTag;
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(decision);
            return true;
        }

        [CustomEditor(typeof(LookDecision)), CanEditMultipleObjects]
        public class LookDecisionEditor : MAIDecisionEditor
        {
            /// <summary>Serialized fields drawn by the base Decision editor, excluded from the default pass</summary>
            private static readonly string[] BaseFields = { "m_Script", "Description", "DecisionID", "send", "interval", "waitForTask", "WaitForAllTasks" };

            private SerializedProperty lookFor;

            /// <summary>Caches the Look For property on top of the base Decision properties</summary>
            protected override void OnEnable()
            {
                base.OnEnable();
                lookFor = serializedObject.FindProperty(nameof(LookDecision.lookFor));
            }

            /// <summary>Draws the obsolete Main Animal Player warning with its one-click fixes, then the remaining fields</summary>
            public override void DecisionParameters()
            {
#pragma warning disable CS0618
                var obsoletePlayer = !lookFor.hasMultipleDifferentValues && lookFor.intValue == (int)LookFor.MainAnimalPlayer;
#pragma warning restore CS0618

                if (obsoletePlayer)
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.HelpBox("Main Animal Player is obsolete (the Main Animal singleton was removed) and finds nothing.\n" +
                            "Use Malbers Tag with the Player Tag, or Transform Var.", MessageType.Warning);

                        using (new GUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Use Malbers Tag [Player]"))
                            {
                                var playerTag = MTools.GetInstance<Tag>("Player");
                                foreach (var t in targets) ConvertObsoletePlayerToTag(t as LookDecision, playerTag);
                                serializedObject.Update();
                            }

                            if (GUILayout.Button("Use Transform Var"))
                                lookFor.intValue = (int)LookFor.TransformVar;
                        }
                    }
                }

                DrawPropertiesExcluding(serializedObject, BaseFields);
            }
        }
#endif
    }
}

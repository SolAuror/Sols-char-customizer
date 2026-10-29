// Last Change: 9/30/2026 10:15am
using MalbersAnimations.Events; //MWC: TrajectoryData, pushed by launchers that are not an IThrower
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;


namespace MalbersAnimations.Weapons
{
    [AddComponentMenu("Malbers/Damage/DisplayTrajectory")]
    /// <summary>  Uses a Line Renderer to Predict and Paint a Trajectory  </summary>
    //[RequireComponent(typeof(IThrower))]
    public class MDisplayTrajectory : MonoBehaviour, IAnimatorListener
    {
        [SerializeField] private IThrower Thrower;
        [RequiredField, Tooltip("Reference for the line renderer")]
        public LineRenderer line;

        [Tooltip("Origin of the Trajectory, if null it will use the Thrower Origin")]
        public Transform Origin;
        //MWC: [AfterDistance] is no longer authored here. It lives on the projectile's Flying movement, and the
        //preview reads it from there so the drawn arc can never disagree with the projectile that actually flies

        [Tooltip("Layer to check for the trajectory")]
        public LayerReference Layer = new(1);

        [Tooltip("Check for Triggers when calculating the trajectory")]
        public QueryTriggerInteraction TriggerInteraction;

        //MWC: [Gravity] is no longer authored here either. Like After Distance it comes from the projectile's
        //Flying movement, so the drawn arc uses exactly the gravity the projectile will fly under

        public GameObject HitPoint;
        [Tooltip("Start width of the line renderer")]
        public float StartWidth = 0.3f;//Width of Line where it starts
        [Tooltip("End width of the line renderer")]
        public float EndWidth = 0.1f;//Width of Line where it ends
        [Tooltip("Start Color of the line renderer")]
        public Color startColor = Color.blue;//Color of Line where it starts
        [Tooltip("End Color of the line renderer")]
        public Color endColor = Color.green;//Color of Line where it ends

        [Tooltip("Line renderer steps")]
        public float Step = 0.1f;
        [Tooltip("Max Steps")]
        public int MaxSteps = 50;

        [Tooltip("Draw the predicted points and the hit normal on the Scene View")]
        public bool debug = false;

        private List<Vector3> Trajectory = new();

        /// <summary> Colliders found on one trajectory segment. Reused so the prediction allocates nothing per frame </summary>
        private readonly RaycastHit[] HitBuffer = new RaycastHit[16];


        public Vector3 CacheVelocity { get; set; }
        public bool ShowTrayectory { get; set; }

        private void Reset()
        {
            if (!TryGetComponent(out line))
                line = gameObject.AddComponent<LineRenderer>();

            Thrower = GetComponent<IThrower>();



            SetLineRenderer();
        }


        private void OnValidate()
        {
            if (line == null || !line.transform.SameHierarchy(transform)) line = GetComponent<LineRenderer>();
            if (Origin == null || !Origin.transform.SameHierarchy(transform)) Origin = transform;
        }


        private void Awake()
        {
            Thrower = GetComponent<IThrower>();

            if (Thrower != null)
            {
                if (Thrower.AimOrigin != null) Origin = Thrower.AimOrigin;
                TriggerInteraction = Thrower.TriggerInteraction;
                Layer.Value = Thrower.Layer;
            }
        }

        //MWC: After Distance - single source of truth ---------------------------------------------------------
        /// <summary> Projectile prefab the thrower is currently set to fire. Null when there is no thrower </summary>
        private GameObject ThrowerProjectile => Thrower != null ? Thrower.Projectile : null;

        /// <summary> Last projectile the After Distance was resolved for, so the lookup only runs when it changes </summary>
        private GameObject cachedProjectile;
        private float cachedAfterDistance;
        private bool afterDistanceResolved;

        //MWC: Pushed trajectory data. A launcher with no IThrower (E.g. the Super Item [Projectile - Fire] processor)
        //sends everything through DisplayTrajectory(velocity, gravity, afterDistance, owner). Without this the preview
        //fell back to Physics.gravity, zero After Distance and no owner filter, so the drawn arc disagreed with the shot
        private Vector3 pushedGravity;
        private float pushedAfterDistance;
        private GameObject pushedOwner;
        private bool hasPushedData;

        /// <summary> Owner of the shot, whose own colliders the preview must fly through instead of stopping on.
        /// The Thrower knows it; a pushed shot carries it. Null means no filter, which was the old behaviour </summary>
        private GameObject OwnerGo => Thrower != null ? Thrower.UserGo : pushedOwner;

        /// <summary> After Distance of the thrower's current projectile, taken from its Flying movement </summary>
        public float AfterDistance
        {
            get
            {
                //A pushed shot carries its own: the thrower-only lookup below would always report zero for it
                if (Thrower == null) return hasPushedData ? pushedAfterDistance : 0;

                var projectile = ThrowerProjectile;

                //Re-resolve only when the thrower switches projectile (SetProjectile can change it at runtime)
                if (!afterDistanceResolved || projectile != cachedProjectile)
                {
                    cachedProjectile = projectile;
                    afterDistanceResolved = true;

                    cachedAfterDistance = projectile != null && projectile.TryGetComponent<MProjectile>(out var p)
                        ? p.FlyingAfterDistance
                        : 0;
                }

                return cachedAfterDistance;
            }
        }

        /// <summary> Gravity of the thrower's current projectile. The thrower already derives it from the
        /// projectile's Flying movement, so both the preview and the real flight use one authored value </summary>
        //MWC: a pushed shot brings the projectile's real gravity. Physics.gravity is only the last resort, for a
        //display that nothing has driven yet
        public Vector3 Gravity => Thrower != null ? Thrower.Gravity
            : hasPushedData ? pushedGravity : Physics.gravity;
        //MWC: After Distance - End ----------------------------------------------------------------------------


        public virtual void Show_Trayectory(bool value)
        {
            ShowTrayectory = value;

            //MWC — was an un-gated Debug.Log (it shipped in builds). Hide immediately instead of waiting for the next
            //Update: a zero velocity means there is nothing to predict, so the line must never light up on it.
            if (!value || CurrentVelocity == Vector3.zero) HideTrajectory();
        }

        /// <summary> Velocity the trail is drawn from: the Thrower's when there is one, the last received otherwise </summary>
        public Vector3 CurrentVelocity => Thrower != null ? Thrower.Velocity : CacheVelocity;

        /// <summary> Turn the line and the hit point off. Shared by every "nothing to draw" path </summary>
        private void HideTrajectory()
        {
            if (HitPoint) HitPoint.SetActive(false);

            if (line == null) return; //Can be missing/destroyed at teardown

            line.enabled = false;
            line.positionCount = 0;
        }

        public virtual bool OnAnimatorBehaviourMessage(string message, object value) => this.InvokeWithParams(message, value);

        private void OnEnable()
        {
            SetLineRenderer();

            if (HitPoint && HitPoint.IsPrefab())
                HitPoint = Instantiate(HitPoint, transform);

            if (line.sharedMaterial == null)
                line.material = new Material(Shader.Find("Sprites/Default"));


            if (Thrower != null)
                Thrower.Predict += DisplayTraj;
        }

        private void OnDisable()
        {
            if (Thrower != null)
                Thrower.Predict -= DisplayTraj;
        }


        private void Update()
        {
            // MWC: Guard against Thrower being null (e.g. component destroyed at runtime).
            //MWC — DisplayTrajectory already hides everything on a zero velocity, so both branches end up in the same place
            if (ShowTrayectory)
                DisplayTrajectory(CurrentVelocity);
            else
                HideTrajectory();
        }

        void SetLineRenderer()
        {
            line.startWidth = StartWidth;
            line.endWidth = EndWidth;

            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new(startColor, 0.0f), new(endColor, 1.0f) },
                new GradientAlphaKey[] { new(startColor.a, 0.0f), new(endColor.a, 1.0f) }
            );
            line.colorGradient = gradient;
            line.useWorldSpace = true;
            line.receiveShadows = false;
            line.enabled = false;
            line.positionCount = 0;
        }

        private void DisplayTraj(bool show)
        {
            if (!enabled) return;

            ShowTrayectory = show;

            //MWC — a zero velocity outranks the [show] request: there is no trajectory to draw yet
            if (!show || CurrentVelocity == Vector3.zero)
            {
                HideTrajectory();
                return;
            }

            line.enabled = true;
            if (HitPoint) HitPoint.SetActive(true);
        }

        /// <summary> Push entry for a launcher that is not an IThrower (E.g. the Super Item [Projectile - Fire]
        /// processor). It sends everything the preview cannot look up on its own, so neither system needs to know the
        /// other exists. A ZERO velocity still means "nothing to predict" and hides the line </summary>
        public virtual void DisplayTrajectory(TrajectoryData data)
        {
            pushedGravity = data.Gravity;
            pushedAfterDistance = data.AfterDistance;
            pushedOwner = data.Owner;
            hasPushedData = true;

            DisplayTrajectory(data.Velocity);
        }

        public virtual void DisplayTrajectory(Vector3 ProjectileVelocity)
        {
            CacheVelocity = ProjectileVelocity;

            //MWC — zero velocity = nothing to predict (the Action ended or failed): no line, no hit point
            if (ProjectileVelocity == Vector3.zero)
            {
                HideTrajectory();
                return;
            }
            else
            {
                if (HitPoint) HitPoint.SetActive(true);
                line.enabled = true;
                Trajectory = TrajectoryPoints(Origin.position, ProjectileVelocity);
                DisplayRenderer();
            }
        }

        private List<Vector3> TrajectoryPoints(Vector3 start, Vector3 velocity)
        {
            // MWC: Reuse the existing Trajectory list to avoid per-frame garbage allocation.
            Trajectory.Clear();
            var points = Trajectory;
            if (Step <= 0) return points;
            points.Add(start);
            Vector3 prev = start;

            int NoGravityStep = 0;
            float TraveledDistance = 0;

            //Resolved once: both properties walk the thrower's projectile, and neither can change mid-arc
            var afterDistance = AfterDistance;
            var gravity = Gravity;

            //MWC: resolved once. The line must fly THROUGH the shooter's own colliders instead of stopping on them.
            //This used to read the Thrower directly, so a pushed shot (no Thrower) had no filter at all
            var owner = OwnerGo;
            var ownerT = owner != null ? owner.transform : null;

            var hasHit = false;
            var hitPoint = Vector3.zero;
            var hitNormal = Vector3.up;

            for (int i = 1; i < MaxSteps; i++)
            {
                float time = Step * i;
                float gravityTime = Step * (i - NoGravityStep);

                Vector3 pos = (start + velocity * time) + (gravityTime * gravityTime * gravity / 2);

                if (SegmentHit(prev, pos, ownerT, out var hit))
                {
                    hasHit = true;
                    hitPoint = hit.point;
                    hitNormal = hit.normal;

                    points.Add(hit.point);
                    break;
                }

                points.Add(pos);

                var Direction = (pos - prev);

                //Check if the gravity can be applied after distance
                if (TraveledDistance < afterDistance)
                {
                    TraveledDistance += Direction.magnitude;
                    NoGravityStep++;
                }

                prev = pos;
            }

            //Nothing was hit: the RaycastHit came back zeroed, so the marker used to be parked on the World Origin
            //with a zero Up vector. There is no impact point to show, so the marker is simply hidden
            if (HitPoint) HitPoint.SetActive(hasHit);

            if (hasHit)
            {
                if (debug) Debug.DrawRay(hitPoint, hitNormal, Color.red);

                if (HitPoint)
                {
                    HitPoint.transform.position = hitPoint;
                    if (hitNormal != Vector3.zero) HitPoint.transform.up = hitNormal; //A zero normal is not a rotation
                }
            }

            return points;
        }

        /// <summary> Closest collider on one trajectory segment that is not part of the shooter itself </summary>
        private bool SegmentHit(Vector3 from, Vector3 to, Transform ownerT, out RaycastHit closest)
        {
            closest = default;

            var direction = to - from;
            var distance = direction.magnitude;

            if (distance <= 0) return false;

            //A Raycast instead of a Linecast: a Linecast only reports the CLOSEST collider, so when that was the
            //shooter's own body the whole segment was skipped and the arc flew straight through anything behind it
            var count = Physics.RaycastNonAlloc(from, direction / distance, HitBuffer, distance, Layer, TriggerInteraction);
            var found = false;

            for (int i = 0; i < count; i++)
            {
                var hit = HitBuffer[i];

                if (ownerT != null && hit.collider.transform.SameHierarchy(ownerT)) continue; //The shooter's own body

                if (!found || hit.distance < closest.distance)
                {
                    closest = hit;
                    found = true;
                }
            }

            return found;
        }


        /// <summary> Push the predicted points into the Line Renderer </summary>
        public void DisplayRenderer()
        {
            line.positionCount = Trajectory.Count;

            //SetPosition per point instead of SetPositions(ToArray()): the array was a fresh allocation every frame
            for (int i = 0; i < Trajectory.Count; i++)
            {
                line.SetPosition(i, Trajectory[i]);

                if (debug && i > 0) Debug.DrawLine(Trajectory[i - 1], Trajectory[i], Color.yellow); //Debug Lines for the Prediction
            }
        }
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(MDisplayTrajectory))]
    [UnityEditor.CanEditMultipleObjects]
    public class MDisplayTrajectoryEd : UnityEditor.Editor
    {
        UnityEditor.SerializedProperty line, StartWidth, EndWidth, startColor, endColor, Step, MaxSteps, Hit, Origin, TriggerInteraction, Layer, debug;
        public static GUIStyle StyleBlue => MTools.Style(new Color(0, 0.5f, 1f, 0.3f));

        private void OnEnable()
        {
            line = serializedObject.FindProperty("line");
            StartWidth = serializedObject.FindProperty("StartWidth");
            EndWidth = serializedObject.FindProperty("EndWidth");
            startColor = serializedObject.FindProperty("startColor");
            endColor = serializedObject.FindProperty("endColor");
            Step = serializedObject.FindProperty("Step");
            MaxSteps = serializedObject.FindProperty("MaxSteps");
            Hit = serializedObject.FindProperty("HitPoint");
            Origin = serializedObject.FindProperty("Origin");

            TriggerInteraction = serializedObject.FindProperty("TriggerInteraction");
            Layer = serializedObject.FindProperty("Layer");
            debug = serializedObject.FindProperty("debug");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new GUILayout.HorizontalScope())
            {
                MalbersEditor.DrawDescription("Displays the Trajectory of a projectile, using a list of points");
                MalbersEditor.DrawDebugIcon(debug);
            }

            if (Application.isPlaying)
            {
                using (new UnityEditor.EditorGUI.DisabledGroupScope(true))
                {
                    UnityEditor.EditorGUILayout.ToggleLeft("Show Trajectory", ((MDisplayTrajectory)target).ShowTrayectory);
                    UnityEditor.EditorGUILayout.Vector3Field("Velocity", ((MDisplayTrajectory)target).CacheVelocity);

                }
            }

            using (new GUILayout.VerticalScope(UnityEditor.EditorStyles.helpBox))
            {
                UnityEditor.EditorGUILayout.PropertyField(Origin);
                UnityEditor.EditorGUILayout.PropertyField(Hit);
                UnityEditor.EditorGUILayout.PropertyField(line);

            }

            using (new GUILayout.VerticalScope(UnityEditor.EditorStyles.helpBox))
            {

                using (new GUILayout.HorizontalScope())
                {
                    UnityEditor.EditorGUILayout.PropertyField(StartWidth, GUILayout.MinWidth(40));
                    UnityEditor.EditorGUIUtility.labelWidth = 50;
                    UnityEditor.EditorGUILayout.PropertyField(EndWidth, new GUIContent("End"), GUILayout.MinWidth(30));
                    UnityEditor.EditorGUIUtility.labelWidth = 0;
                }

                using (new GUILayout.HorizontalScope())
                {
                    UnityEditor.EditorGUILayout.PropertyField(startColor, GUILayout.MinWidth(40));
                    UnityEditor.EditorGUIUtility.labelWidth = 50;
                    UnityEditor.EditorGUILayout.PropertyField(endColor, new GUIContent("End"), GUILayout.MinWidth(30));
                    UnityEditor.EditorGUIUtility.labelWidth = 0;
                }

                using (new GUILayout.HorizontalScope())

                {
                    UnityEditor.EditorGUILayout.PropertyField(Step, GUILayout.MinWidth(40));
                    UnityEditor.EditorGUIUtility.labelWidth = 50;
                    UnityEditor.EditorGUILayout.PropertyField(MaxSteps, new GUIContent("Max"), GUILayout.MinWidth(30));
                    UnityEditor.EditorGUIUtility.labelWidth = 0;
                }

            }

            using (new GUILayout.VerticalScope(UnityEditor.EditorStyles.helpBox))
            {
                UnityEditor.EditorGUILayout.PropertyField(TriggerInteraction);
                UnityEditor.EditorGUILayout.PropertyField(Layer);
            }


            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
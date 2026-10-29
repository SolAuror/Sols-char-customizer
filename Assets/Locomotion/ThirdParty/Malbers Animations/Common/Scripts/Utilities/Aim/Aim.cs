using UnityEngine;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Events;
using UnityEngine.Events;
using System;


#if UNITY_EDITOR
using UnityEditor;
#endif
namespace MalbersAnimations
{
    [DefaultExecutionOrder(10000)]
    [AddComponentMenu("Malbers/Utilities/Aiming/Aim")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/utilities/aim")]
    public class Aim : MonoBehaviour, IAim, IAnimatorListener
    {
        #region Vars and Props

        #region Public Variables
        [SerializeField, Tooltip("Is the Aim Active")]
        protected BoolReference m_active = new(true);

        [SerializeField, Tooltip("Aim Origin Reference (Required)"), ContextMenuItem("Head as AimOrigin", "HeadAimOrigin")]
        [RequiredField] protected Transform m_aimOrigin;
        [SerializeField, Tooltip("Smoothness Lerp value to change from Active to Disable")]
        protected float m_Smoothness = 10f;

        [SerializeField, Tooltip("Layers included on the Aiming Logic")]
        protected LayerReference m_aimLayer = new(-1);
        [SerializeField, Tooltip("Does the Aiming Logic ignore Colliders??")]
        protected QueryTriggerInteraction m_Triggers = QueryTriggerInteraction.Ignore;

        [Tooltip("Forced a Target on the Aiming Logic. Calculate the Aim from the Aim Origin to a Target")]
        public TransformReference m_AimTarget = new();

        [Tooltip("Transform Helper that stores the position of the Hit")]
        public TransformReference m_AimPosition = new();

        [SerializeField, Tooltip("Set a Transform Hierarchy to Ignore on the Aim Ray")]
        protected TransformReference m_Ignore = new();

        [SerializeField, Tooltip("Camera Reference used for calculating the Aim logic from the Camera Center. By Default will use the Camera.Main Transform")]
        protected TransformReference m_camera = new();

        [SerializeField, Tooltip("Cast the Camera Ray a bit forward to avoid colliding with near the camera colliders ")]
        protected FloatReference m_forwardCam = new(0.2f);

        [Tooltip("Horizontal Offset for the Aim when using the Camera as Reference, this will move the Aim Point to the left or right of the screen center based on the value, this is useful for 3rd person games where the camera is not exactly behind the character")]
        public FloatReference m_AimHorizontalOffset = new(0);

        [Tooltip("Vertical Offset for the Aim when using the Camera as Reference, this will move the Aim Point up or down of the screen center based on the value, this is useful for 3rd person games where the camera is not exactly behind the character")]
        public FloatReference m_AimVerticalOffset = new(0);


        protected Camera cam;

        [SerializeField, Tooltip("Do the raycast every X Cycles to increase performance")]
        [Min(1)] protected int m_cycles = 1;
        protected int CurrentCycles;
        private int m_cachedScreenWidth;
        private int m_cachedScreenHeight;

        [SerializeField, Tooltip("Default screen center")]
        protected Vector2Reference m_screenCenter = new(0.5f, 0.5f);

        [Tooltip("Does the Character Requires the Camera to Find Aiming... Disable this for AI Characters")]
        public BoolReference m_UseCamera = new(true);

        [SerializeField, Tooltip("This Parameter is used to Change the Current Camera to the Side of which the Character is relative to Camera or the Target")]
        private AimSide m_AimSide = 0;

        [Tooltip("Update mode for the Aim Logic")]
        public UpdateType updateMode = UpdateType.LateUpdate;

        /// <summary>Maximum Distance for the Camera Ray</summary>
        [Tooltip("Maximum Distance from the Origin to the Possible Target")]
        public float MaxDistance = 100f;

        [SerializeField, Tooltip("Use Raycasting for finding the Hit Point. Disable this if you don't need to know which was the object hit.")]
        private BoolReference m_UseRaycasting = new(true);

        /// <summary>Radius for the Sphere Casting, if this is set to Zero they I will use a Ray Casting</summary>
        [Tooltip("Radius for the Sphere Casting, if this is set to Zero they I will use a Ray Casting")]
        public FloatReference rayRadius = new(0.0f);

        /// <summary>Ray Counts for the Ray casting</summary>
        [Tooltip("Maximum Ray Hits for the Ray casting")]
        public int RayHits = 5;

        [Tooltip("Aim Assist will be active for this Aim component (Targets require the Aim Asist component)")]
        public BoolReference useAimAssist = new(true);

        public bool UseRayCastAnimParam = false;

        public TransformEvent OnAimRayTarget = new();
        public Vector3Event OnScreenCenter = new();
        public IntEvent OnAimSide = new();
        public BoolEvent OnAiming = new();
        public BoolEvent OnUsingTarget = new();
        public TransformEvent OnHit = new();

        public Action<RaycastHit> OnRayHitChanged;

        public TransformEvent OnSetTarget = new();
        public UnityEvent OnClearTarget = new();

        public bool debug;
        protected string hitName;
        protected int hitCount;
        #endregion

        #region Properties

        /// <summary>Store the Target Renderer when a new Target is set</summary>
        public Renderer TargetRenderer { get; protected set; }

        /// <summary>Find the Target Center</summary>
        public Vector3 TargetCenter => TargetRenderer != null ? TargetRenderer.bounds.center : AimTarget.position;

        /// <summary>Main Camera</summary>
        public Transform MainCamera { get => m_camera.Value; set => m_camera.Value = value; }

        /// <summary>Check if use Camera is enabled</summary>
        public bool UseCamera { get => m_UseCamera.Value; set => m_UseCamera.Value = value; }
        public bool UseAimAssist { get => useAimAssist.Value; set => useAimAssist.Value = value; }

        /// <summary>Cast the Camera Ray a bit forward to avoid colliding with near the camera colliders </summary>
        public float ForwardCam { get => m_forwardCam.Value; set => m_forwardCam.Value = value; }

        #region Animator Values
        internal int hash_AimHorizontal;
        internal int hash_AimVertical;

        public int hash_AimH => hash_AimHorizontal;
        public int hash_AimV => hash_AimVertical;

        public Animator m_Animator;
        public string m_AimHorizontal = "AimHorizontal";
        public string m_AimVertical = "AimVertical";

        #endregion


        /// <summary>Use Raycasting for finding the Hit Point</summary>
        public bool UseRaycasting { get => m_UseRaycasting.Value; set => m_UseRaycasting.Value = value; }

        /// <summary>Sets the Aim Origin Transform </summary>
        public Transform AimOrigin
        {
            get => m_aimOrigin;
            set
            {
                //Debug.Log("value = " + value);
                if (value)
                    m_aimOrigin = value;
                else
                    m_aimOrigin = defaultOrigin;
            }
        }

        /// <summary>Store the Original Default Origin Transform, in case someone else changed it</summary>
        protected Transform defaultOrigin;
        protected Transform OwnObjectCore;

        /// <summary>Set a Extra Transform to Ignore it (Used in case of the Mount for the Rider)</summary>
        public Transform IgnoreTransform { get => m_Ignore.Value; set => m_Ignore.Value = value; }

        /// <summary>Direction the GameObject is Aiming (Smoothed) </summary>
        public Vector3 AimDirection { get; protected set; }

        /// <summary>Raw Direction the GameObject is Aiming</summary>
        public Vector3 RawAimDirection { get; protected set; }

        /// <summary>Raw Direction the GameObject is Aiming</summary>
        public Vector3 RawAimDirectionNoRayCast { get; protected set; }

        /// <summary>is the Current AimTarget a Target Assist?</summary>
        public bool IsTargetAssist { get; protected set; }

        /// <summary>Smooth Aim Point the ray is Aiming</summary>
        public Vector3 AimPoint { get; protected set; }

        /// <summary>RAW Aim Point the ray is Aiming</summary>
        public Vector3 RawPoint { get; protected set; }

        public float HorizontalAngle_Raw { get; set; }
        public float VerticalAngle_Raw { get; set; }
        public float HorizontalAngle { get; set; }
        public float VerticalAngle { get; set; }

        /// <summary>Default Screen Center</summary>
        public Vector3 ScreenCenter { get; protected set; }

        public IAimTarget LastAimTarget;

        /// <summary>Is the Aiming Logic Active?</summary>
        public virtual bool Active
        {
            get => m_active;
            set
            {
                m_active.Value = value;

                if (value) EnterAim();
                else ExitAim();
            }
        }

        /// <summary> Last Raycast stored for calculating the Aim</summary>
        public RaycastHit AimHit { get; protected set; }

        // protected RaycastHit aimHit => AimHit; //Old

        protected Transform m_AimTargetAssist;

        /// <summary>Transform hit using Raycast</summary>
        protected Transform AimHitTransform;

        /// <summary>Target Transform Stored from the AimRay</summary>
        public virtual Transform AimRayTargetAssist
        {
            get => m_AimTargetAssist;
            set
            {
                if (m_AimTargetAssist != value)
                {
                    m_AimTargetAssist = value;
                    OnAimRayTarget.Invoke(value);
                }
            }
        }

        /// <summary>Check if the camera is in the right:true or Left: False side of the Character </summary>
        public bool AimingSide { get; protected set; }

        /// <summary>Forced Target on the Aiming Logic</summary>
        public virtual Transform AimTarget
        {
            get => m_AimTarget.Value;
            set
            {
                if (m_AimTarget.Value != value) //Only execute the logic when the values are different
                {
                    SetTargetValueInternal(value);

                    if (debug) Debug.Log($"<B>[{name}]</B> - New Target Set <B>[{(value != null ? value.name : "Null")}]</B>", this);

                    InvokeTargetMethods(value);
                }
            }
        }

        public void SetTargetValueInternal(Transform value)
        {
            if (value != null)
            {
                var assist = value.GetComponentInChildren<IAimTarget>();

                if (assist != null)
                {
                    m_AimTarget.Value = assist.AimPoint;
                }
                else
                {
                    m_AimTarget.Value = value;
                }

                enabled = true; //make sure the is Enabled on Target
            }
            else
            {
                m_AimTarget.Value = null;
            }
        }

        private void InvokeTargetMethods(Transform value)
        {
            if (value == null) OnClearTarget.Invoke();
            OnSetTarget.Invoke(value);
            OnUsingTarget.Invoke(value != null);
            OnAimRayTarget.Invoke(value);
        }

        /// <summary>Transform Helper use to Ping the Hit Point</summary>
        public Transform AimPosition { get => m_AimPosition.Value; set => m_AimPosition.Value = value; }

        /// <summary>Layer to Aim and Hit</summary>
        public LayerMask Layer { get => m_aimLayer.Value; set => m_aimLayer.Value = value; }

        public QueryTriggerInteraction TriggerInteraction { get => m_Triggers; set => m_Triggers = value; }

        public virtual AimSide AimSide
        {
            get => m_AimSide;
            set
            {
                m_AimSide = value;

                switch (value)
                {
                    case AimSide.None: OnAimSide.Invoke(0); break;
                    case AimSide.Left: OnAimSide.Invoke(-1); break;
                    case AimSide.Right: OnAimSide.Invoke(1); break;
                    default: break;
                }
            }
        }
        public RaycastHit[] ArrayHits { get; protected set; }

        #endregion
        #endregion

#pragma warning disable CS0414 // Remove unused private members
        [SerializeField] private int EditorTab1 = 0;
#pragma warning restore CS0414 // Remove unused private members

        protected virtual void Awake()
        {
            FindCamera();

            m_Animator = GetComponentInParent<Animator>();

            if (m_Animator)
            {
                hash_AimHorizontal = m_Animator.TryOptionalParameter(m_AimHorizontal);
                hash_AimVertical = m_Animator.TryOptionalParameter(m_AimVertical);
            }

            if (AimOrigin)
                defaultOrigin = AimOrigin;
            else
                AimOrigin = defaultOrigin = transform;

            OwnObjectCore = transform.FindObjectCore();

            GetCenterScreen();

            CurrentCycles = UnityEngine.Random.Range(0, 999999);
        }

        protected virtual void FindCamera()
        {
            //Find the Main Camera on the Scene
            if (MainCamera == null)
            {
                cam = MTools.FindMainCamera();
                if (cam) MainCamera = cam.transform;
            }
            else
            {
                cam = MainCamera.GetComponent<Camera>();
            }
        }


        protected virtual void OnEnable()
        {
            CalculateAiming();

            if (ArrayHits == null || ArrayHits.Length != RayHits)
                ArrayHits = new RaycastHit[RayHits]; //Initialized the Array of RaycastHits


            //Call the Events if the Aim Target is already set
            if (AimTarget != null)
            {
                OnSetTarget.Invoke(AimTarget);
                OnUsingTarget.Invoke(AimTarget != null);
                OnAimRayTarget.Invoke(AimTarget);
            }
            else
            {
                OnClearTarget.Invoke();
            }

            if (!m_camera.UseConstant && m_camera.Variable)
            {
                m_camera.Variable.OnValueChanged += SearchCamera;
            }

        }

        protected virtual void LateUpdate()
        {
            if (updateMode == UpdateType.LateUpdate)
                UpdateLogic(Time.deltaTime);
        }

        protected virtual void FixedUpdate()
        {
            if (updateMode == UpdateType.FixedUpdate)
                UpdateLogic(Time.fixedDeltaTime);
        }

        protected virtual void OnDisable()
        {
            StopAllCoroutines();

            if (!m_camera.UseConstant && m_camera.Variable)
            {
                m_camera.Variable.OnValueChanged -= SearchCamera;
            }

            if (!LastAimTarget.IsUnityRefNull()) //CustomPatch: corrected null check for unity object interface type
                LastAimTarget.IsBeenAimed(false, this);

            LastAimTarget = null;

            AimHit = new RaycastHit(); //Clear the AIM HIT
            AimHitTransform = null; //Clear the Aim Hit Transform

            HorizontalAngle = 0;
            VerticalAngle = 0;
            OnHit.Invoke(null);
            OnAiming.Invoke(false);
            OnAimRayTarget.Invoke(null);
        }

        protected virtual void SearchCamera(Transform obj) => FindCamera();


        protected virtual void UpdateLogic(float time)
        {
            if (!Active) return;

            CurrentCycles++;
            var UseRay = UseRaycasting && (CurrentCycles % m_cycles == 0);
            if (UseRay) CurrentCycles = 0;

            AimLogic(UseRay);
            SmoothValues(time);
            CalculateAngles(time);

            if (m_Animator)
            {
                TryAnimParameter(hash_AimHorizontal, HorizontalAngle);
                TryAnimParameter(hash_AimVertical, VerticalAngle);
            }

            AimDirection = m_Smoothness <= 0 ? RawAimDirection : Vector3.Lerp(AimDirection, RawAimDirection.normalized, m_Smoothness * time);
            // AimDirectionNoRayCast = Vector3.Lerp(AimDirectionNoRayCast, RawAimDirectionNoRayCast.normalized, m_Smoothness * time);
        }

        public virtual void EnterAim()
        {
            CalculateAiming();
            OnAiming.Invoke(true);
            if (AimPosition) AimPosition.gameObject.SetActive(true); //Hide the Helper
        }

        public virtual void ExitAim()
        {
            GetCenterScreen();
            OnScreenCenter.Invoke(ScreenCenter);
            OnAimRayTarget.Invoke(null);
            AimSide = AimSide.None;

            OnAiming.Invoke(false);

            if (AimPosition)
                AimPosition.gameObject.SetActive(false); //Hide the Helper
        }

        public virtual void TryAnimParameter(int Hash, float value)
        {
            if (Hash != 0) m_Animator.SetFloat(Hash, value);
        }

        public virtual void AimLogic(bool useRaycasting)
        {
            var LastAimHit = AimHit;

            if (AimTarget)
            {
                AimHit = DirectionFromTarget(useRaycasting);
                RawPoint = UseRaycasting ? AimHit.point : TargetCenter;
            }
            else if (UseCamera && MainCamera && cam != null)
            {
                AimHit = DirectionFromCamera(useRaycasting);
                RawPoint = AimHit.point;
            }
            else //Means we are using Forward Direction
            {
                AimHit = DirectionFromDirection(useRaycasting);
                RawPoint = AimHit.point;
            }

            var hitTransform = AimHit.transform;

            if (IsTargetAssist)
            {
                RawPoint = AimRayTargetAssist.position;
                if (LastAimTarget != null) hitTransform = LastAimTarget.AimPoint;
            }

            if (useRaycasting) //Invoke the OnHit Option
            {
                if (AimHitTransform != hitTransform)
                {
                    AimHitTransform = hitTransform;
                    OnHit.Invoke(AimHitTransform);
                    // if (debug) Debug.Log("AimHitTransform = " + AimHitTransform);
                }
            }

            if (LastAimHit.transform != hitTransform)
            {
                OnRayHitChanged?.Invoke(AimHit);
            }
        }

        /// <summary> Calculate the Aiming Direction with no smoothing</summary>
        public virtual void CalculateAiming()
        {
            if (Active)
            {
                AimLogic(UseRaycasting);
                SmoothValues(0);
                CalculateAngles(0);
            }
        }

        public virtual void Active_Set(bool value) => Active = value;
        public virtual void Active_Toggle() => Active ^= true;
        public virtual void SetTarget(Transform target) => AimTarget = target;
        public virtual void SetTarget(TransformVar target) => AimTarget = target.Value;
        public virtual void SetTarget(GameObjectVar target) => AimTarget = target.Value.transform;
        public virtual void SetTarget(Component target) => SetTarget(target.transform);
        public virtual void SetTarget(GameObject target) => SetTarget(target.transform);
        public virtual void ClearTarget() => AimTarget = null;

        private Transform oldTarget;

        public virtual void SetTargetTemp(Transform target)
        {
            oldTarget = this.AimTarget;
            SetTarget(target);
        }

        public virtual void ClearTargetTemp()
        {
            SetTarget(oldTarget);
            oldTarget = null;
        }


        /// <summary>Calculates the Camera/Target Horizontal Angle Normalized </summary>
        public virtual void CalculateAngles(float time)
        {
            var AimDir = (AimPoint - OwnObjectCore.position);

            if (!UseRayCastAnimParam) AimDir = RawAimDirection;

            //Calculate the side of the Origin according to the Aiming Position
            AimingSide = Vector3.Dot(AimDir, transform.right) < 0;

            Vector3 HorizontalDir = Vector3.ProjectOnPlane(AimDir, Vector3.up).normalized;
            Vector3 ForwardDir = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

            if (debug)
            {
                MDebug.DrawRay(OwnObjectCore.position, HorizontalDir * 2, Color.red);
                MDebug.DrawRay(OwnObjectCore.position, ForwardDir * 2, Color.blue);
            }
            HorizontalAngle_Raw = Vector3.SignedAngle(ForwardDir, HorizontalDir, Vector3.up);               //Get the Normalized value for the look direction
            VerticalAngle_Raw = (Vector3.Angle(transform.up, AimDir) - 90) * -1;                            //Get the Normalized value for the look direction


            HorizontalAngle_Raw += m_AimHorizontalOffset;

            if (HorizontalAngle_Raw < -180)
            {
                HorizontalAngle_Raw += 360;
            }
            else if (HorizontalAngle_Raw > 180)
            {
                HorizontalAngle_Raw -= 360;
            }

            HorizontalAngle = HorizontalAngle_Raw;

            VerticalAngle_Raw += m_AimVerticalOffset;
            VerticalAngle = VerticalAngle_Raw;
        }


        protected virtual void SmoothValues(float time)
        {
            float SmoothLerp = Mathf.Clamp01(time * m_Smoothness);
            var isRaw = m_Smoothness == 0 || time == 0;

            AimPoint = isRaw ? RawPoint : Vector3.Lerp(AimPoint, RawPoint, SmoothLerp);

            if (AimPosition != null) //Helper for the Aim Position
            {
                AimPosition.position = AimPoint;
                AimPosition.up = isRaw ? AimHit.normal : Vector3.Lerp(AimPosition.up, AimHit.normal, SmoothLerp);
            }
        }

        protected virtual void GetCenterScreen()
        {
            if (cam != null && Screen.width == m_cachedScreenWidth && Screen.height == m_cachedScreenHeight)
                return;

            m_cachedScreenWidth = Screen.width;
            m_cachedScreenHeight = Screen.height;

            Vector3 SC;

            if (cam != null)
            {
                SC = new Vector3(Screen.width * m_screenCenter.Value.x * cam.rect.width,
                    Screen.height * m_screenCenter.Value.y * cam.rect.height); //Gets the Center of the Aim Dot Transform

                //Add the Split Screen Offset
                SC += new Vector3(Screen.width * cam.rect.x, Screen.height * cam.rect.y);
            }
            else
            {
                SC = new Vector3(Screen.width * m_screenCenter.Value.x,
                    Screen.height * m_screenCenter.Value.y); //Gets the Center of the Aim Dot Transform No Cam
            }

            if (SC != ScreenCenter)
            {
                ScreenCenter = SC;
                OnScreenCenter.Invoke(ScreenCenter);
            }
        }

        public virtual RaycastHit DirectionFromCamera(bool useRay)
        {
            RawAimDirection = cam.transform.forward;
            RawAimDirectionNoRayCast = RawAimDirection; //Store the Raw Direction without RayCast

            Ray ray;

            if (ScreenCenter != Vector3.zero)
            {
                GetCenterScreen();
                ray = cam.ScreenPointToRay(ScreenCenter);
            }
            else
                ray = new Ray(cam.transform.position, cam.transform.forward);

            ray.origin += cam.transform.forward * ForwardCam; //Push the ray forward so it does not touch near colliders

            if (debug)
            {
                Debug.DrawRay(ray.origin, cam.transform.forward * MaxDistance, Color.gray);

            }

            var MaxPoint = ray.GetPoint(MaxDistance);

            var hit = new RaycastHit { distance = MaxDistance, point = MaxPoint };

            return CalculateRayCasting(UseRaycasting, ray, ref hit);
        }

        public virtual RaycastHit DirectionFromDirection(bool UseRaycasting)
        {
            RawAimDirection = AimOrigin.forward;
            RawAimDirectionNoRayCast = RawAimDirection; //Store the Raw Direction without RayCast


            Ray ray = new(AimOrigin.position, RawAimDirection);

            var hit = new RaycastHit()
            {
                distance = MaxDistance,
                point = ray.GetPoint(MaxDistance)
            };

            return CalculateRayCasting(UseRaycasting, ray, ref hit);
        }


        public virtual RaycastHit DirectionFromTarget(bool UseRaycasting)
        {
            if (AimTarget == null) return new RaycastHit();
            var TargetCenter = this.TargetCenter; //Cache the Center

            RawAimDirection = AimOrigin.DirectionTo(TargetCenter);
            RawAimDirectionNoRayCast = RawAimDirection; //Store the Raw Direction without RayCast

            Ray ray = new(AimOrigin.position, RawAimDirection);

            var hit = new RaycastHit()
            {
                distance = MaxDistance,
                point = TargetCenter,
            };

            return CalculateRayCasting(UseRaycasting, ray, ref hit);
        }

        public RaycastHit DirectionFromTarget(bool UseRaycasting, Transform target)
        {
            if (target == null) return new RaycastHit();

            var TargetCenter = target.position; //Cache the Center

            RawAimDirection = AimOrigin.DirectionTo(TargetCenter);

            Ray ray = new(AimOrigin.position, RawAimDirection);

            var hit = new RaycastHit()
            {
                distance = MaxDistance,
                point = TargetCenter,
            };

            return CalculateRayCasting(UseRaycasting, ray, ref hit);
        }
        protected virtual RaycastHit CalculateRayCasting(bool UseRaycasting, Ray ray, ref RaycastHit hit)
        {
            if (!UseRaycasting) return hit;

            // Resize array if needed (rare allocation)
            //if (ArrayHits.Length != RayHits)
            //{
            //    ArrayHits = new RaycastHit[RayHits];
            //}


            if (rayRadius > 0)
                hitCount = Physics.SphereCastNonAlloc(ray, rayRadius, ArrayHits, MaxDistance, Layer, m_Triggers);
            else
                hitCount = Physics.RaycastNonAlloc(ray, ArrayHits, MaxDistance, Layer, m_Triggers);

            if (hitCount > 0)
            {
                for (int i = 0; i < hitCount; i++)
                {
                    // Get reference to the hit without copying
                    ref RaycastHit rHit = ref ArrayHits[i];

                    if (rHit.collider == null) continue;                    //No valid hit
                    if (rHit.transform == null) break;                      //Means nothing was found
                    if (SkipConditions(rHit.transform)) continue;

                    if (hit.distance > rHit.distance) hit = rHit;
                }

                // Clear instead of allocating a new array every time at the beginning, 0 alloc this way.
                // Array.Clear(ArrayHits, 0, ArrayHits.Length);
            }

            return UseAimAssist ? GetAimAssist(hit) : hit;
        }

        protected virtual bool SkipConditions(Transform t)
        {
            if (t.SameHierarchy(IgnoreTransform)) return true;        //Don't Hit anything the Ignore
            if (t.SameHierarchy(OwnObjectCore)) return true;          //Don't Hit anything in the Object Core
            if (t.SameHierarchy(AimOrigin)) return true;              //Don't Hit anything in this hierarchy

            return false;
        }

        protected Collider LastCollider;


        // private IAimTarget IAimTargetAssist;

        /// <summary> Find if the Transform Hit with the RayCast is an AimAssist </summary>
        /// <param name="hit"></param>
        /// <returns></returns>
        protected virtual RaycastHit GetAimAssist(RaycastHit hit)
        {
#if UNITY_EDITOR
            hitName = hit.collider ? hit.collider.name : string.Empty; //For debugging purposes
#endif

            var hitCollider = hit.collider; //CustomPatch: cache the hit collider because Unity does an expensive call to get the collider object from an instance id


            if (LastCollider != hitCollider) //Only check for AimAssist when the collider is different
            {
                LastCollider = hitCollider;
                var Assist = LastCollider != null ? hitCollider.FindInterface<IAimTarget>() : null;

                IsTargetAssist = false;

                if (!Assist.IsUnityRefNull())
                {
                    if (Assist.AimAssist)
                    {
                        IsTargetAssist = true;
                        AimRayTargetAssist = Assist.AimPoint;
                        hit.point = Assist.AimPoint.position;

                    }


                    if (Assist != LastAimTarget)
                    {
                        if (!LastAimTarget.IsUnityRefNull())
                            LastAimTarget.IsBeenAimed(false, this); //Make sure is no longer being aimed

                        // AimTarget = Assist.AimPoint.parent; //Set the Aim Target to the Parent of the Aim Point

                        LastAimTarget = Assist;
                        LastAimTarget.IsBeenAimed(true, this);
                    }
                }
                else
                {
                    if (!LastAimTarget.IsUnityRefNull())
                        LastAimTarget.IsBeenAimed(false, this);

                    LastAimTarget = null;
                    AimRayTargetAssist = null;
                }
            }
            return hit;
        }

        public virtual void ClearAimAssist()
        {
            LastAimTarget = null;
            IsTargetAssist = false;
        }


        /// <summary>This is used to listen the Animator associated to this gameObject </summary>
        public virtual bool OnAnimatorBehaviourMessage(string message, object value) => this.InvokeWithParams(message, value);

        protected virtual void HeadAimOrigin()
        {
            var anim = transform.FindComponent<Animator>();

            if (anim)
            {
                if (anim.isHuman)
                {
                    var head = anim.GetBoneTransform(HumanBodyBones.Head);
                    if (head) AimOrigin = head;
                }
                else
                {
                    AimOrigin = anim.transform.FindGrandChild("Head");
                }
            }
            MTools.SetDirty(this);
        }

#if UNITY_EDITOR
        void Reset()
        {
            if (MainCamera == null)
            {
                cam = MTools.FindMainCamera();
                if (cam) MainCamera = cam.transform;
            }
            else
            {
                cam = MainCamera.GetComponent<Camera>();
            }

            AimOrigin = defaultOrigin = transform;
        }

        private void OnDrawGizmosSelected()
        {
            if (debug && enabled && !Application.isPlaying
#if UNITY_EDITOR
                &&
             UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this)  //Show Gizmos only when the Inspector is Open
#endif
                )
            {
                Gizmos.color = Color.green;
                if (AimOrigin != null)
                {

                    if (rayRadius.Value > 0)
                    {
                        MDebug.DrawCapsule(AimOrigin.position, AimOrigin.position + transform.forward * MaxDistance, rayRadius, Color.green);
                    }
                    else
                    {
                        Gizmos.DrawRay(AimOrigin.position, transform.forward * MaxDistance);
                    }
                }
            }
        }

        void OnDrawGizmos()
        {
            if (debug && enabled)
            {
                if (Application.isPlaying)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawRay(AimOrigin.position, AimDirection);

                    if (AimOrigin && !AimPoint.CloseToZero())
                    {
                        float radius = 0.05f;
                        Gizmos.color = Color.green;
                        Gizmos.DrawWireSphere(AimPoint, radius);
                        Gizmos.DrawSphere(AimPoint, radius);

                        if (rayRadius > 0)
                        {
                            MDebug.DrawCapsule(AimOrigin.position, AimPoint, rayRadius, Color.green);
                        }
                        else
                        {
                            Gizmos.color = Color.green;
                            Gizmos.DrawLine(AimOrigin.position, AimPoint);
                        }

                        Gizmos.color = Color.black;
                        Gizmos.DrawLine(AimOrigin.position, RawPoint);

                        if (UseRaycasting)
                        {
                            GUIStyle style = new(EditorStyles.label)
                            {
                                fontStyle = FontStyle.Bold,
                                fontSize = 10
                            };

                            style.normal.textColor = Color.green;

                            Handles.Label(AimPoint, hitName, style);
                        }
                    }
                }
            }
        }
#endif
    }
    #region Inspector


#if UNITY_EDITOR
    [CanEditMultipleObjects, CustomEditor(typeof(Aim), true)]
    public class AimEditor : Editor
    {
        Aim m;

        SerializedProperty m_active, m_aimOrigin, m_Smoothness, m_Animator, UseRayCastAnimParam,
            m_AimHorizontal, m_AimVertical, m_AimHorizontalOffset, m_AimVerticalOffset,
            m_aimLayer, m_Triggers, m_AimTarget, m_AimPosition,
            m_AimSide, debug, m_UpdateMode, OnAiming, m_cycles, OnHit, useAimAssist,
            m_Ignore, m_camera, m_UseCamera, m_forwardCam,
            m_screenCenter, rayRadius, RayHits, OnAimRayTarget, OnSetTarget, OnClearTarget,
            OnUsingTarget, OnScreenCenter, OnAimSide, EditorTab1, MaxDistance, m_UseRaycasting;
        protected virtual void OnEnable()
        {
            m_Animator = serializedObject.FindProperty("m_Animator");
            UseRayCastAnimParam = serializedObject.FindProperty("UseRayCastAnimParam");

            m_AimHorizontal = serializedObject.FindProperty("m_AimHorizontal");
            m_AimVertical = serializedObject.FindProperty("m_AimVertical");

            m_AimHorizontalOffset = serializedObject.FindProperty("m_AimHorizontalOffset");
            m_AimVerticalOffset = serializedObject.FindProperty("m_AimVerticalOffset");

            m = (Aim)target;

            m_active = serializedObject.FindProperty("m_active");
            OnHit = serializedObject.FindProperty("OnHit");
            m_Smoothness = serializedObject.FindProperty("m_Smoothness");
            m_aimOrigin = serializedObject.FindProperty("m_aimOrigin");

            m_aimLayer = serializedObject.FindProperty("m_aimLayer");
            m_Triggers = serializedObject.FindProperty("m_Triggers");
            m_AimTarget = serializedObject.FindProperty("m_AimTarget");
            m_AimPosition = serializedObject.FindProperty("m_AimPosition");
            m_Ignore = serializedObject.FindProperty("m_Ignore");

            m_camera = serializedObject.FindProperty("m_camera");
            m_UseCamera = serializedObject.FindProperty("m_UseCamera");

            m_forwardCam = serializedObject.FindProperty("m_forwardCam");

            m_screenCenter = serializedObject.FindProperty("m_screenCenter");
            m_AimSide = serializedObject.FindProperty("m_AimSide");
            rayRadius = serializedObject.FindProperty("rayRadius");
            MaxDistance = serializedObject.FindProperty("MaxDistance");
            RayHits = serializedObject.FindProperty("RayHits");
            EditorTab1 = serializedObject.FindProperty("EditorTab1");
            debug = serializedObject.FindProperty("debug");
            m_UpdateMode = serializedObject.FindProperty("updateMode");

            OnAimRayTarget = serializedObject.FindProperty("OnAimRayTarget");
            OnUsingTarget = serializedObject.FindProperty("OnUsingTarget");

            OnScreenCenter = serializedObject.FindProperty("OnScreenCenter");
            OnAimSide = serializedObject.FindProperty("OnAimSide");
            OnAiming = serializedObject.FindProperty("OnAiming");
            m_cycles = serializedObject.FindProperty("m_cycles");
            m_UseRaycasting = serializedObject.FindProperty("m_UseRaycasting");



            OnSetTarget = serializedObject.FindProperty("OnSetTarget");
            OnClearTarget = serializedObject.FindProperty("OnClearTarget");
            useAimAssist = serializedObject.FindProperty("useAimAssist");

        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MalbersEditor.DrawDescription("Aim Logic. Uses raycasting to find object when the character is aiming with the camera or towards a target");
            EditorTab1.intValue = GUILayout.Toolbar(EditorTab1.intValue, new string[] { "General", "References", "Events" });


            if (Application.isPlaying)
            {
                var desc = m.AimTarget != null ?
                    "Using a Target" : (m.UseCamera ?
                    "Using the Camera" :
                    "Using the Aim Origin Forward Direction");
                MalbersEditor.DrawDescription(desc);
            }

            //First Tabs
            int Selection = EditorTab1.intValue;

            if (Selection == 0) ShowGeneral();
            else if (Selection == 1) ShowReferences();
            else if (Selection == 2) ShowEvents();


            serializedObject.ApplyModifiedProperties();
        }



        private void ShowGeneral()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(m_active);
                    if (EditorGUI.EndChangeCheck())
                    {
                        m.enabled = m.Active;
                        EditorUtility.SetDirty(target);
                    }
                    MalbersEditor.DrawDebugIcon(debug);
                }

                EditorGUILayout.PropertyField(m_UpdateMode);
                EditorGUILayout.PropertyField(m_AimSide);
                EditorGUILayout.PropertyField(m_Smoothness);
                EditorGUILayout.PropertyField(m_aimOrigin);
                if (m_aimOrigin.objectReferenceValue == null)
                    EditorGUILayout.HelpBox("Please Select an Aim Origin Reference", MessageType.Error);
            }


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))

            // EditorGUILayout.LabelField("Ray Casting", EditorStyles.boldLabel);
            {
                EditorGUILayout.PropertyField(m_UseRaycasting);
                {
                    if (m.UseRaycasting)
                    {
                        EditorGUILayout.PropertyField(m_aimLayer, new GUIContent("Layer"));
                        EditorGUILayout.PropertyField(m_Triggers);
                        EditorGUILayout.PropertyField(MaxDistance);
                        EditorGUILayout.PropertyField(rayRadius);
                        EditorGUILayout.PropertyField(RayHits);
                        EditorGUILayout.PropertyField(m_cycles);
                        EditorGUILayout.PropertyField(useAimAssist);
                    }
                }
            }


            if (Application.isPlaying && debug.boolValue)
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUI.DisabledGroupScope(true))
                    {
                        EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
                        EditorGUILayout.LabelField("Camera Side: " + (m.AimingSide ? "Right" : "Left"));
                        EditorGUILayout.FloatField("Vertical Angle", (float)Math.Round(m.VerticalAngle, 2));
                        EditorGUILayout.FloatField("Vertical Raw", (float)Math.Round(m.VerticalAngle_Raw, 2));
                        EditorGUILayout.FloatField("Horizontal Angle", (float)Math.Round(m.HorizontalAngle, 2));
                        EditorGUILayout.FloatField("Horizontal Raw", (float)Math.Round(m.HorizontalAngle_Raw, 2));
                        EditorGUILayout.ObjectField("Hit", m.AimHit.transform, typeof(Transform), false);
                        EditorGUILayout.Toggle("Aim Assist", m.IsTargetAssist);
                    }
                }
            }
        }

        private void ShowReferences()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (m_AimTarget.isExpanded = MalbersEditor.Foldout(m_AimTarget.isExpanded, "Target"))
                {
                    EditorGUILayout.PropertyField(m_AimTarget, new GUIContent("Target"));
                }
            }


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (m_camera.isExpanded = MalbersEditor.Foldout(m_camera.isExpanded, "Camera"))
                {
                    EditorGUILayout.PropertyField(m_UseCamera);

                    if (m.m_UseCamera.Value)
                    {
                        EditorGUILayout.PropertyField(m_camera);
                        EditorGUILayout.PropertyField(m_forwardCam);
                        EditorGUILayout.PropertyField(m_screenCenter);
                    }
                }
            }


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (m_Animator.isExpanded = MalbersEditor.Foldout(m_Animator.isExpanded, "Animator"))
                {
                    EditorGUILayout.PropertyField(m_Animator);
                    EditorGUILayout.PropertyField(UseRayCastAnimParam);

                    MalbersEditor.DisplayParam(m.m_Animator, m_AimHorizontal, AnimatorControllerParameterType.Float);
                    MalbersEditor.DisplayParam(m.m_Animator, m_AimVertical, AnimatorControllerParameterType.Float);

                    EditorGUILayout.PropertyField(m_AimHorizontalOffset);
                    EditorGUILayout.PropertyField(m_AimVerticalOffset);
                }
            }


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (m_Ignore.isExpanded = MalbersEditor.Foldout(m_Ignore.isExpanded, "Extras"))
                {
                    EditorGUILayout.LabelField("Extras", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(m_Ignore);
                    EditorGUILayout.PropertyField(m_AimPosition);
                }
            }
        }

        private void ShowEvents()
        {
            EditorGUILayout.PropertyField(OnAiming);
            EditorGUILayout.PropertyField(OnHit);
            EditorGUILayout.PropertyField(OnAimRayTarget);
            EditorGUILayout.PropertyField(OnUsingTarget);
            EditorGUILayout.PropertyField(OnClearTarget);
            EditorGUILayout.PropertyField(OnSetTarget);
            EditorGUILayout.PropertyField(OnScreenCenter);
            EditorGUILayout.PropertyField(OnAimSide);
        }
    }
#endif
    #endregion
}
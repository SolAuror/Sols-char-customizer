// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEngine;
using UnityEngine.Pool;
using System.Collections.Generic;
using System.Linq;
using MalbersAnimations.Weapons;
using MalbersAnimations.Events;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace MalbersAnimations.Items
{
    /// <summary>
    /// Store here all the Ammo for your weapons 
    /// Connect this to any inventory and the Item Manager/Weapon Manager
    /// </summary>
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/super-item/super-item-system/pouch-manager")]
    [AddComponentMenu("Malbers/Pouch Manager")]

    public class PouchManager : MonoBehaviour
    {
        public List<Ammo> ammo = new();

        /// <summary>Runtime lookup of the ammo entries by Projectile ID</summary>
        public Dictionary<ProjectileID, Ammo> D_Ammo { get; set; } = new();

        public bool invokeTotalOnEnable = false;

        //MWC: Ammo row selected in the inspector. It was declared inside the [Ammo] class, so
        //FindProperty("selectedAmmoIndex") on the PouchManager returned null and the editor threw on the first repaint.
        //It belongs to the component, since the selection is a property of the LIST, not of one entry.
#pragma warning disable CS0414 //Only ever read through a SerializedProperty in the custom editor
        [SerializeField, HideInInspector] private int selectedAmmoIndex = -1;
#pragma warning restore CS0414

        //MWC: Master Infinite Ammo switch. Overrides every Ammo entry (God Mode / Debug). Individual entries can also be
        //set to Infinite on their own. Infinite affects the RESERVE only: a Chamber/Magazine still empties and reloads.
        [Tooltip("Infinite Ammo for EVERY projectile in the Pouch. The reserve is never consumed.\n" +
            "Individual Ammo entries can also be set to Infinite on their own.\n" +
            "This affects the reserve only: a weapon using a Chamber still consumes and reloads its magazine")]
        public BoolReference InfiniteAmmo = new(false);

        public GameObject PouchPool;

        private void Awake()
        {
            PouchPool = new GameObject("Pouch Pool");
            PouchPool.transform.parent = transform;
            PouchPool.transform.ResetLocal();

            ammo.ForEach(a => a.Initialize(this));

            //Convert the Ammo List to a Dictionary for faster access
            D_Ammo = new();
            //MWC: Guard against duplicate ProjectileIDs so Awake no longer throws an ArgumentException; keep the first entry and warn.
            ammo.ForEach(a =>
            {
                if (a.ID == null) return;
                if (!D_Ammo.TryAdd(a.ID, a))
                    Debug.LogWarning($"[PouchManager] Duplicate Ammo ID '{a.ID.name}' found. Only the first entry will be used.", this);
            });

        }

        private void OnEnable()
        {
            if (invokeTotalOnEnable)
            {
                foreach (var ammo in D_Ammo)
                {
                    ammo.Value.OnAmmoChanged.Invoke(ammo.Value.Total); //Invoke the First time to update the UI
                }
            }
        }

        public virtual MProjectile GetProjectileObject(ProjectileID id)
        {
            if (D_Ammo.TryGetValue(id, out var p))
                return p.projectile;

            //do the pooling right here

            return null;
        }

        public virtual void Ammo_Reduce(ProjectileID id, int amount)
        {
            if (D_Ammo.TryGetValue(id, out var p)) p.ReduceAmmo(amount);
        }

        /// <summary>  Check if the Ammo has the required amount of projectiles to be fired </summary>
        public virtual bool HasAmmo(ProjectileID id, int amount, ComparerNumber compare)
        {
            if (D_Ammo.TryGetValue(id, out var p))
                //MWC: Infinite reserves compare as int.MaxValue, so every Comparer stays meaningful
                //(Greater than X → true, Less than X → false) instead of blindly returning true.
                return p.EffectiveTotal.MCompare(amount, compare);

            return false;
        }

        public virtual bool HasAmmo(ProjectileID id) => HasAmmo(id, 0, ComparerNumber.Greater);

        /// <summary> Turn the master Infinite Ammo switch on/off at runtime (UnityEvent friendly) </summary>
        public virtual void Infinite_All(bool value)
        {
            InfiniteAmmo.Value = value;

            //Refresh any UI bound to the ammo counters, the displayed reserve does not change but its meaning does
            foreach (var a in D_Ammo.Values) a.OnAmmoChanged.Invoke(a.Total);
        }

        /// <summary> Turn Infinite Ammo on/off for a single projectile reserve </summary>
        public virtual void Ammo_SetInfinite(ProjectileID id, bool value)
        {
            if (id == null) return;

            if (D_Ammo.TryGetValue(id, out var p))
            {
                p.Infinite.Value = value;
                p.OnAmmoChanged.Invoke(p.Total); //Refresh bound UI, the number is the same but its meaning changed
            }
            else
            {
                Debug.LogWarning($"[PouchManager] There's no Ammo entry for [{id.name}]. Cannot set Infinite Ammo.", this);
            }
        }

        /// <summary> True when that projectile's reserve is Infinite (by its own flag or by the Pouch master switch) </summary>
        public virtual bool IsInfinite(ProjectileID id)
            => InfiniteAmmo.Value || (id != null && D_Ammo.TryGetValue(id, out var p) && p.IsInfinite);

        public virtual void Ammo_Add(ProjectileID id, int amount)
        {
            if (D_Ammo.TryGetValue(id, out var p))
            {
                //MWC: was [p.total.Value += amount], which skipped OnAmmoChanged/OnAmmoAddedOrRemoved,
                //so ammo pickups never refreshed the UI. Route through AddAmmo so the events fire.
                p.AddAmmo(amount);
            }
            else
            {
                //add new ammo to the pouch if the ID is not found,
                //this is useful for when you want to add ammo to the pouch from an inventory or from a pickup
                var newAmmo = new Ammo() { ID = id, total = new IntReference(amount) };
                newAmmo.Initialize(this);
                ammo.Add(newAmmo);
                D_Ammo.Add(id, newAmmo);
            }
        }

        /// <summary>Add 1 Ammo to the Pouch </summary>
        /// <param name="id">id of the projectile</param>

        public virtual void Ammo_Add(ProjectileID id) => Ammo_Add(id, 1);

        public Ammo GetProjectile(ProjectileID iD)
        {
            if (iD == null) return null;

            if (D_Ammo.TryGetValue(iD, out var ammo)) return ammo;

            return null;
        }


        public void InvokeAmmoChanged(ProjectileID id)
        {
            if (D_Ammo.TryGetValue(id, out var ammo))
            {
                ammo.OnAmmoChanged.Invoke(ammo.Total);
            }
        }

        public void InvokeAmmoAddOrRemoved(ProjectileID id)
        {
            if (D_Ammo.TryGetValue(id, out var ammo))
            {
                ammo.OnAmmoAddedOrRemoved.Invoke(ammo.Total);
            }
        }

        public int Ammo_Total(ProjectileID id)
        {
            if (id == null) return 0;
            if (D_Ammo != null && D_Ammo.TryGetValue(id, out var ammo))
            {
                return ammo.Total;
            }

            return 0;
        }


        private void Reset()
        {
            ammo = new List<Ammo> { new() { total = new IntReference(10), ID = MTools.GetInstance<ProjectileID>("Arrow"), } };
        }
    }

    [System.Serializable]
    public class Ammo
    {
        [Tooltip("ID value to Identify the Projectile")]
        public ProjectileID ID;
        [Tooltip("Prefab that will be instantiated when firing the projectile")]
        public MProjectile projectile;
        [Tooltip("Total Amount of projectiles")]
        public IntReference total = new(10);

        //MWC: Infinite reserve for THIS projectile. The Total never decreases and [Has Ammo] is always satisfied.
        //A weapon using a Chamber still consumes its magazine and still has to Reload — the reload just never runs dry.
        [Tooltip("Infinite reserve for this projectile. The Total is never consumed.\n" +
            "A weapon using a Chamber still empties its magazine and still needs to Reload")]
        public BoolReference Infinite = new(false);


        [Tooltip("Initial number of Projectiles in Projectile Pool")]
        public IntReference m_ProjectileInitialPoolSize = new(10); // Initial size of pool
                                                                   // [TabGroup("Pooling")]
        [Tooltip("Max number of Projectiles in Projectile Pool")]
        public IntReference m_ProjectileMaxPoolSize = new(50); // Max size of pool
                                                               //  [TabGroup("Pooling")]
        [Tooltip("Start the Pool with the Initial Pool Size (GameObjects Instantiated and disabled)")]
        public BoolReference StartPoolSize = new(false);

        /// <summary>  Check if the Ammo is valid  </summary>
        public bool Valid => ID != null && projectile != null;

        /// <summary> Pool - Added IObjectPool<GameObject> thisProjectilePool new parameter for Projectile_Prepare / Prepare </summary>  
        public IObjectPool<GameObject> Pool;
        /// <summary>  Pool - End </summary>  

        private PouchManager manager;

        /// <summary>
        /// Parent GameObject for the Projectile Pool, this is used to keep the hierarchy organized and to avoid cluttering the scene with too many GameObjects.
        /// It will be created as a child of the Pouch Manager and will be named after the Projectile ID.
        /// </summary>
        public GameObject AmmoPool;

        public IntEvent OnAmmoChanged = new();
        public IntEvent OnAmmoAddedOrRemoved = new();


        public int Total { get => total.Value; set => total.Value = value; }

        /// <summary> True when this reserve is Infinite, by its own flag or by the Pouch master switch </summary>
        public bool IsInfinite => Infinite.Value || (manager != null && manager.InfiniteAmmo.Value);

        /// <summary> Total used for comparisons: int.MaxValue while Infinite, so every check reads as "plenty left" </summary>
        public int EffectiveTotal => IsInfinite ? int.MaxValue : total.Value;

        /// <summary> Get a Projectile from the Pool and consume one unit of the reserve. Returns null when the reserve cannot pay for it </summary>
        public GameObject Get() => Get(1);

        /// <summary> Get a Projectile from the Pool and consume [cost] units of the reserve. Returns null when the reserve cannot pay for it </summary>
        //MWC: [cost] overload — Get() hard-coded a cost of 1, which tied "how many projectiles are spawned" to
        //"how much ammo is spent". A burst that costs a single round has to spend once and then pull N pooled objects,
        //so callers firing more than one projectile should pay with ReduceAmmo(cost) and use GetPooled() per projectile.
        public GameObject Get(int cost)
        {
            //MWC: was firing anyway when the reserve was empty — ReduceAmmo refused and only logged, but the pooled
            //projectile was still handed out, so an ungated Action shot forever at 0 ammo. Now the shot is denied.
            if (Pool != null && cost > 0 && !ReduceAmmo(cost)) return null;

            return GetPooled();
        }

        //MWC — Get a pooled projectile WITHOUT reducing the reserve. Used by the Chamber fire path, where the reserve
        //was already consumed at reload time (magazine model); reducing again here would double-count the ammo.
        public GameObject GetPooled()
        {
            if (Pool != null) return Pool.Get();

            //MWC: the pool is only null when the entry is invalid (no ID / no prefab), so there is nothing to instantiate either
            if (projectile == null)
            {
                Debug.LogWarning($"Ammo [{(ID != null ? ID.name : "<No ID>")}] has no Projectile prefab assigned. Cannot spawn a projectile.", manager);
                return null;
            }

            Debug.LogWarning($"Projectile Pool for {projectile.name} is not initialized. Returning a new instance of the projectile prefab.", manager);
            return GameObject.Instantiate(projectile.gameObject);
        }

        public GameObject Release(GameObject go)
        {
            if (Pool != null)
            {
                Pool.Release(go);
                return go;
            }
            else
            {
                Debug.LogWarning($"Projectile Pool for {ID.name} is not initialized. Destroying the returned projectile instance.", manager);
                GameObject.Destroy(go);
                return null;
            }
        }


        /// <summary> Consume one unit of the reserve. Returns false when there is not enough ammo </summary>
        public bool ReduceAmmo() => ReduceAmmo(1); //MWC — returns bool now, so callers can deny the shot

        public void AddAmmo() => AddAmmo(1);

        public void AddAmmo(int amount)
        {
            if (amount <= 0) return;
            this.total.Value += amount;
            OnAmmoChanged.Invoke(this.total.Value);
            OnAmmoAddedOrRemoved.Invoke(amount);
        }

        /// <summary> Consume [amount] units of the reserve. Returns false when there is not enough ammo (nothing is consumed) </summary>
        public bool ReduceAmmo(int amount) //MWC — was void; the return value is what lets Get() deny an empty shot
        {
            if (amount <= 0) return false;

            //MWC: Infinite reserve — the Total is frozen, but OnAmmoChanged still fires with that frozen value so any
            //UI bound to it keeps updating. OnAmmoAddedOrRemoved reports a DELTA, and the delta here is zero, so it is skipped.
            if (IsInfinite)
            {
                OnAmmoChanged.Invoke(this.total.Value);
                return true;
            }

            if (this.total.Value >= amount)
            {
                this.total.Value -= amount;
                OnAmmoChanged.Invoke(this.total.Value);
                OnAmmoAddedOrRemoved.Invoke(-amount);
                return true;
            }

            Debug.LogWarning($"Not enough ammo to reduce. Current ammo: {this.total.Value}, attempted reduction: {amount}", manager);
            return false;
        }



        public void Initialize(PouchManager owner)
        {
            manager = owner;

            //MWC: an unassigned row used to NRE right here on [ID.name] before the Awake duplicate-guard could see it,
            //and a missing prefab produced a pool that threw on the first Instantiate. Both are now reported and skipped.
            if (!Valid)
            {
                Debug.LogWarning($"[PouchManager] Ammo entry is incomplete " +
                    $"(ID: {(ID != null ? ID.name : "<Missing>")}, Projectile: {(projectile != null ? projectile.name : "<Missing>")}). Skipping its Pool.", owner);
                return;
            }

            AmmoPool = new GameObject($"{ID.name} Pool");
            AmmoPool.transform.parent = manager.PouchPool.transform;

            Pool = new ObjectPool<GameObject>
                       (CreatePooledItem, OnTakeFromPool, OnReturnedToPool, OnDestroyPoolObject, false,
                       m_ProjectileInitialPoolSize.Value, m_ProjectileMaxPoolSize.Value);

            if (StartPoolSize.Value) PopulatePool();
        }

        protected virtual void PopulatePool()
        {
            if (m_ProjectileInitialPoolSize > 0)
            {
                for (int i = 0; i < m_ProjectileInitialPoolSize; i++)
                {
                    Pool.Release(CreatePooledItem());
                }
            }
        }

        private int created;

        private GameObject CreatePooledItem()
        {
            // This is used to return Projectile to the pool when they have stopped.
            // Create instance of Projectile
            GameObject projectileInstance = GameObject.Instantiate(projectile.gameObject, AmmoPool.transform);
            projectileInstance.SetActive(false); // Disable it
            projectileInstance.name = projectileInstance.name.Replace("(Clone)", $"(Pool - {created} )"); // Remove (Clone) from the name

            projectileInstance.transform.parent = AmmoPool.transform; // Set the parent to the Weapon or Pouch Manager
            projectileInstance.transform.ResetLocal(); // Reset the local position, rotation and scale

            created++;
            // Return prefab to be added to Pool
            return projectileInstance;
        }


        private void OnReturnedToPool(GameObject Go)
        {
            Go.transform.SetParent(AmmoPool.transform, false); // or to Weapon
            Go.transform.ResetLocal();
            Go.SetActive(false); // Disable
        }

        private void OnTakeFromPool(GameObject Go)
        {
            Go.transform.SetParent(null, true); // Unparent projectile
            Go.SetActive(true); // Enable 

            // Go.transform.position = AimOriginPos; // Set position to AimOrigin
        }



        /// <summary>
        ///   If the pool capacity is reached then any items returned will be destroyed. We can control what the destroy behavior does, here we destroy the GameObject.
        /// </summary>
        /// <param name="Go"></param>
        private void OnDestroyPoolObject(GameObject Go)
        {
            if (Go) Object.Destroy(Go); // When pool size excides max size destroy projectile
        }
    }


#if UNITY_EDITOR

    [CustomEditor(typeof(PouchManager))]
    public class PouchManagerEditor : Editor
    {
        private SerializedProperty ammoList, invokeTotalOnEnable, InfiniteAmmo, selectedAmmoIndex; //MWC — master Infinite Ammo switch
        private ReorderableList reorderableAmmoList;
        private PouchManager M;

        protected virtual void OnEnable()
        {
            M = (PouchManager)target;
            ammoList = serializedObject.FindProperty("ammo");
            invokeTotalOnEnable = serializedObject.FindProperty("invokeTotalOnEnable");
            InfiniteAmmo = serializedObject.FindProperty("InfiniteAmmo");
            selectedAmmoIndex = serializedObject.FindProperty("selectedAmmoIndex");

            reorderableAmmoList = new ReorderableList(serializedObject, ammoList, true, true, true, true)
            {
                drawHeaderCallback = (Rect rect) =>
                {
                    //MWC: the header columns now use the SAME thirds geometry as the element rows. [Total] used to be
                    //placed at [rect.width - 20], which ignored rect.x entirely and pushed the label off its column.
                    float thirdWidth = rect.width / 3f;
                    float padding = 5f;

                    Rect idRect = new(rect.x + 35, rect.y, thirdWidth - padding - 35, EditorGUIUtility.singleLineHeight);
                    Rect projectileRect = new(rect.x + thirdWidth, rect.y, thirdWidth * 1.25f - padding, EditorGUIUtility.singleLineHeight);
                    Rect amountRect = new(rect.x + 15 + ((thirdWidth * 1.25f) + thirdWidth), rect.y, thirdWidth * 0.75f - padding - 10, EditorGUIUtility.singleLineHeight);

                    EditorGUI.LabelField(idRect, "ID");
                    EditorGUI.LabelField(projectileRect, "Projectile");
                    EditorGUI.LabelField(amountRect, "Total");
                },
                drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
                {
                    rect.y += 2; // Add some padding to the top of the element

                    var oldGui = GUI.contentColor;
                    //highlight moved to the TOP of the callback. It was drawn after the ID field, so the tint
                    //covered the ID popup but not the Projectile/Total columns. Drawing first puts it behind every field.
                    if (selectedAmmoIndex.intValue == index)
                    {
                        var hightligthColor = new Color(0.24f, 0.49f, 0.90f, 0.8f);
                        var xOffset = 20;
                        var selectRect = new Rect(rect.x - xOffset, rect.y - 2, rect.width + xOffset + 5, rect.height);
                        EditorGUI.DrawRect(selectRect, hightligthColor);

                        GUI.contentColor = Color.green + Color.white * 0.5f;
                    }
                    var element = ammoList.GetArrayElementAtIndex(index);
                    var id = element.FindPropertyRelative("ID");
                    var projectile = element.FindPropertyRelative("projectile");
                    var total = element.FindPropertyRelative("total");

                    float thirdWidth = rect.width / 3f;
                    float padding = 5f;

                    Rect idRect = new(rect.x + 15, rect.y, thirdWidth * 1f - padding - 15, EditorGUIUtility.singleLineHeight);
                    Rect projectileRect = new(rect.x + thirdWidth, rect.y, thirdWidth * 1.25f - padding, EditorGUIUtility.singleLineHeight);
                    Rect amountRect = new(rect.x + 15 + ((thirdWidth * 1.25f) + thirdWidth), rect.y, thirdWidth * 0.75f - padding - 10, EditorGUIUtility.singleLineHeight);

                    EditorGUI.PropertyField(idRect, id, GUIContent.none);

                    //MWC: an Infinite reserve shows the infinity glyph instead of a Total that will never change.
                    //Read from the target (not the SerializedProperty) so Var/Member reference modes resolve correctly.
                    //(M.InfiniteAmmo is checked explicitly: Ammo.IsInfinite reads it through [manager], which is only set at runtime)
                    if (M.InfiniteAmmo.Value || (index < M.ammo.Count && M.ammo[index] != null && M.ammo[index].Infinite.Value))
                    {
                        EditorGUI.LabelField(amountRect, new GUIContent("  ∞", "Infinite Ammo. The reserve is never consumed"), EditorStyles.boldLabel);
                    }
                    else
                    {
                        EditorGUI.PropertyField(amountRect, total, GUIContent.none);
                    }

                    EditorGUI.PropertyField(projectileRect, projectile, GUIContent.none);

                    GUI.contentColor = oldGui;
                },
                onSelectCallback = (ReorderableList list) =>
                {
                    selectedAmmoIndex.intValue = list.index;
                }
            };

            //MWC: restore the stored selection AFTER the list exists — this ran before the constructor, on a null list.
            //Clamped, because the stored index can point past the end when entries were removed since it was saved.
            reorderableAmmoList.index = Mathf.Clamp(selectedAmmoIndex.intValue, -1, ammoList.arraySize - 1);
        }

        private int tabIndex = 0;

        private readonly string[] Tab1 = new string[] { "General", "Events" };


        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Pouch Manager");

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                //MWC: master Infinite Ammo switch, drawn above the list so it reads as a Pouch-wide override
                EditorGUILayout.PropertyField(InfiniteAmmo, new GUIContent("Infinite Ammo (All)"));
                EditorGUILayout.PropertyField(invokeTotalOnEnable, new GUIContent("Invoke Total on Enable"));
            }

            if (M.InfiniteAmmo.Value)
                EditorGUILayout.HelpBox("Infinite Ammo is ON for every projectile. Reserves are never consumed. " +
                    "Weapons using a Chamber still empty their magazine and still need to Reload.", MessageType.Info);

            reorderableAmmoList.DoLayoutList();

            //MWC: Warn when the same ProjectileID is used by more than one Ammo entry (causes a duplicate-key crash on Awake).
            DrawDuplicateIDWarning();

            var index = reorderableAmmoList.index;

            if (index >= 0 && index < reorderableAmmoList.count && M.ammo.Count > index)
            {
                var element = ammoList.GetArrayElementAtIndex(index);

                var ammo = M.ammo[index];

                EditorGUI.indentLevel++;
                ammoList.isExpanded = MalbersEditor.Foldout_Bold(ammoList.isExpanded, $"Ammo [{(ammo.ID != null ? ammo.ID.name : "<Missing>")}]", MTools.MBlue, true);
                EditorGUI.indentLevel--;

                if (ammoList.isExpanded)
                {
                    tabIndex = GUILayout.Toolbar(tabIndex, Tab1);

                    if (tabIndex == 0) DrawAmmoSettings(element, index);
                    else if (tabIndex == 1) DrawAmmoEvents(element);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        //MWC: Detect duplicate ProjectileIDs in the Ammo list and show a HelpBox listing them.
        private void DrawDuplicateIDWarning()
        {
            if (M.ammo == null || M.ammo.Count < 2) return;

            var seen = new HashSet<ProjectileID>();
            var duplicates = new HashSet<ProjectileID>();

            foreach (var a in M.ammo)
            {
                if (a == null || a.ID == null) continue;
                if (!seen.Add(a.ID)) duplicates.Add(a.ID);
            }

            if (duplicates.Count > 0)
            {
                var names = string.Join(", ", duplicates.Select(d => d.name));
                EditorGUILayout.HelpBox(
                    $"Duplicate Ammo ID(s) found: {names}.\nEach Projectile ID must be unique. Only the first entry will be used at runtime.",
                    MessageType.Warning);
            }
        }

        private void DrawAmmoEvents(SerializedProperty element)
        {
            var OnAmmoChanged = element.FindPropertyRelative("OnAmmoChanged");
            var OnAmmoAddedOrRemoved = element.FindPropertyRelative("OnAmmoAddedOrRemoved");
            EditorGUILayout.PropertyField(OnAmmoChanged);
            EditorGUILayout.PropertyField(OnAmmoAddedOrRemoved);
        }

        private void DrawAmmoSettings(SerializedProperty element, int index)
        {
            var m_ProjectileInitialPoolSize = element.FindPropertyRelative("m_ProjectileInitialPoolSize");
            var Total = element.FindPropertyRelative("total");
            var Infinite = element.FindPropertyRelative("Infinite");
            var m_ProjectileMaxPoolSize = element.FindPropertyRelative("m_ProjectileMaxPoolSize");
            var StartPoolSize = element.FindPropertyRelative("StartPoolSize");

            EditorGUILayout.PropertyField(Infinite); //MWC — per projectile Infinite reserve
            if (!M.ammo[index].Infinite)
            {
                EditorGUILayout.PropertyField(Total);
            }
            EditorGUILayout.PropertyField(m_ProjectileInitialPoolSize);
            EditorGUILayout.PropertyField(m_ProjectileMaxPoolSize);
            EditorGUILayout.PropertyField(StartPoolSize);
        }
    }
#endif
}

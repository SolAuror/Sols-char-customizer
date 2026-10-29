// Last Change: 9/21/2026 9:40am
using System.Collections.Generic;
using System.Globalization; //MWC — invariant formatting when migrating legacy typed keys
using UnityEngine;
using UnityEngine.Events; //MWC — load/save notifications for UI

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Scriptables
{
    [AddComponentMenu("Malbers/Utilities/Managers/Scriptable Var to Player Pref (Save|Load)")] //MWC — fixed "Scritable" typo
    public class ScriptableVarToPlayerPref : MonoBehaviour
    {
        /// <summary>Current storage format; bumped whenever the key encoding changes so old saves can be migrated.</summary>
        public const int PrefsFormatVersion = 1;

        /// <summary>Sentinel written by versions that stored the first-run flag under a global, un-namespaced key.</summary>
        private const string LegacyInitializedKey = "GameInitalized";

        [Tooltip("Set of Scriptable variables you want to save on Player Pref")]
        public ScriptableVar[] userPreferences;

        [CreateScriptableAsset]
        [Tooltip("Restore the Array of Variables to their default Options")]
        public ResetScriptableVarsAsset defaultUserOptions;

        [Tooltip("MWC — Namespaces every key written by this manager, e.g. \"Audio\" stores \"Audio.MasterVolume\".\n" +
                 "Leave it empty to keep using the plain variable names (the format older versions wrote).")]
        public string keyPrefix = string.Empty;

        [Tooltip("MWC — Load in Awake instead of Start, so anything subscribing to OnValueChanged in its own Start sees the saved values")]
        public bool LoadOnAwake = true;

        [Tooltip("All values will be save to <PlayerPref> On Disable")]
        public bool SaveOnExit = true;

        [Tooltip("MWC — Also save when the application is paused. This is the only reliable save point on Android and iOS")]
        public bool SaveOnPause = true;

        [Tooltip("MWC — Survive scene loads. Duplicates created by reloading a scene destroy themselves")]
        public bool dontDestroyOnLoad = true;

        [Tooltip("MWC — Move this object to the root of the hierarchy on Awake (required for DontDestroyOnLoad)")]
        public bool detachFromParent = true;

        public bool debug = false; //MWC — was true, which logged one line per variable on every boot in shipped builds

        /// <summary>Invoked after the saved values have been pushed into the variables.</summary>
        public UnityEvent OnPreferencesLoaded = new();

        /// <summary>Invoked after the variables have been written to PlayerPrefs.</summary>
        public UnityEvent OnPreferencesSaved = new();

        /// <summary>The manager that owns the preferences; later duplicates destroy themselves.</summary>
        public static ScriptableVarToPlayerPref Main { get; private set; }

        /// <summary>Frame of the last pause-driven save, so OnDisable does not immediately flush to disk again.</summary>
        private int savedOnPauseFrame = -1;

        #region Keys

        /// <summary>PlayerPrefs key for a variable: its asset name, namespaced by [keyPrefix] when one is set.</summary>
        public string Key(ScriptableVar variable) => Key(variable.name);

        /// <summary>Applies [keyPrefix] to a raw key suffix.</summary>
        public string Key(string suffix) => string.IsNullOrEmpty(keyPrefix) ? suffix : $"{keyPrefix}.{suffix}";

        /// <summary>Key holding the first-run flag for this manager.</summary>
        private string InitializedKey => Key("Initialized");

        /// <summary>Key holding the storage format the saved values were written with.</summary>
        private string VersionKey => Key("PrefsFormatVersion");

        /// <summary>True once this manager has written its preferences at least once.</summary>
        public bool IsInitialized =>
            PlayerPrefs.GetInt(InitializedKey, 0) == 1 ||
            //MWC — projects upgrading from an older version only have the global sentinel, and only when unprefixed
            (string.IsNullOrEmpty(keyPrefix) && PlayerPrefs.GetInt(LegacyInitializedKey, 0) == 1);

        /// <summary>Format the stored keys were written with; 0 means the pre-migration typed layout.</summary>
        private int SavedFormatVersion => PlayerPrefs.GetInt(VersionKey, 0);

        #endregion

        #region Unity Callbacks

        private void Awake()
        {
            //MWC — reloading a scene used to leave a second persistent manager fighting over the same keys
            if (Main != null && Main != this)
            {
                Log($"A preferences manager already exists [{Main.name}]. Destroying this duplicate");
                Destroy(gameObject);
                return;
            }

            Main = this;

            //MWC — both were unconditional and hidden; DontDestroyOnLoad also needs the GameObject, not the Transform
            if (detachFromParent && transform.parent != null) transform.parent = null;
            if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);

            if (LoadOnAwake) Initialize();
        }

        private void Start()
        {
            //MWC — keeps the original Start-time timing available for projects that need it
            if (!LoadOnAwake && Main == this) Initialize();
        }

        /// <summary>On mobile the app is often killed without OnApplicationQuit; pausing is the reliable save point.</summary>
        private void OnApplicationPause(bool paused)
        {
            if (!paused || !SaveOnPause) return;

            SaveUserPreferences();
            savedOnPauseFrame = Time.frameCount;
        }

        private void OnDisable()
        {
            //MWC — skip when a pause save already flushed to disk this frame
            if (SaveOnExit && savedOnPauseFrame != Time.frameCount) SaveUserPreferences();
        }

        private void OnDestroy()
        {
            if (Main == this) Main = null; //MWC — clear the static so a reloaded scene can take ownership
        }

        #endregion

        /// <summary>Applies the authored defaults on the very first run, and the player's saved values afterwards.</summary>
        public virtual void Initialize()
        {
            if (IsInitialized)
            {
                GetUserPreferences();
                return;
            }

            if (defaultUserOptions != null) defaultUserOptions.Restart();

            SaveUserPreferences(); //MWC — also stamps the format version and the first-run flag
        }

        /// <summary>Restore all the variables to their Default Values</summary>
        public virtual void RestoreToDefault()
        {
            if (defaultUserOptions == null)
            {
                Debug.LogWarning($"[{name}] has no [Default User Options] asset assigned. There is nothing to restore.", this);
                return;
            }

            defaultUserOptions.Restart();
            SaveUserPreferences(); //MWC — keep PlayerPrefs in sync instead of leaving stale values until the next quit
        }

        /// <summary>  uses Scriptable Varible name as reference  </summary>
        public virtual void GetUserPreferences()
        {
            if (userPreferences == null) return;

            //MWC — anything written before the uniform string format needs the old typed getters to be readable
            var isLegacyFormat = SavedFormatVersion < PrefsFormatVersion;
            var migratedAny = false;

            foreach (var preference in userPreferences)
            {
                if (!CanUse(preference)) continue;

                var key = Key(preference);

                //MWC — no key means the player never saved this one; keep the authored value instead of zeroing it
                if (!PlayerPrefs.HasKey(key)) continue;

                var data = isLegacyFormat ? ReadLegacyValue(preference, key) : PlayerPrefs.GetString(key, string.Empty);

                if (!preference.SetSerializedValue(data))
                {
                    Debug.LogWarning($"[{name}] could not read the saved value of [{preference.name}] (key \"{key}\", data \"{data}\"). Keeping its current value.", this);
                    continue;
                }

                migratedAny |= isLegacyFormat;
                Log($"Load [{key}] -> [{data}]");
            }

            //MWC — rewrite in the current format so the legacy branch is only ever taken once
            if (migratedAny) SaveUserPreferences();

            OnPreferencesLoaded.Invoke();
        }

        /// <summary> Stores to PlayerPrefs using Scriptable Varible name as reference  </summary>
        public virtual void SaveUserPreferences()
        {
            if (userPreferences != null)
            {
                foreach (var preference in userPreferences)
                {
                    if (!CanUse(preference)) continue;

                    var key = Key(preference);
                    var data = preference.GetSerializedValue();

                    PlayerPrefs.SetString(key, data); //MWC — one uniform encoding for every type
                    Log($"Save [{key}] -> [{data}]");
                }
            }

            PlayerPrefs.SetInt(VersionKey, PrefsFormatVersion);
            PlayerPrefs.SetInt(InitializedKey, 1);
            PlayerPrefs.Save();

            OnPreferencesSaved.Invoke();
        }

        /// <summary>Deletes only the keys this manager owns. Unrelated PlayerPrefs (save slots, other systems) are left alone.</summary>
        public virtual void DeleteAllPreferences()
        {
            if (userPreferences != null)
            {
                foreach (var preference in userPreferences)
                {
                    if (preference == null) continue;
                    PlayerPrefs.DeleteKey(Key(preference));
                }
            }

            PlayerPrefs.DeleteKey(InitializedKey);
            PlayerPrefs.DeleteKey(VersionKey);
            if (string.IsNullOrEmpty(keyPrefix)) PlayerPrefs.DeleteKey(LegacyInitializedKey);

            PlayerPrefs.Save();
            Log("Saved preferences deleted");
        }

        /// <summary>Wipes every PlayerPref in the game, including keys owned by other systems. Not wired to any button by default.</summary>
        public virtual void DeleteEveryPlayerPref()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            Log("Every PlayerPref in the project was deleted");
        }

        /// <summary>True when an entry can take part in saving; reports empty slots and types PlayerPrefs cannot store.</summary>
        private bool CanUse(ScriptableVar preference)
        {
            if (preference == null)
            {
                Debug.LogWarning($"[{name}] has an empty slot in [User Preferences]. It was skipped.", this);
                return false;
            }

            if (!preference.CanSerialize)
            {
                Debug.LogError($"[{name}] cannot save [{preference.name}]: {preference.GetType().Name} values cannot be stored in PlayerPrefs.", this);
                return false;
            }

            return true;
        }

        /// <summary>Reads a key written by the old format, which stored ints and floats with their typed setters.</summary>
        private static string ReadLegacyValue(ScriptableVar preference, string key)
        {
            //MWC — mirrors the type checks the old GetUserPreferences used, so each key is read back with the setter that wrote it
            if (preference is IntVar) return PlayerPrefs.GetInt(key, 0).ToString(CultureInfo.InvariantCulture);
            if (preference is FloatVar) return PlayerPrefs.GetFloat(key, 0f).ToString("R", CultureInfo.InvariantCulture);

            //MWC — bools were stored as "True"/"False"; BoolVar.SetSerializedValue still accepts that spelling
            return PlayerPrefs.GetString(key, string.Empty);
        }

        /// <summary>Editor-only debug logging, so per-variable messages never reach a shipped build.</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void Log(string message)
        {
#if UNITY_EDITOR
            if (debug) Debug.Log($"<B>[{name}]</B> {message}", this);
#endif
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ScriptableVarToPlayerPref))]
    public class ScriptableVarToPlayerPrefEditor : Editor
    {
        private ScriptableVarToPlayerPref m;

        private void OnEnable() => m = (ScriptableVarToPlayerPref)target;

        public override void OnInspectorGUI()
        {
            MalbersEditor.DrawDescription("Saves and loads Scriptable Variables using PlayerPrefs");

            DrawDefaultInspector();

            DrawWarnings();

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Save", "Write the current value of every variable to PlayerPrefs")))
                    m.SaveUserPreferences();

                if (GUILayout.Button(new GUIContent("Load", "Push the saved values back into the variables")))
                    m.GetUserPreferences();
            }

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Restore Defaults", "Apply the Default User Options asset, then save")))
                    m.RestoreToDefault();

                if (GUILayout.Button(new GUIContent("Delete Saved", "Delete only the keys this manager owns. Other PlayerPrefs are untouched")))
                    m.DeleteAllPreferences();
            }
        }

        /// <summary>Reports entries that cannot be saved and keys claimed by more than one variable.</summary>
        private void DrawWarnings()
        {
            if (m.userPreferences == null) return;

            var unsupported = new List<string>();
            var collisions = new List<string>();
            var owners = new Dictionary<string, string>();

            foreach (var preference in m.userPreferences)
            {
                if (preference == null) continue;

                if (!preference.CanSerialize)
                {
                    unsupported.Add($"{preference.name}  ({preference.GetType().Name})");
                    continue;
                }

                var key = m.Key(preference);

                if (owners.TryGetValue(key, out var owner))
                    collisions.Add($"{owner} + {preference.name}  ->  \"{key}\"");
                else
                    owners.Add(key, preference.name);
            }

            using (new EditorGUI.IndentLevelScope())
            {
                if (unsupported.Count > 0)
                {
                    EditorGUILayout.HelpBox("These variables cannot be stored in PlayerPrefs and will be skipped:\n• "
                        + string.Join("\n• ", unsupported), MessageType.Error);
                }

                if (collisions.Count > 0)
                {
                    EditorGUILayout.HelpBox("These variables resolve to the same key and will overwrite each other:\n• "
                        + string.Join("\n• ", collisions), MessageType.Error);
                }
            }
        }
    }
#endif
}

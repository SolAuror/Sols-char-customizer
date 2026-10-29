// MWC - Animal Controller update checker — styled EditorWindow with logo, version diff, and daily cache
#if UNITY_EDITOR
using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;

namespace MalbersAnimations
{
    [InitializeOnLoad]
    internal class ACUpdateChecker : EditorWindow
    {
        // MWC - version comes from MAnimalEditor.Version — only one place to update on release
        private static string CurrentVersion => MalbersAnimations.Controller.MAnimalEditor.Version;
        private const string DocsUrl        = "https://malbersanimations.gitbook.io/animal-controller";
        private const string ChangelogUrl   = "https://malbersanimations.gitbook.io/animal-controller/changelog";
        private const string AssetStoreUrl  = "https://assetstore.unity.com/packages/tools/animation/animal-controller-148877";
        private const string IconPath       = "Assets/Malbers Animations/Common/Scripts/Editor/Icons/Animal_Icon.png";
        private const string PrefKey        = "AC_CheckForUpdates";
        private const string SessionKey     = "AC_UpdateChecked";
        private static readonly string      CacheFile     = Path.Combine("Library", "ACUpdateChecker.json");
        private static readonly TimeSpan    CheckInterval = TimeSpan.FromHours(24);
        private static readonly HttpClient  Http          = new() { Timeout = TimeSpan.FromSeconds(10) };
        // MWC - extended pattern captures optional letter suffix (e.g. 1.5.2a, 1.5.2b)
        private static readonly Regex       VersionRegex  = new(
            @"Latest\s+Version:\s*\[(\d+\.\d+\.\d+[a-z]?)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ── State ────────────────────────────────────────────────────────────
        private string   _latestVersion;
        private bool     _isFetching = true;
        private bool     _fetchFailed;
        private Texture2D _logo;

        // Spinner
        private double _lastRepaintTime;
        private int    _spinFrame;

        // Cached styles (null-checked in OnGUI so they survive domain reloads)
        private GUIStyle _headerTitleStyle;
        private GUIStyle _headerSubStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _versionLabelStyle;
        private GUIStyle _versionValueStyle;
        private GUIStyle _updateButtonStyle;
        private GUIStyle _skipButtonStyle;
        private GUIStyle _footerToggleStyle;

        // ── Colors ───────────────────────────────────────────────────────────
        private static readonly Color HeaderBg     = new(0.13f, 0.13f, 0.16f, 1f);
        private static readonly Color SeparatorCol = new(0.08f, 0.08f, 0.10f, 1f);
        private static readonly Color UpdateBg     = new(1.0f, 0.55f, 0.05f, 0.12f);
        private static readonly Color OkBg         = new(0.10f, 0.75f, 0.35f, 0.10f);
        private static readonly Color ErrorBg      = new(0.70f, 0.15f, 0.10f, 0.10f);
        private static readonly Color UpdateText   = new(1.0f, 0.65f, 0.15f, 1.00f);
        private static readonly Color OkText       = new(0.25f, 0.85f, 0.45f, 1.00f);
        private static readonly Color ErrorText    = new(1.0f, 0.40f, 0.35f, 1.00f);
        private static readonly Color SubText      = new(0.55f, 0.70f, 0.90f, 1.00f);

        // ── Auto-check on Editor load ─────────────────────────────────────────
        static ACUpdateChecker()
        {
            EditorApplication.update += CheckOnFirstUpdate;
        }

        private static async void CheckOnFirstUpdate()
        {
            EditorApplication.update -= CheckOnFirstUpdate;

            if (!EditorPrefs.GetBool(PrefKey, true)) return;
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);

            // MWC - if cache already knows about a newer version, show window immediately (no network needed)
            var cache = LoadCache();
            if (cache != null && IsNewer(cache.lastVersion, CurrentVersion))
            {
                OpenWindow(cache.lastVersion);
                return;
            }

            // MWC - only hit the network once per 24 h when no cached update exists
            if (cache != null && DateTime.UtcNow - cache.lastCheck < CheckInterval) return;

            string latest = await FetchLatestVersion();
            if (!string.IsNullOrEmpty(latest) && IsNewer(latest, CurrentVersion))
                OpenWindow(latest);
        }

        // ── Public entry points ───────────────────────────────────────────────
        public static void ShowManual()
        {
            var win = CreateWindow();
            win._isFetching = true;
            win._fetchFailed = false;
            win._latestVersion = null;
            _ = win.FetchAndRefresh();
        }

        private static void OpenWindow(string latestVersion)
        {
            var win = CreateWindow();
            win._latestVersion = latestVersion;
            win._isFetching = false;
            win._fetchFailed = false;
        }

        private static ACUpdateChecker CreateWindow()
        {
            var win = GetWindow<ACUpdateChecker>(true, "Animal Controller — Update Check", true);
            win.minSize = win.maxSize = new Vector2(420, 295);
            return win;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void OnEnable()
        {
            _logo = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (_isFetching) EditorApplication.update += SpinnerRepaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= SpinnerRepaint;
        }

        private void SpinnerRepaint()
        {
            if (EditorApplication.timeSinceStartup - _lastRepaintTime < 0.075) return;
            _lastRepaintTime = EditorApplication.timeSinceStartup;
            _spinFrame = (_spinFrame + 1) % 12;
            Repaint();
        }

        private async Task FetchAndRefresh()
        {
            string latest = await FetchLatestVersion();
            _latestVersion = latest;
            _fetchFailed   = string.IsNullOrEmpty(latest);
            _isFetching    = false;
            EditorApplication.update -= SpinnerRepaint;
            Repaint();
        }

        // ── Network ───────────────────────────────────────────────────────────
        // MWC - fetches GitBook docs page and extracts version via regex
        private static async Task<string> FetchLatestVersion()
        {
            try
            {
                string html  = await Http.GetStringAsync(DocsUrl);
                var    match = VersionRegex.Match(html);
                if (!match.Success) return null;
                string ver = match.Groups[1].Value;
                SaveCache(ver);
                return ver;
            }
            catch { return null; }
        }

        private static bool IsNewer(string latest, string current) =>
            CompareVersionStrings(latest, current) > 0;

        // MWC - splits a version string into its numeric portion and optional lowercase letter suffix,
        //       then compares numerics first and falls back to letter (e.g. 1.5.2a < 1.5.2b, 1.5.2 < 1.5.2a)
        private static int CompareVersionStrings(string a, string b)
        {
            static (string num, char suffix) Split(string v)
            {
                if (string.IsNullOrEmpty(v)) return ("0.0.0", '\0');
                char last = v[v.Length - 1];
                return char.IsLetter(last)
                    ? (v.Substring(0, v.Length - 1), char.ToLower(last))
                    : (v, '\0');
            }

            var (aNum, aSuf) = Split(a);
            var (bNum, bSuf) = Split(b);
            if (!Version.TryParse(aNum, out var aVer) || !Version.TryParse(bNum, out var bVer)) return 0;
            int cmp = aVer.CompareTo(bVer);
            return cmp != 0 ? cmp : aSuf.CompareTo(bSuf);
        }

        // ── Cache (Library/) ──────────────────────────────────────────────────
        private static CacheData LoadCache()
        {
            try { return File.Exists(CacheFile) ? JsonUtility.FromJson<CacheData>(File.ReadAllText(CacheFile)) : null; }
            catch { return null; }
        }

        private static void SaveCache(string version)
        {
            try { File.WriteAllText(CacheFile, JsonUtility.ToJson(new CacheData { lastCheck = DateTime.UtcNow, lastVersion = version })); }
            catch { }
        }

        // ── GUI ───────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            BuildStyles();
            DrawHeader();
            DrawSeparator();

            if (_isFetching)    { DrawLoading();  return; }
            if (_fetchFailed)   { DrawError();    DrawSeparator(); DrawFooter(); return; }

            bool hasUpdate = IsNewer(_latestVersion, CurrentVersion);
            DrawStatusBanner(hasUpdate);
            DrawVersionRows(hasUpdate);
            if (hasUpdate) DrawBackupWarning();
            DrawButtons(hasUpdate);
            DrawSeparator();
            DrawFooter();
        }

        // ── Header ─────────────────────────────────────────────────────────────
        private void DrawHeader()
        {
            var rect = GUILayoutUtility.GetRect(position.width, 64);
            EditorGUI.DrawRect(rect, HeaderBg);

            // Logo
            if (_logo != null)
                GUI.DrawTexture(new Rect(rect.x + 10, rect.y + 10, 44, 44), _logo, ScaleMode.ScaleToFit, true);

            // Title
            GUI.Label(new Rect(rect.x + 62, rect.y + 10, rect.width - 70, 30), "Animal Controller", _headerTitleStyle);

            // Sub-line
            GUI.Label(new Rect(rect.x + 63, rect.y + 38, rect.width - 70, 20), $"Version {CurrentVersion} installed", _headerSubStyle);
        }

        // ── Loading spinner ────────────────────────────────────────────────────
        private void DrawLoading()
        {
            GUILayout.FlexibleSpace();
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                var spinIcon = EditorGUIUtility.IconContent($"WaitSpin{_spinFrame:00}");
                GUILayout.Label(spinIcon, GUILayout.Width(20), GUILayout.Height(20));
                GUILayout.Label("  Checking for updates…", _versionLabelStyle);
                GUILayout.FlexibleSpace();
            }
            GUILayout.FlexibleSpace();
        }

        // ── Status banner ──────────────────────────────────────────────────────
        private void DrawStatusBanner(bool hasUpdate)
        {
            var bgColor   = hasUpdate ? UpdateBg  : OkBg;
            var textColor = hasUpdate ? UpdateText : OkText;
            var icon      = hasUpdate ? "console.warnicon.sml" : "Installed@2x";
            var message   = hasUpdate ? $"Update available — v{_latestVersion} is ready!" : "You're up to date!";

            var rect = GUILayoutUtility.GetRect(position.width, 38);
            EditorGUI.DrawRect(rect, bgColor);

            var iconContent = EditorGUIUtility.IconContent(icon);
            GUI.Label(new Rect(rect.x + 10, rect.y + 9, 20, 20), iconContent);

            _statusStyle.normal.textColor = textColor;
            GUI.Label(new Rect(rect.x + 34, rect.y + 9, rect.width - 44, 22), message, _statusStyle);
        }

        // ── Version rows ───────────────────────────────────────────────────────
        private void DrawVersionRows(bool hasUpdate)
        {
            EditorGUILayout.Space(6);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(12);
                GUILayout.Label("Installed", _versionLabelStyle, GUILayout.Width(70));
                GUILayout.Label(CurrentVersion, _versionValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label("Latest", _versionLabelStyle, GUILayout.Width(46));
                // MWC - highlight latest version in orange when an update is available
                var prev = _versionValueStyle.normal.textColor;
                if (hasUpdate) _versionValueStyle.normal.textColor = UpdateText;
                GUILayout.Label(_latestVersion, _versionValueStyle, GUILayout.Width(54));
                _versionValueStyle.normal.textColor = prev;
                GUILayout.Space(12);
            }
            EditorGUILayout.Space(6);
        }

        // ── Error state ────────────────────────────────────────────────────────
        private void DrawError()
        {
            GUILayout.FlexibleSpace();
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                var icon = EditorGUIUtility.IconContent("console.erroricon.sml");
                GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
                _statusStyle.normal.textColor = ErrorText;
                GUILayout.Label("  Could not reach the server. Check your connection.", _statusStyle);
                GUILayout.FlexibleSpace();
            }
            GUILayout.FlexibleSpace();
        }

        // ── Backup warning ─────────────────────────────────────────────────────
        private void DrawBackupWarning()
        {
            EditorGUILayout.Space(2);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                EditorGUILayout.HelpBox("Always back up your project before updating!", MessageType.Warning);
                GUILayout.Space(10);
            }
            EditorGUILayout.Space(2);
        }

        // ── Buttons ────────────────────────────────────────────────────────────
        private void DrawButtons(bool hasUpdate)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(12);
                if (hasUpdate)
                {
                    // MWC - kharma URL opens Package Manager focused on this asset's page (Unity 2020.1+)
                    if (GUILayout.Button("Update in Package Manager", _updateButtonStyle, GUILayout.Height(28)))
                        OpenInPackageManager();
                    GUILayout.Space(6);
                    if (GUILayout.Button("Skip", _skipButtonStyle, GUILayout.Width(60), GUILayout.Height(28)))
                        Close();
                }
                else
                {
                    if (GUILayout.Button("Close", _skipButtonStyle, GUILayout.Height(28)))
                        Close();
                }
                GUILayout.Space(12);
            }
            EditorGUILayout.Space(6);

            // MWC - changelog link shown below the main action buttons
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(12);
                var changelogIcon = EditorGUIUtility.IconContent("TextAsset Icon");
                changelogIcon.text = "  View Changelog";
                if (GUILayout.Button(changelogIcon, _skipButtonStyle, GUILayout.Height(24)))
                    Application.OpenURL(ChangelogUrl);
                GUILayout.Space(12);
            }
            EditorGUILayout.Space(6);
        }

        // MWC - opens the Package Manager window so the user can find and update Animal Controller in My Assets
        private void OpenInPackageManager()
        {
            Close();
            Window.Open("");
        }

        // ── Footer toggle ──────────────────────────────────────────────────────
        private void DrawFooter()
        {
            EditorGUILayout.Space(4);
            bool checkPref = EditorPrefs.GetBool(PrefKey, true);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                GUILayout.Label("Check for updates automatically", _footerToggleStyle, GUILayout.ExpandWidth(true));
                bool newPref = EditorGUILayout.Toggle(checkPref, GUILayout.Width(16));
                GUILayout.Space(10);
                if (newPref != checkPref) EditorPrefs.SetBool(PrefKey, newPref);
            }
            EditorGUILayout.Space(4);
        }

        // ── Separator ──────────────────────────────────────────────────────────
        private static void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(1f, 1f);
            EditorGUI.DrawRect(rect, SeparatorCol);
        }

        // ── Style builder ──────────────────────────────────────────────────────
        // MWC - lazy-built styles survive domain reloads via null checks
        private void BuildStyles()
        {
            if (_headerTitleStyle != null) return;

            _headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 17,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = Color.white }
            };

            _headerSubStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize  = 12,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = SubText }
            };

            _statusStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 13,
                alignment = TextAnchor.MiddleLeft
            };

            _versionLabelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize  = 12,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = new Color(0.55f, 0.60f, 0.65f, 1f) }
            };

            _versionValueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 13,
                alignment = TextAnchor.MiddleLeft
            };

            _updateButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize  = 13
            };

            _skipButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13
            };

            _footerToggleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize  = 12,
                alignment = TextAnchor.MiddleLeft
            };
        }

        // ── Data ─────────────────────────────────────────────────────────────
        [Serializable]
        private class CacheData
        {
            public string lastCheckUtc;
            public string lastVersion;
            public DateTime lastCheck
            {
                get => DateTime.TryParse(lastCheckUtc, out var d) ? d : DateTime.MinValue;
                set => lastCheckUtc = value.ToString("O");
            }
        }
    }
}
#endif

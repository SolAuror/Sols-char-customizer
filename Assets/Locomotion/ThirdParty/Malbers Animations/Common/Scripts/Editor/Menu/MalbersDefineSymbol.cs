// Last Change: 9/17/2026 5:33pm

#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MalbersAnimations
{
    public class MalbersDefineSymbol : EditorWindow
    {
        /// <summary> Symbol that will be added to the editor  </summary>
        private static string _EditorSymbol = "MALBERS_DEBUG";

        /// <summary>Named Build Target of the platform selected in the Build Settings (replaces the obsolete BuildTargetGroup overloads)</summary>
        private static NamedBuildTarget CurrentTarget => NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);

        [MenuItem("Tools/Malbers Animations/Debug Gizmos [On]", false, 8000)]
        public static void DebugGizmosOn()
        {
            string lSymbols = PlayerSettings.GetScriptingDefineSymbols(CurrentTarget);

            if (!lSymbols.Split(';').Contains(_EditorSymbol))
            {
                PlayerSettings.SetScriptingDefineSymbols(CurrentTarget, lSymbols + ";" + _EditorSymbol);
            }
            Debug.Log($"Scripting Define Symbol Added: [{_EditorSymbol}]");
        }

        [MenuItem("Tools/Malbers Animations/Debug Gizmos [Off]", false, 8000)]
        public static void DebugGizmosOff()
        {
            string lSymbols = PlayerSettings.GetScriptingDefineSymbols(CurrentTarget);

            if (lSymbols.Split(';').Contains(_EditorSymbol))
            {
                lSymbols = lSymbols.Replace($";{_EditorSymbol}", "");

                PlayerSettings.SetScriptingDefineSymbols(CurrentTarget, lSymbols);
            }
            Debug.Log($"Scripting Define Symbol Removed: [{_EditorSymbol}]");
        }
    }
}
#endif

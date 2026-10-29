

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MalbersAnimations
{
    public class MalbersMenu : EditorWindow
    {
        const string URP14_Shader_Path = "Assets/Malbers Animations/Common/Shaders/Malbers_URP_14.unitypackage";
        const string URP17_Shader_Path = "Assets/Malbers Animations/Common/Shaders/Malbers_URP_17.unitypackage";
        const string HRP14_Shader_Path = "Assets/Malbers Animations/Common/Shaders/Malbers_HDRP_15.unitypackage";
        const string HRP17_Shader_Path = "Assets/Malbers Animations/Common/Shaders/Malbers_HDRP_17.unitypackage";

        const string D_Shader_Path = "Assets/Malbers Animations/Common/Shaders/Malbers_Standard.unitypackage";

        const string Volume_Path_HDRP = "Assets/Malbers Animations/Common/Prefabs/HDRP Volume.unitypackage";

        //[MenuItem("Tools/Malbers Animations/Malbers URP 17 Shaders (Unity6) ", false, 2)]
        public static void UpgradeMaterialsURP_17() =>
#if UNITY_6000_6_OR_NEWER
            UnityEditor.AssetPackage.Package.Import
#else
        AssetDatabase.ImportPackage
#endif
            (URP17_Shader_Path, true);

        //[MenuItem("Tools/Malbers Animations/Malbers URP 14 Shaders (Unity 2022.3 LTS) ", false, 1)]
        public static void UpgradeMaterialsURP_14() =>
#if UNITY_6000_6_OR_NEWER
            UnityEditor.AssetPackage.Package.Import
#else
        AssetDatabase.ImportPackage
#endif

            (URP14_Shader_Path, true);

        // [MenuItem("Tools/Malbers Animations/Malbers HDRP 14 Shaders (Unity 2022.3 LTS) ", false, 3)]
        public static void UpgradeMaterialsHDRP_14() =>
#if UNITY_6000_6_OR_NEWER
            UnityEditor.AssetPackage.Package.Import
#else
        AssetDatabase.ImportPackage
#endif
            (HRP14_Shader_Path, true);

        // [MenuItem("Tools/Malbers Animations/Malbers HDRP 17 Shaders (Unity6)", false, 3)]
        public static void UpgradeMaterialsHDRP_17() =>
#if UNITY_6000_6_OR_NEWER
            UnityEditor.AssetPackage.Package.Import
#else
        AssetDatabase.ImportPackage
#endif
            (HRP17_Shader_Path, true);

        // [MenuItem("Tools/Malbers Animations/Malbers HDRP Post Effects", false, 4)]
        public static void UpgradeMaterialsHDRP_PostEffects() =>
#if UNITY_6000_6_OR_NEWER
            UnityEditor.AssetPackage.Package.Import
#else
        AssetDatabase.ImportPackage
#endif
            (Volume_Path_HDRP, true);

        //[MenuItem("Tools/Malbers Animations/Malbers Standard Shaders", false, 1)]
        public static void UpgradeMaterialsStandard() =>
#if UNITY_6000_6_OR_NEWER
            UnityEditor.AssetPackage.Package.Import
#else
        AssetDatabase.ImportPackage
#endif
            (D_Shader_Path, true);

        [MenuItem("Tools/Malbers Animations/Integrations", false, 600)]
        public static void OpenIntegrations() => Application.OpenURL("https://malbersanimations.gitbook.io/animal-controller/annex/integrations");


        [MenuItem("Tools/Malbers Animations/What's New", false, 600)]
        public static void OpenWhatsNew() => Application.OpenURL("https://malbersanimations.gitbook.io/animal-controller/whats-new");

        // MWC - manual trigger for the Animal Controller update checker
        [MenuItem("Tools/Malbers Animations/Check for Updates", false, 601)]
        public static void CheckForUpdates() => ACUpdateChecker.ShowManual();
    }
}
#endif
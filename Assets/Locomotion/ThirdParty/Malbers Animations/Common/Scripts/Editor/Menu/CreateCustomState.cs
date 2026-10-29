
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
namespace MalbersAnimations.Menu
{
    public class CreateCustomState : EditorWindow
    {
        private string newState = "CustomState";
        private string folderPath = "Assets/Malbers Animations/Common/Scripts/Animal Controller/States";
        private readonly string IDPath = "Assets/Malbers Animations/Common/Scriptable Assets/IDs/StatesID";

        [MenuItem("Tools/Malbers Animations/Create/Custom State", false, -100)]
        public static void ShowWindow()
        {
            CreateCustomState window = GetWindow<CreateCustomState>("Create Script");
            window.minSize = new Vector2(150, 300);
        }
        private void OnGUI()
        {
            GUILayout.Label("Create a Script", EditorStyles.boldLabel);

            newState = EditorGUILayout.TextField("State Name", newState);
            folderPath = EditorGUILayout.TextField("Folder Path", folderPath, EditorStyles.textArea);

            GUILayout.Space(10);

            if (GUILayout.Button("Create Script"))
            {
                if (string.IsNullOrEmpty(newState))
                {
                    EditorUtility.DisplayDialog("Error", "Class name cannot be empty!", "OK");
                }
                else if (ContainsInvalidCharacters(newState))
                {
                    EditorUtility.DisplayDialog("Error", "Class name contains invalid characters!", "OK");
                }
                else
                {
                    CreateNewIDState();
                    CreateScript();
                }
            }
        }

        private void CreateNewIDState()
        {
            StateID stateID = ScriptableObject.CreateInstance<StateID>();
            stateID.GetID();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{IDPath}/{newState}.asset");
            AssetDatabase.CreateAsset(stateID, assetPath);
            AssetDatabase.SaveAssets();
        }

        private void CreateScript()
        {
            string fullPath = Path.Combine(folderPath, newState + ".cs");

            if (!Directory.Exists(folderPath))
            {
                EditorUtility.DisplayDialog("Error", "Folder path does not exist!", "OK");
                return;
            }

            if (File.Exists(fullPath))
            {
                EditorUtility.DisplayDialog("Error", "File already exists!", "OK");
                return;
            }

            string scriptContent = GetDefaultScriptContent(newState);
            File.WriteAllText(fullPath, scriptContent);

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Success", $"New Custom state [{newState}] created at {fullPath}. New StateID [{newState}] created", "OK");
        }
        private string GetDefaultScriptContent(string className)
        {
            return $@"
using UnityEngine;

//please check this page for more information: 
//https://malbersanimations.gitbook.io/animal-controller/main-components/manimal-controller/states/creating-a-new-state

namespace MalbersAnimations.Controller
{{
    [AddTypeMenu(""Custom/{className}"")]
    public class {className} : State
    {{
        public override string StateName => ""{className}"";
        public override string StateIDName => ""{className}"";

        /// <summary> [[OPTIONAL]]
        // Use it to cache any objects before starting the state.
        //E.g. the Ladder State stores in a variable any Interactor the character has</summary>
        public override void InitializeState()
        {{
            //Write here all your preparation code here
            //fill all your local variables and references.
        }}

        
        // This method is like use for automatic states, or states that require external conditions for Activation
        // If this method returns true, the New State will be activated.
        // For example, in the Fall State, I use this method to cast the Pink Ray to find the ground beneath the animal.
        // If I don't find the ground or I find a slope too deep, I return true and the state will be activated.
        // Not all states need to implement this method. States like Fly and Death can be activated by Player Input or by calling directly the Activate() method.
        public override bool TryActivate()
        {{
            return false; //By default it will not activate automatically
        }}


        /// <summary> Method called right after the TryActivate(), or if an Input activated the State
        // or by simply calling State_Activate(int ID) on the Animal.
        // Here all the Animator Parameters are updated.
        // That way, in the next frame, the animations are properly executed.
        // In this method, it is mandatory to keep the base.Activate(); reference.</summary>
        public override void Activate()
        {{
            base.Activate(); //This is mandatory (But it can be on set first or last in this method)

           //Write here all your activation code 
        }}


        // Method called when entering core animation. The core animation must have the Core [Tag] in the Animator Controller
        public override void EnterCoreAnimation()
        {{
            // This method is called when the first frame of the Core animation of the state is played.
            // E.g. when the glide state Animation with the ""Glide"" tag plays.
            // Example: This method could be used to initialize specific actions when entering core animation.
        }}

        // Method called when entering animation tagged with specific tags
        public override void EnterTagAnimation()
        {{
            // This method is called every time the state enters the EnterTag Animation State (See States.
            // E.g. Locomotion has enter 'StartLocomotion' tag animation.
            // This method is called once if an animation state is tagged with the  [Enter Tag].
        }} 


        // State logic update method.
        // This method handles all the state-specific logic. It's where you implement custom movements and behaviors for the state.
        // E.g. the Fall State, it manages air control to move the character and applies gravitational forces.
        // Example: ApplyGravity(); ApplyAirControl(); Use AdditivePosition or AdditiveRotation to move the character around.
        public override void OnStateMove(float deltaTime)
        {{
            if (InCoreAnimation)
            {{
                // Core update functions. This is called every frame
                
            }}
        }}


        //The logic for exiting the state is here. Add all the conditions you need to allow your state to exit.
        //if the State allows conditions are true. Call the AllowExit() method.
        //E.g. on the Glide and Fall State; if the character is near the ground, I call  AllowExit() here... which will allow other states to try to activate themselves.
        public override void TryExitState(float DeltaTime)
        {{
           // if (YourExitConditions == true)
           // {{
           //    Debugging($""[Allow Exit] - State {className} can exit"");
           //     AllowExit(); 
           // }}
        }}

    }}
}}
";
        }

        private bool ContainsInvalidCharacters(string input)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                if (input.Contains(c.ToString()))
                    return true;
            }
            return false;
        }
    }
}
#endif
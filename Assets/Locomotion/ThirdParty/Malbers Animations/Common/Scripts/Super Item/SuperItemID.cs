// Last Change: 9/17/2026 5:33pm
using UnityEngine;

namespace MalbersAnimations
{
    [System.Serializable]
    [UnityEngine.HelpURL("https://malbersanimations.gitbook.io/animal-controller/super-item/super-item-system/id-assets")]
    [UnityEngine.CreateAssetMenu(menuName = "Malbers Animations/ID/Super Item ID", fileName = "New Item ID", order = -1000)]
    public class SuperItemID : ModeID
    {
        /// <summary>
        /// Color for the Super Item ID. This color is used for the editor and gizmos. It is a light orange color.
        /// </summary>
        public override Color IDColor => new Color(0.8f, 0.35f, 0.2f);

        public static implicit operator int(SuperItemID reference) => reference != null ? reference.ID : 0;
    }
}

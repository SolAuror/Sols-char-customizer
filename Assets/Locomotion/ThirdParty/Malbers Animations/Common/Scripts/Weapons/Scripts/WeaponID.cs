using UnityEngine;

namespace MalbersAnimations
{
    [System.Serializable]
    [UnityEngine.CreateAssetMenu(menuName = "Malbers Animations/ID/Weapon", fileName = "New Weapon ID", order = -1000)]
    public class WeaponID : ModeID
    {
        public override Color IDColor => new(0.82f, 0.38f, 0.14f);
    }
}

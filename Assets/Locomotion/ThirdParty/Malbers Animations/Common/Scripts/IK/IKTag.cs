using UnityEngine;

namespace MalbersAnimations.IK
{
    [CreateAssetMenu(menuName = "Malbers Animations/IK Tag", fileName = "New IK Tag", order = 3000)]

    public class IKTag : IDs
    {
        public override Color IDColor => new(0.2f, 0.5f, 1f, 1f);

        protected override void OnValidate()
        {
           ID = name.GetHashCode();
        }
    }
}

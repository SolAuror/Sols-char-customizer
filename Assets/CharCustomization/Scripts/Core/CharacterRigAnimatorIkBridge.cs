using UnityEngine;

namespace Sol.CharacterCustomization
{
    [RequireComponent(typeof(Animator))]
    public sealed class CharacterRigAnimatorIkBridge : MonoBehaviour
    {
        [SerializeField] private CharacterRigProportionDriver driver;

        public CharacterRigProportionDriver Driver => driver;

        public void Bind(CharacterRigProportionDriver proportionDriver)
        {
            driver = proportionDriver;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            driver?.ApplyAnimatorIk(layerIndex);
        }
    }
}

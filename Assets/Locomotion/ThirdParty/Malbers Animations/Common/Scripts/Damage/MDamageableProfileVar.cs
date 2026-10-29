// Last Change: 9/14/2026 12:20pm
using UnityEngine;

namespace MalbersAnimations
{
    [CreateAssetMenu(fileName = "Damageable Profile", menuName = "Malbers Animations/Damageable Profile")]
    public class MDamageableProfileVar : ScriptableObject
    {
        [Tooltip("The Damageable Profile to use")]
        public MDamageableProfile value;

        /// <summary> Replaces the stored Profile </summary>
        public void SetValue(MDamageableProfile newValue) => value = newValue;

        /// <summary> A new asset starts as a ready-to-use Profile instead of a zero-initialized one (empty Var slots everywhere) </summary>
        private void Reset() => value = MDamageableProfile.New("New Damage Profile");

        /// <summary> Sets this Profile on the given Damageable </summary>
        public void Apply(MDamageable mDamageable)
        {
            if (mDamageable != null) mDamageable.Profile_Set(value);
        }
    }
}

using UnityEngine;

namespace Sol.CharacterCustomization
{
    /// <summary>Optional gameplay endpoint. The character editor does not depend on a motor.</summary>
    public interface ICharacterGameplayHandoff
    {
        void BindAnimator(Animator activeAnimator);
        void SetGameplayInputEnabled(bool enabled);
    }
}

// Last Change: 9/17/2026 5:33pm
namespace MalbersAnimations
{
    //MWC: Hoisted from SuperItem (nested enum) so Equip Point IDs can declare their side (Dual Wielding).
    //Values unchanged (Right = 0) so existing serialized prefab data stays valid.
    public enum WeaponSide { Right, Left, None }

    [System.Serializable]
    [UnityEngine.HelpURL("https://malbersanimations.gitbook.io/animal-controller/super-item/super-item-system/super-item-manager/equip-points")]
    [UnityEngine.CreateAssetMenu(menuName = "Malbers Animations/Super Item/Equip Point ID", fileName = "New Equip Point ", order = -1000)]
    public class EquipPointID : IDs
    {
        //MWC: Side of the character this Equip Point belongs to. Items equipped here derive their Effective Side from this value.
        //Default None: existing assets keep today's behavior (item's own Side is used) until authored.
        [UnityEngine.Tooltip("Side of the character this Equip Point belongs to (e.g. Right Hand → Right). " +
            "Items equipped on this point will use this Side for the Animator parameters. None: use the Item's own Side value")]
        public WeaponSide Side = WeaponSide.None;

        #region CalculateID
#if UNITY_EDITOR
        private void Reset()
        {
            GetID();
            GuessSide();
        }

        [UnityEngine.ContextMenu("Get ID")]
        private void GetID() => FindID<EquipPointID>();

        //MWC: Name heuristic to author the Side automatically on new/legacy assets (only when Side is still None)
        [UnityEngine.ContextMenu("Guess Side from Name")]
        private void GuessSide()
        {
            if (Side != WeaponSide.None) return;

            var n = name.ToLowerInvariant();
            if (n.Contains("left")) Side = WeaponSide.Left;
            else if (n.Contains("right")) Side = WeaponSide.Right;
        }
#endif
        #endregion
    }
}

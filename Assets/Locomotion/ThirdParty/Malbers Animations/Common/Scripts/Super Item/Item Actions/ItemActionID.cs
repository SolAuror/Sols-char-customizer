// Last Change: 9/17/2026 5:33pm
namespace MalbersAnimations
{
    [System.Serializable]
    [UnityEngine.HelpURL("https://malbersanimations.gitbook.io/animal-controller/super-item/super-item-system/id-assets")]
    [UnityEngine.CreateAssetMenu(menuName = "Malbers Animations/Super Item/Item Actions ID", fileName = "New Item Action ID", order = -1000)]
    public class ItemActionID : IDs
    {
        #region CalculateID
#if UNITY_EDITOR
        private void Reset() => GetID();

        [UnityEngine.ContextMenu("Get ID")]
        private void GetID() => FindID<ItemActionID>();
#endif
        #endregion
    }
}


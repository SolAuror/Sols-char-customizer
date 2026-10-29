using UnityEngine;

namespace MalbersAnimations
{
    [System.Serializable]
    [UnityEngine.CreateAssetMenu(menuName = "Malbers Animations/ID/Mode", fileName = "New Mode ID", order = -1000)]
    public class ModeID : IDs
    {
        private static readonly Color MODE_COLOR = new(0.08f, 0.48f, 0.22f);
        public override Color IDColor => MODE_COLOR;

        #region CalculateID
#if UNITY_EDITOR
        private void Reset() => GetID();

        [UnityEngine.ContextMenu("Get ID")]
        public void GetID() => FindID<ModeID>();
#endif
        #endregion
    }
}
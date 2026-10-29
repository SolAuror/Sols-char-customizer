using UnityEngine;

namespace MalbersAnimations
{
    [System.Serializable]
    [UnityEngine.CreateAssetMenu(menuName = "Malbers Animations/ID/Stance", fileName = "New Stance ID", order = -1000)]
    public class StanceID : IDs
    {
        //Stance color is #D54156



        public override Color IDColor => new(0.83f, 0.25f, 0.34f, 1f);

        #region CalculateID
#if UNITY_EDITOR
        private void Reset() => GetID();

        [UnityEngine.ContextMenu("Get ID")]
        public void GetID() => FindID<StanceID>();
#endif
        #endregion
    }
}

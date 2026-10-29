// Last Change: 9/17/2026 6:05pm
using UnityEngine;

namespace MalbersAnimations.Scriptables
{
    ///<summary>  Float Scriptable Variable. Based on the Talk - Game Architecture with Scriptable Objects by Ryan Hipple  </summary>
    [CreateAssetMenu(menuName = "Malbers Animations/Variables/Int Range", order = 1000)]
    public class IntRangeVar : IntVar
    {
        public IntReference minValue;
        public IntReference maxValue;

        /// <summary>Value of the Float Scriptable variable </summary>
        public override int Value
        {
            get => UnityEngine.Random.Range(minValue, maxValue + 1);
            set {/*Do nothing on Set*/ }
        }

        /// <summary>Not serializable: [Value] returns a fresh random number, so persisting it would overwrite the authored range.</summary>
        public override bool CanSerialize => false;
    }
}
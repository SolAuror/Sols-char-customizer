// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Reactions;
using System;
using UnityEngine;

namespace MalbersAnimations.Weapons
{
    [System.Serializable]
    [AddTypeMenu("Tools/Projectile Thrower")]
    [HelpURL(ReactionDocs.Tools + "projectile-thrower")]
    public class MProjectileThrowerReaction : Reaction
    {

        public override string DynamicName
        {
            get
            {
                //MWC: flag the retired action on the reaction row so an old setup is visible at a glance
                var display = action == ProjectileThrowerActions.SetAfterDistance
                    ? "Projectile Thrower [Set After Distance] (Retired)"
                    : $"Projectile Thrower [{action}]"; //Name of the Reaction

                switch (action)
                {
                    case ProjectileThrowerActions.SetProjectile:
                        display += $" [{(projectile != null ? projectile.name : "None")}]";
                        break;
                    case ProjectileThrowerActions.SetTarget:
                        display += $" [{(target != null ? target.name : "None")}]";
                        break;
                    case ProjectileThrowerActions.SetDamageMultiplier:
                    case ProjectileThrowerActions.SetScaleMultiplier:
                    case ProjectileThrowerActions.SetForceMultiplier:
                    case ProjectileThrowerActions.SetForce:
                    case ProjectileThrowerActions.SetAngle:
                    case ProjectileThrowerActions.SetAfterDistance:
                        display += $" [{value}]";
                        break;
                    default:
                        break;
                }
                return display;
            }
        }


        //MWC: [SetAfterDistance] is retired, not deleted. The value stays at index 7 so every reaction already
        //saved as [Fire] keeps resolving to 8. Removing it would silently repoint those to the wrong action
        public enum ProjectileThrowerActions
        {
            SetProjectile,
            SetTarget,
            SetDamageMultiplier,
            SetScaleMultiplier,
            SetForceMultiplier,
            SetForce,
            SetAngle,
            /// <summary> Retired no-op. After Distance is authored on the projectile's Flying movement </summary>
            SetAfterDistance,
            Fire
        }

        public ProjectileThrowerActions action = ProjectileThrowerActions.SetProjectile;

        [Hide("action", (int)ProjectileThrowerActions.SetProjectile)]
        public GameObject projectile;
        [Hide("action", (int)ProjectileThrowerActions.SetTarget)]
        public Transform target;
        [Hide("action", true, (int)ProjectileThrowerActions.SetProjectile, (int)ProjectileThrowerActions.SetTarget, (int)ProjectileThrowerActions.Fire)]
        public float value;

        public override Type ReactionType => typeof(MProjectileThrower);

        protected override bool _TryReact(Component reactor)
        {
            if (reactor is MProjectileThrower thrower)
            {
                switch (action)
                {
                    case ProjectileThrowerActions.SetProjectile:
                        thrower.SetProjectile(projectile);
                        break;
                    case ProjectileThrowerActions.SetTarget:
                        thrower.SetTarget(target);
                        break;
                    case ProjectileThrowerActions.SetDamageMultiplier:
                        thrower.SetDamageMultiplier(value);
                        break;
                    case ProjectileThrowerActions.SetScaleMultiplier:
                        thrower.SetScaleMultiplier(value);
                        break;
                    case ProjectileThrowerActions.SetForceMultiplier:
                        thrower.SetForceMultiplier(value);
                        break;
                    case ProjectileThrowerActions.SetForce:
                        thrower.Power = value;
                        break;
                    case ProjectileThrowerActions.SetAngle:
                        thrower.Angle = value;
                        break;
                    //MWC: retired no-op. After Distance moved to the projectile's Flying movement, so there is
                    //nothing on the thrower left to set. Warns instead of failing silently on an old reaction
                    case ProjectileThrowerActions.SetAfterDistance:
                        Debug.LogWarning($"[{reactor.name}] Projectile Thrower Reaction: [Set After Distance] is retired and does nothing. " +
                            "After Distance is authored on the projectile's Flying Movement.", reactor);
                        break;
                    case ProjectileThrowerActions.Fire:
                        thrower.Fire();
                        break;
                    default:
                        break;
                }
            }
            return true;
        }
    }
}

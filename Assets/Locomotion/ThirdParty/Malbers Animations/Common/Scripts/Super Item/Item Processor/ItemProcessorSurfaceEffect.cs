// Last Change: 9/19/2026 12:45pm
using MalbersAnimations.Reactions;
using MalbersAnimations.Utilities;
using UnityEngine;

namespace MalbersAnimations.Items
{
    [System.Serializable, AddTypeMenu("General/Surface Hit")]
    [HelpURL(ItemDocs.Processors + "surface-hit")]
    public class ItemProcessorSurfaceEffect : ItemProcessor
    {
        [RequiredField] public SurfaceEffects HitEffect; //The Surface Effect to be applied
        public float duration = 0.5f; //Duration of the effect, if applicable

        public Reaction2 OnHit;

        public override string DynamicName =>
            $"Surface Hit: [{(HitEffect != null ? HitEffect.name : "<None>")}] Duration: [{duration}]";

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action) { }


        public override ExecuteProcessor InitialExecute => ExecuteProcessor.OnTriggerEnter;

        public override void EnterTrigger(GameObject go, Collider collider, TriggerProxy proxy, SuperItem item, ItemActionSet set, ItemAction action, SuperItemManager user)
        {
            var Trigger = action.Trigger;

            if (Trigger != null && Trigger != proxy) return; //If the Trigger is not the one assigned, do nothing

            if (HitEffect == null) return;
            var Surf = go.FindInterface<ISurface>();


            var effect = Surf != null ? HitEffect.Get(Surf.Surface) : HitEffect.Default;
            if (effect == null) return;

            //No Trigger assigned on the Action: the proxy that fired IS the trigger (was a NullReference on every hit)
            var center = Trigger != null ? Trigger.transform.position : proxy.transform.position;

            TryHitEffect(collider, center, effect, user); //Try to hit the effect on the collider
        }


        protected void TryHitEffect(Collider col, Vector3 DamageCenter, EffectType effect, SuperItemManager user)
        {
            if (col is MeshCollider meshCollider && !meshCollider.convex) return; //Do not hit NonConvex Collider 
            if (col is TerrainCollider) return; //Do not hit  a Terrain Collider

            var HitPosition = col.ClosestPoint(DamageCenter); //Find the closest point on the Collider hit
            var HitRotation = Quaternion.FromToRotation(Vector3.up, col.bounds.center - DamageCenter);

            //MWC — the timed Play overload: the instance returns itself to the pool after [duration] (EffectPoolRelease).
            //It used to be released by a fire-and-forget coroutine on the USER, so deactivating the character mid-effect
            //killed that coroutine silently and the instance never went back to its pool (leaked, and the pool grew).
            effect.Play(HitPosition, HitRotation, col.transform, duration); //Play the effect at the Hit Position and Rotation

            OnHit.React(col);
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (HitEffect == null)
                return $"Missing the Surface Effect reference.";
            return string.Empty;
        }

#if UNITY_EDITOR
        public override void OnCreated(SuperItem item, ItemActionSet set, ItemAction action)
        {
            HitEffect = MTools.GetInstance<SurfaceEffects>("Hit Default");
        }
#endif

    }
}
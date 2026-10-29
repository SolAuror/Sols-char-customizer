using UnityEngine;

namespace MalbersAnimations
{
    //MWC: new marker component. A scale-fixer [Link] created at runtime (stuck projectile, footstep track...) is
    //grafted into a foreign hierarchy (E.g. a character's rig). Searches that walk that hierarchy looking for its
    //OWN parts (E.g. the projectile's closest-bone search) use this marker to skip the graft and its whole subtree.
    //Without it, a projectile could stick to another projectile's link instead of the bone both of them hit
    /// <summary> Marks a runtime [Link] object grafted into a hierarchy, so hierarchy searches can skip it and everything hanging from it </summary>
    [AddComponentMenu("")] //Runtime-created only, hidden from the Add Component menu
    public class HierarchyLink : MonoBehaviour { }
}

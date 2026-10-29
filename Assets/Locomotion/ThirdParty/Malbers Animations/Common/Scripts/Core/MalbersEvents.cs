// Last Change: 9/17/2026 6:05pm
using System;
using UnityEngine;
using UnityEngine.Events;

namespace MalbersAnimations.Events
{
    [Serializable] public class GameObjectEvent : UnityEvent<GameObject> { }
    [Serializable] public class UnityObjectEvent : UnityEvent<UnityEngine.Object> { }
    [Serializable] public class TransformEvent : UnityEvent<Transform> { }
    [Serializable] public class SpriteEvent : UnityEvent<Sprite> { }
    [Serializable] public class RayCastHitEvent : UnityEvent<RaycastHit> { }
    [Serializable] public class Vector3Event : UnityEvent<Vector3> { }
    [Serializable] public class Vector2Event : UnityEvent<Vector2> { }
    [Serializable] public class ColorEvent : UnityEvent<Color> { }
    [Serializable] public class IntEvent : UnityEvent<int> { }
    [Serializable] public class Int2Event : UnityEvent<int, int> { }
    [Serializable] public class FloatEvent : UnityEvent<float> { }
    [Serializable] public class IntFloatEvent : UnityEvent<int, float> { }
    [Serializable] public class BoolEvent : UnityEvent<bool> { }
    [Serializable] public class StringEvent : UnityEvent<string> { }
    [Serializable] public class String2Event : UnityEvent<string, string> { }
    [Serializable] public class ColliderEvent : UnityEvent<Collider> { }
    [Serializable] public class CollisionEvent : UnityEvent<Collision> { }
    [Serializable] public class ComponentEvent : UnityEvent<Component> { }
    [Serializable] public class AnimatorEvent : UnityEvent<Animator> { }
    [Serializable] public class AudioEvent : UnityEvent<AudioClip> { }
    [Serializable] public class RigidbodyEvent : UnityEvent<Rigidbody> { }
    [Serializable] public class IDsEvent : UnityEvent<IDs> { }

    /// <summary> Everything a trajectory preview needs, pushed by a launcher that is not an IThrower.
    /// Carried as ONE struct on purpose: a UnityEvent with more than one argument cannot be wired in the
    /// inspector (the serialized call never fires), which is why the project also passes RaycastHit this way </summary>
    [Serializable]
    public struct TrajectoryData
    {
        /// <summary> Launch velocity. ZERO is the "nothing to predict" signal that hides the preview </summary>
        public Vector3 Velocity;
        /// <summary> Gravity the projectile will actually fly under, so the drawn arc matches the shot </summary>
        public Vector3 Gravity;
        /// <summary> Distance the projectile flies flat before gravity starts applying </summary>
        public float AfterDistance;
        /// <summary> Who fired it. The preview flies through the shooter's own colliders instead of stopping on them </summary>
        public GameObject Owner;
    }

    /// <summary> Pushes a predicted trajectory to a listener (E.g. MDisplayTrajectory.DisplayTrajectory) </summary>
    [Serializable] public class TrajectoryEvent : UnityEvent<TrajectoryData> { }
}
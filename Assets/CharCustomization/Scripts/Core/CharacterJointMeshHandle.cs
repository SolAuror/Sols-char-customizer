using UnityEngine;

namespace Sol.CharacterCustomization
{
    // Releases a shared joint-offset mesh when its renderer is destroyed, so the
    // mesh lives as long as the character using it, with or without the rig driver.
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class CharacterJointMeshHandle : MonoBehaviour
    {
        private Mesh sharedMesh;

        public Mesh SharedMesh => sharedMesh;

        internal void Track(Mesh mesh)
        {
            Release();
            sharedMesh = mesh;
        }

        internal void Release()
        {
            if (!ReferenceEquals(sharedMesh, null))
            {
                CharacterBindPoseMeshCache.Release(sharedMesh);
            }

            sharedMesh = null;
        }

        private void OnDestroy()
        {
            Release();
        }
    }
}

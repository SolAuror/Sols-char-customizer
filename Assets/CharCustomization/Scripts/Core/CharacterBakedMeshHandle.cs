using UnityEngine;

namespace Sol.CharacterCustomization
{
    // Releases a shared baked mesh when its renderer is destroyed, so the mesh
    // lives exactly as long as the character using it, with or without the
    // customization manager.
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class CharacterBakedMeshHandle : MonoBehaviour
    {
        private Mesh bakedMesh;

        public Mesh BakedMesh => bakedMesh;

        internal void Track(Mesh mesh)
        {
            Release();
            bakedMesh = mesh;
        }

        internal void Release()
        {
            if (!ReferenceEquals(bakedMesh, null))
            {
                CharacterMeshBaker.ReleaseShared(bakedMesh);
            }

            bakedMesh = null;
        }

        private void OnDestroy()
        {
            Release();
        }
    }
}

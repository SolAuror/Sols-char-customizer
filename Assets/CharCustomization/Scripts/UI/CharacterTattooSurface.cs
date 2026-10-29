using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sol.CharacterCustomization
{
    // Finds where a screen ray meets a character's skin, as a point and normal in skin
    // mesh space. The skin is baked as it is shown (pose and morphs) and the ray tested
    // against its triangles; the triangle and barycentric position hit are then read from
    // the unposed, unmorphed mesh, so the point is the same patch of skin a placement
    // stays on. No collider or physics is involved, so it can run every frame of a drag.
    public sealed class CharacterTattooSurface : IDisposable
    {
        private readonly Mesh baked;
        private readonly List<Vector3> shown = new();
        private Mesh restMesh;
        private Vector3[] restVertices = Array.Empty<Vector3>();
        private Vector3[] restNormals = Array.Empty<Vector3>();
        private int[] restTriangles = Array.Empty<int>();

        public CharacterTattooSurface()
        {
            baked = new Mesh { name = "Tattoo Surface", hideFlags = HideFlags.HideAndDontSave };
            baked.MarkDynamic();
        }

        // Hits the skin as currently shown. mesh is the mesh placements are in: the
        // skin's mesh before any NPC bake (CharacterTattoos uses the same one).
        public bool TryHit(SkinnedMeshRenderer skin, Mesh mesh, Ray ray, out Vector3 point, out Vector3 normal)
        {
            point = default;
            normal = default;
            if (skin == null || mesh == null || !mesh.isReadable)
            {
                return false;
            }

            CacheRest(mesh);
            // With scale, the baked vertices are in the renderer's space less its
            // position and rotation.
            skin.BakeMesh(baked, true);
            baked.GetVertices(shown);
            if (shown.Count != restVertices.Length)
            {
                return false;
            }

            Quaternion toLocal = Quaternion.Inverse(skin.transform.rotation);
            Vector3 origin = toLocal * (ray.origin - skin.transform.position);
            Vector3 direction = toLocal * ray.direction;
            if (!TryNearestTriangle(origin, direction, out int corner, out float u, out float v))
            {
                return false;
            }

            int a = restTriangles[corner];
            int b = restTriangles[corner + 1];
            int c = restTriangles[corner + 2];
            float w = 1f - u - v;
            point = restVertices[a] * w + restVertices[b] * u + restVertices[c] * v;
            normal = restNormals.Length == restVertices.Length
                ? restNormals[a] * w + restNormals[b] * u + restNormals[c] * v
                : Vector3.Cross(restVertices[b] - restVertices[a], restVertices[c] - restVertices[a]);
            normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.forward;
            return true;
        }

        public void Dispose()
        {
            if (baked == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(baked);
            }
            else
            {
                Object.DestroyImmediate(baked);
            }
        }

        // The nearest triangle in front of the ray (Moller-Trumbore), either side facing.
        private bool TryNearestTriangle(Vector3 origin, Vector3 direction, out int hitCorner, out float hitU, out float hitV)
        {
            hitCorner = -1;
            hitU = hitV = 0f;
            float nearest = float.MaxValue;
            for (int corner = 0; corner + 2 < restTriangles.Length; corner += 3)
            {
                Vector3 p0 = shown[restTriangles[corner]];
                Vector3 edge1 = shown[restTriangles[corner + 1]] - p0;
                Vector3 edge2 = shown[restTriangles[corner + 2]] - p0;
                Vector3 p = Vector3.Cross(direction, edge2);
                float determinant = Vector3.Dot(edge1, p);
                if (Mathf.Abs(determinant) < 1e-12f)
                {
                    continue;
                }

                float inverse = 1f / determinant;
                Vector3 t = origin - p0;
                float u = Vector3.Dot(t, p) * inverse;
                if (u < 0f || u > 1f)
                {
                    continue;
                }

                Vector3 q = Vector3.Cross(t, edge1);
                float v = Vector3.Dot(direction, q) * inverse;
                if (v < 0f || u + v > 1f)
                {
                    continue;
                }

                float distance = Vector3.Dot(edge2, q) * inverse;
                if (distance > 0f && distance < nearest)
                {
                    nearest = distance;
                    hitCorner = corner;
                    hitU = u;
                    hitV = v;
                }
            }

            return hitCorner >= 0;
        }

        private void CacheRest(Mesh mesh)
        {
            if (mesh == restMesh)
            {
                return;
            }

            restMesh = mesh;
            restVertices = mesh.vertices;
            restNormals = mesh.normals;
            restTriangles = mesh.triangles;
        }
    }
}

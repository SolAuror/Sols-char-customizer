// Last Change: 9/11/2026 10:05am
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations
{
    /// <summary>Resolves which Step Surface a ground collider belongs to. Shared by the Steps Manager and the Wagon Controller</summary>
    public static class SurfaceResolver
    {
        /// <summary>Texture property names probed on a material, in order (Built-in, URP, HDRP, common custom names)</summary>
        private static readonly string[] MainTextureProperties = { "_MainTex", "_BaseMap", "_BaseColorMap", "_Albedo", "_AlbedoMap" };

        /// <summary>Finds the Surface entry under a world point. Priority: Surface Tag, Terrain Layer, Material texture. Null when nothing matches</summary>
        public static StepSurface Resolve(MStepSurfaces surfaces, Collider ground, Vector3 worldPos, Dictionary<Collider, StepGroundInfo> cache)
        {
            if (surfaces == null || ground == null) return null;

            StepGroundInfo info;

            if (cache != null)
            {
                if (!cache.TryGetValue(ground, out info))
                    cache[ground] = info = ClassifyGround(ground); //Classify the ground collider only once
            }
            else
            {
                info = ClassifyGround(ground);
            }

            //(a) SurfaceTag / ISurface component on the ground — highest priority
            if (info.Tag != null && info.Tag.Surface != null)
            {
                var entry = surfaces.FindBySurfaceID(info.Tag.Surface);
                if (entry != null) return entry;
            }

            //No Textures on the asset → skip the Terrain alphamap sampling and the Material probing entirely
            if (!surfaces.HasTextures) return null;

            //(b) Terrain — dominant splatmap Terrain Layer under the point
            if (info.Data != null)
            {
                var layerTexture = SampleDominantLayer(info, worldPos);
                if (layerTexture != null) return surfaces.FindByTexture(layerTexture);
            }
            //(c) Mesh — the renderer's shared material main texture
            else if (info.Renderer != null)
            {
                var mat = info.Renderer.sharedMaterial; //Always sharedMaterial (no material instancing/leaks)
                var tex = GetMaterialMainTexture(mat);  //Never mat.mainTexture: it logs an error on shaders without _MainTex (Shader Graph)
                if (tex != null) return surfaces.FindByTexture(tex);
            }

            return null; //No match → caller falls back to the asset Default Sound
        }

        /// <summary>Classifies a ground collider once: Surface Tag, Terrain, or Renderer</summary>
        public static StepGroundInfo ClassifyGround(Collider ground)
        {
            var info = new StepGroundInfo { Tag = ground.FindInterface<ISurface>() };

            if (ground is TerrainCollider)
            {
                info.Terrain = ground.GetComponent<Terrain>();
                if (info.Terrain) info.Data = info.Terrain.terrainData;
            }
            else
            {
                info.Renderer = ground.GetComponentInParent<Renderer>();
            }
            return info;
        }

        /// <summary>Dominant Terrain Layer diffuse texture at a world point (1x1 alphamap read)</summary>
        public static Texture SampleDominantLayer(in StepGroundInfo info, Vector3 worldPos)
        {
            var data = info.Data;
            if (data == null || info.Terrain == null) return null;

            var layers = data.terrainLayers;
            if (layers == null || layers.Length == 0) return null;

            //World position → alphamap texel (re-read the terrain position per call: free, and handles moved terrains)
            Vector3 local = worldPos - info.Terrain.GetPosition();
            int x = Mathf.Clamp((int)(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp((int)(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);

            float[,,] weights = data.GetAlphamaps(x, z, 1, 1);

            int dominant = 0;
            float max = 0;
            for (int i = 0; i < weights.GetLength(2); i++)
            {
                if (weights[0, 0, i] > max) { max = weights[0, 0, i]; dominant = i; }
            }

            return dominant < layers.Length && layers[dominant] != null ? layers[dominant].diffuseTexture : null;
        }

        /// <summary>Main or albedo texture of a material without logging on shaders that lack _MainTex</summary>
        public static Texture GetMaterialMainTexture(Material mat)
        {
            if (mat == null) return null;

            foreach (var prop in MainTextureProperties)
            {
                if (mat.HasTexture(prop)) //HasTexture is silent; mat.mainTexture / GetTexture on a missing property logs an error
                {
                    var tex = mat.GetTexture(prop);
                    if (tex != null) return tex;
                }
            }
            return null;
        }
    }
}

// Last Change: 9/17/2026 5:33pm
using UnityEngine;

namespace MalbersAnimations.Utilities
{
    // MWC — Order matters: Custom is index 5, which the [Hide] attributes on SrcBlend/DstBlend key off.
    public enum TrailBlend { None, Additive, AlphaBlend, PremultipliedAlpha, Multiply, Custom }

    // MWC — Preset asset for the Weapon Trail. The MWeaponTrail component is a thin shell: every tuning
    // value lives here so one profile can be shared by many weapons ("Fast Sword", "Heavy Axe", "Magic").
    [CreateAssetMenu(menuName = "Malbers Animations/Effects/Weapon Trail Profile",
                     fileName = "New Weapon Trail Profile", order = -1000)]
    public class WeaponTrailProfile : ScriptableObject
    {
        [Header("Material")]
        [Tooltip("Material used by the trail mesh. Leave empty to use a built-in unlit fallback.\n" +
                 "The shader must be transparent, two sided (Cull Off) and multiply by vertex color")]
        public Material Material;

        // MWC — Property names are exposed because every shader family names its colour differently and
        // guessing is fragile. Empty means "do not touch it", which lets the material keep its own value.
        [Tooltip("Shader property that receives the Tint. Empty leaves the material's own colour alone.\n" +
                 "URP: _BaseColor  |  Sprites-Default & Built-in: _Color  |  Legacy particles: _TintColor")]
        public string ColorProperty = "_BaseColor";

        [Tooltip("Tint pushed into ColorProperty through a MaterialPropertyBlock")]
        public Color Tint = Color.white;



        [Tooltip("Shader property that receives the Emission colour. Empty leaves the material alone.\n" +
                 "URP: _EmissionColor  (the material's Emission toggle must be ON)")]
        public string EmissionProperty = "_EmissionColor";

        [Tooltip("HDR emission colour. RGB is multiplied by the global fade so the glow fades out with " +
                 "the trail. Black means no emission")]
        [ColorUsage(true, true)] public Color Emission = Color.black;

        // MWC — Unlike blending, the clip threshold IS a plain shader uniform, so it can go through the
        // MaterialPropertyBlock and stay per-trail without copying the material.
        [Tooltip("Shader property that receives the Alpha Clip Threshold. Empty leaves the material alone.\n" +
                 "Shader Graph: _AlphaClipThreshold  |  URP Lit/Unlit: _Cutoff\n" +
                 "The material's Alpha Clipping must be ON for this to do anything")]
        public string AlphaClipProperty = "";

        [Tooltip("Alpha clip threshold. Pixels below this alpha are discarded")]
        [Range(0f, 1f)] public float AlphaClip = 0f;


        // MWC — Folding the fade into the colour's alpha is invisible coupling and backfires on shaders that
        // use that alpha as a master opacity or an alpha-clip source: the whole ribbon dims or is discarded
        // outright instead of fading. Turn this OFF to send Tint's alpha through untouched.
        [Tooltip("Multiply the global fade into the ALPHA of ColorProperty.\n" +
                 "Turn OFF when the shader uses that alpha for alpha clipping, or as a master opacity that " +
                 "has to stay at 1 - otherwise fading makes the whole trail vanish")]
        public bool FadeAffectsColorAlpha = true;

        [Tooltip("Drive the threshold with the global fade so the trail DISSOLVES instead of dimming.\n" +
                 "At fade 1 the threshold is AlphaClip; as the trail fades out it rises to 1 and burns away")]
        public bool FadeDrivesAlphaClip = false;

        [Tooltip("Cast shadows from the trail mesh. Almost always off")]
        public bool CastShadows = false;

        // MWC — Blend state is NOT a shader uniform: ShaderLab resolves Blend [_SrcBlend] [_DstBlend] from
        // the Material when it builds the pipeline state, and a MaterialPropertyBlock cannot reach it. So
        // overriding blending means using a real copy of the material. It is opt-in for that reason: None
        // keeps using the shared material asset and costs nothing.
        [Tooltip("Override the material's blending.\n" +
                 "None uses the material asset exactly as authored (no copy, best batching).\n" +
                 "Anything else makes this profile create ONE shared runtime copy of the material, because " +
                 "blend state cannot be driven by a MaterialPropertyBlock")]
        public TrailBlend Blending = TrailBlend.None;

        [Tooltip("Source blend factor")]
        [Hide(nameof(Blending), 5)] public UnityEngine.Rendering.BlendMode SrcBlend = UnityEngine.Rendering.BlendMode.SrcAlpha;

        [Tooltip("Destination blend factor")]
        [Hide(nameof(Blending), 5)] public UnityEngine.Rendering.BlendMode DstBlend = UnityEngine.Rendering.BlendMode.One;


        [Header("Trail Geometry")]

        // MWC — U runs along the trail, V across its width. Which END of the texture lands on the blade
        // depends on how it was authored, so make it switchable instead of guessing.
        [Tooltip("Flip the texture along the trail. Off: U=0 at the blade, U=1 at the tail")]
        public bool FlipU = false;

        // MWC — Age only reaches 1 once the ribbon is a full Duration long. At the start of a swing, and
        // again while it ages out, the tail would stop partway into the texture and land on opaque pixels,
        // which reads as a hard straight cut. Stretching maps the texture across whatever rows are live.
        [Tooltip("Stretch the texture across the whole ribbon, so both ends always reach the edges of the " +
                 "texture. Off maps U by age instead, so a short trail only shows part of the texture")]
        public bool StretchUV = true;

        [Tooltip("Seconds a row stays alive. This is the visual length of the ribbon.\n" +
                 "One row is captured per frame, so a longer Duration also means MORE rows across the " +
                 "swing, which makes the arc rounder")]
        [Min(0.01f)] public float Duration = 0.25f;

        [Tooltip("Row budget. Must be at least Duration x framerate to reach the full length " +
                 "(0.25s at 144 FPS = 36 rows). Caps memory and cost")]
        [Range(2, 256)] public int MaxSegments = 64;

        [Tooltip("Catmull-Rom rows inserted between captured rows at render time.\n" +
                 "This is what keeps fast swings smooth. 0 disables smoothing entirely (cheapest)")]
        [Range(0, 8)] public int Subdivisions = 3;

        // MWC — The render loop usually runs faster than the Animator. Without this the trail captures
        // duplicate rows for every frame where the pose did not change: they eat the row budget and give
        // Catmull-Rom coincident points (zero tangent), which is what makes a trail look faceted at high FPS.
        [Tooltip("Distance the blade must travel before a NEW row is captured. Below this the newest row is " +
                 "refreshed in place, so frames where the Animator did not advance cannot create duplicates")]
        [Min(0)] public float MinDistance = 0.002f;

        // MWC — There is deliberately no Alpha curve. The trail's fade along its length comes from the
        // material's texture (U runs head to tail), so a curve here would just fight it.
        [Tooltip("Width multiplier over the age of a segment (0 = at the blade, 1 = oldest). " +
                 "This is geometry, so it shapes the actual mesh - a texture cannot do it")]
        public AnimationCurve Width = AnimationCurve.Linear(0, 1, 1, 0);

        [Tooltip("Seconds to fade the trail in when Play() is called")]
        [Min(0)] public float FadeIn = 0.03f;

        [Tooltip("Seconds to fade the trail out when Stop() is called. The trail goes dormant afterwards")]
        [Min(0)] public float FadeOut = 0.1f;

        // MWC — Rows are captured once per frame and pruned by age, so the buffer is simply the row budget.
        // Previously this was derived from a fixed capture rate, which threw away real blade positions
        // whenever the framerate exceeded that rate.
        public int Capacity => Mathf.Max(2, MaxSegments);

        /// <summary> Vertex rows actually rendered once Catmull-Rom subdivision is applied. </summary>
        public int RenderRows => (Capacity - 1) * (Subdivisions + 1) + 1;

        #region Blending

        // MWC — one shared copy per profile, so ten weapons using this profile still share a single
        // material and keep batching, rather than instancing one material each.
        [System.NonSerialized] private Material runtimeMaterial;
        [System.NonSerialized] private Material runtimeSource;

        private static readonly int ID_SrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int ID_DstBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int ID_SrcBlendAlpha = Shader.PropertyToID("_SrcBlendAlpha");
        private static readonly int ID_DstBlendAlpha = Shader.PropertyToID("_DstBlendAlpha");

        /// <summary>
        /// The material the trail should render with: the asset itself when Blending is None, otherwise a
        /// shared runtime copy carrying the overridden blend state.
        /// </summary>
        public Material GetMaterial()
        {
            if (Material == null) return null;
            if (Blending == TrailBlend.None) return Material;

            if (runtimeMaterial == null || runtimeSource != Material)
            {
                DestroyRuntimeMaterial();
                runtimeSource = Material;
                runtimeMaterial = new Material(Material)
                {
                    name = $"{Material.name} (Trail Blend)",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            ApplyBlendTo(runtimeMaterial);
            return runtimeMaterial;
        }

        /// <summary> Writes the chosen blend factors onto a material. No-op when Blending is None. </summary>
        public void ApplyBlendTo(Material m)
        {
            if (m == null || Blending == TrailBlend.None) return;

            GetBlendFactors(out var src, out var dst);

            // Names differ a little between pipelines; SetFloat on a property the shader does not declare
            // is a silent no-op, so writing both pairs covers URP, HDRP and Built-in.
            m.SetFloat(ID_SrcBlend, (float)src);
            m.SetFloat(ID_DstBlend, (float)dst);
            m.SetFloat(ID_SrcBlendAlpha, (float)src);
            m.SetFloat(ID_DstBlendAlpha, (float)dst);
        }

        public void GetBlendFactors(out UnityEngine.Rendering.BlendMode src, out UnityEngine.Rendering.BlendMode dst)
        {
            switch (Blending)
            {
                case TrailBlend.Additive:
                    src = UnityEngine.Rendering.BlendMode.SrcAlpha; dst = UnityEngine.Rendering.BlendMode.One; break;
                case TrailBlend.AlphaBlend:
                    src = UnityEngine.Rendering.BlendMode.SrcAlpha; dst = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha; break;
                case TrailBlend.PremultipliedAlpha:
                    src = UnityEngine.Rendering.BlendMode.One; dst = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha; break;
                case TrailBlend.Multiply:
                    src = UnityEngine.Rendering.BlendMode.DstColor; dst = UnityEngine.Rendering.BlendMode.Zero; break;
                default:
                    src = SrcBlend; dst = DstBlend; break;
            }
        }

        private void DestroyRuntimeMaterial()
        {
            if (runtimeMaterial == null) return;
            if (Application.isPlaying) Destroy(runtimeMaterial); else DestroyImmediate(runtimeMaterial);
            runtimeMaterial = null;
            runtimeSource = null;
        }

        private void OnDisable() => DestroyRuntimeMaterial(); // MWC — domain reload / quit

        #endregion

        private void OnValidate() // MWC — keep the derived sizes sane while editing
        {
            if (Duration < 0.01f) Duration = 0.01f;

            // MWC — push blend edits onto the existing copy rather than destroying it, so a renderer
            // already holding the reference does not end up pointing at a dead material.
            if (runtimeMaterial != null) ApplyBlendTo(runtimeMaterial);
        }
    }
}

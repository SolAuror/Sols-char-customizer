#ifndef SOL_TATTOO_PROJECTION_INCLUDED
#define SOL_TATTOO_PROJECTION_INCLUDED

// Where one tattoo lands on the skin, for the Stamp and Outline passes of
// Hidden/Sol/Tattoo Bake: each texel of the layer is taken from its bind-pose position
// into the tattoo's box, flat or wrapped round a limb.

float4x4 _TattooMeshToDecal;
float3 _TattooDirection;
half4 _TattooInk;
// Curved tattoos: the axis (xyz) and radius (w, 0 for flat) of the cylinder they wrap
// around, the point where it touches the skin, and the direction out of the skin there,
// across the axis.
float4 _TattooWrap;
float3 _TattooWrapAnchor;
float3 _TattooWrapOutward;
TEXTURE2D(_TattooDesign);
SAMPLER(sampler_TattooDesign);

// How far round the cylinder the design may wrap each way, in radians, and how far the
// skin may stand off it, in radii, before it belongs to another surface.
static const float WrapLimit = 2.8;
static const float WrapShell = 0.6;

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionOS : TEXCOORD0;
    float3 normalOS : TEXCOORD1;
};

Varyings StampVertex(UvAttributes input)
{
    Varyings output;
    output.positionCS = UvToClip(input.uv);
    output.positionOS = input.positionOS;
    output.normalOS = input.normalOS;
    return output;
}

// The texel's place in the design (xy from -0.5 to 0.5, z its depth in the box), and
// how squarely its skin faces the tattoo. Texels the tattoo does not reach are clipped.
void ProjectTattoo(Varyings input, out float3 decal, out half facing)
{
    float3 normal = normalize(input.normalOS);
    float3 projected = input.positionOS;
    // Flat tattoos stop at the box's depth; curved ones at the cylinder's shell.
    float depthLimit = 0.5;
    UNITY_BRANCH
    if (_TattooWrap.w > 0.0)
    {
        // Unrolls the skin around the axis onto the plane touching it at the tattoo,
        // like a label peeled off a bottle: the distance round the cylinder becomes
        // distance across the plane, so the design keeps its size all the way round
        // instead of stretching where the limb turns away.
        float3 axis = _TattooWrap.xyz;
        float radius = _TattooWrap.w;
        float3 offset = input.positionOS - (_TattooWrapAnchor - _TattooWrapOutward * radius);
        float along = dot(offset, axis);
        float3 radial = offset - axis * along;
        float distance = length(radial);
        float3 outward = radial / max(distance, 1e-5);
        float3 around = cross(axis, _TattooWrapOutward);
        float turn = atan2(dot(outward, around), dot(outward, _TattooWrapOutward));
        clip(WrapLimit - abs(turn));
        clip(WrapShell * radius - abs(distance - radius));
        projected = _TattooWrapAnchor + around * (turn * radius) + axis * along;
        // Skin facing out from the axis takes the ink; inner folds do not.
        facing = dot(normal, outward);
        depthLimit = 1e6;
    }
    else
    {
        // Surfaces turning away from the projector fade out instead of smearing the
        // design, and the far side of a limb inside the box faces away entirely.
        facing = dot(normal, -_TattooDirection);
    }

    decal = mul(_TattooMeshToDecal, float4(projected, 1.0)).xyz;
    clip(0.5 - abs(decal.xy));
    clip(depthLimit - abs(decal.z));
}

#endif

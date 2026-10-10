// HD anime cel shading shared by the avatar / garment / hair / disk shaders (AGENTS.md "Art quality bar").
// Colour stays the painted texture (flat anime paint); light only decides WHICH band a pixel is in:
//  - one crisp lit / shadow terminator from the main light (a fixed key light when the scene has none), shadow tinted
//    cool like anime shadow paint, never multiplied by the light colour (AR stages vary wildly);
//  - a thin rim light on the lit side;
//  - an ink outline pass (inverted hull, constant width in pixels).
#ifndef WRLDZ_ANIME_TOON_INCLUDED
#define WRLDZ_ANIME_TOON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Main light direction (world), or a key light from the upper front left when there is no directional light.
float3 AnimeKeyDir(float3 viewDirWS)
{
    Light l = GetMainLight();
    half lum = dot(l.color, half3(0.2126h, 0.7152h, 0.0722h));
    float3 fallback = normalize(viewDirWS + float3(-0.35, 0.9, 0.0));
    return lum > 0.02h ? normalize(l.direction) : fallback;
}

// col: the painted colour. shadowAmt: extra darkening of the shadow band beyond the cool tint (0 = no shadow band;
// 0.08 = anime default, shadow ~ 0.73 of lit).
half3 AnimeShade(half3 col, float3 normalWS, float3 viewDirWS, half shadowAmt, half rimAmt)
{
    float3 n = normalize(normalWS);
    float3 v = normalize(viewDirWS);
    float3 l = AnimeKeyDir(v);
    float ndl = dot(n, l) * 0.5 + 0.5;                                     // half lambert
    half lit = (half)smoothstep(0.43, 0.47, ndl);                          // one crisp terminator, anti-aliased
    half3 shadowTint = half3(0.80h, 0.78h, 0.92h);                        // cool anime shadow paint
    half3 shadowCol = col * lerp(half3(1, 1, 1), shadowTint * (1.0h - shadowAmt), saturate(shadowAmt * 5.0h));
    half3 c = lerp(shadowCol, col, lit);
    half fres = (half)smoothstep(0.64, 0.70, 1.0 - saturate(dot(n, v)));
    c += (col * 0.35h + 0.06h) * fres * lit * rimAmt;                     // thin rim on the lit edge
    return c;
}

// Anime hair "angel ring": a thin highlight band where the half vector grazes the surface.
half AnimeHairSheen(float3 normalWS, float3 viewDirWS)
{
    float3 n = normalize(normalWS);
    float3 v = normalize(viewDirWS);
    float3 h = normalize(AnimeKeyDir(v) + v);
    return (half)smoothstep(0.90, 0.93, dot(n, h)) * (half)(1.0 - smoothstep(0.96, 0.99, dot(n, h)));
}

// Edge light for a silhouette: a coloured rim all round the figure (both the lit and the shadow side), so a flat
// dark body still reads against a dark street. 0 inside, 1 on the grazing edge.
half AnimeEdgeLight(float3 normalWS, float3 viewDirWS)
{
    float3 n = normalize(normalWS);
    float3 v = normalize(viewDirWS);
    return (half)smoothstep(0.58, 0.78, 1.0 - saturate(dot(n, v)));
}

// Outline hull: push the vertex out along its normal by widthPx pixels on screen.
float4 AnimeOutlineCS(float3 positionOS, float3 normalOS, float widthPx)
{
    float4 cs = TransformObjectToHClip(positionOS);
    float3 nWS = TransformObjectToWorldNormal(normalOS);
    float2 nCS = mul((float3x3)UNITY_MATRIX_VP, nWS).xy;
    float len = max(length(nCS), 1e-5);
    cs.xy += (nCS / len) * (widthPx * 2.0 / _ScreenParams.xy) * cs.w;
    return cs;
}

#endif

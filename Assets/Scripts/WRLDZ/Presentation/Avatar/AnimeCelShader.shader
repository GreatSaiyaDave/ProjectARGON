// 2D anime character sprites that EXIST IN 3D: the painted sprite is lit and parallaxed by a
// per-pixel DEPTH + NORMAL map (build_formmap_v2.py for the DK sets, build_normal_depth.py for
// the originals), so the flat art gains real form — the cel terminator follows the body, not the
// whole billboard. No ink outline.
//
// _KeyBias: a camera-facing plane lit by a frontal key is uniformly lit, so form alone is not
// enough — sets that need form pull the key sideways with AnimeSpriteKeyDir (0 = fixed lateral
// portrait key, 1 = plain scene key, the default, unchanged for every pre-existing set).
//
// _NormalMap:  RGB = tangent-space normal (normal*0.5+0.5),  A = depth (0..1).
// The billboard is yaw-only, so the depth parallax shows as the camera looks from above/below.
// TransparentCutout for the sprite alpha; receives URP shadows via the shadow pass.
Shader "WRLDZ/AnimeCel"
{
    Properties
    {
        _BaseMap ("Sprite", 2D) = "white" {}
        _BaseColor ("Tint / alpha", Color) = (1, 1, 1, 1)
        _NormalMap ("Normal (RGB) + depth (A)", 2D) = "bump" {}
        _NormalStrength ("Form-light strength", Range(0, 1)) = 0.8
        _Parallax ("Depth parallax", Range(0, 0.05)) = 0.015
        _KeyBias ("Scene-key influence (0 = fixed portrait key)", Range(0, 1)) = 1
        _ShadowAmt ("Anime shadow band depth", Range(0, 0.5)) = 0.08
        _RimAmt ("Rim light", Range(0, 1)) = 0.6
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [HideInInspector] _Surface ("Surface", Float) = 0
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _NormalStrength, _Parallax;
            half _ShadowAmt, _RimAmt, _KeyBias;
            half _Surface, _SrcBlend, _DstBlend, _ZWrite;
        CBUFFER_END

        // Depth-parallaxed sprite UV, shared by every pass so the cutout stays consistent.
        float2 AnimeParallaxUV(float2 uv, float3 positionWS)
        {
            half depth = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv).a;
            float3 vOS = normalize(TransformWorldToObjectDir(GetWorldSpaceViewDir(positionWS)));
            vOS.xy /= max(abs(vOS.z), 0.25);          // into the surface; yaw billboard keeps z ~ 1
            return uv - vOS.xy * (depth * _Parallax);
        }

        struct AttributesD { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct VaryingsD { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; float2 uv : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };

        VaryingsD vertDepth(AttributesD v)
        {
            VaryingsD o = (VaryingsD)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "AnimeCel"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "AnimeToon.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;    // quad local +X in world
                float3 bitangentWS : TEXCOORD4;  // quad local +Y in world
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.tangentWS = TransformObjectToWorldDir(float3(1, 0, 0));
                o.bitangentWS = TransformObjectToWorldDir(float3(0, 1, 0));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 viewDir = GetWorldSpaceViewDir(i.positionWS);
                float2 uv = AnimeParallaxUV(i.uv, i.positionWS);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                clip(tex.a - 0.5h);

                // Per-pixel form normal: blend the flat quad normal toward the mapped normal.
                half4 nSample = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv);
                half3 nT = normalize(half3((nSample.rgb * 2.0h - 1.0h).xy * _NormalStrength, 1.0h));
                float3 nWS = normalize(i.tangentWS * nT.x + i.bitangentWS * nT.y + i.normalWS * nT.z);

                half3 col = AnimeShadeKey(tex.rgb, nWS, viewDir, _ShadowAmt, _RimAmt,
                                          AnimeSpriteKeyDir(viewDir, _KeyBias));
                return half4(col * _BaseColor.rgb, _BaseColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            half fragDepth(VaryingsD i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = AnimeParallaxUV(i.uv, i.positionWS);
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a - 0.5h);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            void fragNormals(VaryingsD i, bool front : SV_IsFrontFace, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = AnimeParallaxUV(i.uv, i.positionWS);
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a - 0.5h);
                half4 nSample = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv);
                half3 nT = normalize(half3((nSample.rgb * 2.0h - 1.0h).xy * _NormalStrength, 1.0h));
                float3 n = normalize(TransformObjectToWorldDir(float3(1, 0, 0)) * nT.x
                                   + TransformObjectToWorldDir(float3(0, 1, 0)) * nT.y
                                   + i.normalWS * nT.z);
                if (!front) n = -n;
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(n);
                outNormalWS = half4(half3(PackFloat2To888(saturate(oct * 0.5 + 0.5))), 0.0);
            #else
                outNormalWS = half4(n, 0.0);
            #endif
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}

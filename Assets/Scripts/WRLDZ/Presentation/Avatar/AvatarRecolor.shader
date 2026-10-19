// Player avatar garments / body (OverworldAvatar3d), AI duelists, spirit companions and duel disk models: the Meshy base
// colour with up to three colour regions swapped for the player's paint, drawn as HD anime cel shading (AnimeToon.hlsl):
// one crisp cool shadow band from the key light, a thin rim, and an ink outline pass.
// _MaskMap (linear, from tools/custom/custom_body.py) holds the region weights (garments: R main, G accent, B trim;
// body: R skin, G undersuit). A region's new colour keeps the painted shading: detail = (texture luminance / the
// region's mean luminance)^0.75, so a black coat painted white still shows its folds.
// _ColorA/B/C.a = 1 turns a region's paint on. _HideMap / _HideOn: body texels covered by an outfit are discarded (in
// the outline too). _BaseColor.a + the blend properties carry OverworldAvatar3d's presence.
Shader "WRLDZ/AvatarRecolor"
{
    Properties
    {
        _BaseMap ("Base", 2D) = "white" {}
        _MaskMap ("Region mask (linear)", 2D) = "black" {}
        _BaseColor ("Tint / alpha", Color) = (1, 1, 1, 1)
        _ColorA ("Region R paint (a = on)", Color) = (1, 1, 1, 0)
        _ColorB ("Region G paint (a = on)", Color) = (1, 1, 1, 0)
        _ColorC ("Region B paint (a = on)", Color) = (1, 1, 1, 0)
        _RefLum ("Region mean luminance (linear)", Vector) = (0.2, 0.2, 0.2, 0)
        _HideMap ("Hidden under an outfit (R, body UV space)", 2D) = "black" {}
        _ScalpMap ("Head region (R, body UV space)", 2D) = "black" {}
        _ScalpColor ("Painted hairline colour (a = on)", Color) = (0, 0, 0, 0)
        _HideOn ("Use the hide map", Float) = 0
        // the animated face (AvatarAnimeFace, 2026-10-18): the painted eyes / brows / nose / mouth texels of the body
        // albedo become plain skin where this mask is set, so the drawn features replace them instead of sitting on them
        _FaceFlatMap ("Painted face features (R, body UV space)", 2D) = "black" {}
        _FaceFlatColor ("Flat skin colour under them (a = on)", Color) = (0.98, 0.77, 0.58, 0)
        _ShadowAmt ("Anime shadow band depth", Range(0, 0.5)) = 0.08
        _RimAmt ("Rim light", Range(0, 1)) = 0.6
        _OutlineWidth ("Ink outline width (px)", Range(0, 4)) = 1.4
        _OutlineDark ("Ink = base colour x", Range(0, 1)) = 0.28
        _UseTexAlpha ("Alpha from the base map (decals)", Float) = 0
        _RimColor ("Silhouette edge light (a = strength)", Color) = (0, 0, 0, 0)
        // Meshy sculpts (2026-10-18) ship their own tangent-space normal map: the fine folds and seams Meshy painted
        // bend the cel shadow band and rim exactly as they do in the owner's Blender view
        _NormalMap ("Normal map (tangent space)", 2D) = "bump" {}
        _NormalOn ("Use the normal map", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (cloth shells render both sides)", Float) = 2
        [HideInInspector] _Surface ("Surface", Float) = 0
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
        TEXTURE2D(_HideMap); SAMPLER(sampler_HideMap);
        TEXTURE2D(_ScalpMap); SAMPLER(sampler_ScalpMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        TEXTURE2D(_FaceFlatMap); SAMPLER(sampler_FaceFlatMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ColorA, _ColorB, _ColorC, _ScalpColor, _FaceFlatColor;
            float4 _RefLum;
            half _HideOn, _ShadowAmt, _RimAmt, _OutlineWidth, _OutlineDark, _UseTexAlpha;
            half4 _RimColor;
            half _NormalOn;
            half _Surface, _SrcBlend, _DstBlend, _ZWrite;
        CBUFFER_END

        // the shading normal: the sculpt's normal map (when the material carries one) over the vertex normal
        float3 ShadingNormal(float3 normalWS, float4 tangentWS, float2 uv)
        {
            float3 n = normalize(normalWS);
            if (_NormalOn < 0.5h) return n;
            half3 tn = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv));
            float3 t = normalize(tangentWS.xyz - n * dot(n, tangentWS.xyz));
            float3 b = cross(n, t) * tangentWS.w;
            return normalize(tn.x * t + tn.y * b + tn.z * n);
        }

        half3 Paint(half3 col, half lum, half weight, half4 paint, float refLum)
        {
            half detail = clamp(pow(max(lum, 1e-4) / max(refLum, 1e-4), 0.75), 0.35, 1.6);
            return lerp(col, paint.rgb * detail, saturate(weight * paint.a));
        }

        // the painted (recoloured) surface colour at uv; clips body texels hidden under an outfit
        half PaintedAlpha(float2 uv)
        {
            return _UseTexAlpha > 0.5h ? SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a : 1.0h;
        }

        half3 PaintedColour(float2 uv)
        {
            // the body under a worn outfit is not drawn (tools/custom/outfit_build.py bakes which faces)
            if (_HideOn > 0.5h && SAMPLE_TEXTURE2D(_HideMap, sampler_HideMap, uv).r > 0.5h) discard;
            half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
            half3 m = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv).rgb;
            // the face's painted features give way to plain skin under the drawn, animated ones (AvatarAnimeFace): the
            // texel becomes the mean skin colour at the skin region's reference luminance (so the skin paint lands flat,
            // detail = 1), and the region mask becomes pure skin there (the base bodies carry their eye whites and
            // lashes as undersuit / other regions, not as albedo)
            half flat = SAMPLE_TEXTURE2D(_FaceFlatMap, sampler_FaceFlatMap, uv).r * _FaceFlatColor.a;
            half flatLum = max(dot(_FaceFlatColor.rgb, half3(0.2126h, 0.7152h, 0.0722h)), 1e-4h);
            tex.rgb = lerp(tex.rgb, _FaceFlatColor.rgb * (_RefLum.x / flatLum), flat);
            m = lerp(m, half3(1.0h, 0.0h, 0.0h), flat);
            half lum = dot(tex.rgb, half3(0.2126h, 0.7152h, 0.0722h));
            half3 col = tex.rgb;
            // the body texture paints a short-hair cap inside the G (undersuit) region: on the head that part takes the
            // hair colour, not the shirt's undersuit (a beige / lavender band under hats, scripts/scalp_maps.py)
            half head = SAMPLE_TEXTURE2D(_ScalpMap, sampler_ScalpMap, uv).r * _ScalpColor.a;
            col = Paint(col, lum, m.r, _ColorA, _RefLum.x);
            col = Paint(col, lum, m.g * (1.0h - head), _ColorB, _RefLum.y);
            col = Paint(col, lum, m.b, _ColorC, _RefLum.z);
            col = Paint(col, lum, m.g * head, half4(_ScalpColor.rgb, 1.0h), _RefLum.y);
            return col;
        }

        // what the depth prepasses must skip, same as the colour pass: body texels hidden under an outfit, decal alpha
        void DepthClip(float2 uv)
        {
            if (_HideOn > 0.5h && SAMPLE_TEXTURE2D(_HideMap, sampler_HideMap, uv).r > 0.5h) discard;
            if (_UseTexAlpha > 0.5h && SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a < 0.5h) discard;
        }

        struct AttributesD { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct VaryingsD { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

        VaryingsD vertDepth(AttributesD v)
        {
            VaryingsD o = (VaryingsD)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "AvatarRecolor"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "AnimeToon.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; float4 tangentWS : TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.tangentWS = float4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 col = PaintedColour(i.uv);
                float3 viewDir = GetWorldSpaceViewDir(i.positionWS);
                // two-sided cloth (_Cull Off): the inside of a coat or skirt is shaded with the flipped normal and a
                // little darker, so it reads as lining instead of piecewise shadow blotches
                float3 nShade = ShadingNormal(i.normalWS, i.tangentWS, i.uv);
                float3 n = front ? nShade : -nShade;
                col = AnimeShade(col, n, viewDir, _ShadowAmt, _RimAmt);
                if (!front) col *= 0.78h;
                col = col * _BaseColor.rgb + _RimColor.rgb * (AnimeEdgeLight(n, viewDir) * _RimColor.a);
                return half4(col, _BaseColor.a * PaintedAlpha(i.uv));
            }
            ENDHLSL
        }

        Pass
        {
            Name "AnimeOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Front
            // the ink hull sits a little behind the surface it outlines: on two-sided cloth it lay at the lining's depth
            // and z-fought with it as dark blotches inside open coats and skirts
            Offset 2, 4

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "AnimeToon.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = AnimeOutlineCS(v.positionOS.xyz, v.normalOS, _OutlineWidth);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_OutlineWidth <= 0.01h) discard;
                half3 col = PaintedColour(i.uv);
                return half4(col * _OutlineDark * _BaseColor.rgb, _BaseColor.a);
            }
            ENDHLSL
        }

        // depth prepasses (SSAO reads the depth-normals texture): without them the Fallback's passes wrote what the colour
        // pass discards, so a darkening halo could follow geometry nobody sees
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
                DepthClip(i.uv);
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
                DepthClip(i.uv);
                float3 n = normalize(front ? i.normalWS : -i.normalWS);
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

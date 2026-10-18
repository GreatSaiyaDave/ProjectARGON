// Player hairstyles (OverworldAvatar3d, Resources/WRLDZ/Avatar3D/Hair), HD anime cel shading (AnimeToon.hlsl: shadow
// band, rim, an "angel ring" sheen, ink outline pass). The mesh carries its colour zones
// (tools/custom/hair_build.py): vertex colour R tip, G fringe/bangs, B streak, A side (0 right .. 1 left), and
// UV1.x = the linear luminance of the vertex's original colour, so detail = (texture luminance / UV1.x)^0.75 keeps
// the painted lock shading at ~1 whatever colour the style was generated in.
// _Original = 1 draws the style's own painted colours. Otherwise root, then split side, tips, streaks, fringe on top;
// _Zones = (tip, fringe, streak, split) on/off.
Shader "WRLDZ/AvatarHair"
{
    Properties
    {
        _BaseMap ("Base", 2D) = "white" {}
        _BaseColor ("Tint / alpha", Color) = (1, 1, 1, 1)
        _Original ("Original colours", Float) = 1
        _HairRoot ("Root", Color) = (0.1, 0.1, 0.12, 1)
        _HairTip ("Tip", Color) = (0.8, 0.27, 0.5, 1)
        _HairFringe ("Fringe", Color) = (0.9, 0.72, 0.3, 1)
        _HairStreak ("Streak", Color) = (0.9, 0.72, 0.3, 1)
        _HairSide ("Split side", Color) = (0.25, 0.8, 1, 1)
        _Zones ("Tip, fringe, streak, split on", Vector) = (0, 0, 0, 0)
        _ShadowAmt ("Anime shadow band depth", Range(0, 0.5)) = 0.1
        _RimAmt ("Rim light", Range(0, 1)) = 0.5
        _Sheen ("Hair sheen", Range(0, 1)) = 0.35
        _OutlineWidth ("Ink outline width (px)", Range(0, 4)) = 1.3
        _OutlineDark ("Ink = hair colour x", Range(0, 1)) = 0.3
        _HatClip ("Hat clip plane (world xyz normal, w)", Vector) = (0, 1, 0, 0)
        _HatClipOn ("Clip the hair inside a worn hat", Float) = 0
        _HatTuckC ("Tuck ellipsoid centre (world xyz, w = band m)", Vector) = (0, 0, 0, 0.04)
        _HatTuckX ("Tuck axis X / radius", Vector) = (0, 0, 0, 0)
        _HatTuckY ("Tuck axis Y / radius", Vector) = (0, 0, 0, 0)
        _HatTuckZ ("Tuck axis Z / radius", Vector) = (0, 0, 0, 0)
        _HatTuckOn ("Tuck the hair under a crowned hat's rim", Float) = 0
        // Meshy hair sculpts (2026-10-18) ship a tangent-space normal map: the strand detail Meshy painted shapes the
        // cel band and sheen as in the owner's Blender view
        _NormalMap ("Normal map (tangent space)", 2D) = "bump" {}
        _NormalOn ("Use the normal map", Float) = 0
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
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _HairRoot, _HairTip, _HairFringe, _HairStreak, _HairSide;
            half4 _Zones;
            float4 _HatClip;
            half _HatClipOn;
            float4 _HatTuckC, _HatTuckX, _HatTuckY, _HatTuckZ;
            half _HatTuckOn;
            half _Original, _ShadowAmt, _RimAmt, _Sheen, _OutlineWidth, _OutlineDark;
            half _NormalOn;
            half _Surface, _SrcBlend, _DstBlend, _ZWrite;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float2 uv1 : TEXCOORD1;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

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

        half3 HairColour(float2 uv, half4 zones, float refLum)
        {
            half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
            half3 col = tex.rgb;
            if (_Original < 0.5h)
            {
                half lum = dot(tex.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                half detail = clamp(pow(max(lum, 1e-4) / max(refLum, 1e-3), 0.75), 0.4, 1.55);
                half side = smoothstep(0.35h, 0.65h, zones.a);
                col = _HairRoot.rgb;
                col = lerp(col, _HairSide.rgb, side * _Zones.w);
                col = lerp(col, _HairTip.rgb, saturate(zones.r) * _Zones.x);
                col = lerp(col, _HairStreak.rgb, saturate(zones.b) * _Zones.z);
                col = lerp(col, _HairFringe.rgb, saturate(zones.g) * _Zones.y);
                col *= detail;
            }
            return col;
        }

        // under a crowned hat the hair just below the rim is drawn in toward the head (an ellipsoid round the cranium,
        // ~2 cm out, where the hat's rim sits), fading out over _HatTuckC.w below the rim plane: the strands tuck under
        // the rim instead of standing out as a shelf with a flat cut showing (review 2026-10-04)
        float3 TuckUnderHat(float3 p)
        {
            if (_HatTuckOn < 0.5h || _HatClipOn < 0.5h) return p;
            float below = -(dot(_HatClip.xyz, p) + _HatClip.w);
            float f = 1.0 - smoothstep(0.0, max(_HatTuckC.w, 1e-3), below);
            if (f <= 0.0) return p;
            float3 d = p - _HatTuckC.xyz;
            float rho = length(float3(dot(d, _HatTuckX.xyz), dot(d, _HatTuckY.xyz), dot(d, _HatTuckZ.xyz)));
            if (rho <= 1.0) return p;
            return _HatTuckC.xyz + d * lerp(1.0, 1.0 / rho, f);
        }

        // a crowned hat hides only the hair above its rim plane (fringe, sideburns and long hair below it stay)
        void ClipUnderHat(float3 positionWS)
        {
            if (_HatClipOn > 0.5h && dot(_HatClip.xyz, positionWS) + _HatClip.w > 0.0) discard;
        }

        struct VaryingsD { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

        VaryingsD vertDepth(Attributes v)
        {
            VaryingsD o = (VaryingsD)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS = TuckUnderHat(TransformObjectToWorld(v.positionOS.xyz));
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "AvatarHair"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "AnimeToon.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 zones : TEXCOORD1;
                float refLum : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                float4 tangentWS : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TuckUnderHat(TransformObjectToWorld(v.positionOS.xyz));
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.tangentWS = float4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.zones = v.color;
                o.refLum = v.uv1.x;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                ClipUnderHat(i.positionWS);
                half3 col = HairColour(i.uv, i.zones, i.refLum);
                float3 view = GetWorldSpaceViewDir(i.positionWS);
                float3 n = ShadingNormal(i.normalWS, i.tangentWS, i.uv);
                half3 shaded = AnimeShade(col, n, view, _ShadowAmt, _RimAmt);
                shaded += (col * 0.6h + 0.25h) * AnimeHairSheen(n, view) * _Sheen;
                return half4(shaded * _BaseColor.rgb, _BaseColor.a);
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

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "AnimeToon.hlsl"

            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 zones : TEXCOORD1; float refLum : TEXCOORD2; float3 positionWS : TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TuckUnderHat(TransformObjectToWorld(v.positionOS.xyz));
                o.positionCS = AnimeOutlineCS(TransformWorldToObject(o.positionWS), v.normalOS, _OutlineWidth);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.zones = v.color;
                o.refLum = v.uv1.x;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_OutlineWidth <= 0.01h) discard;
                ClipUnderHat(i.positionWS);
                return half4(HairColour(i.uv, i.zones, i.refLum) * _OutlineDark * _BaseColor.rgb, _BaseColor.a);
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
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            half fragDepth(VaryingsD i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                ClipUnderHat(i.positionWS);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

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
                ClipUnderHat(i.positionWS);
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

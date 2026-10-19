// WRLDZ/SpiritSuperAura — the Spirit Dueler's aura, hugging the disk's own silhouette.
// SpiritDuelerAura renders the disk into _MaskTex every frame from the viewer's eye, through the same window this
// quad covers (pass "SpiritAuraMask"), so mask texel (u, v) lies exactly behind quad point (u, v). The mask's mips
// are progressively blurred copies of the silhouette, i.e. cheap dilations. Then:
//  · anime: two crisp cel bands around the outline — the Spirit colour outward, white-hot next to the body — whose
//    lookup is pulled DOWN by rising ridged noise, so the outline shoots up into sharp flame tongues;
//    plus a thin rim light just inside the silhouette.
//  · Skyrim: a wide soft haze around it all with slow wisps drifting up through it.
// Additive (One One): it only adds light. The body itself stays clear (the mask hollows it out).
//
// Lives in a Resources folder so player builds include it. Single-pass-instanced stereo safe.
Shader "WRLDZ/SpiritSuperAura"
{
    Properties
    {
        _MaskTex ("Silhouette mask (mipmapped)", 2D) = "black" {}
        _Color ("Aura", Color) = (1, 0.82, 0.2, 1)
        _CoreColor ("Hot edge", Color) = (1, 1, 0.92, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Speed ("Rise speed", Float) = 1.4
        _Seed ("Seed", Float) = 0
        _TongueUV ("Tongue height (UV)", Float) = 0.12
        _Mips ("Mip: hot, flame, haze", Vector) = (1.6, 3.2, 5.0, 0)
        _Haze ("Haze", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MaskTex);
        SAMPLER(sampler_MaskTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MaskTex_ST;
            half4 _Color;
            half4 _CoreColor;
            half _Intensity;
            float _Speed;
            float _Seed;
            float _TongueUV;
            float4 _Mips;
            half _Haze;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "SpiritSuperAura"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                return Noise(p) * 0.55 + Noise(p * 2.07 + 5.3) * 0.3 + Noise(p * 4.13 + 11.9) * 0.15;
            }

            float Mask(float2 uv, float mip)
            {
                return SAMPLE_TEXTURE2D_LOD(_MaskTex, sampler_MaskTex, uv, mip).r;
            }

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.uv;
                float t = _Time.y * _Speed + _Seed;

                // Flame tongues: read the silhouette BELOW this pixel by a rising, vertically stretched ridged
                // noise. Where that lands on the body, this pixel lights up — every tongue points up.
                float n = Fbm(float2(uv.x * 16.0, uv.y * 3.2 - t * 1.4));
                float ridge = pow(saturate(1.0 - abs(n * 2.0 - 1.0)), 2.5);
                float tongue = (0.3 * n + 0.7 * ridge) * _TongueUV;
                float2 uq = uv - float2((n - 0.5) * _TongueUV * 0.18, tongue);

                float body = Mask(uv, 0.0);
                float outside = 1.0 - body;
                float hotM = Mask(uq, _Mips.x);
                float flameM = Mask(uq, _Mips.y);

                // anime cel bands: crisp, screen-space anti-aliased thresholds
                float aaF = max(fwidth(flameM), 1e-3);
                float flame = smoothstep(0.1 - aaF, 0.1 + aaF, flameM);
                float aaH = max(fwidth(hotM), 1e-3);
                float hot = smoothstep(0.3 - aaH, 0.3 + aaH, hotM);
                // thin rim light just inside the silhouette
                float innerEdge = saturate((1.0 - Mask(uv, 1.2)) * 3.0) * body;

                // Skyrim: a soft haze well past the flames, with wisps drifting up through it
                float haze = smoothstep(0.0, 0.35, Mask(uv - float2(0.0, tongue * 0.5 + 0.015), _Mips.z));
                float wisp = Fbm(float2(uv.x * 7.0 + sin(uv.y * 6.0 + t * 0.6) * 0.35, uv.y * 5.0 - t * 0.55));
                wisp = smoothstep(0.55, 0.85, wisp);

                half3 col = _Color.rgb * (half)(flame * outside * 0.55)
                          + _CoreColor.rgb * (half)(hot * outside * 0.85 + innerEdge * 0.5)
                          + _Color.rgb * (half)(haze * outside * (0.1 + 0.4 * wisp) * _Haze);
                // alpha = the light it adds (was 1): with Blend One One a constant 1 made the whole quad opaque wherever the
                // target keeps alpha - a black box round the disk in the customizer's transparent preview (2026-10-19)
                col *= _Intensity;
                return half4(col, saturate(max(col.r, max(col.g, col.b))));
            }
            ENDHLSL
        }

        // Drawn only by SpiritDuelerAura's command buffer (URP never draws this LightMode): the body as flat white.
        Pass
        {
            Name "SpiritAuraMask"
            Tags { "LightMode" = "SpiritAuraMask" }
            Blend One Zero
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vertMask
            #pragma fragment fragMask

            float4 vertMask(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 fragMask() : SV_Target
            {
                return half4(1, 1, 1, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

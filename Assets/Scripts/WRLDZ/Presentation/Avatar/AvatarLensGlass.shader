// Eyewear glass (2026-10-19, owner: "Please clear Weevil's lenses. There should be a visible asset showing the glasses
// lenses are there ... a floating asset on the lenses that moves that represents reflection/refraction"). Drawn as a
// second material on the eyewear sculpt's own renderer, so it rides the same skinned mesh: only the lens texels
// (_LensMap, Tools/avatar/lens_mask.py) draw; the frame's AvatarRecolor material discards the same texels, so the eyes
// behind are seen through the glass. On the glass: a faint cool tint, a fresnel sheen toward grazing angles, a thin
// refraction rim inside the frame, and the anime glare - one broad and one thin diagonal bar with hard inked edges (the
// series draws Weevil's lenses that way) that slide across the lens against the head's turn and sweep across now and
// then (EyewearGlass feeds the lens frames and the sweep every frame).
Shader "WRLDZ/AvatarLensGlass"
{
    Properties
    {
        _LensMap ("Lens texels (R, the item's UV space)", 2D) = "black" {}
        _Alpha ("Presence", Float) = 1
        _Tint ("Glass tint (a = base opacity)", Color) = (0.86, 0.93, 1.0, 0.10)
        _Sheen ("Fresnel sheen", Float) = 0.35
        _RimLight ("Refraction rim (rgb, a = opacity)", Color) = (0.92, 0.97, 1.0, 0.45)
        _Glare ("Glare (rgb, a = opacity)", Color) = (1, 1, 1, 0.92)
        _GlareSlide ("Glare slide per unit of view turn", Float) = 0.55
        // per frame (EyewearGlass): xyz world centre, w radius; xyz outward normal; the head's right; x sweep offset
        _LensC0 ("Lens 0 centre / radius", Vector) = (0, 0, 0, 0.02)
        _LensC1 ("Lens 1 centre / radius", Vector) = (0, 0, 0, 0.02)
        _LensN0 ("Lens 0 normal", Vector) = (0, 0, 1, 0)
        _LensN1 ("Lens 1 normal", Vector) = (0, 0, 1, 0)
        _LensRight ("Head right", Vector) = (1, 0, 0, 0)
        _Sweep ("Glare sweep (x offset, y extra opacity)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "LensGlass"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_LensMap); SAMPLER(sampler_LensMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _LensMap_ST;
                float _Alpha, _Sheen, _GlareSlide;
                half4 _Tint, _RimLight, _Glare;
                float4 _LensC0, _LensC1, _LensN0, _LensN1, _LensRight, _Sweep;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 posWS : TEXCOORD1; float3 nWS : TEXCOORD2; };

            V vert(A a)
            {
                V o;
                o.posWS = TransformObjectToWorld(a.pos.xyz);
                o.pos = TransformWorldToHClip(o.posWS);
                o.nWS = TransformObjectToWorldNormal(a.normal);
                o.uv = a.uv;
                return o;
            }

            float aa(float d) { float w = clamp(fwidth(d), 1e-5, 0.05) * 1.2; return saturate(0.5 - d / w); }

            half4 frag(V i) : SV_Target
            {
                if (SAMPLE_TEXTURE2D(_LensMap, sampler_LensMap, i.uv).r < 0.5h) discard;
                // the lens this texel belongs to, and its own frame (x along the head's right, y up the lens)
                float d0 = distance(i.posWS, _LensC0.xyz), d1 = distance(i.posWS, _LensC1.xyz);
                float4 c = d0 < d1 ? _LensC0 : _LensC1;
                float3 n = normalize((d0 < d1 ? _LensN0 : _LensN1).xyz);
                float3 r = _LensRight.xyz - n * dot(_LensRight.xyz, n);
                r = r / max(length(r), 1e-5);
                float3 u = cross(n, r);
                float3 rel = i.posWS - c.xyz;
                float2 q = float2(dot(rel, r), dot(rel, u)) / max(c.w, 1e-5);          // -1 .. 1 across the lens
                float rad = length(q);
                float3 view = normalize(GetWorldSpaceViewDir(i.posWS));
                // the reflection slides against the view: turning the head (or walking round it) moves the glare across
                float slide = (dot(view, r) * 1.6 + dot(view, u) * 0.9) * _GlareSlide + _Sweep.x;
                float diag = (q.x + q.y) * 0.70710678;
                float bar1 = aa(abs(diag - (-0.18 + slide)) - 0.17);
                float bar2 = aa(abs(diag - (0.30 + slide)) - 0.055);
                float inLens = aa(rad - 0.96);
                float glare = max(bar1, bar2) * inLens;
                float fres = pow(1.0 - saturate(abs(dot(view, normalize(i.nWS)))), 3.0);
                float rim = aa(abs(rad - 0.90) - 0.035) * step(rad, 1.05);
                half3 col = _Tint.rgb;
                half a = _Tint.a + fres * _Sheen;
                col = lerp(col, _RimLight.rgb, rim * _RimLight.a);
                a = max(a, rim * _RimLight.a);
                col = lerp(col, _Glare.rgb, glare);
                a = max(a, glare * saturate(_Glare.a + _Sweep.y));
                return half4(col, saturate(a) * _Alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

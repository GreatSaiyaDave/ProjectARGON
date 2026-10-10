// Eldritch Solid Vision — translucent holographic slate (ELDRITCH_CONTRACT §3.1).
// UI/Default skeleton (stencil, _ClipRect, alpha clip, premultiplied) + edge glow, scanlines,
// top→bottom sweep, Lovecraft veins, non-Euclidean warp, materialize reveal, hash flicker.
// contract note: UGUI batches vertices into canvas-root space, so v.vertex.xy is NOT Image-local.
// TEXCOORD1 still carries v.vertex.xy (for _ClipRect, as UI/Default does) but rect-space px is
// rebuilt from the sprite UV of the full-rect Simple quad: px = uv * _RectSize. _RectPivot is
// declared for API compatibility (EldritchMaterials.SetRect writes it) and unused here.
Shader "WRLDZ/UI/SolidVisionSlate"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        [Header(Rect)]
        // 0 = "never synced by C#": the fragment shader then measures the quad with fwidth.
        _RectSize ("Rect Size (px)", Vector) = (0, 0, 0, 0)
        _RectPivot ("Rect Pivot", Vector) = (0.5, 0.5, 0, 0)

        [Header(Slate)]
        _FillColor ("Fill", Color) = (0.055, 0.063, 0.110, 0.88)
        _EdgeColor ("Edge", Color) = (1.0, 0.839, 0.278, 1.0)
        _EdgeWidth ("Edge Width (px)", Range(0.5, 24)) = 3
        _EdgeGlow ("Edge Glow", Range(0, 4)) = 0.62
        _Hover ("Hover", Range(0, 1)) = 0
        _HoverGlowMul ("Hover Glow Mul", Range(1, 4)) = 2.2
        _Open ("Open (1 = materialized)", Range(0, 1)) = 1

        [Header(Scanlines and Sweep)]
        _ScanColor ("Scan Color", Color) = (0.2, 0.922, 1.0, 1.0)
        _ScanDensity ("Scan Density (lines per px x0.1)", Range(0, 2)) = 1
        _ScanSpeed ("Scan Speed", Range(-4, 4)) = 0.6
        _ScanIntensity ("Scan Intensity", Range(0, 1)) = 0.16
        _SweepIntensity ("Sweep Intensity", Range(0, 2)) = 0.55
        _SweepSpeed ("Sweep Speed", Range(0, 2)) = 0.35
        _SweepWidth ("Sweep Width", Range(0.02, 0.6)) = 0.16

        [Header(Eldritch)]
        _NoiseTex ("Noise (R warp G veins B breath)", 2D) = "gray" {}
        _Warp ("Warp", Range(0, 0.08)) = 0.006
        _VeinColor ("Vein Color", Color) = (0.478, 0.169, 1.0, 1.0)
        _VeinStrength ("Vein Strength", Range(0, 1)) = 0.12
        // Authored tier values. The fragment ignores these; EldritchMaterials.SetRect reads
        // them and scales the live _VeinStrength/_SweepIntensity by surface size, so a wide
        // chrome plate does not smear the 256px noise tile into violet blobs.
        _ScanBase ("Scan Base (authored)", Range(0, 1)) = 0.16
        _VeinBase ("Vein Base (authored)", Range(0, 1)) = 0.12
        _SweepBase ("Sweep Base (authored)", Range(0, 2)) = 0.55
        _Flicker ("Flicker", Range(0, 0.3)) = 0.03
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0   // fwidth() fallback needs derivatives

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                half4  color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;   // canvas-space vertex (clip rect)
                float2 rectUV        : TEXCOORD2;   // raw sprite uv → rect space
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _NoiseTex;
            half4 _Color;
            half4 _TextureSampleAdd;
            float4 _ClipRect;

            float4 _RectSize;
            float4 _RectPivot;
            half4 _FillColor;
            half4 _EdgeColor;
            float _EdgeWidth;
            half _EdgeGlow;
            half _Hover;
            half _HoverGlowMul;
            half _Open;
            half4 _ScanColor;
            float _ScanDensity;
            float _ScanSpeed;
            half _ScanIntensity;
            half _SweepIntensity;
            float _SweepSpeed;
            float _SweepWidth;
            half _Warp;
            half4 _VeinColor;
            half _VeinStrength;
            half _Flicker;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.rectUV = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            half4 frag(v2f IN) : SV_Target
            {
                // Rect size in px. When C# has not synced _RectSize yet (edit-mode preview, prefab
                // thumbnail, first frame) derive the on-screen quad size from the UV gradient instead.
                float2 szSynced = _RectSize.xy;
                float2 szAuto = 1.0 / max(fwidth(IN.rectUV), 1e-5);
                float2 sz = max((szSynced.x < 2.0 || szSynced.y < 2.0) ? szAuto : szSynced, 1.0);
                float2 px = IN.rectUV * sz;
                float d = min(min(px.x, sz.x - px.x), min(px.y, sz.y - px.y));
                float t = _Time.y;
                half hover = saturate(_Hover);

                // Noise in screen-ish px so the 256 tile never stretches with the rect.
                float2 nuv = px * (1.0 / 256.0);
                half4 nWarp = tex2D(_NoiseTex, nuv * 0.5 + float2(t * 0.011, -t * 0.007));
                float2 warp = (nWarp.rg - 0.5) * _Warp * (1.0 + hover * 0.6);
                half4 tex = tex2D(_MainTex, IN.texcoord + warp) + _TextureSampleAdd;
                half4 vein = tex2D(_NoiseTex, nuv * 0.85 + warp * 6.0 + float2(-t * 0.016, t * 0.009));

                half4 col = IN.color * tex * _FillColor;      // straight alpha
                half shape = tex.a * IN.color.a;              // sprite silhouette × CanvasGroup fade

                // Horizontal scanlines drifting on px.y.
                half scan = 0.5 + 0.5 * sin((px.y * _ScanDensity * 0.1 - t * _ScanSpeed * 3.0) * 6.2831853);
                scan = scan * scan * scan * _ScanIntensity;

                // Bright band sweeping top → bottom (v: 0 top, 1 bottom).
                float v = 1.0 - px.y / sz.y;
                float sweepPos = frac(t * _SweepSpeed) * (1.0 + _SweepWidth * 2.0) - _SweepWidth;
                half sweep = 1.0 - smoothstep(0.0, _SweepWidth, abs(v - sweepPos));
                sweep = sweep * sweep * _SweepIntensity;

                // Edge glow + 1–2 px hairline so the slate reads as a plate.
                half glowMul = lerp(1.0, _HoverGlowMul, hover);
                half edge = exp(-d / max(_EdgeWidth, 0.5)) * _EdgeGlow * glowMul;
                half core = 1.0 - smoothstep(1.0, 2.2, d);
                edge += core * 0.45 * glowMul;

                // Veins: ridged G breathing on B, boosted by hover.
                half breath = 0.65 + 0.35 * sin(t * 0.8 + vein.b * 6.2831853);
                half veins = smoothstep(0.52, 0.86, vein.g) * breath * _VeinStrength * (1.0 + hover * 1.5);

                // Materialize: reveal top → bottom, cyan above / magenta below the line.
                float open = saturate(_Open);
                float revealLine = open * 1.12 - v;                                    // > 0 revealed
                half fadeIn = saturate(open * 3.0);
                half reveal = smoothstep(0.0, 0.05, revealLine) * fadeIn;
                half fringe = (1.0 - smoothstep(0.0, 0.045, abs(revealLine))) * (1.0 - open) * fadeIn;
                half3 fringeCol = lerp(half3(1.0, 0.3, 0.9), half3(0.2, 0.92, 1.0), step(0.0, revealLine)) * fringe * 2.5;

                // Cheap hash flicker in 6 px row bands at 24 Hz.
                float h = frac(sin(dot(float2(floor(t * 24.0), floor(px.y * 0.16)), float2(12.9898, 78.233))) * 43758.5453);
                half flick = 1.0 - _Flicker * h;

                col.rgb *= 1.0 + hover * 0.18;
                col.a *= reveal;

                half3 emis = (_ScanColor.rgb * (scan + sweep) * _ScanColor.a
                            + _EdgeColor.rgb * edge * _EdgeColor.a
                            + _VeinColor.rgb * veins * _VeinColor.a
                            + fringeCol) * shape * reveal;

                half4 o;
                o.rgb = (col.rgb * col.a + emis) * flick;   // premultiplied
                o.a = saturate(col.a + edge * _EdgeColor.a * shape * reveal * 0.5) * lerp(1.0, flick, 0.5);

                #ifdef UNITY_UI_CLIP_RECT
                o *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(o.a - 0.001);
                #endif

                return o;
            }
        ENDCG
        }
    }
    Fallback "UI/Default"
}

// Eldritch Solid Vision — arcane energy button face (ELDRITCH_CONTRACT §3.2).
// Vertical gradient fill + a comet of energy running clockwise along an inset ring,
// inner mana haze, hover/pressed response, materialize reveal, hash flicker.
// contract note: rect-space px is rebuilt from the full-rect sprite UV (UGUI vertices arrive in
// canvas-root space, see SolidVisionSlate.shader). The perimeter parameter runs along the ring
// rect inset by _BorderInset (W' = W-2r, H' = H-2r, normalized by 2(W'+H')) so it is exactly
// continuous at the ring corners; with _BorderInset = 0 this is the contract's 2(W+H).
Shader "WRLDZ/UI/ArcaneEnergyFlow"
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

        [Header(Fill)]
        _FillColorA ("Fill Top", Color) = (0.070, 0.078, 0.135, 0.90)
        _FillColorB ("Fill Bottom", Color) = (0.030, 0.035, 0.070, 0.92)
        _Hover ("Hover", Range(0, 1)) = 0
        _Pressed ("Pressed", Range(0, 1)) = 0
        _Open ("Open (1 = materialized)", Range(0, 1)) = 1

        [Header(Energy Line)]
        _BorderInset ("Border Inset (px)", Range(0, 24)) = 6
        _LineColor ("Line Head", Color) = (1.0, 0.839, 0.278, 1.0)
        _LineColor2 ("Line Tail", Color) = (0.478, 0.353, 0.204, 1.0)
        _LineWidth ("Line Width (px)", Range(0.5, 16)) = 4
        _LineSpeed ("Line Speed (laps/s)", Range(-2, 2)) = 0.5
        _LineLength ("Line Length (perimeter frac)", Range(0.05, 1)) = 0.35
        _LineCount ("Line Count", Range(1, 4)) = 1
        _Phase ("Phase Offset", Range(0, 1)) = 0
        _Bloom ("Bloom", Range(0, 3)) = 1.6
        _HazeStrength ("Inner Haze", Range(0, 1)) = 0.4
        _HazeWidth ("Inner Haze Width (px)", Range(4, 160)) = 36

        [Header(Eldritch)]
        _NoiseTex ("Noise (R crackle B breath)", 2D) = "gray" {}
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
            half4 _FillColorA;
            half4 _FillColorB;
            half _Hover;
            half _Pressed;
            half _Open;
            float _BorderInset;
            half4 _LineColor;
            half4 _LineColor2;
            float _LineWidth;
            float _LineSpeed;
            float _LineLength;
            float _LineCount;
            float _Phase;
            half _Bloom;
            half _HazeStrength;
            float _HazeWidth;
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

            // Clockwise perimeter parameter in [0,1) from the top-left corner of the ring rect.
            float Perimeter(float2 q, float2 isz)
            {
                q = clamp(q, float2(0.0, 0.0), isz); // outer margin extrapolates from the ring itself
                float dl = q.x;
                float dr = isz.x - q.x;
                float dt = isz.y - q.y;
                float db = q.y;
                float sTop = q.x;
                float sRight = isz.x + (isz.y - q.y);
                float sBot = isz.x + isz.y + (isz.x - q.x);
                float sLeft = 2.0 * isz.x + isz.y + q.y;
                float s = (dt <= dl && dt <= dr && dt <= db) ? sTop
                        : ((dr <= dl && dr <= db) ? sRight
                        : ((db <= dl) ? sBot : sLeft));
                return s / (2.0 * (isz.x + isz.y));
            }

            half4 frag(v2f IN) : SV_Target
            {
                // Rect size in px; fall back to the UV gradient when _RectSize is not synced yet
                // (edit-mode preview, prefab thumbnail, first frame).
                float2 szSynced = _RectSize.xy;
                float2 szAuto = 1.0 / max(fwidth(IN.rectUV), 1e-5);
                float2 sz = max((szSynced.x < 2.0 || szSynced.y < 2.0) ? szAuto : szSynced, 1.0);
                float2 px = IN.rectUV * sz;
                float d = min(min(px.x, sz.x - px.x), min(px.y, sz.y - px.y));
                float t = _Time.y;
                half hover = saturate(_Hover);
                half pressed = saturate(_Pressed);

                half4 tex = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                half4 n = tex2D(_NoiseTex, px * (0.7 / 256.0) + float2(t * 0.02, -t * 0.013));

                // Gradient fill (A top → B bottom), pressed darkens, hover lifts.
                half vy = saturate(px.y / sz.y);
                half4 fill = lerp(_FillColorB, _FillColorA, vy);
                half4 col = IN.color * tex * fill;
                col.rgb *= (1.0 + hover * 0.12) * (1.0 - pressed * 0.35);
                half shape = tex.a * IN.color.a;

                // Energy comet along the inset ring.
                float r = min(_BorderInset, min(sz.x, sz.y) * 0.45);
                float2 isz = max(sz - 2.0 * r, 1.0);
                float tp = Perimeter(px - r, isz);
                float ph = frac(tp * max(_LineCount, 1.0) - t * _LineSpeed + _Phase);
                half head = smoothstep(1.0 - max(_LineLength, 0.02), 1.0, ph);
                head *= head;                                                   // comet tail falloff
                half headGain = 1.0 + hover * 0.5 + pressed * 1.2;

                float lw = _LineWidth * (1.0 + hover * 0.6) * (1.0 + (n.r - 0.5) * 0.5);   // crackle
                half band = exp(-abs(d - r) / max(lw, 0.5));
                half baseRing = band * 0.22 * (1.0 + hover * 0.5);
                half3 lineCol = lerp(_LineColor2.rgb, _LineColor.rgb, head);
                half lineA = lerp(_LineColor2.a, _LineColor.a, head);
                half energy = band * head * _Bloom * headGain;
                half3 ring = _LineColor2.rgb * _LineColor2.a * baseRing + lineCol * lineA * energy;

                // Inner mana haze hugging the border, breathing on noise B. Fixed px falloff:
                // a fraction of the rect made big plates read as a bevelled tunnel.
                float inner = max(d - r, 0.0);
                half haze = exp(-inner / max(_HazeWidth, 4.0)) * _HazeStrength;
                haze *= (0.75 + 0.25 * sin(t * 0.9 + n.b * 6.2831853)) * (1.0 + hover * 0.6);
                half3 hazeRGB = lerp(_LineColor2.rgb, _LineColor.rgb, 0.35) * haze * 0.35;

                // Materialize: reveal top → bottom with a cyan/magenta fringe.
                float v = 1.0 - px.y / sz.y;
                float open = saturate(_Open);
                float revealLine = open * 1.12 - v;
                half fadeIn = saturate(open * 3.0);
                half reveal = smoothstep(0.0, 0.05, revealLine) * fadeIn;
                half fringe = (1.0 - smoothstep(0.0, 0.045, abs(revealLine))) * (1.0 - open) * fadeIn;
                half3 fringeCol = lerp(half3(1.0, 0.3, 0.9), half3(0.2, 0.92, 1.0), step(0.0, revealLine)) * fringe * 2.5;

                float h = frac(sin(dot(float2(floor(t * 24.0), floor(px.y * 0.16)), float2(12.9898, 78.233))) * 43758.5453);
                half flick = 1.0 - _Flicker * h;

                col.a *= reveal;
                half3 emis = (ring + hazeRGB + fringeCol) * shape * reveal;

                half4 o;
                o.rgb = (col.rgb * col.a + emis) * flick;   // premultiplied
                o.a = saturate(col.a + (energy * lineA * 0.6 + baseRing * 0.3 + haze * 0.3) * shape * reveal)
                    * lerp(1.0, flick, 0.5);

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

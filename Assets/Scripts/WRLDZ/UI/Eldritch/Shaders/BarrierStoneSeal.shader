// Eldritch Solid Vision — the barrier-stone seal.
//
// Canon (GDD 2.0): Ptah and Anubis sealed Umbrax's soul and skull behind a contract-barrier.
// Akun carried a barrier-stone into Egypt and that stone is the seed of the Millennium Items.
// Crawford Pretoria raised the skull, used it as a battery, and Tears opened across realities.
//
// So every panel IS a fragment of that failing seal:
//   intact stone  = carved lapis + Millennium gold inlay, held by Andrax's blue barrier light
//   breach        = a hole into the Shadow Realm — void, drifting stars, violet pressure, smoke
//   interaction   = the seal STRAINS. Hover widens the breach and brightens what is behind it.
//
// Everything animates off _Time.y so the seal is never still: Umbrax breathes against the crack,
// stars drift, the void swirls, smoke seeps upward, and the barrier flickers as it holds.
Shader "WRLDZ/UI/BarrierStoneSeal"
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
        // 0 = never synced by C#; the fragment then measures the quad with fwidth.
        _RectSize ("Rect Size (px)", Vector) = (0, 0, 0, 0)
        _RectPivot ("Rect Pivot", Vector) = (0.5, 0.5, 0, 0)

        [Header(Stone)]
        _StoneTex ("Carved Stone (RGB lit, A height)", 2D) = "gray" {}
        _StoneTint ("Stone Tint", Color) = (1,1,1,1)
        _StoneScale ("Stone Scale (px per tile)", Range(64, 1024)) = 300
        _Brightness ("Stone Brightness", Range(0, 2)) = 0.95

        [Header(Breach)]
        _CrackTex ("Fracture Sheet (R core G lip B wide A seep)", 2D) = "black" {}
        _CrackCell ("Fracture Variant (0-3)", Range(0, 3)) = 0
        _CrackScale ("Fracture Scale", Range(0.4, 3)) = 1
        _CrackOffset ("Fracture Offset", Vector) = (0, 0, 0, 0)
        _Breach ("Breach Amount", Range(0, 2)) = 1
        _VoidColor ("Void", Color) = (0.04, 0.02, 0.09, 1)
        _PressureColor ("Umbrax Pressure", Color) = (0.769, 0.294, 1, 1)
        _BleedColor ("Bleed", Color) = (0.478, 0.169, 1, 1)
        _LipColor ("Chipped Lip", Color) = (0.788, 0.639, 0.420, 1)

        [Header(Shadow Realm)]
        _StarTex ("Starfield (R stars G twinkle)", 2D) = "black" {}
        _StarDrift ("Star Drift", Range(0, 0.5)) = 0.06
        _SwirlTex ("Swirl / Smoke Noise", 2D) = "gray" {}
        _SwirlSpeed ("Swirl Speed", Range(0, 1)) = 0.10
        _SmokeSpeed ("Smoke Rise", Range(0, 2)) = 0.34
        _SmokeAmount ("Smoke", Range(0, 2)) = 0.60

        [Header(Barrier)]
        _BarrierColor ("Andrax Barrier", Color) = (0.353, 0.667, 1, 1)
        _SealColor ("Millennium Seal", Color) = (1, 0.839, 0.278, 1)
        _BarrierWidth ("Barrier Width (px)", Range(1, 24)) = 3.2
        _SealWidth ("Seal Width (px)", Range(0.5, 12)) = 2
        _BarrierGlow ("Barrier Glow", Range(0, 3)) = 0.9

        [Header(Life)]
        _Breath ("Breath Speed", Range(0, 3)) = 0.55
        _Strain ("Strain Flicker", Range(0, 1)) = 0.20
        _Spasm ("Fracture Spasm", Range(0, 1)) = 0.55
        _Phase ("Phase Offset", Range(0, 1)) = 0
        _Hover ("Hover", Range(0, 1)) = 0
        _Pressed ("Pressed", Range(0, 1)) = 0
        _Open ("Open (1 = sealed in)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"
            "PreviewType"="Plane" "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
            ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]
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
            #pragma target 3.0

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
                float2 rectUV        : TEXCOORD2;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _StoneTex; sampler2D _CrackTex; sampler2D _StarTex; sampler2D _SwirlTex;
            half4 _Color, _TextureSampleAdd;
            float4 _ClipRect;
            float4 _RectSize, _RectPivot, _CrackOffset;

            half4 _StoneTint, _VoidColor, _PressureColor, _BleedColor, _LipColor;
            half4 _BarrierColor, _SealColor;
            float _StoneScale, _CrackCell, _CrackScale, _BarrierWidth, _SealWidth;
            half _Brightness, _Breach, _StarDrift, _SwirlSpeed, _SmokeSpeed, _SmokeAmount;
            half _BarrierGlow, _Breath, _Strain, _Spasm, _Phase, _Hover, _Pressed, _Open;

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
                // Rect-space pixels. UGUI batches into canvas space, so v.vertex.xy is not
                // Image-local — rebuild from the full-rect sprite UV, with a derivative
                // fallback for materials C# has not synced yet.
                float2 szSynced = _RectSize.xy;
                float2 szAuto   = 1.0 / max(fwidth(IN.rectUV), 1e-5);
                float2 sz = max((szSynced.x < 2.0 || szSynced.y < 2.0) ? szAuto : szSynced, 1.0);
                float2 px = IN.rectUV * sz;
                float  d  = min(min(px.x, sz.x - px.x), min(px.y, sz.y - px.y));
                float  t  = _Time.y;

                half hover = saturate(_Hover);
                half press = saturate(_Pressed);

                // ── Umbrax breathing against the seal: irregular, never a clean sine ──
                float bt    = t * _Breath + _Phase * 6.2831853;
                half  pulse = 0.55 + 0.45 * sin(bt) * (0.75 + 0.25 * sin(bt * 3.0 + 1.1));
                // rare spasm — the fracture jolts
                half  spasm = pow(saturate(sin(t * 0.37 + _Phase * 5.0)), 24.0) * _Spasm;
                half  pressure = pulse + spasm + hover * 0.55 + press * 0.8;

                // ── carved stone, tiled in real pixels so it never stretches ──
                float2 stoneUV = px / max(_StoneScale, 32.0);
                half4 stone = tex2D(_StoneTex, stoneUV);
                half3 rock  = stone.rgb * _StoneTint.rgb * _Brightness;

                // ── the breach ──
                // The fracture sheet is a 2x2 atlas of four distinct networks. Sample it as SQUARE
                // tiles stepped along the surface rather than stretching one cell over the whole
                // rect: squaring by the full aspect marooned a single stamp in the middle of a wide
                // slab, and not squaring at all smeared the art into horizontal streaks. A long
                // slab now reads as a run of separate fractures, which is what cracked stone does.
                // Size the square off the LONG side, so a slab carries one or two fractures rather
                // than a web of them. The short axis then covers only a slice of the cell, so bias
                // that axis to the cell's middle where the fracture actually runs — otherwise a
                // short tile can sample an empty band and show no crack at all.
                float unit = max(max(sz.x, sz.y) * 0.55 * max(_CrackScale, 0.01), 16.0);
                float2 span = sz / unit;
                float2 bias = max(0.5 - span * 0.5, 0.0);
                float2 q  = px / unit + bias + _CrackOffset.xy;
                float2 gid = floor(q);
                // A different sheet cell per square, so neighbouring fractures never repeat.
                float cell = fmod(floor(_CrackCell + 0.5) + gid.x + gid.y * 2.0, 4.0);
                // Inset inside the cell: its authored margin is black, so fractures stop at the
                // square's edge instead of running across the seam.
                float2 cUV = (frac(q) * 0.94 + 0.03) * 0.5
                           + float2(fmod(cell, 2.0) * 0.5, floor(cell * 0.5) * 0.5);
                half4 ck = tex2D(_CrackTex, cUV);
                half core = saturate(ck.r * _Breach);
                half lip  = saturate(ck.g * _Breach);
                half wide = saturate(ck.b * _Breach);
                half seep = saturate(ck.a * _Breach);

                // ── behind the breach: the Shadow Realm, not a lamp ──
                float2 starUV = px / 420.0 + float2(_Phase, -t * _StarDrift);
                half2  star   = tex2D(_StarTex, starUV).rg;
                half   twinkle = 0.55 + 0.45 * sin(t * 2.1 + star.g * 6.2831853);
                float2 swUV   = px / 560.0 + float2(t * _SwirlSpeed, t * _SwirlSpeed * 0.3);
                half   swirl  = tex2D(_SwirlTex, swUV).r;

                half3 voidCol = _VoidColor.rgb;
                voidCol += star.r * twinkle * half3(1.0, 0.96, 0.90) * 1.6;
                voidCol += swirl * swirl * _BleedColor.rgb * (0.26 + 0.20 * pressure);

                half3 col = lerp(rock, voidCol, core);

                // freshly broken stone catching light at the rim of the hole
                col += lip * _LipColor.rgb * 0.55;
                // pressure at the seam, and a wider bleed into the stone
                col += wide * (1.0 - core) * _PressureColor.rgb * (0.24 + 0.34 * pressure);
                col += wide * wide * _BleedColor.rgb * (0.13 + 0.20 * pressure);

                // ── smoke seeping upward out of the seam ──
                float2 smUV = px / 300.0 + float2(sin(t * 0.21) * 0.05, -t * _SmokeSpeed);
                half smoke = tex2D(_SwirlTex, smUV).g;
                smoke = saturate(smoke * 1.6 - 0.38) * seep * saturate(1.15 - IN.rectUV.y);
                col += smoke * _PressureColor.rgb * _SmokeAmount * (0.55 + 0.45 * pressure);

                // ── Andrax's barrier, straining, and pushed back near a breach ──
                half strain = 1.0 - _Strain * (0.5 + 0.5 * sin(t * 2.0 + _Phase * 4.0)) - spasm * 0.6;
                strain = saturate(strain + hover * 0.25);
                half hold = saturate(1.0 - wide * 2.6);
                half barrier = exp(-d / max(_BarrierWidth, 0.5));
                half seal    = exp(-d / max(_SealWidth, 0.5));
                col += barrier * hold * _BarrierColor.rgb * _BarrierGlow * strain;
                col += seal    * hold * _SealColor.rgb * 0.55 * strain;

                // ── materialize ──
                half4 tex = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                half shape = tex.a * IN.color.a;
                float reveal = saturate((_Open * 1.12) - (1.0 - IN.rectUV.y));
                half fadeIn = saturate(_Open * 3.0);
                half rev = smoothstep(0.0, 0.05, reveal) * fadeIn;

                half a = saturate(_Color.a * IN.color.a) * shape * rev;
                half4 o;
                o.rgb = col * a;                       // premultiplied
                o.a   = a;

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

// Eldritch Solid Vision — hieroglyph cell glow (ELDRITCH_CONTRACT §3.3).
// White-ink glyph × Image tint (rgb) + a 4-tap dilated halo in _GlowColor. Image color.a is the
// per-cell PULSE PHASE (0–1), not opacity — 40 cells share one material and dim via tint rgb.
// contract note: keep the material _Color alpha at 1; CanvasGroup fades do not apply to cells.
// _Spread is in _MainTex texels (128 px atlas cells), so the halo scales with the drawn cell.
Shader "WRLDZ/UI/HieroglyphGlow"
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

        [Header(Glow)]
        _GlowColor ("Glow Color", Color) = (1.0, 0.839, 0.278, 1.0)
        _Glow ("Glow", Range(0, 3)) = 1.2
        _Pulse ("Pulse Depth", Range(0, 1)) = 0.5
        _PulseSpeed ("Pulse Speed", Range(0, 6)) = 2.2
        _Spread ("Halo Spread (atlas texels)", Range(0, 8)) = 3
        // CanvasRenderer binds _MainTex, so _MainTex_TexelSize is never right here — C# supplies it.
        _GlyphTexel ("Glyph Texel Size (1/w, 1/h)", Vector) = (0.0009765625, 0.0009765625, 0, 0)
        // Group/CanvasGroup opacity: vertex alpha is spent on the pulse phase, so fades arrive here.
        _Fade ("Fade", Range(0, 1)) = 1
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
            #pragma target 2.5

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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            half4 _Color;
            half4 _TextureSampleAdd;
            float4 _ClipRect;

            half4 _GlowColor;
            half _Glow;
            half _Pulse;
            float _PulseSpeed;
            float _Spread;
            float4 _GlyphTexel;
            half _Fade;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            half4 frag(v2f IN) : SV_Target
            {
                half4 tex = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                half ink = tex.a;

                // 4-tap plus-shaped dilation for the halo.
                float2 sp = _GlyphTexel.xy * _Spread;
                half a1 = tex2D(_MainTex, IN.texcoord + float2(sp.x, 0.0)).a;
                half a2 = tex2D(_MainTex, IN.texcoord - float2(sp.x, 0.0)).a;
                half a3 = tex2D(_MainTex, IN.texcoord + float2(0.0, sp.y)).a;
                half a4 = tex2D(_MainTex, IN.texcoord - float2(0.0, sp.y)).a;
                half halo = max(max(max(a1, a2), max(a3, a4)), ink);

                half phase = IN.color.a;   // per-cell phase, not opacity
                half pulse = lerp(1.0, 0.5 + 0.5 * sin(_Time.y * _PulseSpeed + phase * 6.2831853), _Pulse);

                half3 tint = IN.color.rgb;
                half3 glyph = tint * tex.rgb * ink * (0.8 + 0.4 * pulse);
                half glowAmt = halo * _Glow * pulse * _GlowColor.a;
                half3 glow = _GlowColor.rgb * glowAmt * (0.35 + 0.65 * (1.0 - ink));   // brightest just outside the ink

                half4 o;
                o.rgb = glyph + glow;                     // premultiplied (glyph already × ink)
                o.a = saturate(ink + glowAmt * 0.5);
                o *= _Fade;                               // CanvasGroup / border fade (vertex alpha is phase)

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

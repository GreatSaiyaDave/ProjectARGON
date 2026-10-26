// "The Stone Beneath Nut" v6.3 — the live cel smoke (BUILD_PLAN D3, §6 P5 step 4; smoke_<layout>.json shader.route).
// The ONE new hand-written UI shader of the menu build: no ShaderGraph, no VFX Graph. It runs the page's own SVG cel
// filter (anime_v6.js:163-187, `cel(id, P, th, sl, line)`) per pixel over a CPU-splatted R8 density (SmokeField.cs,
// 1/2 stage res, bilinear) and a CPU-blurred R8 glow source (G0 = blur(M, sigma 9 stage px), 1/4 stage res):
//   M    = sat(slope A + intercept)                         (feComponentTransfer on SourceAlpha)
//   LIT  = M (1 - M(p - off.lit))                           (feOffset 8,10 + composite out)
//   MID  = M M(p - off.mid)                                 (feOffset -6,-8 + composite in)
//   SLV  = LIT (1 - LIT(p - off.sliver)) M                  (feOffset -3,-4 + out, then in M)
//   LINE = dilate_r(M) (1 - M)                              (feMorphology dilate r + composite out; r = 2 raster px)
//   merge (source-over, premultiplied, in sRGB as the page: color-interpolation-filters sRGB):
//   glow (glowOpacity G0), ink (LINE), fill (M), mid (MID), lit (LIT), sliver (sliverOpacity SLV)
// The dilate's max over the (2r+1)^2 square is taken on the density (M is monotone in A) at the 3x3 points +-r, a
// decided approximation of the 25-tap square (the line is 1.5-2 px wide). Offsets are stage px, y DOWN (the density's
// row 0 is the stage's top row, so v grows with stage y). The layer's alpha (the entrance's CanvasGroup, the ink blot's
// whole-layer fade) arrives in the vertex colour and scales the premultiplied result once (the page's asLayer).
// Linear colour space: the merged sRGB colour is laid over the room's dark level d0 the way the page blends it
// (sRGB "over"), and the premultiplied linear colour that reproduces that blend at d0 is output (exact at d0 and at
// alpha 0 / 1; the cels are hard-edged, so only the glow and the sliver's .75 are fractional).
// Palette colours come in as Vectors (raw sRGB, never converted by the material's Color path).
Shader "WRLDZ/UI/MenuCelSmoke"
{
    Properties
    {
        [PerRendererData] _MainTex ("Density (R8, 1/2 stage res)", 2D) = "black" {}
        _GlowTex ("Glow source G0 (R8, 1/4 stage res)", 2D) = "black" {}
        _Texel ("UV per stage px (x, y)", Vector) = (0.000520833, 0.000925926, 0, 0)
        _Slope ("Slope", Float) = 26
        _Intercept ("Intercept", Float) = -9.9
        _OffLit ("Lit offset (stage px)", Vector) = (8, 10, 0, 0)
        _OffMid ("Mid offset (stage px)", Vector) = (-6, -8, 0, 0)
        _OffSlv ("Sliver offset (stage px)", Vector) = (-3, -4, 0, 0)
        _LineR ("Line dilate radius (stage px)", Float) = 2
        _CFill ("Fill (sRGB)", Vector) = (0.165, 0.102, 0.251, 1)
        _CMid ("Mid (sRGB)", Vector) = (0.271, 0.188, 0.431, 1)
        _CLit ("Lit (sRGB)", Vector) = (0.431, 0.322, 0.651, 1)
        _CSliver ("Sliver (sRGB)", Vector) = (0.431, 0.322, 0.651, 1)
        _CInk ("Ink line (sRGB)", Vector) = (0.141, 0.082, 0.208, 1)
        _CGlow ("Glow (sRGB)", Vector) = (0.420, 0.247, 0.627, 1)
        _SliverA ("Sliver opacity", Float) = 0.75
        _GlowA ("Glow opacity", Float) = 0.42
        _RefDark ("The room's dark level (sRGB)", Float) = 0.129

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="False"
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
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            sampler2D _GlowTex;
            float4 _Texel;
            float _Slope, _Intercept, _LineR, _SliverA, _GlowA, _RefDark;
            float4 _OffLit, _OffMid, _OffSlv;
            float4 _CFill, _CMid, _CLit, _CSliver, _CInk, _CGlow;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;   // stage UV: u = x / W, v = y / H (y down)
                OUT.color = v.color;         // the layer alpha (CanvasGroup / the whole-layer fade)
                return OUT;
            }

            inline float A(float2 uv) { return tex2D(_MainTex, uv).r; }
            inline float Mk(float a) { return saturate(_Slope * a + _Intercept); }

            // premultiplied source-over of a flood colour col at coverage a over c
            inline float4 Over(float4 c, float3 col, float a) { return float4(col * a + c.rgb * (1.0 - a), a + c.a * (1.0 - a)); }

            inline float3 ToLin(float3 c)
            {
                float3 lo = c / 12.92;
                float3 hi = pow(max((c + 0.055) / 1.055, 1e-5), 2.4);
                return lerp(hi, lo, step(c, 0.04045));
            }

            half4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float2 px = _Texel.xy;
                float a0 = A(uv);
                float m = Mk(a0);

                float kLit = m * (1.0 - Mk(A(uv - _OffLit.xy * px)));
                float kMid = m * Mk(A(uv - _OffMid.xy * px));
                float2 q = uv - _OffSlv.xy * px;
                float litS = Mk(A(q)) * (1.0 - Mk(A(q - _OffLit.xy * px)));
                float kSlv = kLit * (1.0 - litS) * m;

                float2 r = _LineR * px;
                float amax = a0;
                amax = max(amax, A(uv + float2(-r.x, -r.y)));
                amax = max(amax, A(uv + float2(0, -r.y)));
                amax = max(amax, A(uv + float2(r.x, -r.y)));
                amax = max(amax, A(uv + float2(-r.x, 0)));
                amax = max(amax, A(uv + float2(r.x, 0)));
                amax = max(amax, A(uv + float2(-r.x, r.y)));
                amax = max(amax, A(uv + float2(0, r.y)));
                amax = max(amax, A(uv + float2(r.x, r.y)));
                float kLine = Mk(amax) * (1.0 - m);

                float g = tex2D(_GlowTex, uv).r * _GlowA;
                float4 c = float4(_CGlow.rgb * g, g);
                c = Over(c, _CInk.rgb, kLine);
                c = Over(c, _CFill.rgb, m);
                c = Over(c, _CMid.rgb, kMid);
                c = Over(c, _CLit.rgb, kLit);
                c = Over(c, _CSliver.rgb, kSlv * _SliverA);

                float k = IN.color.a;
                c *= k;   // the whole layer fades as one (premultiplied)

                #ifdef UNITY_UI_CLIP_RECT
                float clipA = UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                c *= clipA;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif

                #if !defined(UNITY_COLORSPACE_GAMMA)
                // the page blends in sRGB: out = C + d (1 - a). Reproduce it exactly over the room's dark level d0.
                float3 d0 = float3(_RefDark, _RefDark, _RefDark);
                float3 target = ToLin(saturate(c.rgb + d0 * (1.0 - c.a)));
                c.rgb = max(target - ToLin(d0) * (1.0 - c.a), 0.0);
                #endif

                return half4(c);
            }
        ENDCG
        }
    }

    Fallback Off
}

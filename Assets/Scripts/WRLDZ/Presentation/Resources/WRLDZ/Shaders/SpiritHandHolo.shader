// WRLDZ/SpiritHandHolo — the floating right hand as a Solid Vision spirit projection, drawn as HD anime
// (AGENTS.md "Art quality bar": cel shading, ink outline, never opaque).
//  · translucent skin-tinted glass with ONE crisp anime shadow band from the key light (AnimeToon.hlsl), so the palm,
//    the knuckles and the thumb read as a hand instead of a flat lump
//  · an ink outline pass (inverted hull, constant width in pixels) that dissolves with the wrist, so the thumb over
//    the card faces and the fingers around the fan keep a clean anime line even at half alpha
//  · a bright fresnel rim in the Spirit magic colour, faint energy bands flowing wrist → fingertips, fingertips a
//    little hotter, and the wrist dissolving into light on a noisy glowing seam
//  · a depth-only pass first, so only the nearest surface draws (no palm showing through the fingers) and the ink
//    hull only survives outside that surface
// ArFloatingHand writes _WristPos / _HandAxis every frame (world space) and fades with _Fade.
//
// Lives in a Resources folder so player builds include it. Single-pass-instanced stereo safe.
Shader "WRLDZ/SpiritHandHolo"
{
    Properties
    {
        _Color ("Glass (a = base alpha)", Color) = (0.8, 0.9, 1, 0.5)
        _RimColor ("Rim / energy", Color) = (0.4, 0.95, 1, 1)
        _OutlineColor ("Ink (a = ink alpha)", Color) = (0.05, 0.03, 0.12, 0.85)
        _OutlineWidth ("Ink outline width (px)", Range(0, 4)) = 1.6
        _ShadowAmt ("Anime shadow band depth", Range(0, 0.5)) = 0.12
        _RimAmt ("Anime rim on the lit edge", Range(0, 1)) = 0.6
        _Fill ("Fill", Range(0, 1.5)) = 0.85
        _RimPower ("Spirit rim power", Range(0.5, 8)) = 3.0
        _RimStrength ("Spirit rim strength", Range(0, 3)) = 1.0
        _ScanAmt ("Hologram scanline depth", Range(0, 0.3)) = 0.05
        _BandAmt ("Energy band strength", Range(0, 1)) = 0.3
        _Fade ("Fade", Range(0, 1)) = 1
        _WristPos ("Wrist (world)", Vector) = (0, 0, 0, 0)
        _HandAxis ("Wrist to fingertips (world xyz, length w)", Vector) = (0, 1, 0, 0.19)
        _DissolveAt ("Seam centre (hand lengths from the wrist, negative = forearm)", Range(-0.5, 0.5)) = -0.18
        _Dissolve ("Seam width (fraction of hand length)", Range(0, 0.6)) = 0.12
        _InkDepthPull ("Ink depth pull toward the eye (hand lengths)", Range(0, 0.1)) = 0.03
        _FlowSpeed ("Flow speed", Float) = 0.9
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+50"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "../../../Avatar/AnimeToon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half4 _RimColor;
            half4 _OutlineColor;
            half _OutlineWidth;
            half _ShadowAmt;
            half _RimAmt;
            half _Fill;
            half _RimPower;
            half _RimStrength;
            half _ScanAmt;
            half _BandAmt;
            half _Fade;
            float4 _WristPos;
            float4 _HandAxis;
            half _DissolveAt;
            half _Dissolve;
            half _InkDepthPull;
            float _FlowSpeed;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float Hash(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x);
            float b = lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x);
            float c = lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x);
            float d = lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x);
            return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
        }

        Varyings vert(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionWS = posWS;
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.positionCS = TransformWorldToHClip(posWS);
            return o;
        }

        // the ink hull: the same vertex pushed out along its screen-space normal by _OutlineWidth pixels
        Varyings vertInk(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionWS = posWS;
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            float4 cs = AnimeOutlineCS(v.positionOS.xyz, v.normalOS, _OutlineWidth);
            // The hull is the hand's BACK faces, so its depth is the far side of a finger: the thumb's pad lies on
            // the card face, and its ink used to fail the depth test exactly where it crosses the card. Pull the
            // hull's depth a little toward the eye (keeping the screen-space push) so the line survives over a
            // card the front surface is in front of, while the interior still hides behind the depth pass.
            float3 toEye = normalize(GetCameraPositionWS() - posWS);
            float4 csNear = TransformWorldToHClip(posWS + toEye * (_InkDepthPull * max(1e-4, _HandAxis.w)));
            cs.z = csNear.z / max(1e-5, csNear.w) * cs.w;
            o.positionCS = cs;
            return o;
        }

        // 0 at the wrist, 1 at the middle fingertip
        float AlongHand(float3 posWS)
        {
            float len = max(1e-4, _HandAxis.w);
            float3 axis = normalize(_HandAxis.xyz + float3(0, 1e-6, 0));
            return dot(posWS - _WristPos.xyz, axis) / len;
        }

        // signed distance (in hand lengths) past the noisy seam below the wrist; < 0 is dissolved
        float DissolveEdge(float3 posWS, float d)
        {
            float len = max(1e-4, _HandAxis.w);
            float nz = ValueNoise(posWS / len * 9.0 + float3(0.0, -_Time.y * 0.8, 0.0));
            return d - (_DissolveAt + _Dissolve * (0.55 + 0.9 * nz));
        }
        ENDHLSL

        // Depth only: the nearest hand surface wins, so fingers never show the palm through them, and the ink hull
        // below only survives outside the hand's own silhouette.
        Pass
        {
            Name "SpiritHandDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            half4 fragDepth(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(DissolveEdge(i.positionWS, AlongHand(i.positionWS)) - 0.02);
                clip(_Fade - 0.02);
                return 0;
            }
            ENDHLSL
        }

        // Ink outline: inverted hull, depth-tested against the pass above (and the opaque cards), so it draws only
        // around the visible silhouette — the thumb over the fan, the fingers beside it, the heel of the hand.
        Pass
        {
            Name "SpiritHandInk"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex vertInk
            #pragma fragment fragInk
            #pragma multi_compile_instancing

            half4 fragInk(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_OutlineWidth <= 0.01h) discard;
                float edge = DissolveEdge(i.positionWS, AlongHand(i.positionWS));
                half vis = smoothstep(0.0h, 0.08h, (half)edge);
                return half4(_OutlineColor.rgb, _OutlineColor.a * vis * _Fade);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SpiritHandHolo"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 n = normalize(i.normalWS);
                float3 vdirF = GetWorldSpaceViewDir(i.positionWS);
                half3 vdir = normalize(vdirF);
                half ndv = saturate(abs(dot(n, vdir)));
                half rim = pow(1.0h - ndv, _RimPower) * _RimStrength;

                float t = _Time.y;
                float d = AlongHand(i.positionWS);
                float edge = DissolveEdge(i.positionWS, d);
                half vis = smoothstep(0.0h, 0.10h, (half)edge);
                half seam = saturate(1.0h - abs((half)edge) / 0.06h);
                seam *= seam;

                // anime paint: the glass colour split into a lit side and one cool shadow band, thin rim on the lit edge
                half3 paint = AnimeShade(_Color.rgb, i.normalWS, vdirF, _ShadowAmt, _RimAmt);

                // energy rising from the wrist into the fingers (faint: the form has to read first)
                half wave = saturate(sin((d * 7.0 - t * _FlowSpeed) * 6.2831853) * 0.5 + 0.5);
                half band = pow(wave, 8.0h) * smoothstep(0.12h, 0.4h, (half)d) * _BandAmt;
                half tip = smoothstep(0.78h, 1.02h, (half)d);
                float len = max(1e-4, _HandAxis.w);
                // faint hologram bands that ride the hand (world-Y lines shimmered over it as it bobbed)
                half scan = 1.0h - _ScanAmt + _ScanAmt * sin(d * 24.0 - t * 0.6);

                half3 col = paint * _Fill * scan + _RimColor.rgb * (rim + band * 0.5h + tip * 0.25h);
                half a = saturate(_Color.a * scan + rim * 0.45h + band * 0.15h + tip * 0.1h) * vis;
                half seamA = seam * 0.9h;
                half outA = saturate(a + seamA);
                half3 seamCol = lerp(_RimColor.rgb, half3(1, 1, 1), 0.45h);
                col = lerp(col, seamCol, saturate(seamA / max(outA, 1e-3h)));
                return half4(col, outA * _Fade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

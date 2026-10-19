// Anime face features for the ArgonAvatarRig bodies (2026-10-18, owner: "several eyes, noses, mouths … make all the eyes
// and mouths fully animated"). Each feature is one quad on the face, drawn as signed-distance shapes (crisp at any zoom,
// no sprite frames): _Part 0 = eye, 1 = mouth, 2 = nose, 3 = brow, 4 = a skin cover (hides the painted feature under it).
// Every shape is a parameter, so a blink, a glance, a smile or a spoken vowel is a value changing over time
// (AvatarFaceRig drives them). Style guide: flat cel colours, hard edges anti-aliased over ~1 px, ink lines, no gradients
// inside a band (the iris has three hard tones).
Shader "WRLDZ/AvatarAnimeFace"
{
    Properties
    {
        _Part ("Part (0 eye 1 mouth 2 nose 3 brow 4 cover)", Float) = 0
        _Alpha ("Presence", Float) = 1
        _Skin ("Skin", Color) = (0.96, 0.80, 0.66, 1)
        _Ink ("Ink", Color) = (0.10, 0.06, 0.08, 1)
        // ---- eye
        _Mirror ("Mirror (1 right eye)", Float) = 0
        _EyeW ("Eye half width", Float) = 0.40
        _EyeH ("Eye half height", Float) = 0.26
        _Tilt ("Outer corner lift", Float) = 0.06
        _Round ("Roundness", Float) = 0.5
        _TopBulge ("Upper curve bulge", Float) = 1.0
        _BotBulge ("Lower curve bulge", Float) = 0.55
        _Lash ("Upper lash thickness", Float) = 0.055
        _LashOuter ("Lash thickening at the outer corner", Float) = 1.6
        _LowerLash ("Lower lash thickness", Float) = 0.012
        _Lid ("Upper lid (0 open, 1 closed)", Float) = 0
        _LidCurve ("Closed-lid curve (1 = happy ^)", Float) = 0
        _LowerLid ("Lower lid rise (squint)", Float) = 0
        _LidShade ("Lid crease shade", Float) = 0.5
        _Iris ("Iris radius", Float) = 0.23
        _IrisPos ("Iris offset (gaze)", Vector) = (0, 0, 0, 0)
        _IrisScaleY ("Iris vertical scale", Float) = 1.15
        _Pupil ("Pupil radius", Float) = 0.085
        _Sclera ("Sclera", Color) = (0.97, 0.97, 0.98, 1)
        _IrisColor ("Iris", Color) = (0.36, 0.24, 0.62, 1)
        _IrisLight ("Iris light band", Color) = (0.58, 0.46, 0.84, 1)
        _IrisDark ("Iris dark band", Color) = (0.18, 0.10, 0.36, 1)
        _Highlight ("Highlight 1 (x, y, r)", Vector) = (-0.08, 0.09, 0.075, 0)
        _Highlight2 ("Highlight 2 (x, y, r)", Vector) = (0.07, -0.07, 0.035, 0)
        // ---- mouth
        _MouthW ("Mouth half width", Float) = 0.30
        _Corner ("Corners (-1 frown, +1 smile)", Float) = 0.1
        _Open ("Open (0..1)", Float) = 0
        _OpenShape ("Open shape (0 wide, 1 round 'o')", Float) = 0.3
        _LipLine ("Lip line thickness", Float) = 0.035
        _LowerLip ("Lower lip fullness shade", Float) = 0.3
        _Teeth ("Teeth (0..1)", Float) = 0.5
        _Tongue ("Tongue (0..1)", Float) = 0.4
        _Inner ("Mouth interior", Color) = (0.33, 0.10, 0.12, 1)
        _TeethColor ("Teeth", Color) = (0.96, 0.95, 0.92, 1)
        _TongueColor ("Tongue", Color) = (0.80, 0.36, 0.40, 1)
        // ---- nose
        _NoseStyle ("Nose style (0 tick 1 line 2 point 3 button)", Float) = 0
        _NoseSize ("Nose size", Float) = 0.5
        _NoseShade ("Nose shade strength", Float) = 0.35
        // ---- brow
        _BrowThick ("Brow thickness", Float) = 0.09
        _BrowArch ("Brow arch", Float) = 0.12
        _BrowTilt ("Brow tilt (+ angry inner-down, - sad)", Float) = 0
        _BrowLift ("Brow lift", Float) = 0
        _BrowColor ("Brow", Color) = (0.16, 0.10, 0.08, 1)
        // ---- cover
        _CoverSoft ("Cover edge softness", Float) = 0.25
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AnimeFace"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -2, -2
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _Part, _Mirror, _Alpha;
            half4 _Skin, _Ink;
            float _EyeW, _EyeH, _Tilt, _Round, _TopBulge, _BotBulge, _Lash, _LashOuter, _LowerLash, _Lid, _LidCurve, _LowerLid, _LidShade;
            float _Iris, _IrisScaleY, _Pupil; float4 _IrisPos, _Highlight, _Highlight2;
            half4 _Sclera, _IrisColor, _IrisLight, _IrisDark;
            float _MouthW, _Corner, _Open, _OpenShape, _LipLine, _LowerLip, _Teeth, _Tongue;
            half4 _Inner, _TeethColor, _TongueColor;
            float _NoseStyle, _NoseSize, _NoseShade;
            float _BrowThick, _BrowArch, _BrowTilt, _BrowLift; half4 _BrowColor;
            float _CoverSoft;
            CBUFFER_END

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A a) { V o; o.pos = TransformObjectToHClip(a.pos.xyz); o.uv = a.uv; return o; }

            // anti-aliased step: 1 inside (d < 0), 0 outside, over ~1.2 px
            float aa(float d) { float w = max(fwidth(d), 1e-5) * 1.2; return saturate(0.5 - d / w); }
            float band(float d, float t) { return aa(abs(d) - t); }   // a line of half-thickness t around d = 0

            // ---------------------------------------------------------------- eye
            // p: centred coordinates, x to the OUTER corner (mirrored for the right eye), y up. The lid curves are
            // parabolas between the corners; the upper lid descends toward the lower one with _Lid.
            half4 eye(float2 p)
            {
                float x = p.x / _EyeW;                              // -1 inner .. +1 outer
                float inside = saturate(1.0 - x * x);               // 0 at the corners
                float sq = lerp(sqrt(inside), inside, 1.0 - _Round); // round eyes bulge like a circle, almond ones like a parabola
                float tiltY = _Tilt * x;                            // the outer corner sits higher
                float top = tiltY + _EyeH * _TopBulge * sq;
                float bot = tiltY - _EyeH * _BotBulge * sq;
                // the lower lid rises in a squint, the upper lid closes to a curve: a plain shut line, or a happy ^ arc
                bot = lerp(bot, bot + (top - bot) * 0.35, _LowerLid);
                float shutY = lerp(tiltY, tiltY + _EyeH * 0.45 * sq, _LidCurve);
                float lidTop = lerp(top, shutY, _Lid);
                float lidBot = lerp(bot, shutY, _Lid * _LidCurve);
                float openD = max(p.y - lidTop, lidBot - p.y);      // < 0 inside the open eye
                openD = max(openD, abs(x) - 1.0);
                float open = aa(openD);
                // lashes: the upper lid line, thicker toward the outer corner; a thin lower line
                float lashT = _Lash * lerp(1.0, _LashOuter, saturate(x * 0.5 + 0.5)) * lerp(1.0, 0.6, _Lid);
                float upperLine = band(p.y - lidTop, lashT) * step(abs(x), 1.02);
                float lowerLine = band(p.y - lidBot, _LowerLash) * step(abs(x), 0.95) * (1.0 - _Lid * _LidCurve);
                // iris / pupil / highlights, clipped to the open eye
                float2 g = clamp(_IrisPos.xy, -0.6, 0.6) * float2(_EyeW, _EyeH) * 0.8;
                float2 ip = p - g; ip.y /= _IrisScaleY;
                float ir = length(ip);
                float iris = aa(ir - _Iris);
                float irisLight = aa(ir - _Iris * 0.70) * step(0.0, ip.y + _Iris * 0.1);   // the lit upper half band
                float irisDark = aa(_Iris * 0.55 - ir) * step(ip.y, -_Iris * 0.05) ;        // the deep lower band
                float pupil = aa(ir - _Pupil);
                float hl1 = aa(length((p - g - _Highlight.xy) * float2(1.0, 1.25)) - _Highlight.z);
                float hl2 = aa(length((p - g - _Highlight2.xy)) - _Highlight2.z);
                half3 col = _Sclera.rgb;
                col = lerp(col, _IrisColor.rgb, iris);
                col = lerp(col, _IrisLight.rgb, iris * irisLight * 0.9);
                col = lerp(col, _IrisDark.rgb, iris * irisDark);
                col = lerp(col, _Ink.rgb, pupil);
                col = lerp(col, half3(1, 1, 1), max(hl1, hl2 * 0.9) * iris);
                // the lid crease: a soft shade just under the upper lash
                float crease = saturate(1.0 - (lidTop - p.y) / (_EyeH * 0.35)) * step(p.y, lidTop) * _LidShade * 0.35;
                col = lerp(col, col * 0.75, crease);
                float a = open;
                col = lerp(col, _Ink.rgb, max(upperLine, lowerLine));
                a = max(a, max(upperLine, lowerLine));
                return half4(col, a);
            }

            // ---------------------------------------------------------------- mouth
            half4 mouth(float2 p)
            {
                float x = p.x / _MouthW;
                float inside = saturate(1.0 - x * x);
                float cornerY = _Corner * 0.16 * (x * x);          // smile lifts the corners (+), frown drops them
                float mid = -cornerY * 0.6;                       // keep the centre where it is
                float lipY = mid + cornerY;
                float openW = lerp(sqrt(inside), inside * inside, _OpenShape);   // wide grin vs round 'o'
                float gap = _Open * 0.42 * openW;
                float upper = lipY + gap * 0.35;
                float lower = lipY - gap * 0.65;
                float innerD = max(p.y - upper, lower - p.y);
                innerD = max(innerD, abs(x) - 1.0);
                float innerA = aa(innerD) * step(0.02, _Open);
                // teeth: a band under the upper lip; tongue: a mound from the lower lip
                float teethH = gap * 0.30 * _Teeth;
                float teeth = aa(max(p.y - upper, (upper - teethH) - p.y)) * innerA * step(0.04, gap);
                float tongueR = _MouthW * 0.45 * _Tongue;
                float tongue = aa(length((p - float2(0, lower - tongueR * 0.55)) * float2(1, 1.6)) - tongueR) * innerA;
                // ink: the lip line (closed) or both lip edges (open), thicker in the middle
                float t = _LipLine * (0.6 + 0.4 * inside);
                float lineU = band(p.y - upper, t) * step(abs(x), 1.0);
                float lineL = band(p.y - lower, t * 0.8) * step(abs(x), 1.0) * step(0.02, _Open);
                float closed = band(p.y - lipY, t) * step(abs(x), 1.0) * (1.0 - step(0.02, _Open));
                float corners = aa(length(float2(abs(x) - 1.0, (p.y - (lipY)) / max(t, 1e-3) * 0.02)) - 0.03) * 0.0;
                // the lower lip's fullness: a soft skin shade under the mouth
                float fullness = saturate(1.0 - (lower - t - p.y) / (0.09 * _LowerLip + 1e-3)) * step(p.y, lower - t) * step(abs(x), 0.9) * _LowerLip;
                half3 col = _Inner.rgb;
                col = lerp(col, _TeethColor.rgb, teeth);
                col = lerp(col, _TongueColor.rgb, tongue * (1.0 - teeth));
                float a = innerA;
                half3 shade = _Skin.rgb * 0.82;
                col = lerp(col, shade, fullness * (1.0 - a));
                a = max(a, fullness * 0.6);
                float ink = max(max(lineU, lineL), closed);
                col = lerp(col, _Ink.rgb, ink);
                a = max(a, ink);
                return half4(col, a);
            }

            // ---------------------------------------------------------------- nose (anime: a shadow stroke, not a volume)
            half4 nose(float2 p)
            {
                float s = _NoseSize;
                half3 shade = _Skin.rgb * 0.72;
                float a = 0;
                if (_NoseStyle < 0.5)
                {   // tick: a short diagonal stroke under the tip, to the lit side
                    float2 q = p - float2(0.05 * s, -0.08 * s);
                    float2 d = normalize(float2(0.75, -0.65));
                    float along = dot(q, d); float perp = abs(dot(q, float2(-d.y, d.x)));
                    a = aa(perp - 0.028 * s) * step(abs(along - 0.08 * s), 0.10 * s);
                }
                else if (_NoseStyle < 1.5)
                {   // line: the bridge shadow down one side plus the nostril wing
                    float2 q = p - float2(-0.06 * s, 0.0);
                    a = band(q.x - q.y * 0.18, 0.02 * s) * step(abs(q.y - 0.08 * s), 0.22 * s);
                    a = max(a, aa(length((p - float2(0.0, -0.14 * s)) * float2(0.7, 1.2)) - 0.07 * s) * 0.9);
                }
                else if (_NoseStyle < 2.5)
                {   // point: the small triangular shadow under a sharp tip
                    float2 q = p - float2(0, -0.10 * s);
                    float tri = max(abs(q.x) * 1.9 + q.y, -q.y - 0.09 * s);
                    a = aa(tri - 0.02 * s);
                }
                else
                {   // button: a round shadow with a lit dot
                    float r = length((p - float2(0, -0.06 * s)) * float2(1.0, 1.15));
                    a = aa(r - 0.11 * s) * (1.0 - aa(length(p - float2(-0.035 * s, -0.02 * s)) - 0.03 * s));
                }
                return half4(shade, a * _NoseShade * 2.0);
            }

            // ---------------------------------------------------------------- brow (one ink stroke: arch, tilt, lift)
            half4 brow(float2 p)
            {
                float x = p.x / 0.45;                                // -1 inner .. +1 outer
                float arch = _BrowArch * (1.0 - x * x) * 1.3;
                float y0 = _BrowLift + arch + _BrowTilt * (-x) * 0.14; // + tilt: inner end down (anger), - : inner up (sorrow)
                float thick = _BrowThick * lerp(1.0, 0.6, saturate(x * 0.5 + 0.5)) * 0.9; // tapers to the outer end
                float d = abs(p.y - y0) - thick;
                float ends = max(abs(x) - 1.0, 0.0);
                d = max(d, ends * 0.45);
                return half4(_BrowColor.rgb, aa(d));
            }

            // ---------------------------------------------------------------- cover: skin over the painted feature
            half4 cover(float2 p)
            {
                float r = length(p * float2(1.0, 1.6));
                float a = 1.0 - smoothstep(0.5 - _CoverSoft * 0.5, 0.5, r);
                return half4(_Skin.rgb, a);
            }

            half4 frag(V i) : SV_Target
            {
                float2 p = i.uv - 0.5;
                if (_Mirror > 0.5) p.x = -p.x;
                half4 c;
                if (_Part < 0.5) c = eye(p);
                else if (_Part < 1.5) c = mouth(p);
                else if (_Part < 2.5) c = nose(p);
                else if (_Part < 3.5) c = brow(p);
                else c = cover(p);
                c.a *= _Alpha;
                clip(c.a - 0.002);
                return c;
            }
            ENDHLSL
        }
    }
    FallBack Off
}

// Anime face features for the ArgonAvatarRig bodies (2026-10-18, owner: "several eyes, noses, mouths … make all the eyes
// and mouths fully animated"; same day: "Higher quality please"). Each feature is one quad on the face, drawn as
// signed-distance shapes (crisp at any zoom, no sprite frames): _Part 0 = eye, 1 = mouth, 2 = nose, 3 = brow, 4 = a skin
// cover (hides the painted feature under it). Every shape is a parameter, so a blink, a glance, a smile or a spoken
// vowel is a value changing over time (AvatarFaceRig drives them).
// Style guide (HD anime cel, Duel Monsters key-art register): ink lines with weight - the upper lash is heavy, thins to
// a point at the inner corner and flicks past the outer one; a double-eyelid crease; the iris has a dark limbal ring, a
// lid shadow at the top, a light pool at the bottom, a pupil with a soft halo, a big soft highlight, a small one and a
// wet rim arc; brows are tapered strokes that peak in the outer third; the mouth is a weighted curve with corner marks
// and a lit lower lip; the nose is a shadow with a tick. Hard edges are anti-aliased over ~1.2 px; gradients are few
// and broad.
Shader "WRLDZ/AvatarAnimeFace"
{
    Properties
    {
        _Part ("Part (0 eye 1 mouth 2 nose 3 brow 4 cover)", Float) = 0
        _Alpha ("Presence", Float) = 1
        // the card may be larger than the feature's reference frame (the frame every parameter is measured in): p is scaled
        // back to it, so a long flick, tall lashes or a steep brow never clip at the card's edge (AvatarFaceRig sets it)
        _CardScale ("Card size / reference frame (xy)", Vector) = (1, 1, 0, 0)
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
        _Flick ("Lash flick rise past the outer corner", Float) = 0.09
        _FlickLen ("Lash flick length (eye half widths)", Float) = 0.18
        _Crease ("Double-eyelid crease strength", Float) = 0.55
        _Lid ("Upper lid (0 open, 1 closed)", Float) = 0
        _LidCurve ("Closed-lid curve (1 = happy ^)", Float) = 0
        _LowerLid ("Lower lid rise (squint)", Float) = 0
        _LidShade ("Lid shadow on the eye", Float) = 0.5
        _Iris ("Iris radius", Float) = 0.23
        _IrisPos ("Iris offset (gaze)", Vector) = (0, 0, 0, 0)
        _IrisScaleY ("Iris vertical scale", Float) = 1.15
        _Pupil ("Pupil radius", Float) = 0.085
        _LimbalRing ("Limbal ring width (iris radii)", Float) = 0.13
        _Sclera ("Sclera", Color) = (0.97, 0.97, 0.98, 1)
        _IrisColor ("Iris", Color) = (0.36, 0.24, 0.62, 1)
        _IrisLight ("Iris light band", Color) = (0.58, 0.46, 0.84, 1)
        _IrisDark ("Iris dark band", Color) = (0.18, 0.10, 0.36, 1)
        _Highlight ("Highlight 1 (x, y, r)", Vector) = (-0.085, 0.085, 0.08, 0)
        _Highlight2 ("Highlight 2 (x, y, r)", Vector) = (0.075, -0.075, 0.036, 0)
        // ---- mouth
        _MouthW ("Mouth half width", Float) = 0.30
        _Corner ("Corners (-1 frown, +1 smile)", Float) = 0.1
        _Open ("Open (0..1)", Float) = 0
        _OpenShape ("Open shape (0 wide, 1 round 'o')", Float) = 0.3
        _LipLine ("Lip line thickness", Float) = 0.035
        _LowerLip ("Lower lip fullness shade", Float) = 0.3
        _LipShade ("Lip shading (lit lower lip, shadow above)", Float) = 0.6
        _CornerMark ("Corner marks", Float) = 0.7
        _Teeth ("Teeth (0..1)", Float) = 0.5
        _Tongue ("Tongue (0..1)", Float) = 0.4
        _Inner ("Mouth interior", Color) = (0.33, 0.10, 0.12, 1)
        _TeethColor ("Teeth", Color) = (0.96, 0.95, 0.92, 1)
        _TongueColor ("Tongue", Color) = (0.80, 0.36, 0.40, 1)
        // ---- nose
        _NoseStyle ("Nose style (0 tick 1 line 2 point 3 button)", Float) = 0
        _NoseSize ("Nose size", Float) = 0.5
        _NoseShade ("Nose shade strength", Float) = 0.35
        _NoseSoft ("Soft side shadow", Float) = 0.5
        // ---- brow
        _BrowThick ("Brow thickness", Float) = 0.09
        _BrowArch ("Brow arch", Float) = 0.12
        _BrowTilt ("Brow tilt (+ angry inner-down, - sad)", Float) = 0
        _BrowLift ("Brow lift", Float) = 0
        _BrowPeak ("Brow peak position (-1 inner .. 1 outer)", Float) = 0.25
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
            float _Part, _Mirror, _Alpha; float4 _CardScale;
            half4 _Skin, _Ink;
            float _EyeW, _EyeH, _Tilt, _Round, _TopBulge, _BotBulge, _Lash, _LashOuter, _LowerLash, _Flick, _FlickLen, _Crease;
            float _Lid, _LidCurve, _LowerLid, _LidShade;
            float _Iris, _IrisScaleY, _Pupil, _LimbalRing; float4 _IrisPos, _Highlight, _Highlight2;
            half4 _Sclera, _IrisColor, _IrisLight, _IrisDark;
            float _MouthW, _Corner, _Open, _OpenShape, _LipLine, _LowerLip, _LipShade, _CornerMark, _Teeth, _Tongue;
            half4 _Inner, _TeethColor, _TongueColor;
            float _NoseStyle, _NoseSize, _NoseShade, _NoseSoft;
            float _BrowThick, _BrowArch, _BrowTilt, _BrowLift, _BrowPeak; half4 _BrowColor;
            float _CoverSoft;
            CBUFFER_END

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A a) { V o; o.pos = TransformObjectToHClip(a.pos.xyz); o.uv = a.uv; return o; }

            // anti-aliased step: 1 inside (d < 0), 0 outside, over ~1.2 px; the width is clamped so a kink in d can
            // never smear a line across the quad
            float aa(float d) { float w = clamp(fwidth(d), 1e-5, 0.02) * 1.2; return saturate(0.5 - d / w); }
            float band(float d, float t) { return aa(abs(d) - t); }   // a line of half-thickness t around d = 0
            float soft(float d, float w) { return 1.0 - smoothstep(0.0, max(w, 1e-4), d); }   // a broad soft edge

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
                float awake = 1.0 - _Lid * (1.0 - _LidCurve * 0.5);

                // ---- the eye itself
                float2 g = clamp(_IrisPos.xy, -0.6, 0.6) * float2(_EyeW, _EyeH) * 0.8;
                float2 ip = p - g; ip.y /= _IrisScaleY;
                float ir = length(ip);
                float irisA = aa(ir - _Iris);
                float yn = ip.y / max(_Iris, 1e-4);                                   // -1 bottom .. +1 top of the iris
                // the iris: dark under the lid, the base tone in the middle, a light pool at the bottom, a limbal ring
                half3 irisCol = lerp(_IrisDark.rgb, _IrisColor.rgb, smoothstep(-0.15, 0.55, 0.5 - yn * 0.5 + 0.25));
                irisCol = lerp(irisCol, _IrisColor.rgb * 0.82 + _IrisDark.rgb * 0.18, smoothstep(0.35, 0.95, yn));
                float pool = aa(ir - _Iris * 0.74) * smoothstep(-0.05, -0.45, yn);
                irisCol = lerp(irisCol, _IrisLight.rgb, pool * 0.85);
                float ring = aa(_Iris * (1.0 - _LimbalRing) - ir);                   // 1 inside the ring's inner edge
                irisCol = lerp(_IrisDark.rgb * 0.75, irisCol, ring);
                // the pupil with a soft halo
                float pupil = aa(ir - _Pupil);
                float halo = soft(ir - _Pupil, _Pupil * 0.55);
                irisCol = lerp(irisCol, irisCol * 0.6, halo * 0.6);
                irisCol = lerp(irisCol, _Ink.rgb, pupil);
                // highlights: the big soft one, the small sharp one, the wet rim arc at the bottom
                float hl1 = aa(length((p - g - _Highlight.xy) * float2(1.0, 1.3)) - _Highlight.z);
                float hl1soft = soft(length((p - g - _Highlight.xy) * float2(1.0, 1.3)) - _Highlight.z, _Highlight.z * 0.5);
                float hl2 = aa(length(p - g - _Highlight2.xy) - _Highlight2.z);
                float rim = band(ir - _Iris * 0.80, _Iris * 0.045) * smoothstep(-0.3, -0.75, yn);
                irisCol = lerp(irisCol, lerp(irisCol, half3(1, 1, 1), 0.35), hl1soft);
                irisCol = lerp(irisCol, half3(1, 1, 1), max(hl1, max(hl2 * 0.92, rim * 0.6)));
                half3 col = lerp(_Sclera.rgb, irisCol, irisA);
                // the lid's shadow on the eye: a cool band under the upper lash
                float shadeD = (lidTop - p.y) / max(_EyeH * 0.42, 1e-4);
                float shade = (1.0 - smoothstep(0.0, 1.0, shadeD)) * step(p.y, lidTop) * _LidShade;
                col = lerp(col, col * half3(0.70, 0.72, 0.80), shade * 0.55);
                // the sclera's own corner shading (the eye is a ball)
                col = lerp(col, col * 0.90, (1.0 - irisA) * saturate(abs(x) * 1.4 - 0.5) * 0.6);

                // ---- ink: the upper lash (a point at the inner corner, heavy outward, a flick past the outer corner)
                float u = saturate(x * 0.5 + 0.5);                                        // 0 inner .. 1 outer
                float weight = lerp(0.55, _LashOuter, smoothstep(0.0, 0.85, u));
                weight *= saturate((x + 1.06) / 0.28);                                    // the inner point
                float lashT = _Lash * weight * lerp(1.0, 0.65, _Lid);
                float upperLine = band(p.y - (lidTop + lashT * 0.25), lashT) * step(abs(x), 1.02);
                // the flick: continues the outer corner, rises, tapers to a point
                float fx = (x - 1.0) / max(_FlickLen, 1e-3);
                float flickY = _Tilt + lerp(0.0, _Flick, fx) + _EyeH * _TopBulge * 0.08 * (1.0 - fx);
                float flickT = _Lash * _LashOuter * (1.0 - fx) * 0.75 * awake;
                float flick = aa(abs(p.y - flickY) - flickT) * step(0.0, fx) * step(fx, 1.0);
                // the lower lash: a thin line from mid eye to the outer corner, a little heavier at the end
                float lowT = _LowerLash * lerp(0.6, 1.5, u) * saturate((x + 0.35) / 0.3);
                float lowerLine = band(p.y - (lidBot - lowT * 0.5), lowT) * step(abs(x), 0.98) * (1.0 - _Lid * _LidCurve);
                // the double-eyelid crease: a soft, thin stroke above the lash, fading at both ends
                float creaseY = top + _EyeH * 0.24 - _Lid * _EyeH * 0.08;
                float crease = band(p.y - creaseY, 0.010) * saturate(inside * 2.2) * _Crease * awake * 0.55;

                float a = open;
                col = lerp(col, _Ink.rgb, max(max(upperLine, flick), lowerLine));
                a = max(a, max(max(upperLine, flick), lowerLine));
                half3 creaseCol = lerp(_Skin.rgb * 0.70, _Ink.rgb, 0.35);
                col = lerp(col, creaseCol, crease * (1.0 - a));
                a = max(a, crease);
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
                float isOpen = step(0.02, _Open);
                float innerD = max(p.y - upper, lower - p.y);
                innerD = max(innerD, abs(x) - 1.0);
                float innerA = aa(innerD) * isOpen;
                // teeth: a band under the upper lip; tongue: a mound from the lower lip
                float teethH = gap * 0.30 * _Teeth;
                float teeth = aa(max(p.y - upper, (upper - teethH) - p.y)) * innerA * step(0.04, gap);
                float tongueR = _MouthW * 0.45 * _Tongue;
                float tongue = aa(length((p - float2(0, lower - tongueR * 0.55)) * float2(1, 1.6)) - tongueR) * innerA;
                // ink: the lip line (closed) or both lip edges (open); weighted in the middle, tapering to the corners
                float t = _LipLine * 0.55 * (0.22 + 0.78 * pow(inside, 0.9));
                float lineU = band(p.y - upper, t) * step(abs(x), 1.0);
                float lineL = band(p.y - lower, t * 0.8) * step(abs(x), 1.0) * isOpen;
                float closed = band(p.y - lipY, t) * step(abs(x), 1.0) * (1.0 - isOpen);
                // corner marks: a small weighted dot at each end of the line
                float2 cR = float2(_MouthW, _Corner * 0.16 + mid); float2 cL = float2(-_MouthW, _Corner * 0.16 + mid);
                float mr = _LipLine * 0.55 * 0.5;
                float marks = max(aa(length((p - cR) * float2(1.0, 1.3)) - mr), aa(length((p - cL) * float2(1.0, 1.3)) - mr)) * _CornerMark * 0.8;
                // lip shading: a lit lower lip under the line, a thin shadow above it
                float below = (lower - t - p.y);
                float litLip = soft(below, 0.055) * step(0.0, below) * saturate(inside * 1.6) * _LipShade * 0.5;
                float above = p.y - (upper + t);
                float shadowLip = soft(above, 0.02) * step(0.0, above) * saturate(inside * 1.8) * _LipShade * 0.5;
                // the lower lip's fullness: a very soft skin shade under an OPEN mouth only (closed, the line is enough)
                float fullness = soft(lower - t - p.y, 0.07 * _LowerLip + 1e-3) * step(p.y, lower - t) * step(abs(x), 0.9) * _LowerLip * isOpen;
                half3 col = _Inner.rgb;
                col = lerp(col, _TeethColor.rgb, teeth);
                col = lerp(col, _TongueColor.rgb, tongue * (1.0 - teeth));
                float a = innerA;
                half3 shade = _Skin.rgb * 0.86;
                col = lerp(col, shade, fullness * (1.0 - a));
                a = max(a, fullness * 0.45);
                col = lerp(col, _Skin.rgb * 1.05, litLip * (1.0 - a));
                a = max(a, litLip * 0.45);
                col = lerp(col, _Skin.rgb * 0.88, shadowLip * (1.0 - a));
                a = max(a, shadowLip * 0.5);
                float ink = max(max(max(lineU, lineL), closed), marks);
                col = lerp(col, _Ink.rgb, ink);
                a = max(a, ink);
                return half4(col, a);
            }

            // ---------------------------------------------------------------- nose (anime: a shadow and a tick, not a volume)
            half4 nose(float2 p)
            {
                float s = _NoseSize;
                half3 shade = lerp(_Skin.rgb * 0.58, half3(0.50, 0.30, 0.27), 0.2);
                float a = 0;
                // a broad soft shadow on the shaded side of the tip (left), under every style
                float softA = soft(length((p - float2(-0.035 * s, -0.045 * s)) * float2(1.0, 0.85)) - 0.02 * s, 0.11 * s) * _NoseSoft * 0.35;
                if (_NoseStyle < 0.5)
                {   // tick: a short diagonal stroke under the tip, to the lit side
                    float2 q = p - float2(0.05 * s, -0.08 * s);
                    float2 d = normalize(float2(0.75, -0.65));
                    float along = dot(q, d); float perp = abs(dot(q, float2(-d.y, d.x)));
                    a = aa(perp - 0.026 * s) * step(abs(along - 0.09 * s), 0.125 * s);
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
                float crisp = a * saturate(0.65 + _NoseShade);
                return half4(shade, max(crisp, softA));
            }

            // ---------------------------------------------------------------- brow (one tapered stroke: arch, peak, tilt, lift)
            half4 brow(float2 p)
            {
                float x = p.x / 0.45;                                // -1 inner .. +1 outer
                float xp = (x - _BrowPeak) / (1.0 + abs(_BrowPeak));
                float arch = _BrowArch * saturate(1.0 - xp * xp) * 1.3;
                float y0 = _BrowLift + arch + _BrowTilt * (-x) * 0.14; // + tilt: inner end down (anger), - : inner up (sorrow)
                float u = saturate(x * 0.5 + 0.5);
                float thick = _BrowThick * lerp(1.05, 0.42, smoothstep(0.0, 1.0, u));   // heavy head, thin tail
                thick *= saturate((1.10 - x) / 0.40);                                    // the tail runs out to a point
                thick *= saturate((x + 1.10) / 0.22);                                    // the head is rounded, not cut
                float d = abs(p.y - y0) - thick;
                d = max(d, abs(x) - 1.08);
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
                float2 p = (i.uv - 0.5) * max(_CardScale.xy, float2(0.01, 0.01));
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

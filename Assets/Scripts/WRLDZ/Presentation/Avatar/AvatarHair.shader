// Player hairstyles (OverworldAvatar3d, Resources/WRLDZ/Avatar3D/Hair), HD anime cel shading (AnimeToon.hlsl: shadow
// band, rim, an "angel ring" sheen, ink outline pass). The mesh carries its colour zones
// (tools/custom/hair_build.py): vertex colour R tip, G fringe/bangs, B streak, A side (0 right .. 1 left), and
// UV1.x = the linear luminance of the vertex's original colour, so detail = (texture luminance / UV1.x)^0.75 keeps
// the painted lock shading at ~1 whatever colour the style was generated in.
// _Original = 1 draws the style's own painted colours. Otherwise root, then split side, tips, streaks, fringe on top;
// _Zones = (tip, fringe, streak, split) on/off.
Shader "WRLDZ/AvatarHair"
{
    Properties
    {
        _BaseMap ("Base", 2D) = "white" {}
        _BaseColor ("Tint / alpha", Color) = (1, 1, 1, 1)
        _Original ("Original colours", Float) = 1
        _HairRoot ("Root", Color) = (0.1, 0.1, 0.12, 1)
        _HairTip ("Tip", Color) = (0.8, 0.27, 0.5, 1)
        _HairFringe ("Fringe", Color) = (0.9, 0.72, 0.3, 1)
        _HairStreak ("Streak", Color) = (0.9, 0.72, 0.3, 1)
        _HairSide ("Split side", Color) = (0.25, 0.8, 1, 1)
        _Zones ("Tip, fringe, streak, split on", Vector) = (0, 0, 0, 0)
        _ShadowAmt ("Anime shadow band depth", Range(0, 0.5)) = 0.1
        _RimAmt ("Rim light", Range(0, 1)) = 0.5
        _Sheen ("Hair sheen", Range(0, 1)) = 0.35
        _OutlineWidth ("Ink outline width (px)", Range(0, 4)) = 1.3
        _OutlineDark ("Ink = hair colour x", Range(0, 1)) = 0.3
        _HatClip ("Hat clip plane (world xyz normal, w)", Vector) = (0, 1, 0, 0)
        _HatClipOn ("Clip the hair inside a worn hat", Float) = 0
        _HatTuckC ("Tuck ellipsoid centre (world xyz, w = band m)", Vector) = (0, 0, 0, 0.04)
        _HatTuckX ("Tuck axis X / radius", Vector) = (0, 0, 0, 0)
        _HatTuckY ("Tuck axis Y / radius", Vector) = (0, 0, 0, 0)
        _HatTuckZ ("Tuck axis Z / radius", Vector) = (0, 0, 0, 0)
        _HatTuckOn ("Tuck the hair under a crowned hat's rim", Float) = 0
        // hair physics (2026-10-19, owner: "the hair and assets need to obey real physics and not clip"): HairPhysics.cs
        // feeds a damped-spring swing, the head frame and the colliders every frame
        _HairPhysOn ("Hair physics on", Float) = 0
        _HairHead ("Skull centre (world xyz), w = world units per metre", Vector) = (0, 0, 0, 1)
        _HairAxisR ("Head right (world)", Vector) = (1, 0, 0, 0)
        _HairAxisU ("Head up (world)", Vector) = (0, 1, 0, 0)
        _HairAxisF ("Head forward (world)", Vector) = (0, 0, 1, 0)
        _HairSkull ("Skull radii r / u / f (m), w = sway reach (m)", Vector) = (0.085, 0.11, 0.1, 0.12)
        _HairSwing ("Swing at full weight (world xyz), w = stiffness factor above the ears", Vector) = (0, 0, 0, 0.3)
        _HairNeckA ("Neck capsule base (world xyz), w = radius (world)", Vector) = (0, 0, 0, 0)
        _HairNeckB ("Neck capsule top (world xyz)", Vector) = (0, 0, 0, 0)
        _HairChest ("Chest ellipsoid centre (world xyz)", Vector) = (0, 0, 0, 0)
        _HairChestR ("Chest radii along the body's r / u / f (world)", Vector) = (0, 0, 0, 0)
        _HairBodyR ("Body right (world)", Vector) = (1, 0, 0, 0)
        _HairBodyU ("Body up (world)", Vector) = (0, 1, 0, 0)
        _HairBodyF ("Body forward (world)", Vector) = (0, 0, 1, 0)
        _HairArm0A ("Arm capsule 0 start (world xyz), w = radius (world)", Vector) = (0, 0, 0, 0)
        _HairArm0B ("Arm capsule 0 end (world xyz)", Vector) = (0, 0, 0, 0)
        _HairArm1A ("Arm capsule 1 start / radius", Vector) = (0, 0, 0, 0)
        _HairArm1B ("Arm capsule 1 end", Vector) = (0, 0, 0, 0)
        _HairArm2A ("Arm capsule 2 start / radius", Vector) = (0, 0, 0, 0)
        _HairArm2B ("Arm capsule 2 end", Vector) = (0, 0, 0, 0)
        _HairArm3A ("Arm capsule 3 start / radius", Vector) = (0, 0, 0, 0)
        _HairArm3B ("Arm capsule 3 end", Vector) = (0, 0, 0, 0)
        _HairHullOn ("Torso hull on", Float) = 0
        _HairHullO ("Torso hull origin (world xyz), w = world units per metre", Vector) = (0, 0, 0, 1)
        _HairHullU ("Torso hull right (world)", Vector) = (1, 0, 0, 0)
        _HairHullV ("Torso hull up (world)", Vector) = (0, 1, 0, 0)
        _HairHullN ("Torso hull backward (world)", Vector) = (0, 0, -1, 0)
        _HairHullPush ("The mane's move back over the worn layers (R, m) and its inner layer's depth (G, m), across / down the back", 2D) = "black" {}
        _HairGlassOn ("Eyewear clearance on", Float) = 0
        _HairGlassMap ("Eyewear clearance (R = how far forward a strand may come, head-bone units)", 2D) = "black" {}
        _HairGlassO ("Clearance origin (world xyz), w = head-bone scale", Vector) = (0, 0, 0, 1)
        _HairGlassU ("Clearance across (world)", Vector) = (1, 0, 0, 0)
        _HairGlassV ("Clearance up (world)", Vector) = (0, 1, 0, 0)
        _HairGlassN ("Clearance outward (world)", Vector) = (0, 0, 1, 0)
        _HairGlassRect ("Clearance rect u0 / v0 / u span / v span (head-bone units)", Vector) = (0, 0, 1, 1)
        // Meshy hair sculpts (2026-10-18) ship a tangent-space normal map: the strand detail Meshy painted shapes the
        // cel band and sheen as in the owner's Blender view
        _NormalMap ("Normal map (tangent space)", 2D) = "bump" {}
        _NormalOn ("Use the normal map", Float) = 0
        [HideInInspector] _Surface ("Surface", Float) = 0
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        TEXTURE2D(_HairGlassMap); SAMPLER(sampler_HairGlassMap);
        TEXTURE2D(_HairHullPush); SAMPLER(sampler_HairHullPush);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _HairRoot, _HairTip, _HairFringe, _HairStreak, _HairSide;
            half4 _Zones;
            float4 _HatClip;
            half _HatClipOn;
            float4 _HatTuckC, _HatTuckX, _HatTuckY, _HatTuckZ;
            half _HatTuckOn;
            half _HairPhysOn;
            float4 _HairHead, _HairAxisR, _HairAxisU, _HairAxisF, _HairSkull, _HairSwing, _HairNeckA, _HairNeckB, _HairChest, _HairChestR;
            float4 _HairBodyR, _HairBodyU, _HairBodyF;
            float4 _HairArm0A, _HairArm0B, _HairArm1A, _HairArm1B, _HairArm2A, _HairArm2B, _HairArm3A, _HairArm3B;
            half _HairGlassOn, _HairHullOn;
            float4 _HairHullO, _HairHullU, _HairHullV, _HairHullN;
            float4 _HairGlassO, _HairGlassU, _HairGlassV, _HairGlassN, _HairGlassRect;
            half _Original, _ShadowAmt, _RimAmt, _Sheen, _OutlineWidth, _OutlineDark;
            half _NormalOn;
            half _Surface, _SrcBlend, _DstBlend, _ZWrite;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float2 uv1 : TEXCOORD1;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // the shading normal: the sculpt's normal map (when the material carries one) over the vertex normal
        float3 ShadingNormal(float3 normalWS, float4 tangentWS, float2 uv)
        {
            float3 n = normalize(normalWS);
            if (_NormalOn < 0.5h) return n;
            half3 tn = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv));
            float3 t = normalize(tangentWS.xyz - n * dot(n, tangentWS.xyz));
            float3 b = cross(n, t) * tangentWS.w;
            return normalize(tn.x * t + tn.y * b + tn.z * n);
        }

        half3 HairColour(float2 uv, half4 zones, float refLum)
        {
            half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
            half3 col = tex.rgb;
            if (_Original < 0.5h)
            {
                half lum = dot(tex.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                half detail = clamp(pow(max(lum, 1e-4) / max(refLum, 1e-3), 0.75), 0.4, 1.55);
                half side = smoothstep(0.35h, 0.65h, zones.a);
                col = _HairRoot.rgb;
                col = lerp(col, _HairSide.rgb, side * _Zones.w);
                col = lerp(col, _HairTip.rgb, saturate(zones.r) * _Zones.x);
                col = lerp(col, _HairStreak.rgb, saturate(zones.b) * _Zones.z);
                col = lerp(col, _HairFringe.rgb, saturate(zones.g) * _Zones.y);
                col *= detail;
            }
            return col;
        }

        // ---- hair physics (HairPhysics.cs). A strand sways with the damped-spring swing in proportion to how far it reaches
        // out of the scalp (roots never move), fully where it hangs below the ears, by _HairSwing.w where it stands up
        // (gelled spikes). Hair lying over eyewear is tucked behind the lens plane (the glasses sit on the nose, in front
        // of the bangs). A swung strand never enters the skull, the neck or the chest: it is pushed back to their surface
        // - but only a strand whose sculpted rest position is outside them, so the owner's sculpt never changes shape.
        float EllipsoidMetric(float3 p, float3 c, float3 r, float3 ax, float3 ay, float3 az)
        {
            float3 d = p - c;
            return length(float3(dot(d, ax) / max(r.x, 1e-5), dot(d, ay) / max(r.y, 1e-5), dot(d, az) / max(r.z, 1e-5)));
        }
        float3 PushOutEllipsoid(float3 p, float3 rest, float3 c, float3 r, float3 ax, float3 ay, float3 az)
        {
            if (r.x <= 0.0 || EllipsoidMetric(rest, c, r, ax, ay, az) < 1.0) return p;
            float m = EllipsoidMetric(p, c, r, ax, ay, az);
            return m < 1.0 ? c + (p - c) / max(m, 1e-3) : p;
        }
        float3 PushOutCapsule(float3 p, float3 rest, float3 a, float3 b, float rad)
        {
            if (rad <= 0.0) return p;
            float3 ab = b - a;
            float tr = saturate(dot(rest - a, ab) / max(dot(ab, ab), 1e-8));
            if (length(rest - (a + ab * tr)) < rad) return p;
            float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-8));
            float3 c = a + ab * t; float3 d = p - c; float l = length(d);
            return l < rad ? c + d / max(l, 1e-6) * rad : p;
        }
        // eyewear: HairPhysics rasterises, in the glasses' plane, how far forward a strand may come at each spot - just
        // behind the glass and the frame, ramping back out past the rim, never into the face's skin. A strand in front of
        // that is moved straight back to it: the bangs pass behind the glasses as the series draws them, no strand
        // cuts through the frame and none sinks into the brow.
        float3 TuckBehindGlasses(float3 p)
        {
            if (_HairGlassOn < 0.5h) return p;
            float hs = max(_HairGlassO.w, 1e-6);
            float3 d = p - _HairGlassO.xyz;
            float2 uv = (float2(dot(d, _HairGlassU.xyz), dot(d, _HairGlassV.xyz)) / hs - _HairGlassRect.xy) / _HairGlassRect.zw;
            if (any(uv < 0.0) || any(uv > 1.0)) return p;
            float depth = dot(d, _HairGlassN.xyz);
            float lim = SAMPLE_TEXTURE2D_LOD(_HairGlassMap, sampler_HairGlassMap, uv, 0).r * hs;
            return depth > lim ? p - _HairGlassN.xyz * (depth - lim) : p;
        }
        // the torso: a mane hanging inside a coat moves back over it as a block - every layer at a spot on the back by
        // the same amount (HairPhysics measures how deep the deepest layer sat, per spot, smoothed), so the mane keeps
        // its thickness and shape. Only the mane moves (from its inner layer back); front and side locks never. Frame: HullMin / HullMax in
        // HairPhysics, metres right / up of the upper chest.
        float3 KeepOutOfTorso(float3 p)
        {
            if (_HairHullOn < 0.5h) return p;
            float s = max(_HairHullO.w, 1e-5);
            float3 d = p - _HairHullO.xyz;
            float3 l = float3(dot(d, _HairHullU.xyz), dot(d, _HairHullV.xyz), dot(d, _HairHullN.xyz)) / s;
            float2 t = (l.xy - float2(-0.36, -0.85)) / float2(0.72, 1.17);
            if (any(t < 0.0) || any(t > 1.0)) return p;
            float2 push = SAMPLE_TEXTURE2D_LOD(_HairHullPush, sampler_HairHullPush, t, 0).rg;
            // the mane from its inner layer back moves; hair in front of it there (side locks beside the neck, front
            // locks) eases out over 3 cm and stays
            float f = smoothstep(push.g - 0.04, push.g - 0.01, l.z) * smoothstep(-0.02, 0.02, l.z);
            return p + _HairHullN.xyz * push.r * f * s;
        }
        // the arms: always kept out, wherever the strand rests - the sculpts were shaped for arms held out, so any strand
        // inside a lowered arm is a pose clip, never the sculpt's intent
        float3 PushOutArm(float3 p, float4 a, float3 b)
        {
            if (a.w <= 0.0) return p;
            float3 ab = b - a.xyz;
            float t = saturate(dot(p - a.xyz, ab) / max(dot(ab, ab), 1e-8));
            float3 c = a.xyz + ab * t; float3 d = p - c; float l = length(d);
            return l < a.w ? c + d / max(l, 1e-6) * a.w : p;
        }
        float3 HairPhysics(float3 p)
        {
            if (_HairPhysOn < 0.5h) return p;
            float s = max(_HairHead.w, 1e-5);
            float3 q = p - _HairHead.xyz;
            float3 l = float3(dot(q, _HairAxisR.xyz), dot(q, _HairAxisU.xyz), dot(q, _HairAxisF.xyz)) / s;   // metres, head frame
            float e = length(l / max(_HairSkull.xyz, 1e-4));
            float beyond = max(0.0, e - 1.0) * min(_HairSkull.x, _HairSkull.z);
            float w = saturate(beyond / max(_HairSkull.w, 1e-3));
            w = w * w * (3.0 - 2.0 * w);
            float hang = saturate((_HairSkull.y * 0.1 - l.y) / (_HairSkull.y * 0.9));
            w *= lerp(_HairSwing.w, 1.0, hang);
            float3 rest = p;
            float3 pd = p + _HairSwing.xyz * w;
            pd = TuckBehindGlasses(pd);
            if (w > 0.001)
            {
                pd = PushOutEllipsoid(pd, rest, _HairHead.xyz, _HairSkull.xyz * s, _HairAxisR.xyz, _HairAxisU.xyz, _HairAxisF.xyz);
                pd = PushOutCapsule(pd, rest, _HairNeckA.xyz, _HairNeckB.xyz, _HairNeckA.w);
                pd = PushOutEllipsoid(pd, rest, _HairChest.xyz, _HairChestR.xyz, _HairBodyR.xyz, _HairBodyU.xyz, _HairBodyF.xyz);
            }
            pd = KeepOutOfTorso(pd);
            pd = PushOutArm(pd, _HairArm0A, _HairArm0B.xyz);
            pd = PushOutArm(pd, _HairArm1A, _HairArm1B.xyz);
            pd = PushOutArm(pd, _HairArm2A, _HairArm2B.xyz);
            pd = PushOutArm(pd, _HairArm3A, _HairArm3B.xyz);
            return pd;
        }

        // under a crowned hat the hair just below the rim is drawn in toward the head (an ellipsoid round the cranium,
        // ~2 cm out, where the hat's rim sits), fading out over _HatTuckC.w below the rim plane: the strands tuck under
        // the rim instead of standing out as a shelf with a flat cut showing (review 2026-10-04)
        float3 TuckUnderHat(float3 p)
        {
            if (_HatTuckOn < 0.5h || _HatClipOn < 0.5h) return p;
            float below = -(dot(_HatClip.xyz, p) + _HatClip.w);
            float f = 1.0 - smoothstep(0.0, max(_HatTuckC.w, 1e-3), below);
            if (f <= 0.0) return p;
            float3 d = p - _HatTuckC.xyz;
            float rho = length(float3(dot(d, _HatTuckX.xyz), dot(d, _HatTuckY.xyz), dot(d, _HatTuckZ.xyz)));
            if (rho <= 1.0) return p;
            return _HatTuckC.xyz + d * lerp(1.0, 1.0 / rho, f);
        }

        // a crowned hat hides only the hair above its rim plane (fringe, sideburns and long hair below it stay)
        void ClipUnderHat(float3 positionWS)
        {
            if (_HatClipOn > 0.5h && dot(_HatClip.xyz, positionWS) + _HatClip.w > 0.0) discard;
        }

        struct VaryingsD { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

        VaryingsD vertDepth(Attributes v)
        {
            VaryingsD o = (VaryingsD)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS = TuckUnderHat(HairPhysics(TransformObjectToWorld(v.positionOS.xyz)));
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "AvatarHair"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "AnimeToon.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 zones : TEXCOORD1;
                float refLum : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                float4 tangentWS : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TuckUnderHat(HairPhysics(TransformObjectToWorld(v.positionOS.xyz)));
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.tangentWS = float4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.zones = v.color;
                o.refLum = v.uv1.x;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                ClipUnderHat(i.positionWS);
                half3 col = HairColour(i.uv, i.zones, i.refLum);
                float3 view = GetWorldSpaceViewDir(i.positionWS);
                float3 n = ShadingNormal(i.normalWS, i.tangentWS, i.uv);
                half3 shaded = AnimeShade(col, n, view, _ShadowAmt, _RimAmt);
                shaded += (col * 0.6h + 0.25h) * AnimeHairSheen(n, view) * _Sheen;
                return half4(shaded * _BaseColor.rgb, _BaseColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "AnimeOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "AnimeToon.hlsl"

            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 zones : TEXCOORD1; float refLum : TEXCOORD2; float3 positionWS : TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TuckUnderHat(HairPhysics(TransformObjectToWorld(v.positionOS.xyz)));
                o.positionCS = AnimeOutlineCS(TransformWorldToObject(o.positionWS), v.normalOS, _OutlineWidth);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.zones = v.color;
                o.refLum = v.uv1.x;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_OutlineWidth <= 0.01h) discard;
                ClipUnderHat(i.positionWS);
                return half4(HairColour(i.uv, i.zones, i.refLum) * _OutlineDark * _BaseColor.rgb, _BaseColor.a);
            }
            ENDHLSL
        }

        // depth prepasses (SSAO reads the depth-normals texture): without them the Fallback's passes wrote what the colour
        // pass discards, so a darkening halo could follow geometry nobody sees
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            half fragDepth(VaryingsD i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                ClipUnderHat(i.positionWS);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            void fragNormals(VaryingsD i, bool front : SV_IsFrontFace, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                ClipUnderHat(i.positionWS);
                float3 n = normalize(front ? i.normalWS : -i.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(n);
                outNormalWS = half4(half3(PackFloat2To888(saturate(oct * 0.5 + 0.5))), 0.0);
            #else
                outNormalWS = half4(n, 0.0);
            #endif
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}

Shader "WRLDZ/SpiritGhostUnlit"
{
    Properties
    {
        _MainTex ("Detail", 2D) = "white" {}
        _Color ("Color", Color) = (0.35, 0.85, 1, 0.40)
        _Fill ("Fill", Range(0, 1)) = 0.34
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.6
        _RimStrength ("Rim Strength", Range(0, 3)) = 1.15
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SpiritGhost"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Fill;
                half _RimPower;
                half _RimStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceNormalizeViewDir(posWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 vdir = normalize(i.viewWS);
                half ndv = abs(dot(n, vdir));
                half rim = pow(saturate(1.0h - ndv), _RimPower) * _RimStrength;

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half lum = dot(tex.rgb, half3(0.22h, 0.67h, 0.11h));
                half detail = lerp(0.88h, 1.12h, saturate(lum));

                half3 fill = _Color.rgb * _Fill * detail;
                half3 col = fill + _Color.rgb * rim;
                half a = saturate(_Color.a * detail + rim * 0.40h);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback "Sprites/Default"
}

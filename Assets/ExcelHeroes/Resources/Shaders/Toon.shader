// Cel shading for the 3D SD cast and the office stage, in the reference's manner: two tones
// with a hard terminator, a soft rim, and a dark inverted-hull outline (#1b1d25, the same edge
// colour the pixel art uses). Colour comes from vertex colour x _Color x _MainTex, so one
// material draws a whole character built from vertex-coloured primitives.
//
// Lives under Resources so it ships in the player without an Always Included entry.
Shader "ExcelHeroes/Toon"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _ShadeStrength ("Shade Strength", Range(0,1)) = 0.26
        _ShadeTint ("Shade Tint", Color) = (0.78,0.8,1,1)
        _Rim ("Rim", Range(0,1)) = 0.18
        _Flash ("Hit Flash", Range(0,1)) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _OutlineWidth ("Outline Width", Float) = 0.012
        _OutlineColor ("Outline Color", Color) = (0.106,0.114,0.145,1)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _Color;
            float _ShadeStrength;
            float4 _ShadeTint;
            float _Rim;
            float _Flash;
            float _Cutoff;
            float _OutlineWidth;
            float4 _OutlineColor;
        CBUFFER_END

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        float4 _EhLightDir;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
        };
        ENDHLSL

        // The project's URP asset uses the 2D Renderer, which draws "Universal2D" and
        // "SRPDefaultUnlit" passes only; the forward renderer draws "UniversalForward". The main
        // pass is written once and tagged for both, so the shader works under either.
        Pass
        {
            Name "Toon"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
                float4 color : COLOR;
            };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(ws);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.color = i.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color * _Color;
                clip(t.a - _Cutoff);
                float3 l = _EhLightDir.xyz;
                l = dot(l, l) > 0.0001 ? normalize(l) : normalize(float3(-0.35, 0.85, -0.45));
                float3 n = normalize(i.normalWS);
                float lit = smoothstep(-0.02, 0.06, dot(n, l));
                float3 shade = t.rgb * lerp(float3(1,1,1), _ShadeTint.rgb, _ShadeStrength) * (1 - _ShadeStrength * 0.5);
                float3 col = lerp(shade, t.rgb, lit);
                float rim = pow(1 - saturate(dot(n, normalize(i.viewWS))), 3) * _Rim;
                col += rim;
                col = lerp(col, float3(1,1,1), _Flash);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Toon2D"
            Tags { "LightMode"="Universal2D" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
                float4 color : COLOR;
            };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(ws);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.color = i.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color * _Color;
                clip(t.a - _Cutoff);
                float3 l = _EhLightDir.xyz;
                l = dot(l, l) > 0.0001 ? normalize(l) : normalize(float3(-0.35, 0.85, -0.45));
                float3 n = normalize(i.normalWS);
                float lit = smoothstep(-0.02, 0.06, dot(n, l));
                float3 shade = t.rgb * lerp(float3(1,1,1), _ShadeTint.rgb, _ShadeStrength) * (1 - _ShadeStrength * 0.5);
                float3 col = lerp(shade, t.rgb, lit);
                float rim = pow(1 - saturate(dot(n, normalize(i.viewWS))), 3) * _Rim;
                col += rim;
                col = lerp(col, float3(1,1,1), _Flash);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                float3 nws = normalize(TransformObjectToWorldNormal(i.normalOS));
                ws += nws * _OutlineWidth;
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.color = i.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.color.a * _Color.a;
                clip(a - _Cutoff);
                clip(_OutlineWidth - 0.0001);
                return half4(_OutlineColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}

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
        _Spec ("Hair Spec Band", Range(0,1)) = 0
        _Flash ("Hit Flash", Range(0,1)) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _OutlineWidth ("Outline Width", Float) = 0.012
        _OutlineColor ("Outline Color", Color) = (0.106,0.114,0.145,1)
        // BA's layered face: the eye white writes the stencil, the iris draws only inside it, and
        // the brows / lash lines are pulled toward the camera so they read over the fringe.
        [IntRange] _StencilRef ("Stencil Ref", Range(0,255)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comp", Float) = 8
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass ("Stencil Pass", Float) = 0
        _DepthPull ("Depth Pull (world units toward camera)", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
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
            float _Spec;
            float _Flash;
            float _Cutoff;
            float _OutlineWidth;
            float4 _OutlineColor;
            float _DepthPull;
        CBUFFER_END

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        float4 _EhLightDir;
        // the outline's floor in screen pixels (global; 0 = the world width only): a crisp edge of the
        // same weight at any distance, as the reference's models keep (the BA cross-check: "no crisp outlines")
        float _EhOutlinePx;
        // the cast's colour lift (global, 0 = off): saturation added over the painted sheets, which came out
        // greyed by the repaint (the part cross-check: "increase albedo saturation")
        float _EhSatBoost;
        // the cast's albedo lift (global, 0 = off): a gamma on the sheet that opens up its muddy mid-darks
        // (the BA cross-check: "muddy textures, flat") without washing the whites
        float _EhLift;
        // the cast's shading multipliers as offsets from 1 (global; unset = 0 = the materials' own): shadow, rim, hair ring
        float _EhShadeD, _EhRimD, _EhSpecD;
        float _EhHairSoft;

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
            ZTest [_ZTest]
            Stencil { Ref [_StencilRef] Comp [_StencilComp] Pass [_StencilPass] }

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
                // pulled toward the camera for depth only (the brows over the fringe): the pixel
                // stays where it is, only its depth moves
                float4 cs = TransformWorldToHClip(ws);
                if (_DepthPull > 0)
                {
                    float4 csNear = TransformWorldToHClip(ws + normalize(GetWorldSpaceViewDir(ws)) * _DepthPull);
                    cs.z = csNear.z / csNear.w * cs.w;
                }
                o.positionCS = cs;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(ws);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.color = i.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color * _Color;
                // hair (the materials with a spec band) read flat, as painted anime hair: the sheet taken a few
                // mips softer, so the sample's streaky gloss evens out (global _EhHairSoft, 0 = off; alpha stays sharp)
                if (_Spec > 0 && _EhHairSoft > 0) t.rgb = (SAMPLE_TEXTURE2D_BIAS(_MainTex, sampler_MainTex, i.uv, _EhHairSoft) * i.color * _Color).rgb;
                clip(t.a - _Cutoff);
                t.rgb = pow(max(t.rgb, 0.0001), 1.0 / (1.0 + _EhLift));
                float3 l = _EhLightDir.xyz;
                l = dot(l, l) > 0.0001 ? normalize(l) : normalize(float3(-0.35, 0.85, -0.45));
                float3 n = normalize(i.normalWS);
                float lit = smoothstep(-0.02, 0.06, dot(n, l));
                float ss = saturate(_ShadeStrength * (1 + _EhShadeD));
                float3 shade = t.rgb * lerp(float3(1,1,1), _ShadeTint.rgb, ss) * (1 - ss * 0.5);
                float3 col = lerp(shade, t.rgb, lit);
                float rim = pow(1 - saturate(dot(n, normalize(i.viewWS))), 3) * _Rim * (1 + _EhRimD);
                col += rim;
                // the anime hair's angel ring: a crisp bright band where the key light glints (hair materials only)
                // a horizontal band round the upper head: normals tipped ~35° up, facing the lens
                float band = 1 - smoothstep(0.035, 0.06, abs(n.y - 0.58));
                float facing = smoothstep(0.25, 0.5, dot(n, normalize(i.viewWS)));
                col += band * facing * _Spec * 0.34 * (1 + _EhSpecD);
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = max(0, lerp(lum.xxx, col, 1 + _EhSatBoost));
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
            ZTest [_ZTest]
            Stencil { Ref [_StencilRef] Comp [_StencilComp] Pass [_StencilPass] }

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
                float4 cs = TransformWorldToHClip(ws);
                if (_DepthPull > 0)
                {
                    float4 csNear = TransformWorldToHClip(ws + normalize(GetWorldSpaceViewDir(ws)) * _DepthPull);
                    cs.z = csNear.z / csNear.w * cs.w;
                }
                o.positionCS = cs;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(ws);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.color = i.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color * _Color;
                // hair (the materials with a spec band) read flat, as painted anime hair: the sheet taken a few
                // mips softer, so the sample's streaky gloss evens out (global _EhHairSoft, 0 = off; alpha stays sharp)
                if (_Spec > 0 && _EhHairSoft > 0) t.rgb = (SAMPLE_TEXTURE2D_BIAS(_MainTex, sampler_MainTex, i.uv, _EhHairSoft) * i.color * _Color).rgb;
                clip(t.a - _Cutoff);
                t.rgb = pow(max(t.rgb, 0.0001), 1.0 / (1.0 + _EhLift));
                float3 l = _EhLightDir.xyz;
                l = dot(l, l) > 0.0001 ? normalize(l) : normalize(float3(-0.35, 0.85, -0.45));
                float3 n = normalize(i.normalWS);
                float lit = smoothstep(-0.02, 0.06, dot(n, l));
                float ss = saturate(_ShadeStrength * (1 + _EhShadeD));
                float3 shade = t.rgb * lerp(float3(1,1,1), _ShadeTint.rgb, ss) * (1 - ss * 0.5);
                float3 col = lerp(shade, t.rgb, lit);
                float rim = pow(1 - saturate(dot(n, normalize(i.viewWS))), 3) * _Rim * (1 + _EhRimD);
                col += rim;
                // the anime hair's angel ring: a crisp bright band where the key light glints (hair materials only)
                // a horizontal band round the upper head: normals tipped ~35° up, facing the lens
                float band = 1 - smoothstep(0.035, 0.06, abs(n.y - 0.58));
                float facing = smoothstep(0.25, 0.5, dot(n, normalize(i.viewWS)));
                col += band * facing * _Spec * 0.34 * (1 + _EhSpecD);
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = max(0, lerp(lum.xxx, col, 1 + _EhSatBoost));
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
                float w = _OutlineWidth;
                if (w > 0 && _EhOutlinePx > 0)
                {
                    float px = distance(ws, _WorldSpaceCameraPos) * 2.0 / (UNITY_MATRIX_P._m11 * _ScreenParams.y);
                    w = max(w, px * _EhOutlinePx);
                }
                ws += nws * w;
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

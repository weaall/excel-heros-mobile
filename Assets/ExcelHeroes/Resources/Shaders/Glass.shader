// Unlit, alpha-blended, double-sided: the translucent spreadsheet each character carries on
// their back, contact shadows, projectiles and hit sparks. _Glow lifts the colour toward white
// for the moment a sheet fires.
Shader "ExcelHeroes/Glass"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Glow ("Glow", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            Name "Glass"
            // No LightMode: drawn as SRPDefaultUnlit by both the 2D and the forward renderer.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Glow;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.color = i.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color * _Color;
                c.rgb = lerp(c.rgb, float3(1,1,1), _Glow * 0.6);
                c.a = saturate(c.a * (1 + _Glow));
                return c;
            }
            ENDHLSL
        }
    }
}

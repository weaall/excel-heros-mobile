// The painted street (the Blue Archive cross-check, 2026-09-30: "the 3D environment is extremely flat").
// The low-poly street set (StreetSet) keeps its geometry, and its colour comes from a painting of that
// very set seen from a plate camera (tools/street_plate_gemini.py over StreetPlate.Render), projected
// back onto it: uv = the plate camera's view-projection of the world position. Because the painting
// sticks to the geometry, it holds as the battle camera follows the fight and zooms. Outside the
// plate's frame the set's own vertex colour shows, lit like the toon set.
//
// Lives under Resources so it ships in the player without an Always Included entry.
Shader "ExcelHeroes/Plate"
{
    Properties
    {
        _PlateTex ("Plate", 2D) = "white" {}
        _PlateMix ("Plate Mix", Range(0,1)) = 1
        _Tint ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _PlateMix;
            float4 _Tint;
        CBUFFER_END
        float4x4 _PlateVP;
        TEXTURE2D(_PlateTex);
        SAMPLER(sampler_PlateTex);
        float4 _EhLightDir;

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
        struct Varyings { float4 positionCS : SV_POSITION; float4 plate : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 color : COLOR; };

        Varyings vert (Attributes i)
        {
            Varyings o;
            float3 ws = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(ws);
            o.plate = mul(_PlateVP, float4(ws, 1));
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.color = i.color;
            return o;
        }

        half4 frag (Varyings i) : SV_Target
        {
            float3 l = _EhLightDir.xyz;
            l = dot(l, l) > 0.0001 ? normalize(l) : normalize(float3(-0.35, 0.85, -0.45));
            float lit = smoothstep(-0.02, 0.06, dot(normalize(i.normalWS), l));
            float3 own = i.color.rgb * lerp(0.82, 1.0, lit);
            float2 ndc = i.plate.xy / max(1e-5, i.plate.w);
            float2 uv = ndc * 0.5 + 0.5;
            float inside = step(0.0, i.plate.w) * step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
            // soften the plate's own frame edge into the set's colour
            float edge = saturate(min(min(uv.x, 1 - uv.x), min(uv.y, 1 - uv.y)) * 30.0);
            float3 plate = SAMPLE_TEXTURE2D(_PlateTex, sampler_PlateTex, uv).rgb;
            float3 col = lerp(own, plate, inside * edge * _PlateMix);
            return half4(col * _Tint.rgb, 1);
        }
        ENDHLSL

        Pass
        {
            Name "Plate"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
        Pass
        {
            Name "Plate2D"
            Tags { "LightMode"="Universal2D" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
    }
}

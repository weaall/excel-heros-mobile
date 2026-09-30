// The cast shadow of a figure on the street (the Blue Archive part cross-check, 2026-09-30: "characters float —
// no crisp directional cast shadows"): the figure's own skinned mesh drawn a second time, every vertex slid
// along the key light (_EhLightDir) down onto the ground plane (_EhGroundY), in one flat translucent dark tone.
// The stencil lets each pixel darken once, so overlapping limbs do not stack.
//
// Lives under Resources so it ships in the player without an Always Included entry.
Shader "ExcelHeroes/PlanarShadow"
{
    Properties
    {
        _ShadowColor ("Shadow", Color) = (0.05, 0.07, 0.16, 0.38)
        [IntRange] _StencilRef ("Stencil Ref", Range(0,255)) = 7
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-10" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _ShadowColor;
        CBUFFER_END
        float4 _EhLightDir;
        float _EhGroundY;

        struct Attributes { float4 positionOS : POSITION; };
        struct Varyings { float4 positionCS : SV_POSITION; float fade : TEXCOORD0; };

        Varyings vert (Attributes i)
        {
            Varyings o;
            float3 ws = TransformObjectToWorld(i.positionOS.xyz);
            float3 l = _EhLightDir.xyz;
            l = dot(l, l) > 0.0001 ? normalize(l) : normalize(float3(-0.35, 0.85, -0.45));
            float h = max(0, ws.y - _EhGroundY);
            ws.xz -= l.xz / max(0.25, l.y) * h;
            ws.y = _EhGroundY;
            o.positionCS = TransformWorldToHClip(ws);
            o.fade = saturate(1.0 - h * 0.25);          // a little lighter where it falls far from the feet
            return o;
        }

        half4 frag (Varyings i) : SV_Target
        {
            return half4(_ShadowColor.rgb, _ShadowColor.a * lerp(0.7, 1.0, i.fade));
        }
        ENDHLSL

        Pass
        {
            Name "PlanarShadow"
            Tags { "LightMode"="Universal2D" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1
            Stencil { Ref [_StencilRef] Comp NotEqual Pass Replace }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
        Pass
        {
            Name "PlanarShadowForward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1
            Stencil { Ref [_StencilRef] Comp NotEqual Pass Replace }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
    }
}

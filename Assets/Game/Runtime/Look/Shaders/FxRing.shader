// Shockwave rings (web render/fx/rings.js): the annulus is computed in the
// fragment from UV, so radius and thickness animate independently (a wave
// thinning as it passes, not a ring getting closer), with a per-ring angular
// wobble so it reads as force, not UI. One mesh draws every ring on screen.
// uv1 = (radius01, thickness01, wobble, seed). Additive, linear HDR.
Shader "Bonanza/FxRing"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+2" }
        Blend SrcAlpha One
        ZWrite Off
        ZTest [_ZTest]
        Cull Off
        Pass
        {
            Name "Ring"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 prm : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 prm : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(i.positionOS);
                o.color = i.color; o.uv = i.uv; o.prm = i.prm;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv - 0.5;
                float r = length(p) * 2.0;
                if (r > 1.0) discard;
                float ang = atan2(p.y, p.x);
                float wob = i.prm.z * (sin(ang * 3.0 + i.prm.w) * 0.65 + sin(ang * 7.0 - i.prm.w * 1.7) * 0.35);
                float d = abs(r + wob - i.prm.x);
                float th = max(i.prm.y, 0.004);
                float band = 1.0 - smoothstep(0.0, th, d);
                float core = 1.0 - smoothstep(0.0, th * 0.3, d);
                float a = (band * band * 0.8 + core * 0.85) * i.color.a;
                a *= smoothstep(1.0, 0.9, r);
                if (a < 0.004) discard;
                return half4(i.color.rgb * (0.65 + core * 1.6), a);
            }
            ENDHLSL
        }
    }
}

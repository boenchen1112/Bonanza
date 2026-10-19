// Pooled VFX sprites (web render/fx/pool.js): quads built on the CPU, one
// dynamic mesh per family, sampled from the procedural sprite atlas. Vertex
// colour carries rgb tint and alpha. Writes LINEAR HDR (the web's colours go
// into its linear scene buffer the same way), so hot cores cross the bloom
// threshold. Blend and depth test are material properties: additive for sparks
// and flares, alpha for confetti, ZTest Always for overlay sprites.
Shader "Bonanza/Fx"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _Gain ("Gain", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        ZTest [_ZTest]
        Cull Off
        Pass
        {
            Name "Fx"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Gain;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(i.positionOS);   // vertices are written in world space
                o.color = i.color;
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half a = t.a * i.color.a;
                if (a < 0.004) discard;
                return half4(t.rgb * i.color.rgb * _Gain, a);
            }
            ENDHLSL
        }
    }
}

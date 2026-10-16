// Blob shadow: one soft ellipse under an object (web shadows.js). Strength
// comes per renderer through a property block.
Shader "Bonanza/Blob"
{
    Properties { _Strength ("Strength", Float) = 0.5 }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -2, -2
        Pass
        {
            Name "Blob"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BonanzaLook.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Strength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv - 0.5) * 2.0;
                float a = 1.0 - saturate(r);
                a = a * a * (3.0 - 2.0 * a);
                return half4(_BonShadowColor.rgb, a * _Strength);
            }
            ENDHLSL
        }
    }
}

// Fake volumetric spotlight cone (web render/env/spotlights.js): additive, unlit, no depth write. Alpha
// follows |dot(normal, view)| so the body of the cone is bright and its silhouette fades out, times a
// vertical ramp (uv.y: 1 at the lamp, 0 at the floor) so the beam thins as it falls. Linear light out.
Shader "Bonanza/Beam"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; float h : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.n = TransformObjectToWorldNormal(i.normalOS);
                o.v = GetCameraPositionWS() - ws;
                o.h = i.uv.y;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float facing = abs(dot(normalize(i.n), normalize(i.v)));
                float body = pow(facing, 0.85);
                float fall = smoothstep(0.0, 0.35, i.h) * (0.25 + 0.75 * i.h);
                float a = body * fall * _Intensity;
                return half4(_Color.rgb * a, a);
            }
            ENDHLSL
        }
    }
}

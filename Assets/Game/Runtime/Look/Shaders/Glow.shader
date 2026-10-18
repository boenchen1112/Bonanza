// A soft additive glow card (web swingKings/world.js makeGlowTexture: white core to transparent amber), computed
// in the fragment so it needs no texture. Object-space quad in XY (uv 0..1); _Billboard 1 turns it to face the
// camera around its object origin (muzzle flash, tower-head flares), 0 keeps it as placed (floodlight pools lying
// on the turf). Linear HDR out: a hot core crosses the bloom threshold. Intensity 0 draws nothing visible but
// keeps the program linked, so a set can prewarm it in Load and light it mid-round without a stall.
Shader "Bonanza/Glow"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.95, 0.8, 1)
        _Intensity ("Intensity", Float) = 1
        _Core ("Core tightness", Float) = 2
        _Billboard ("Billboard", Float) = 1
        _Stretch ("Stretch (x, y)", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+20" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity, _Core, _Billboard;
                float4 _Stretch;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws;
                if (_Billboard > 0.5)
                {
                    float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                    float sx = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                    float sy = length(float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y));
                    float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                    ws = origin + right * i.positionOS.x * sx * _Stretch.x + up * i.positionOS.y * sy * _Stretch.y;
                }
                else ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d = saturate(length(i.uv * 2.0 - 1.0));
                float halo = (1.0 - d) * (1.0 - d);
                float core = pow(1.0 - d, _Core * 3.0);
                float3 warm = lerp(float3(1.0, 0.72, 0.42), float3(1.0, 1.0, 1.0), saturate(core * 1.5));
                float a = (halo * 0.55 + core * 1.4) * _Intensity * i.color.a;
                return half4(_Color.rgb * i.color.rgb * warm * a, 0);
            }
            ENDHLSL
        }
    }
}

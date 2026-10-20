// The house toon surface for set pieces (Look/Env): the Toon lighting plus a tiling value map (_BaseMap, a
// near-grey detail texture the palette colour multiplies, so the day -> night swap still owns the hue), vertex
// colour (per-item tints of the CPU-batched pieces), optional two-sided lighting and a fog multiplier (the far
// skyline opts out, like the web's fog: false). Linear light out, like Toon.
Shader "Bonanza/ToonEnv"
{
    Properties
    {
        [MainColor] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap ("Value map", 2D) = "white" {}
        _MapStrength ("Map strength", Range(0, 1)) = 1
        _Bands ("Light bands", Float) = 3
        _Softness ("Band softness", Float) = 0.22
        _Rim ("Rim multiplier", Float) = 1
        _Pulse ("Beat pulse", Float) = 0.12
        _Lift ("Emissive lift", Float) = 0
        _Desat ("Desaturate", Range(0, 1)) = 0
        _ShadowRecv ("Receive the key's shadow", Range(0, 1)) = 1
        _FogMul ("Fog", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull [_Cull]

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "BonanzaToon.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float _MapStrength, _Bands, _Softness, _Rim, _Pulse, _Lift, _FogMul, _Cull, _Desat, _ShadowRecv;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1;
                float viewDepth : TEXCOORD2; float2 uv : TEXCOORD3; half4 color : COLOR;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewDepth = -TransformWorldToView(o.positionWS).z;
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.color = i.color;
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                if (_Cull < 0.5 && !front) n = -n;   // two-sided cloth and shells light their back faces too
                half3 map = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                float3 albedo = i.color.rgb * lerp(1.0, map, _MapStrength);
                // A drained, greyed surface (a singer whose voice is out): toward its own luminance before the tint.
                albedo = lerp(albedo, dot(albedo, float3(0.2126, 0.7152, 0.0722)).xxx, _Desat);
                albedo *= _BaseColor.rgb;
                float3 c = BonToonS(albedo, n, i.positionWS, _Bands, _Softness, _Rim, _Pulse, _Lift, _ShadowRecv);
                c = lerp(c, _BonFogColor.rgb, BonFogH(i.viewDepth, i.positionWS.y) * _FogMul);
                return half4(c, 1);
            }
            ENDHLSL
        }

        // Set pieces that opt in (ShadowCaster on their renderer) throw the key's real shadow, like Toon.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(i.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}

// The house surface: banded toon ramp, directional rim, beat-driven emissive
// lift, linear fog. Outputs linear light; Bonanza/Composite tone-maps. Lit by the three-light rig in the _Bon* globals, not by
// URP lights, so it costs no light loop and needs no keywords or variants.
Shader "Bonanza/Toon"
{
    Properties
    {
        [MainColor] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Bands ("Light bands", Float) = 3
        _Softness ("Band softness", Float) = 0.22
        _Rim ("Rim multiplier", Float) = 1
        _Pulse ("Beat pulse", Float) = 0.12
        _Lift ("Emissive lift", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // The key's real shadow (a scene opts in with LookRig.ShadowFocus); off, these compile to nothing.
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "BonanzaLook.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Bands, _Softness, _Rim, _Pulse, _Lift;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; float viewDepth : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewDepth = -TransformWorldToView(o.positionWS).z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 albedo = _BaseColor.rgb;
                float pulse = _BonRimParams.z;
                const float invPi = 0.31830989;

                float keyR = BonRamp(dot(n, _BonKeyDir.xyz) * 0.5 + 0.5, _Bands, _Softness);
                // Real shadow on the key only (fill and kick stay), and only inside the scene's focus circle.
                float shadow = 1.0;
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    if (_BonShadowFocus.w > 0.0)
                    {
                        float inFocus = 1.0 - smoothstep(_BonShadowFocus.w * 0.82, _BonShadowFocus.w, distance(i.positionWS.xz, _BonShadowFocus.xz));
                        shadow = lerp(1.0, MainLightRealtimeShadow(TransformWorldToShadowCoord(i.positionWS)), inFocus);
                    }
                #endif
                float kickR = BonRamp(dot(n, _BonKickDir.xyz) * 0.5 + 0.5, _Bands, _Softness);
                float3 fill = lerp(_BonFillGround.rgb, _BonFillSky.rgb, n.y * 0.5 + 0.5);
                float3 light = (_BonKeyColor.rgb * keyR * shadow + _BonKickColor.rgb * kickR + fill) * invPi;
                // Inside the real shadow the fill and kick are dimmed too (a real cast shadow also blocks sky and bounce): a clear dark shape.
                float3 c = albedo * light * lerp(0.66, 1.0, shadow);

                float3 v = normalize(GetCameraPositionWS() - i.positionWS);
                float fres = 1.0 - saturate(dot(n, v));
                float facing = smoothstep(-0.30, 0.80, dot(n, _BonRimDir.xyz));
                float rim = pow(fres, _BonRimParams.y) * facing * _BonRimParams.x * _Rim;
                c += _BonRimColor.rgb * rim * (1.0 + pulse * 0.45);
                c *= 1.0 + pulse * _Pulse;
                c += albedo * _Lift * (0.55 + 0.45 * pulse);

                // Linear light out: fog here, the tone curve is the composite pass's job.
                c = lerp(c, _BonFogColor.rgb, BonFog(i.viewDepth));
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
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

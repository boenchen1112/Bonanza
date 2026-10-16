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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
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
                float kickR = BonRamp(dot(n, _BonKickDir.xyz) * 0.5 + 0.5, _Bands, _Softness);
                float3 fill = lerp(_BonFillGround.rgb, _BonFillSky.rgb, n.y * 0.5 + 0.5);
                float3 light = (_BonKeyColor.rgb * keyR + _BonKickColor.rgb * kickR + fill) * invPi;
                float3 c = albedo * light;

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

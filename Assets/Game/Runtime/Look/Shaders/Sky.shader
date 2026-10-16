// Gradient sky dome (web sky.js): zenith -> body -> hot horizon, a sun lobe,
// slow haze wedges. Camera-locked, drawn
// first with no depth, so it never meets the far plane.
Shader "Bonanza/Sky"
{
    SubShader
    {
        Tags { "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" "IgnoreProjector" = "True" }
        Cull Front
        ZWrite Off
        ZTest Always
        Pass
        {
            Name "Sky"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BonanzaLook.hlsl"

            float4 _BonSkyTop, _BonSkyMid, _BonSkyBot, _BonSun, _BonSunDir;
            float4 _BonSkyParams;   // x sun size, y sun strength, z time, w beat pulse

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.dir = normalize(i.positionOS.xyz);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.positionCS.z = o.positionCS.w * 0.999;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 col = lerp(_BonSkyMid.rgb, _BonSkyTop.rgb, smoothstep(0.02, 0.85, h));
                float3 low = lerp(_BonSkyBot.rgb * 0.28, _BonSkyBot.rgb, smoothstep(-0.55, -0.01, h));
                col = lerp(low, col, smoothstep(-0.02, 0.30, h));

                float s = max(dot(d, normalize(_BonSunDir.xyz)), 0.0);
                float core = pow(s, lerp(220.0, 40.0, _BonSkyParams.x));
                float halo = pow(s, lerp(14.0, 2.2, _BonSkyParams.x));
                col += _BonSun.rgb * (core * 1.6 + halo * 0.55) * _BonSkyParams.y * (1.0 + _BonSkyParams.w * 0.25);

                float a = atan2(d.x, -d.z);
                float t = _BonSkyParams.z;
                float rays = (0.5 + 0.5 * sin(a * 7.0 + t * 0.09)) * (0.5 + 0.5 * sin(a * 3.0 - t * 0.05));
                col += _BonSun.rgb * rays * 0.05 * smoothstep(0.55, -0.05, abs(h)) * _BonSkyParams.y;

                return half4(col, 1);   // linear; the composite pass tone-maps and dithers
            }
            ENDHLSL
        }
    }
}

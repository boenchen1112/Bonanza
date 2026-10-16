// The dark rounded plate behind a world callout (web swingKings/world.js makeWordTexture):
// over a multicoloured crowd a stroke alone left the word hard to read. Computed as an SDF
// so any aspect keeps true round corners; uv1 = (aspect, stroke rgb). Alpha blended,
// ignores depth (callouts are information: geometry must never eat them).
Shader "Bonanza/FxPlate"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+400" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            Name "Plate"
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

            float3 Srgb(float3 c)
            {
                return lerp(c / 12.92, pow((c + 0.055) / 1.055, 2.4), step(0.04045, c));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float aspect = i.prm.x;
                float2 p = (i.uv - 0.5) * float2(aspect, 1.0);
                float2 hs = float2(aspect * 0.5, 0.5);
                float r = 0.28;
                float2 q = abs(p) - (hs - r);
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;   // signed distance, height units
                float aa = 0.012;
                float cover = 1.0 - smoothstep(-aa, aa, d);
                float stroke = smoothstep(-0.045 - aa, -0.045 + aa, d);        // 1 in the outline band
                float3 rgb = lerp(Srgb(i.color.rgb), i.prm.yzw, stroke);   // fill arrives as sRGB bytes, stroke as linear floats
                float a = cover * i.color.a;
                if (a < 0.004) discard;
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
}

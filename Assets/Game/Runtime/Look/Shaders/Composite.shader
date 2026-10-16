// The web composite (render/look/post.js COMP_FRAG) as one full-screen pass:
// exposure, the soft shoulder c*(1+0.18c)/(1+c) (no ACES: it crushes chroma),
// saturation, vignette and an sRGB-space dither. Toon, Sky and Blob write
// linear light; this is the only place they are tone-mapped. Run by a Full
// Screen Pass renderer feature on the player's renderer, material referenced
// from the project so the build keeps it (no Shader.Find).
Shader "Bonanza/Composite"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _BonPostParams;   // x exposure, y saturation, z vignette, w time

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                float3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                c *= _BonPostParams.x;
                c = max(c, 0.0);
                c = c * (1.0 + 0.18 * c) / (1.0 + c);
                float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
                c = lerp(lum.xxx, c, _BonPostParams.y);
                float2 d = uv - 0.5;
                c *= 1.0 - _BonPostParams.z * smoothstep(0.15, 0.95, dot(d, d) * 2.1);
                c = max(c, 0.0);
                // Dither in sRGB space (the web writes sRGB straight to the framebuffer).
                float3 g = pow(c, 1.0 / 2.2);
                g += (hash21(i.positionCS.xy + _BonPostParams.w) - 0.5) * 0.0025;
                return half4(pow(max(g, 0.0), 2.2), 1);
            }
            ENDHLSL
        }
    }
}

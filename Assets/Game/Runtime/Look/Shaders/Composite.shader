// The web composite (render/look/post.js) as full-screen passes, run by
// PostPass.cs after the scene has rendered into an HDR colour buffer:
//
//   0 Bright   quarter-res bright cut with a soft knee (the bloom source)
//   1 BlurH    5-tap separable gaussian (9 taps' worth through linear sampling)
//   2 BlurV
//   3 Composite chromatic offset, + bloom in LINEAR light, exposure, the soft
//               shoulder c*(1+0.18c)/(1+c) (no ACES: it crushes chroma),
//               saturation, + flash, vignette, an sRGB-space dither
//
// Toon, Sky, Blob and the FX shaders write linear light; this is the only
// place they are tone-mapped. The bloom textures are bound per pass by
// PostPass (never Shader.Find, so the material is referenced from Resources).
Shader "Bonanza/Composite"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float4 _BonPostParams;   // x exposure, y saturation, z vignette, w time
        float4 _BonFxParams;     // x bloom strength, y wide bloom weight, z chroma, w flash alpha
        float4 _BonFlashColor;   // rgb flash colour (linear)
        float4 _BonBloomParams;  // x threshold, y knee
        float4 _BonTexel;        // 1/width, 1/height of the pass's source texture
        TEXTURE2D_X(_BonBloomA);
        TEXTURE2D_X(_BonBloomB);

        float3 SampleSrc(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
        }
        ENDHLSL

        // 0: bright cut. Four taps around the quarter-res texel (a cheap box downsample),
        // then max-channel soft knee so a surface drifting past the threshold ramps in.
        Pass
        {
            Name "Bright"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target
            {
                float2 t = _BonTexel.xy;
                float3 c = SampleSrc(i.texcoord + t * float2(-1, -1)) + SampleSrc(i.texcoord + t * float2(1, -1))
                         + SampleSrc(i.texcoord + t * float2(-1, 1)) + SampleSrc(i.texcoord + t * float2(1, 1));
                c *= 0.25;
                c = min(c, 24.0);   // a hot FX core must not smear the whole frame
                float l = max(c.r, max(c.g, c.b));
                float s = saturate((l - _BonBloomParams.x) / max(_BonBloomParams.y, 0.0001));
                s *= s;
                return half4(c * s, 1);
            }
            ENDHLSL
        }

        // 1, 2: separable blur. The web kernel: weights/offsets of a 9-tap gaussian in 5 taps.
        Pass
        {
            Name "BlurH"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target
            {
                float2 d = float2(_BonTexel.x, 0);
                float3 s = SampleSrc(i.texcoord) * 0.2270270270;
                s += SampleSrc(i.texcoord + d * 1.3846153846) * 0.3162162162;
                s += SampleSrc(i.texcoord - d * 1.3846153846) * 0.3162162162;
                s += SampleSrc(i.texcoord + d * 3.2307692308) * 0.0702702703;
                s += SampleSrc(i.texcoord - d * 3.2307692308) * 0.0702702703;
                return half4(s, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BlurV"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target
            {
                float2 d = float2(0, _BonTexel.y);
                float3 s = SampleSrc(i.texcoord) * 0.2270270270;
                s += SampleSrc(i.texcoord + d * 1.3846153846) * 0.3162162162;
                s += SampleSrc(i.texcoord - d * 1.3846153846) * 0.3162162162;
                s += SampleSrc(i.texcoord + d * 3.2307692308) * 0.0702702703;
                s += SampleSrc(i.texcoord - d * 3.2307692308) * 0.0702702703;
                return half4(s, 1);
            }
            ENDHLSL
        }

        // 3: the composite.
        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                float2 d = uv - 0.5;
                float r2 = dot(d, d);

                // Radial chromatic offset, strongest at the edges, zero dead centre.
                float ca = _BonFxParams.z * (0.35 + r2 * 3.0);
                float3 c;
                if (ca > 0.0001)
                {
                    c.r = SampleSrc(uv + d * ca).r;
                    c.g = SampleSrc(uv).g;
                    c.b = SampleSrc(uv - d * ca).b;
                }
                else c = SampleSrc(uv);

                // Opt-in edge smoothing (_BonTexel.z, PostState.EdgeAA): where the luma steps hard against its four
                // neighbours, blend toward their mean, so a stair-stepped silhouette at a reduced render scale softens.
                if (_BonTexel.z > 0.0)
                {
                    float2 t = _BonTexel.xy;
                    float3 n0 = SampleSrc(uv + float2(t.x, 0)), n1 = SampleSrc(uv - float2(t.x, 0));
                    float3 n2 = SampleSrc(uv + float2(0, t.y)), n3 = SampleSrc(uv - float2(0, t.y));
                    float3 w = float3(0.2126, 0.7152, 0.0722);
                    float l0 = dot(c, w), la = dot(n0, w), lb = dot(n1, w), lc = dot(n2, w), ld = dot(n3, w);
                    l0 /= 1.0 + l0; la /= 1.0 + la; lb /= 1.0 + lb; lc /= 1.0 + lc; ld /= 1.0 + ld;
                    float lmax = max(l0, max(max(la, lb), max(lc, ld))), lmin = min(l0, min(min(la, lb), min(lc, ld)));
                    float k = saturate((lmax - lmin - 0.04) * 5.0) * 0.55 * _BonTexel.z;
                    c = lerp(c, (n0 + n1 + n2 + n3 + c) * 0.2, k);
                }

                // Bloom joins the scene in linear light, before exposure and the curve.
                float3 bloom = SAMPLE_TEXTURE2D_X_LOD(_BonBloomA, sampler_LinearClamp, uv, 0).rgb
                             + SAMPLE_TEXTURE2D_X_LOD(_BonBloomB, sampler_LinearClamp, uv, 0).rgb * _BonFxParams.y;
                c += bloom * _BonFxParams.x;

                c *= _BonPostParams.x;
                c = max(c, 0.0);
                c = c * (1.0 + 0.18 * c) / (1.0 + c);
                float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
                c = lerp(lum.xxx, c, _BonPostParams.y);

                c += _BonFlashColor.rgb * _BonFxParams.w;

                // Vignette: multiplicative, gentle, never a black ring.
                c *= 1.0 - _BonPostParams.z * smoothstep(0.15, 0.95, r2 * 2.1);
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

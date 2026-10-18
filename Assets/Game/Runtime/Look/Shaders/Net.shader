// A chain-link backstop net (web swingKings/world.js makeNetTexture: a diamond lattice on transparent, tiled
// around the arc), drawn procedurally so it stays crisp at any distance and costs no texture: anti-aliased
// diagonal lines from the uv lattice (fwidth), alpha blended, two-sided, no depth write. The vertex stage sways
// the free lower edge a little (a net catching the breeze) and bulges it where a ball hit it (_Hit*).
// uv: x around the arc (0..1), y up (0 bottom, 1 rail).
Shader "Bonanza/Net"
{
    Properties
    {
        _Color ("Color", Color) = (0.81, 0.92, 1, 1)
        _Opacity ("Opacity", Float) = 0.55
        _Tiles ("Lattice tiles (x, y)", Vector) = (18, 5, 0, 0)
        _Width ("Line width (cells)", Float) = 0.06
        _Sway ("Sway amount", Float) = 0.05
        _SwayTime ("Sway clock", Float) = 0
        _HitU ("Hit position (u, v)", Vector) = (0.5, 0.5, 0, 0)
        _Hit ("Hit bulge", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "Net"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BonanzaLook.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Opacity, _Width, _Sway, _SwayTime, _Hit;
                float4 _Tiles, _HitU;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float viewDepth : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                float free = 1.0 - i.uv.y;                                  // the rail holds the top still
                float wave = sin(_SwayTime * 1.3 + i.uv.x * 7.0) * 0.6 + sin(_SwayTime * 2.1 + i.uv.x * 13.0 + 1.7) * 0.4;
                float2 dh = (i.uv - _HitU.xy) * float2(3.0, 1.6);
                float bulge = exp(-dot(dh, dh) * 6.0) * _Hit;
                float3 p = i.positionOS.xyz + i.normalOS * (wave * _Sway * free + bulge * 0.35);
                Varyings o;
                float3 ws = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = i.uv;
                o.viewDepth = -TransformWorldToView(ws).z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 g = i.uv * _Tiles.xy;
                float a1 = frac(g.x + g.y), a2 = frac(g.x - g.y);
                float d1 = min(a1, 1.0 - a1), d2 = min(a2, 1.0 - a2);
                float w1 = max(fwidth(g.x + g.y), 1e-4), w2 = max(fwidth(g.x - g.y), 1e-4);
                float l1 = 1.0 - smoothstep(_Width - w1, _Width + w1, d1);
                float l2 = 1.0 - smoothstep(_Width - w2, _Width + w2, d2);
                float lat = max(l1, l2);
                // A dense far net turns to a soft veil instead of moire: fade the lattice toward its mean coverage.
                float far = saturate((max(w1, w2) - 0.25) * 2.0);
                lat = lerp(lat, _Width * 3.0, far);
                float edge = smoothstep(0.0, 0.04, i.uv.y);                   // the bottom hem fades into the grass
                float a = lat * _Opacity * edge;
                float3 c = lerp(_Color.rgb, _BonFogColor.rgb, BonFog(i.viewDepth));
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}

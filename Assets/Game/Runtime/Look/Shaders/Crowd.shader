// The crowd (web chars/crowd.js) as ONE static mesh animated in the vertex shader: every figure's vertices
// carry its root, facing and per-figure numbers, the material carries the beat and the crowd's mood, and
// the vertex stage rebuilds the web's per-instance matrix  T * Ry(facing) * Rz(lean) * S  per vertex.
// One draw, no per-frame CPU work, no instancing variants to strip. Toon-lit like the rest of the set.
//
//   POSITION   figure-local vertex (feet at the origin, facing local +Z)
//   NORMAL     figure-local normal
//   COLOR      rgb = this figure's tint x the baked eye darkness, a = row dim
//   TEXCOORD0  xyz = root (object space), w = facing yaw (radians, Unity's Euler(0, yaw, 0))
//   TEXCOORD1  x = beat phase offset, y = 0..1 along the arc (the wave), z = size, w = bounce amount
//   TEXCOORD2  x = palette crowd slot (0..4), y = brightness, z = arm pose (0 low V, 1 arms up, 2 clap, 3 one fist pumping),
//              w = which arm this vertex belongs to (-1 left, +1 right, 0 body: never posed)
// Colour = lerp(pastel, palette crowd slot, _PalMix) x brightness x shade (the eyes stay dark on every body).
Shader "Bonanza/Crowd"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Bands ("Light bands", Float) = 2
        _Softness ("Band softness", Float) = 0.22
        _Rim ("Rim multiplier", Float) = 1.4
        _Pulse ("Beat pulse", Float) = 0.2
        _Lift ("Emissive lift", Float) = 0.04
        _Beat ("Beat", Float) = 0
        _Eighths ("Eighths blend", Float) = 0
        _JumpH ("Jump height", Float) = 0.25
        _LeanA ("Lean", Float) = 0.15
        _Sag ("Sag", Float) = 0
        _WaveT ("Wave position (-1 = none)", Float) = -1
        _WaveAmt ("Wave amount", Float) = 0
        _Glow ("Cheer glow", Float) = 0
        _PalMix ("Palette mix", Range(0, 1)) = 1
        _Arms ("Arm energy", Float) = 0
        _FogMul ("Fog multiplier", Float) = 1
        _C0 ("Crowd 0", Color) = (1, 1, 1, 1)
        _C1 ("Crowd 1", Color) = (1, 1, 1, 1)
        _C2 ("Crowd 2", Color) = (1, 1, 1, 1)
        _C3 ("Crowd 3", Color) = (1, 1, 1, 1)
        _C4 ("Crowd 4", Color) = (1, 1, 1, 1)
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "BonanzaToon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Bands, _Softness, _Rim, _Pulse, _Lift;
                float _Beat, _Eighths, _JumpH, _LeanA, _Sag, _WaveT, _WaveAmt, _Glow, _PalMix, _Arms, _FogMul;
                half4 _C0, _C1, _C2, _C3, _C4;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR;
                float4 root : TEXCOORD0; float4 prm : TEXCOORD1; float4 slot : TEXCOORD2;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1;
                float viewDepth : TEXCOORD2; half4 color : COLOR; float hop : TEXCOORD3; float ao : TEXCOORD4;
            };

            float Hop(float p) { return 4.0 * p * (1.0 - p); }

            Varyings vert(Attributes i)
            {
                float b = _Beat + i.prm.x;
                float h1 = Hop(frac(b));
                float j = lerp(h1, Hop(frac(b * 2.0)), _Eighths) * i.prm.w;
                float crest = 0.0;
                if (_WaveT >= 0.0)
                {
                    float d = i.prm.y - (_WaveT * 0.85 - 0.15);
                    crest = exp(-(d * d) / 0.010) * _WaveAmt;
                    j += crest * 1.6;
                }
                float3 pos0 = i.positionOS.xyz;
                float3 nrm0 = i.normalOS;
                if (abs(i.slot.w) > 0.5)
                {
                    // Arm poses: rotate the arm about its shoulder in the figure's own plane. The authored arm hangs
                    // in a low V; +1.7 rad (mirrored per side) turns it up and out.
                    float sx = i.slot.w, pose = i.slot.z;
                    float e = _Arms;
                    float ang;
                    if (pose < 0.5) ang = sin(b * PI * 2.0) * 0.14 * e;
                    else if (pose < 1.5) ang = 1.7 * lerp(0.45, 1.0, e) + h1 * 0.28 * e;
                    else if (pose < 2.5) ang = lerp(1.2, 2.25 - 0.4 * h1, e);
                    // 3: one fist pumping high on the beat, the other arm swinging low (a figure that is not mirrored)
                    else ang = sx > 0.0 ? lerp(1.5, 2.55 - 0.55 * h1, e) : sin(b * PI * 2.0 + 1.0) * 0.3 * e;
                    ang = lerp(ang, 1.75, saturate(crest * 1.4));   // the wave throws every arm up as it passes
                    ang *= sx;
                    float2 piv = float2(sx * 0.078, 0.559);
                    float ca = cos(ang), sa = sin(ang);
                    float2 q = pos0.xy - piv;
                    pos0.xy = piv + float2(q.x * ca - q.y * sa, q.x * sa + q.y * ca);
                    nrm0.xy = float2(nrm0.x * ca - nrm0.y * sa, nrm0.x * sa + nrm0.y * ca);
                }
                float lift = j * _JumpH;
                // Squash and stretch: flatter at the bottom, taller at the top.
                float sq = 1.0 + (j - 0.42) * 0.30 - _Sag * 0.35;
                float sxz = rsqrt(max(0.3, sq));
                float s = i.prm.z;
                float3 p = pos0 * float3(sxz * s, sq * s, sxz * s);
                float3 nn = nrm0 * float3(1.0 / sxz, 1.0 / sq, 1.0 / sxz);
                // Sway: a small lean on a two-beat cycle (eighths when hyped).
                float lean = lerp(sin(b * PI), sin(b * PI * 2.0), _Eighths) * _LeanA;
                float cz = cos(lean), sz = sin(lean);
                p = float3(p.x * cz - p.y * sz, p.x * sz + p.y * cz, p.z);
                nn = float3(nn.x * cz - nn.y * sz, nn.x * sz + nn.y * cz, nn.z);
                float cy = cos(i.root.w), sy = sin(i.root.w);
                p = float3(p.x * cy + p.z * sy, p.y, -p.x * sy + p.z * cy);
                nn = float3(nn.x * cy + nn.z * sy, nn.y, -nn.x * sy + nn.z * cy);
                p += i.root.xyz + float3(0.0, lift - _Sag * 0.22, 0.0);

                Varyings o;
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(nn);
                o.viewDepth = -TransformWorldToView(o.positionWS).z;
                int k = (int)(i.slot.x + 0.5);
                half3 pal = k == 0 ? _C0.rgb : k == 1 ? _C1.rgb : k == 2 ? _C2.rgb : k == 3 ? _C3.rgb : _C4.rgb;
                o.color = half4(lerp(i.color.rgb, pal, _PalMix) * i.slot.y * i.color.a, 1);
                o.hop = saturate(j);
                // Cheap occlusion: feet and lower body sit in the row's shadow, heads catch the light (the web's
                // Lambert crowd gets this from its normals; the toon ramp's floor would flatten it).
                o.ao = lerp(0.55, 1.0, saturate(pos0.y / 0.72));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 albedo = _BaseColor.rgb * i.color.rgb * i.ao;
                // A figure at the top of its hop catches a little more light (web: hop lifts the instance colour).
                float lift = _Lift + i.hop * _Glow;
                float3 c = BonToon(albedo, normalize(i.normalWS), i.positionWS, _Bands, _Softness, _Rim, _Pulse, lift);
                c = lerp(c, _BonFogColor.rgb, BonFog(i.viewDepth) * _FogMul);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}

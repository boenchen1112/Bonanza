// Additive, unlit glow for CPU-batched set pieces (MeshBatch: per-instance tint in the vertex colour, vertices
// baked in the renderer's local space): Chomp Chorus' breath rings, air-pipe fills, voice beams, chord bridges and
// key glyphs. Black is free transparency, so a family fades by tinting toward black and never changes blend state.
//   _Facing  0 = flat; > 0 = brightness follows |dot(normal, view)| ^ _Facing, so a tube glows in its middle and
//            its silhouette melts away (a neon tube, a light shaft) instead of reading as a painted solid;
//            < 0 = the inverse, (1 - |dot|) ^ -_Facing: the edges glow and the middle clears (a glass tube).
//   _Ramp    0 = none; 1 = the vertical ramp in uv.y (1 at the source, 0 at the far end), like Bonanza/Beam.
// Linear HDR out (crosses the bloom threshold like the web's toneMapped:false MeshBasic). Draws at intensity 0
// from Load so its program links before the round.
Shader "Bonanza/GlowAdd"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Facing ("Facing falloff", Float) = 0
        _Ramp ("Vertical ramp", Float) = 0
        _FogMul ("Fog", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+12" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "GlowAdd"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BonanzaLook.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity, _Facing, _Ramp, _FogMul;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; float2 uv : TEXCOORD2; float depth : TEXCOORD3; half4 color : COLOR; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.n = TransformObjectToWorldNormal(i.normalOS);
                o.v = GetCameraPositionWS() - ws;
                o.uv = i.uv;
                o.depth = -TransformWorldToView(ws).z;
                o.color = i.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float k = 1.0;
                float facing = saturate(abs(dot(normalize(i.n), normalize(i.v))));
                if (_Facing > 0.0) k = pow(max(facing, 1e-4), _Facing);
                else if (_Facing < 0.0) k = pow(max(1.0 - facing, 1e-4), -_Facing);   // glass: the silhouette glows, the middle clears
                if (_Ramp > 0.0) k *= lerp(1.0, smoothstep(0.0, 0.3, i.uv.y) * (0.2 + 0.8 * i.uv.y), _Ramp);
                k *= 1.0 - BonFog(i.depth) * _FogMul;
                float3 c = _Color.rgb * i.color.rgb * _Intensity * k;
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}

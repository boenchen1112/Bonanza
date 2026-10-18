// The house toon surface as one function, shared by Toon, ToonEnv and Crowd: banded key + kick + hemisphere
// fill from the _Bon* globals, the key's real shadow inside the scene's focus circle, the directional rim,
// the beat lift and the emissive lift. Returns LINEAR light before fog (BonFog is the caller's call).
// Include after Core.hlsl and Shadows.hlsl.
#ifndef BONANZA_TOON_INCLUDED
#define BONANZA_TOON_INCLUDED

#include "BonanzaLook.hlsl"

float3 BonToonS(float3 albedo, float3 n, float3 positionWS, float bands, float softness, float rimMul, float pulseMul, float lift, float shadowMul)
{
    float pulse = _BonRimParams.z;
    const float invPi = 0.31830989;

    float keyR = BonRamp(dot(n, _BonKeyDir.xyz) * 0.5 + 0.5, bands, softness);
    // Real shadow on the key only (fill and kick stay), and only inside the scene's focus circle.
    float shadow = 1.0;
    #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
        if (_BonShadowFocus.w > 0.0)
        {
            float inFocus = 1.0 - smoothstep(_BonShadowFocus.w * 0.82, _BonShadowFocus.w, distance(positionWS.xz, _BonShadowFocus.xz));
            shadow = lerp(1.0, MainLightRealtimeShadow(TransformWorldToShadowCoord(positionWS)), inFocus * shadowMul);
        }
    #endif
    float kickR = BonRamp(dot(n, _BonKickDir.xyz) * 0.5 + 0.5, bands, softness);
    float3 fill = lerp(_BonFillGround.rgb, _BonFillSky.rgb, n.y * 0.5 + 0.5);
    float3 light = (_BonKeyColor.rgb * keyR * shadow + _BonKickColor.rgb * kickR + fill) * invPi;
    // Inside the real shadow the fill and kick are dimmed too (a real cast shadow also blocks sky and bounce): a clear dark shape.
    float3 c = albedo * light * lerp(0.66, 1.0, shadow);

    float3 v = normalize(GetCameraPositionWS() - positionWS);
    float fres = 1.0 - saturate(dot(n, v));
    float facing = smoothstep(-0.30, 0.80, dot(n, _BonRimDir.xyz));
    float rim = pow(fres, _BonRimParams.y) * facing * _BonRimParams.x * rimMul;
    c += _BonRimColor.rgb * rim * (1.0 + pulse * 0.45);
    c *= 1.0 + pulse * pulseMul;
    c += albedo * lift * (0.55 + 0.45 * pulse);
    return c;
}

// The house surface; a surface that casts but must not receive (a curved character body) calls BonToonS with 0.
float3 BonToon(float3 albedo, float3 n, float3 positionWS, float bands, float softness, float rimMul, float pulseMul, float lift)
{
    return BonToonS(albedo, n, positionWS, bands, softness, rimMul, pulseMul, lift, 1.0);
}

#endif

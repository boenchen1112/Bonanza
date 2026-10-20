// Shared by the Bonanza look shaders. The rig (LookRig.cs) writes every
// _Bon* global once a frame, so the whole show re-lights in one write.
#ifndef BONANZA_LOOK_INCLUDED
#define BONANZA_LOOK_INCLUDED

// rgb = linear colour x intensity; dirs point TOWARD the light, world space.
float4 _BonKeyDir, _BonKeyColor;
float4 _BonKickDir, _BonKickColor;
float4 _BonFillSky, _BonFillGround;
float4 _BonRimColor, _BonRimDir;
float4 _BonRimParams;   // x strength, y power, z beat pulse (0..1)
float4 _BonFogColor;
float4 _BonFogParams;   // x near, y far, z height-fog strength (0 = off), w height it starts at
float4 _BonShadowColor;
float4 _BonShadowFocus;   // xyz centre, w radius of the circle the key's real shadow covers (0 = none)

// Banded lambert ramp (web materials.js makeRamp): 'steps' bands, a little
// softness across each terminator, darkest band lifted to 0.34.
float BonRamp(float x, float steps, float softness)
{
    float i = min(floor(x * steps), steps - 1.0);
    float denom = max(steps - 1.0, 1.0);
    float b = i / denom;
    float bNext = min(1.0, (i + 1.0) / denom);
    float d = ((i + 1.0) / steps - x) * steps;
    float s = softness > 0.0 ? smoothstep(0.5 - softness, 0.5 + softness, 1.0 - d) : 0.0;
    return 0.34 + 0.66 * (b + (bNext - b) * s);
}

// Linear fog on view-space depth (the web's three.js Fog uses depth, not radial distance).
// Applied to LINEAR light, before the composite pass's tone curve.
float BonFog(float viewDepth)
{
    return saturate((viewDepth - _BonFogParams.x) / max(_BonFogParams.y - _BonFogParams.x, 0.001));
}

// Depth fog plus an opt-in height term (palette FogHeight): high and far surfaces (upper stands, a skyline) sink
// into the haze, the near field keeps its contrast. With z = 0 this is exactly BonFog.
float BonFogH(float viewDepth, float worldY)
{
    float h = _BonFogParams.z * saturate((worldY - _BonFogParams.w) / 6.0) * saturate((viewDepth - _BonFogParams.x * 0.5) / 20.0);
    return saturate(BonFog(viewDepth) + h);
}

#endif

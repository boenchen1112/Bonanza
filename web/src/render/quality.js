/**
 * Render-quality policy: what a Graphics setting (Auto / High / Medium / Low)
 * means on a given device, as a (render scale, tier) level.
 *
 * Pure: the WebGL renderer name and the device pixel ratio are passed in, so
 * the policy is testable without a browser. The Stage applies a level; the
 * render governor walks `ladderFor()` when the setting is Auto.
 */

/**
 * Best to cheapest. Render scale drops before the tier does: on a high-DPI
 * screen a softer image costs far less of the look than losing bloom, MSAA
 * and the shadow map. Measured on an Iris Xe at 2560x1440: (2, high) 42fps,
 * (1.5, medium) 101-117fps.
 */
export const LADDER = Object.freeze([
  { scale: 2, tier: 'high' },
  { scale: 1.75, tier: 'high' },
  { scale: 1.5, tier: 'high' },
  { scale: 1.5, tier: 'medium' },
  { scale: 1.25, tier: 'medium' },
  { scale: 1, tier: 'medium' },
  { scale: 1, tier: 'low' },
]);

/** The ladder as this device can use it: no step above its ratio, no repeats. */
export function ladderFor(deviceRatio) {
  const seen = new Set();
  const out = [];
  for (const l of LADDER) {
    const scale = Math.min(l.scale, deviceRatio);
    const key = `${scale}|${l.tier}`;
    if (seen.has(key)) continue;
    seen.add(key);
    out.push({ scale, tier: l.tier });
  }
  return out;
}

/** Integrated GPUs: Intel HD/UHD/Iris and AMD APU graphics. */
export function isIntegratedGpu(renderer) {
  return /\b(Intel|Iris|UHD Graphics|HD Graphics)\b|Radeon\(TM\) Graphics|Radeon Graphics/i.test(renderer || '');
}

const INTEGRATED_HIDPI_START = { scale: 1.5, tier: 'medium' };

/** The closest usable ladder step to `level` on this device. */
function nearest(ladder, level) {
  const scale = Math.min(level.scale, ladder[0].scale);
  return ladder.find((l) => l.tier === level.tier && l.scale <= scale + 1e-9)
    || ladder.find((l) => l.tier === level.tier)
    || ladder[0];
}

/**
 * @param {'auto'|'high'|'medium'|'low'|string} setting
 * @param {{renderer?:string, deviceRatio:number, remembered?:{scale:number,tier:string}|null}} env
 * @returns {{scale:number, tier:string, auto:boolean}}
 */
export function levelForSetting(setting, { renderer = '', deviceRatio, remembered = null }) {
  const dr = Math.max(1, deviceRatio || 1);
  const ladder = ladderFor(dr);
  switch (setting) {
    case 'high': return { scale: dr, tier: 'high', auto: false };
    case 'medium': return { scale: Math.min(1.5, dr), tier: 'medium', auto: false };
    case 'low': return { scale: 1, tier: 'low', auto: false };
    default: {
      let level;
      if (remembered && remembered.tier) level = nearest(ladder, remembered);
      else if (isIntegratedGpu(renderer) && dr > 1.5) level = nearest(ladder, INTEGRATED_HIDPI_START);
      else level = ladder[0];
      return { scale: level.scale, tier: level.tier, auto: true };
    }
  }
}

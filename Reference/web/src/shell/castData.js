/**
 * The eight cast members as plain data, plus the colour-role rule.
 *
 * Dependency-free on purpose: the shell (portraits, 3D figures) and the
 * Blender cast build (tools/assets/blender, run from Node) read the same
 * definitions, so a character's colours cannot drift between menu and model.
 * Approved look: docs/design/cast-sheet.html.
 */

/** @typedef {{id:string,name:string,color:number,accent:number,trait:string,shape:string,crest:string}} CastDef */

/** @type {CastDef[]} */
export const CAST = [
  { id: 'bopp', name: 'BOPP', color: 0xffd93d, accent: 0xff9f45, trait: 'All rhythm, no brakes.', shape: 'round', crest: 'antenna' },
  { id: 'zizz', name: 'ZIZZ', color: 0x4dd6ff, accent: 0x7aa6ff, trait: 'Runs on static and spite.', shape: 'spike', crest: 'bolt' },
  { id: 'kwark', name: 'KWARK', color: 0xff5d73, accent: 0xff9f45, trait: 'Beak first, ask later.', shape: 'beak', crest: 'plume' },
  { id: 'tuff', name: 'TUFF', color: 0x9ee87a, accent: 0x39d4b4, trait: 'Built like a downbeat.', shape: 'block', crest: 'horns' },
  { id: 'mimo', name: 'MIMO', color: 0xc08cff, accent: 0xff7ad9, trait: 'Two beats ahead, always.', shape: 'tall', crest: 'cap' },
  { id: 'nibb', name: 'NIBB', color: 0xff9f45, accent: 0xffd93d, trait: 'Small. Loud. Everywhere.', shape: 'tiny', crest: 'antenna' },
  { id: 'glub', name: 'GLUB', color: 0x39d4b4, accent: 0x4dd6ff, trait: 'Wobbles exactly on time.', shape: 'blob', crest: 'fin' },
  { id: 'fizz', name: 'FIZZ', color: 0xff7ad9, accent: 0xc08cff, trait: 'Sparkles on the offbeat.', shape: 'star', crest: 'plume' },
];

/** Which rig build (silhouette) each portrait shape reads closest to. */
export const BUILD_BY_SHAPE = {
  round: 'round', beak: 'round', blob: 'round',
  tall: 'tall',
  tiny: 'small', star: 'small',
  spike: 'wide', block: 'wide',
};

const toLinear = (c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4);
const toSrgb = (c) => (c <= 0.0031308 ? c * 12.92 : 1.055 * c ** (1 / 2.4) - 0.055);

/** Mix two sRGB hex colours in linear light (what THREE.Color.lerp does). */
export function mixHex(a, b, t) {
  let out = 0;
  for (const shift of [16, 8, 0]) {
    const ca = toLinear(((a >> shift) & 255) / 255);
    const cb = toLinear(((b >> shift) & 255) / 255);
    const v = Math.round(Math.min(1, Math.max(0, toSrgb(ca + (cb - ca) * t))) * 255);
    out |= v << shift;
  }
  return out >>> 0;
}

/**
 * Colour roles for a character: body is its colour, trim is darker for value
 * contrast, skin a pale tint (eye whites, hands, teeth), eye near-black.
 * @returns {{body:number, trim:number, skin:number, accent:number, eye:number, limb:number, rim:number}}
 */
export function colourRoles(def) {
  return {
    body: def.color,
    trim: mixHex(def.color, 0x100818, 0.62),
    skin: mixHex(def.color, 0xffffff, 0.8),
    accent: def.accent,
    eye: mixHex(def.color, 0x06030c, 0.88),
    limb: mixHex(def.color, 0xffffff, 0.18),
    rim: mixHex(def.color, 0xffffff, 0.35),
  };
}

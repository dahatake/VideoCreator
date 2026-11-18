// Animated layout storyboards (API /projects/{id}/layouts → Planning.LayoutPreview): which video is where on the 9:16 canvas, and when it switches.
// Pure functions returning markup; every text goes through esc(). The loop timing follows each beat's seconds (hard cuts, like the edit).
import { trUi } from './i18n.js';

const esc = (v) => String(v ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const PALETTE = ['#0f6cbd', '#c4314b', '#107c10', '#8764b8', '#ca5010', '#038387'];
const SLOT_COLOR = { main: 0, presenter: 0, cam_a: 0, screen: 1, cam_b: 1, wide: 2, audio_master: 3 };
const CAMERA_SLOTS = new Set(['main', 'presenter', 'cam_a', 'cam_b', 'wide', 'audio_master']);
let seq = 0;

export const assetLetter = (i) => String.fromCharCode(65 + (i % 26));
export const assetColor = (i) => PALETTE[i % PALETTE.length];

/** The A/B chip shown on asset cards and in the diagram (same colour, same letter). */
export const assetChip = (i) => `<span class="lp-chip" style="background:${assetColor(i)}" aria-hidden="true">${assetLetter(i)}</span>`;

function who(box, assets) {
  const i = box.asset_id ? assets.findIndex(a => a.id === box.asset_id) : -1;
  return i >= 0 ? { i, color: assetColor(i), letter: assetLetter(i) } : { i: -1, color: PALETTE[SLOT_COLOR[box.slot] ?? 3], letter: '' };
}

function glyph(box, scale) {
  const s = Math.min(box.w, box.h) * scale, cx = box.x + box.w / 2, cy = box.y + box.h / 2;
  if (s < 40) return '';
  if (CAMERA_SLOTS.has(box.slot)) {
    // head and shoulders
    const r = s * 0.2;
    return `<g class="lp-glyph"><circle cx="${cx}" cy="${cy - s * 0.18}" r="${r}"/><path d="M${cx - s * 0.36} ${cy + s * 0.42} q0 ${-s * 0.4} ${s * 0.36} ${-s * 0.4} q${s * 0.36} 0 ${s * 0.36} ${s * 0.4} z"/></g>`;
  }
  // a slide: title bar and bullet lines
  const w = s * 1.2, h = s * 0.7, x = cx - w / 2, y = cy - h / 2;
  return `<g class="lp-glyph"><rect x="${x}" y="${y}" width="${w}" height="${h * 0.18}" rx="${h * 0.05}"/>` +
    [0.38, 0.6, 0.82].map((f, k) => `<rect x="${x + w * 0.08}" y="${y + h * f}" width="${w * (k === 2 ? 0.5 : 0.8)}" height="${h * 0.09}" rx="${h * 0.04}"/>`).join('') + '</g>';
}

function boxShape(box, assets, large, inset) {
  const { color, letter } = who(box, assets);
  if (box.backdrop) return `<rect x="${box.x}" y="${box.y}" width="${box.w}" height="${box.h}" fill="${color}" opacity="0.32"/>`;
  const stroke = inset ? ` stroke="#fff" stroke-width="${large ? 10 : 16}"` : '';
  const shape = box.shape === 'circle'
    ? `<circle cx="${box.x + box.w / 2}" cy="${box.y + box.h / 2}" r="${box.w / 2}" fill="${color}"${stroke}/>`
    : `<rect x="${box.x}" y="${box.y}" width="${box.w}" height="${box.h}" fill="${color}"${stroke}/>`;
  let badge = '';
  if (large && letter) {
    const r = 70, bx = box.shape === 'circle' ? box.x + box.w / 2 : box.x + 30 + r, by = box.shape === 'circle' ? box.y - r * 0.6 : box.y + 30 + r;
    badge = `<g class="lp-badge"><circle cx="${bx}" cy="${Math.max(r, by)}" r="${r}" fill="#fff" stroke="${color}" stroke-width="10"/><text x="${bx}" y="${Math.max(r, by) + 34}" text-anchor="middle" fill="${color}">${letter}</text></g>`;
  }
  return shape + glyph(box, box.shape === 'circle' ? 0.55 : 0.4) + badge;
}

function keyBeat(lp) {
  const pref = ['pip', 'screen'];
  const i = lp.beats.findIndex(b => pref.includes(b.kind));
  if (i >= 0) return i;
  return lp.beats.length > 1 ? 1 : 0;
}

/** Seconds of the whole loop: the beats' own lengths, kept between 4 and 12 s so a cycle is watchable. */
const loopSec = (lp) => Math.min(12, Math.max(4, lp.beats.reduce((s, b) => s + b.sec, 0)));
const pct = (x) => `${(x * 100).toFixed(3)}%`;

function frames(lp, uid) {
  if (lp.beats.length < 2) return '';
  const total = lp.beats.reduce((s, b) => s + b.sec, 0);
  let t = 0;
  return lp.beats.map((b, i) => {
    const s = t / total, e = (t + b.sec) / total; t += b.sec;
    const k = [];
    if (s > 0) k.push(`0%,${pct(s - 0.0001)}{opacity:0}`);
    k.push(`${pct(s)},${pct(Math.max(s, e - 0.0001))}{opacity:1}`);
    if (e < 1) k.push(`${pct(e)},100%{opacity:0}`);
    return `@keyframes ${uid}-${i}{${k.join('')}}`;
  }).join('');
}

const anim = (lp, uid, i) => lp.beats.length < 2 ? '' : ` style="animation:${uid}-${i} ${loopSec(lp)}s step-end infinite"`;

function beatSvg(lp, beat, assets, large) {
  const caps = lp.captions ? `<g class="lp-captions"><rect x="${lp.captions.x}" y="${lp.captions.y}" width="${lp.captions.w}" height="${lp.captions.h}" fill="#000" opacity="0.35"/>` +
    `<rect x="${lp.width * 0.2}" y="${lp.captions.y + lp.captions.h * 0.3}" width="${lp.width * 0.6}" height="${lp.captions.h * 0.4}" rx="${lp.captions.h * 0.2}" fill="#fff" opacity="0.9"/></g>` : '';
  // a picture drawn over another one in the same beat is an inset (PiP / wipe): outlined like the white ring in the render
  const front = beat.boxes.filter(b => !b.backdrop);
  return beat.boxes.map(b => boxShape(b, assets, large, front.indexOf(b) > 0)).join('') + caps;
}

/**
 * The animated canvas. size 'icon' = decorative (aria-hidden, the card text describes the pattern); 'large' = role=img with a text alternative.
 * Without motion (prefers-reduced-motion) the most telling beat is shown still.
 */
export function layoutSvg(lp, assets, { size = 'icon', label = '' } = {}) {
  const uid = `lp${++seq}`, large = size === 'large', key = keyBeat(lp);
  const a11y = large ? `role="img" aria-label="${esc(label)}"` : 'aria-hidden="true" focusable="false"';
  return `<svg class="lp-svg lp-${size}${lp.width > lp.height ? ' lp-land' : ''}" viewBox="0 0 ${lp.width} ${lp.height}" ${a11y} data-uid="${uid}">` +
    `<style>${frames(lp, uid)}</style>` +
    `<rect width="${lp.width}" height="${lp.height}" rx="70" fill="#1b1a19"/>` +
    lp.beats.map((b, i) => `<g class="lp-beat${i === key ? ' lp-key' : ''}" data-kind="${esc(b.kind)}"${anim(lp, uid, i)}>${beatSvg(lp, b, assets, large)}</g>`).join('') +
    `<rect width="${lp.width}" height="${lp.height}" rx="70" fill="none" stroke="#605e5c" stroke-width="12"/></svg>`;
}

/** Display name of a box's video: "A（主映像）" for a project asset, the slot name for an illustration. */
function name(box, assets, roleLabel) {
  if (!box) return '';
  const { i, letter } = who(box, assets);
  if (i >= 0) return trUi('lp.asset', [letter, roleLabel[assets[i].role] ?? assets[i].role]);
  return trUi(`lp.slot.${box.slot}`);
}

const front = (beat) => beat.boxes.filter(b => !b.backdrop);

/** One sentence per beat (screen-reader text and the step list under the large diagram). */
export function beatText(lp, beat, assets, roleLabel) {
  const [first, second] = front(beat);
  const n = (b) => name(b, assets, roleLabel);
  switch (beat.kind) {
    case 'pip': return trUi('lp.b.pip', [n(first), n(second)]);
    case 'screen': return trUi('lp.b.screen', [n(first), n(second)]);
    case 'cam': return trUi('lp.b.cam', [beat.speaker, n(first)]);
    case 'hook': case 'wide': return trUi(`lp.b.${beat.kind}`, [beat.sec.toFixed(0), n(first)]);
    default: return trUi(`lp.b.${beat.kind}`, [n(first)]);
  }
}

export function issueText(lp, code, assets, roleLabel) {
  const mainBox = lp.beats[0]?.boxes.find(b => !b.backdrop);
  const scr = lp.beats.flatMap(b => b.boxes).find(b => b.slot === 'screen');
  return trUi(`lp.i.${code}`, [name(mainBox, assets, roleLabel), name(scr, assets, roleLabel)]);
}

/** The large panel: animated canvas, the steps in order with their seconds (the current one highlighted in step with the loop), and notes. */
export function layoutPanel(lp, preset, assets, roleLabel) {
  const steps = lp.beats.map(b => beatText(lp, b, assets, roleLabel));
  const label = `${trUi('lp.title', [preset.id, preset.name])}: ${steps.join(' → ')}`;
  const svg = layoutSvg(lp, assets, { size: 'large', label });
  const uid = svg.match(/data-uid="([^"]+)"/)[1];
  const total = lp.beats.reduce((s, b) => s + b.sec, 0);
  const list = lp.beats.map((b, i) => `<li><span class="lp-now"${anim(lp, uid, i)}></span>` +
    `<span class="lp-step-svg">${layoutSvg({ ...lp, beats: [b] }, assets, { size: 'thumb' })}</span><span class="caption">${esc(steps[i])}</span></li>`).join('');
  const notes = [...lp.issues.map(c => `<li class="warn">${esc(issueText(lp, c, assets, roleLabel))}</li>`),
    lp.face_basis ? `<li>${esc(trUi(`lp.face.${lp.face_basis}`))}</li>` : ''].join('');
  return `<div class="lp-panel" role="group" aria-labelledby="lp-title">
      <h3 id="lp-title" class="lp-title">${esc(trUi('lp.title', [preset.id, preset.name]))}</h3>
      <div class="lp-body">${svg}
        <ol class="lp-steps" aria-label="${esc(trUi('lp.steps'))}">${list}</ol></div>
      <p class="caption">${esc(trUi('lp.loop', [total.toFixed(0)]))}</p>
      ${notes ? `<ul class="lp-notes caption">${notes}</ul>` : ''}
    </div>`;
}

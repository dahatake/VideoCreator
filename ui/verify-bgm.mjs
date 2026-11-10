// End-to-end check of scene-matched BGM against a running app (isolated instance): registers several BGM files at once, generates with
// bgm = "scene", then drives the review screen (Playwright + Edge): per-scene track change, level change, preview re-render, measured S-15.
// Usage: node ui/verify-bgm.mjs [baseUrl] [video] [bgm files...]
import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';

const base = process.argv[2] ?? 'http://127.0.0.1:8799';
const video = path.resolve(process.argv[3] ?? 'spikes/media/talk.mp4');
const files = process.argv.slice(4);
const H = { 'X-VideoCreator': '1' };
const api = async (p, opt = {}) => {
  const r = await fetch(base + p, { ...opt, headers: { ...H, ...(opt.body && !(opt.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}), ...(opt.headers ?? {}) } });
  const t = await r.text(); const j = t ? JSON.parse(t) : null;
  if (!r.ok) throw new Error(`${opt.method ?? 'GET'} ${p} → ${r.status} ${t}`);
  return j;
};
const check = (ok, msg) => { console.log(`[${ok ? 'PASS' : 'FAIL'}] ${msg}`); if (!ok) process.exitCode = 1; };

const project = await api('/api/projects', { method: 'POST', body: JSON.stringify({ name: 'BGM e2e' }) });
const asset = await api(`/api/projects/${project.id}/assets`, { method: 'POST', body: JSON.stringify({ path: video }) });
await api(`/api/projects/${project.id}/rights`, { method: 'POST', body: JSON.stringify({ asset_id: asset.id, confirmed: true }) });

const fd = new FormData();
for (const f of files) fd.append('file', new Blob([fs.readFileSync(f)]), path.basename(f));
fd.append('rights_confirmed', 'true');
const up = await api(`/api/projects/${project.id}/bgm`, { method: 'POST', body: fd });
check(up.tracks.length === files.length, `registered ${up.tracks.length}/${files.length} tracks in one upload`);
for (const t of up.tracks) console.log(`  ${t.name}: ${t.analysis.lufs} LUFS, ${t.analysis.bpm} BPM, attacks ${t.analysis.onset_rate}/s, centroid ${t.analysis.brightness} Hz, energy ${t.analysis.energy}, playable ${t.analysis.start}–${t.analysis.end}s`);
check(up.tracks.every(t => t.analysis.start > 1.5 && t.analysis.start < 2.5), 'leading 2 s silence is skipped (playable part starts at ~2 s)');

const job = await api(`/api/projects/${project.id}/jobs`, { method: 'POST', body: JSON.stringify({ preset_ids: ['P6'], options: { bgm: 'scene', sfx: false } }) });
let j;
for (let i = 0; i < 400; i++) {
  j = await api(`/api/jobs/${job.job_id}`);
  if (['ready', 'failed', 'done', 'cancelled'].includes(j.status)) break;
  await new Promise(r => setTimeout(r, 3000));
}
check(j.status === 'ready', `job finished: ${j.status} ${j.message ?? ''}`);
const cands = await api(`/api/jobs/${job.job_id}/candidates`);
const epId = cands[0].candidate.edit_plan_id;
const ep = await api(`/api/editplans/${epId}`);
const a = ep.plan.audio;
check(a.bgm_mode === 'scene' && a.bgm_cues.length >= 1, `plan has ${a.bgm_cues.length} scene cue(s), mode ${a.bgm_mode}`);
for (const c of a.bgm_cues) console.log(`  ${c.t0}–${c.t1}s energy ${c.scene_energy} → ${up.tracks.find(t => t.id === c.track)?.name ?? c.track}: ${c.reason}`);
check(a.duck_spans.length > 0 && a.duck_spans.every(d => d.db >= 0), `${a.duck_spans.length} duck spans, depth ${Math.min(...a.duck_spans.map(d => d.db))}–${Math.max(...a.duck_spans.map(d => d.db))} dB`);
const s15 = ep.qa.find(q => q.check_id === 'S-15');
check(!!s15 && s15.status === 'pass', `S-15 on the preview: ${s15?.status} ${s15?.measured}`);
console.log('  notes: ' + cands[0].candidate.notes.filter(n => n.includes('BGM')).join(' / '));

const browser = await chromium.launch({ channel: 'msedge' });
const page = await browser.newPage({ viewport: { width: 1500, height: 1100 } });
await page.goto(`${base}/#/ep/${epId}`);
await page.waitForSelector('#h-bgm', { timeout: 60000 });
await page.locator('#h-bgm').scrollIntoViewIfNeeded();
await page.screenshot({ path: path.join(os.tmpdir(), 'vc-bgm-review.png'), fullPage: true });
const rows = await page.locator('.bgm-cue').count();
check(rows === a.bgm_cues.length, `review table shows ${rows} scene row(s)`);
// choose the other track for the first scene, raise the speech gap, re-render the preview
const other = up.tracks.find(t => t.id !== a.bgm_cues[0].track);
await page.selectOption('select.bgm-cue-track >> nth=0', other.id);
await page.waitForFunction((id) => document.querySelector('select.bgm-cue-track')?.value === id && document.querySelector('.bgm-cue')?.textContent.includes('あなたが選択'), other.id, { timeout: 30000 });
const mid = (await api(`/api/editplans/${epId}`)).plan.audio;
const ldTarget = mid.bgm_speech_ld >= 20 ? 14 : 24;
await page.selectOption('#bgmLd', String(ldTarget));
await page.waitForFunction((v) => document.querySelector('#bgmLd')?.value === v, String(ldTarget), { timeout: 30000 });
const after = (await api(`/api/editplans/${epId}`)).plan.audio;
check(after.bgm_cues[0].track === other.id && !after.bgm_cues[0].auto, 'scene 1 now uses the track chosen on the review screen');
const dLd = ldTarget - mid.bgm_speech_ld;
check(after.bgm_speech_ld === ldTarget && after.duck_spans.every((d, i) => Math.abs(d.db - Math.min(30, Math.max(0, mid.duck_spans[i].db + dLd))) <= 0.6 || d.db >= 29.9),
  `changing the target ${mid.bgm_speech_ld} → ${ldTarget} LU moved every duck by ${dLd} dB (${mid.duck_spans.map(d => d.db).join(',')} → ${after.duck_spans.map(d => d.db).join(',')})`);
await page.click('#repreview');
await page.waitForFunction(() => /実測/.test(document.querySelector('#bgmMeasured')?.textContent ?? '') && !document.querySelector('#rpBar'), null, { timeout: 300000 });
const measured = await page.textContent('#bgmMeasured');
console.log('  ' + measured.trim());
const ld = Number((measured.match(/話している間 ([\d.]+) LU/) ?? [])[1]);
check(ld >= ldTarget - 1.5, `after the edit the preview measures ${ld} LU (target ${ldTarget})`);
await page.locator('#h-bgm').scrollIntoViewIfNeeded();
await page.screenshot({ path: path.join(os.tmpdir(), 'vc-bgm-review-after.png'), fullPage: true });
await browser.close();

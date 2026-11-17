// Layout previews (where each video appears / when it switches) on the project page, against an isolated running app with two
// caller-supplied synthetic videos. The geometry itself is checked by the .NET tests (MultiSourceTests.Layout_preview_*).
import assert from 'node:assert/strict';
import { chromium } from 'playwright';

const [base = 'http://127.0.0.1:18791', camPath, screenPath, shotDir] = process.argv.slice(2);
assert.ok(camPath && screenPath, 'pass two synthetic video paths (portrait camera, landscape screen)');
const request = async (path, method = 'GET', body) => {
  const response = await fetch(`${base}/api${path}`, {
    method, headers: { 'X-VideoCreator': '1', 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  assert.ok(response.ok, `${method} ${path}: ${await response.clone().text()}`);
  return response.json();
};
let count = 0;
const check = (ok, name) => { assert.ok(ok, name); count++; console.log(`PASS ${name}`); };
const project = await request('/projects', 'POST', { name: 'Layout preview verification', language: 'ja-JP' });
const cam = await request(`/projects/${project.id}/assets`, 'POST', { path: camPath });
const screen = await request(`/projects/${project.id}/assets`, 'POST', { path: screenPath });
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ locale: 'ja-JP', viewport: { width: 1440, height: 1100 } });
const errors = [];
page.on('pageerror', e => errors.push(e.message));
const panel = page.locator('#layoutPreview');
const visibleBeat = () => panel.locator('svg.lp-large .lp-beat').evaluateAll(gs => gs.map(g => getComputedStyle(g).opacity === '1' ? g.dataset.kind : null).filter(Boolean));
try {
  await page.goto(`${base}/#/p/${project.id}`);
  await page.locator('#presets [role=radio][data-id="P11"]').click();
  await panel.locator('svg.lp-large').waitFor();

  // roles not set yet: the sketch says what the pipeline would do and what is missing
  check(await page.locator('.asset .lp-chip').count() === 2, 'each video has an A/B chip matching the sketch');
  check(await panel.locator('svg.lp-large .lp-beat').count() === 4, 'P11 loop: hook, PiP, talk, switch');
  const warns = await panel.locator('.lp-notes li.warn').allTextContents();
  check(warns.length === 1 && warns[0].includes('画面'), `the missing screen role is called out: ${warns.join(' / ')}`);
  check(await page.locator('#presets .lp-card-icon svg').count() >= 3, 'multi-video patterns have an icon on their card');
  check(await page.locator('#presets .lp-card-icon svg[aria-hidden="true"]').count() === await page.locator('#presets .lp-card-icon svg').count(), 'card icons are decorative');
  const label = await panel.locator('svg.lp-large').getAttribute('aria-label');
  check(/[AB]：/.test(label ?? '') && label.length > 10, `large sketch has a text alternative: ${label}`);

  // the animation actually switches
  const seen = new Set();
  for (let i = 0; i < 14; i++) { (await visibleBeat()).forEach(k => seen.add(k)); await page.waitForTimeout(500); }
  check(seen.size >= 3, `the loop shows several layouts over time: ${[...seen].join(',')}`);
  if (shotDir) await panel.screenshot({ path: `${shotDir}/layout-unset.png` });

  // choose roles in the UI → the page reloads the sketch with the new assignment
  await page.locator(`select.role[data-asset="${cam.id}"]`).selectOption('main');
  await panel.locator('svg.lp-large').waitFor();
  await page.locator(`select.role[data-asset="${screen.id}"]`).selectOption('screen');
  await page.waitForFunction(() => !document.querySelector('#layoutPreview')?.textContent.includes('役割が「画面」'), null, { timeout: 10000 }).catch(() => { });
  await request(`/assets/${screen.id}`, 'PATCH', { pip_scenes: [{ t0: 2, t1: 8 }] });
  await page.reload();
  await page.locator('#presets [role=radio][data-id="P11"]').click();
  await panel.locator('svg.lp-large').waitFor();
  check(await panel.locator('.lp-notes li.warn').count() === 0, 'with main + screen + scenes there is nothing to fix');
  const inset = panel.locator('svg.lp-large .lp-beat[data-kind="pip"] rect[stroke="#fff"]');
  check(await inset.count() === 1, 'the PiP beat draws the screen as an outlined inset');
  const lay = (await request(`/projects/${project.id}/layouts`)).find(l => l.preset_id === 'P11');
  const box = lay.beats.find(b => b.kind === 'pip').boxes.find(b => b.slot === 'screen');
  check(box.asset_id === screen.id && Number(await inset.getAttribute('y')) === box.y, `inset drawn at the planner's position (y=${box.y})`);
  check(await panel.locator('.lp-steps li').count() === 4, 'step list explains each layout');
  if (shotDir) await panel.screenshot({ path: `${shotDir}/layout-p11.png` });

  // another pattern updates the panel without a reload
  await page.locator('#presets [role=radio][data-id="P10"]').click();
  check(await panel.locator('svg.lp-large circle').count() >= 1, 'P10 shows the presenter as a round wipe');
  if (shotDir) await panel.screenshot({ path: `${shotDir}/layout-p10.png` });

  // reduced motion: no animation, the most telling layout stays visible
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.locator('#presets [role=radio][data-id="P11"]').click();
  await page.waitForTimeout(300);
  const still = await visibleBeat();
  check(still.length === 1 && still[0] === 'pip', `reduced motion shows the PiP layout still: ${still}`);
  check(errors.length === 0, `no page errors ${errors.join(' | ')}`);
  console.log(`${count} checks passed`);
} finally {
  await browser.close();
}

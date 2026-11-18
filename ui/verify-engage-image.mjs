// Real settings persistence; job requests stop at the browser boundary so no paid AI calls are made.
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const base = process.argv[2] ?? 'http://127.0.0.1:18931';
const temp = await mkdtemp(path.join(tmpdir(), 'vc-engage-ui-'));
const repo = fileURLToPath(new URL('../', import.meta.url));
let browser, projectId;
const api = async (url, method = 'GET', body) => {
  const response = await fetch(base + url, {
    method, headers: { 'X-VideoCreator': '1', 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  assert.ok(response.ok, `${method} ${url}: ${response.status} ${response.ok ? '' : await response.text()}`);
  return response.status === 204 ? null : response.json();
};
try {
  const source = path.join(temp, 'source.mp4');
  execFileSync(path.join(repo, 'tools', 'ffmpeg', 'bin', 'ffmpeg.exe'), [
    '-v', 'error', '-nostdin', '-y', '-f', 'lavfi', '-i', 'testsrc2=s=640x360:r=5:d=30',
    '-an', '-c:v', 'libx264', '-preset', 'ultrafast', source,
  ]);
  const project = await api('/api/projects', 'POST', { name: 'Engage image UI test' });
  projectId = project.id;
  const added = await api(`/api/projects/${projectId}/assets`, 'POST', { path: source });
  const asset = added.asset ?? added;
  await api(`/api/projects/${projectId}/rights`, 'POST', { asset_id: asset.id, confirmed: true });
  browser = await chromium.launch({ channel: 'msedge', headless: true });
  const page = await browser.newPage({ locale: 'ja-JP', viewport: { width: 1440, height: 1100 } });
  const errors = [], jobs = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.route('**/api/projects/*/jobs', async route => {
    jobs.push(route.request().postDataJSON());
    await route.fulfill({ status: 422, json: { error: { code: 'E-IMAGE-CONFIG', message: 'UI fixture: no paid generation' } } });
  });
  const open = async () => {
    await page.goto(`${base}/#/p/${projectId}/settings`);
    await page.locator('#engageImageChoice').waitFor();
    await page.waitForFunction(() => !!document.querySelector('#gen') && document.querySelectorAll('#patterns').length === 1);
  };
  await open();
  assert.equal(await page.locator('#engageImageChoice').inputValue(), 'false');
  assert.equal(await page.locator('#engageImageDeploymentBox').isVisible(), false);
  await page.locator('#engageImageChoice').selectOption('true');
  await page.locator('#engageImageDeployment').fill('engage-image-model');
  await page.waitForResponse(r => r.url().endsWith('/generation-settings') && r.request().postDataJSON()?.engage_image_deployment === 'engage-image-model');
  let saved = (await api(`/api/projects/${projectId}`)).project.generation_settings;
  assert.equal(saved.make_engage_image, true);
  assert.equal(saved.engage_image_deployment, 'engage-image-model');
  await page.reload();
  await page.locator('#engageImageChoice').waitFor();
  assert.equal(await page.locator('#engageImageChoice').inputValue(), 'true');
  assert.equal(await page.locator('#engageImageDeployment').inputValue(), 'engage-image-model');
  assert.equal(await page.locator('#engageImageDeployment').isVisible(), true);
  await page.locator('#gen').click();
  await page.waitForFunction(() => document.querySelector('#genMsg')?.textContent.includes('UI fixture'));
  assert.equal(jobs.at(-1).options.make_engage_image, true);
  assert.equal(jobs.at(-1).options.engage_image_deployment, 'engage-image-model');
  if (process.argv[3]) {
    await page.locator('#engageImageChoice').scrollIntoViewIfNeeded();
    await page.screenshot({ path: process.argv[3], fullPage: true });
  }
  await page.locator('#engageImageChoice').selectOption('false');
  await page.locator('#gen').click();
  await page.waitForResponse(r => r.url().endsWith('/jobs'));
  assert.equal(jobs.at(-1).options.make_engage_image, false);
  saved = (await api(`/api/projects/${projectId}`)).project.generation_settings;
  assert.equal(saved.make_engage_image, false);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.locator('#engageImageChoice').scrollIntoViewIfNeeded();
  assert.equal(await page.locator('#engageImageChoice').evaluate(e =>
    e.getBoundingClientRect().width <= e.closest('section').getBoundingClientRect().width), true);
  assert.deepEqual(errors, []);
  console.log('PASS: image opt-in/default-off, saved deployment, reload, job snapshots, opt-out and control sizing.');
} finally {
  await browser?.close();
  if (projectId) await api(`/api/projects/${projectId}`, 'DELETE');
  await rm(temp, { recursive: true, force: true });
}

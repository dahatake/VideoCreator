// Uses two caller-supplied synthetic videos and an isolated running app; no cloud services are invoked.
import assert from 'node:assert/strict';
import { chromium } from 'playwright';

const [base = 'http://127.0.0.1:18779', mainPath, screenPath] = process.argv.slice(2);
assert.ok(mainPath && screenPath, 'pass two synthetic video paths');
const request = async (path, method = 'GET', body) => {
  const response = await fetch(`${base}/api${path}`, {
    method, headers: { 'X-VideoCreator': '1', 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  assert.ok(response.ok, `${method} ${path}: ${await response.clone().text()}`);
  return response.json();
};
const project = await request('/projects', 'POST', { name: 'PiP UI verification' });
const main = await request(`/projects/${project.id}/assets`, 'POST', { path: mainPath });
const screen = await request(`/projects/${project.id}/assets`, 'POST', { path: screenPath });
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
const errors = [];
page.on('pageerror', e => errors.push(e.message));
try {
  await page.goto(`${base}/#/p/${project.id}`);
  await page.locator(`.role-pick[data-asset="${screen.id}"] [data-role="screen"]`).click();
  await page.locator(`.pip-scenes[data-asset="${screen.id}"]`).click();
  const list = page.locator(`.pip-scene-list[data-asset="${screen.id}"]`);
  await list.locator('.pip-scene').first().waitFor();
  assert.ok(await list.locator('.pip-scene').count() >= 2, 'visual scenes are split');
  const second = list.locator('.pip-scene').nth(1);
  await second.locator('.pip-use').check();
  await second.locator('.pip-start').fill('6.5');
  await second.locator('.pip-end').fill('11');
  await list.locator('.pip-save').click();
  await list.locator('.pip-message fluent-message-bar[intent="success"]').waitFor();
  let saved = await request(`/projects/${project.id}`);
  assert.deepEqual(saved.assets.find(a => a.id === screen.id).pip_scenes.map(({ t0, t1 }) => ({ t0, t1 })), [{ t0: 6.5, t1: 11 }]);
  await page.reload();
  await page.locator(`.pip-scenes[data-asset="${screen.id}"]`).click();
  await list.locator('.pip-use:checked').waitFor();
  const restored = list.locator('.pip-scene').filter({ has: page.locator('.pip-use:checked') });
  assert.equal(await restored.locator('.pip-start').inputValue(), '6.5');
  assert.equal(await restored.locator('.pip-end').inputValue(), '11');
  await page.locator('.preset-radio[data-id="P11"]').click();
  assert.equal(await page.locator('.preset-radio[data-id="P11"]').getAttribute('aria-checked'), 'true');
  await request(`/assets/${screen.id}`, 'PATCH', { sync_offset: 2 });
  await page.locator(`.role-pick[data-asset="${screen.id}"] [data-role="main"]`).click();
  await page.waitForFunction(id => document.querySelector(`.role-pick[data-asset="${id}"] [aria-checked="true"]`)?.dataset.role === 'screen', main.id);
  saved = await request(`/projects/${project.id}`);
  assert.equal(saved.assets.find(a => a.id === screen.id).sync_offset, 0);
  assert.equal(saved.assets.find(a => a.id === main.id).role, 'screen');
  assert.deepEqual(errors, []);
  console.log('PASS PiP scene detection, editable selection, persistence, P11 selection, main camera change and sync invalidation');
} finally {
  await browser.close();
}

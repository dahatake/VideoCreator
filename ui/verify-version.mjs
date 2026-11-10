import assert from 'node:assert/strict';
import { chromium } from 'playwright';

const base = process.argv[2] ?? 'http://127.0.0.1:8765';
const expected = process.argv[3] ?? '1.0.0';
const browser = await chromium.launch({ channel: 'msedge', headless: true });
try {
  const page = await browser.newPage({ locale: 'ja-JP' });
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  const response = await page.request.get(`${base}/api/health`);
  assert.equal(response.status(), 200);
  const health = await response.json();
  assert.equal(health.version, expected);
  assert.equal(health.informational_version.split('+')[0], expected);
  await page.goto(base);
  await page.locator('#settings-btn').click();
  await page.locator('#app-version').waitFor({ state: 'visible' });
  assert.equal(await page.locator('#app-version').textContent(), expected);
  assert.ok((await page.locator('#settings-body').textContent()).includes(health.informational_version));
  assert.deepEqual(errors, []);
  console.log(`PASS API and settings display the running version ${expected}`);
} finally {
  await browser.close();
}

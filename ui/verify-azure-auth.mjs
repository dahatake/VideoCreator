import assert from 'node:assert/strict';
import { chromium } from 'playwright';

const base = process.argv[2] ?? 'http://127.0.0.1:18779';
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ locale: 'ja-JP', viewport: { width: 1440, height: 1100 } });
const errors = [], sent = [];
page.on('pageerror', e => errors.push(e.message));
const endpoint = 'https://speech.cognitiveservices.azure.com/';
const settings = {
  llm_provider: 'none', local_only: true, copilot_cli: null, asr_configured: true,
  azure_configured: true, azure_deployment: 'gpt-chat-latest', secret_protection: 'dpapi',
  transmission: { llm_disabled: true, asr_endpoint: endpoint.slice(0, -1), authentication: 'Microsoft Entra ID', llm_destination: null },
  speech: { source: 'config', endpoint, auth: 'entra' },
  foundry: { source: 'config', endpoint, deployment: 'gpt-chat-latest', auth: 'entra' },
  copilot: { source: 'config', auth: 'user' },
};
try {
  await page.route('**/api/settings', route => route.fulfill({ json: settings }));
  await page.route(/\/api\/settings\/ai\/speech(?:\/test)?$/, async route => {
    const request = route.request();
    if (request.method() === 'DELETE') {
      settings.speech = { source: 'config', endpoint, auth: 'entra' };
      return route.fulfill({ json: { speech: settings.speech } });
    }
    const body = request.postDataJSON();
    sent.push({ method: request.method(), body });
    const test = { ok: true, steps: [{ id: 'speech', ok: true, ms: 10 }], diagnosis: null, endpoint };
    if (request.method() === 'POST') return route.fulfill({ json: test });
    settings.speech = { source: 'app', endpoint, auth: body.auth, tenant_id: body.tenant_id, has_api_key: body.auth === 'key', tested_at: new Date().toISOString() };
    return route.fulfill({ json: { test, speech: settings.speech } });
  });
  await page.goto(base);
  await page.locator('#settings-btn').click();
  await page.locator('#sEndpoint').waitFor();
  assert.equal(await page.locator('#sEndpoint').evaluate(el => el.value), endpoint);
  assert.equal(await page.locator('#sKeyBox').isVisible(), false);
  assert.equal(await page.locator('#sEntraBox').isVisible(), true);
  assert.equal(await page.locator('#sTest').evaluate(el => !!el.disabled), false, 'Speech works with LLM disabled');
  assert.equal(await page.locator('#fTest').evaluate(el => !!el.disabled), true, 'LLM test remains blocked');
  assert.ok((await page.locator('#sEntraBox').innerText()).includes('Cognitive Services Speech User'));
  assert.ok((await page.locator('#sEntraBox').innerText()).includes('<テナントID>'), 'tenant placeholder remains visible');

  await page.locator('#sAuth fluent-radio[value="key"]').click();
  await page.locator('#sKeyBox').waitFor({ state: 'visible' });
  assert.equal(await page.locator('#sEntraBox').isVisible(), false);
  assert.equal(await page.locator('#sKey').getAttribute('type'), 'password');
  await page.locator('#sKey').evaluate(el => { el.value = 'ui-test-key-not-a-real-secret'; });
  await page.locator('#sTest').click();
  await page.waitForFunction(() => document.querySelector('#sResult')?.textContent.includes('合格'));
  assert.equal(sent.at(-1).method, 'POST');
  assert.equal(sent.at(-1).body.auth, 'key');
  assert.equal(sent.at(-1).body.api_key, 'ui-test-key-not-a-real-secret');
  assert.equal(settings.speech.source, 'config', 'test does not save');
  await page.locator('#sSave').click();
  await page.locator('#sForget').waitFor();
  assert.equal(sent.at(-1).method, 'PUT');
  assert.equal(await page.locator('#sKey').evaluate(el => el.value), '', 'saved key is cleared');
  assert.ok(!(await page.locator('#settings-body').innerText()).includes('ui-test-key-not-a-real-secret'));
  assert.equal(await page.locator('#fTest').evaluate(el => !!el.disabled), true);

  await page.locator('#sAuth fluent-radio[value="entra"]').click();
  await page.locator('#sEntraBox').waitFor({ state: 'visible' });
  await page.locator('#sTenant').evaluate(el => { el.value = '16b3c013-d300-468d-ac64-7eda0820b6d3'; });
  await page.locator('#sSave').click();
  await page.waitForFunction(() => document.querySelector('#sKey')?.getAttribute('placeholder') === null && !document.querySelector('#sSave')?.disabled);
  assert.equal(sent.at(-1).body.auth, 'entra');
  assert.equal(sent.at(-1).body.tenant_id, '16b3c013-d300-468d-ac64-7eda0820b6d3');
  assert.equal(sent.at(-1).body.api_key, '');
  assert.equal(settings.speech.has_api_key, false);

  page.once('dialog', dialog => dialog.accept());
  await page.locator('#sForget').click();
  await page.locator('#sForget').waitFor({ state: 'detached' });
  assert.equal(settings.speech.source, 'config');
  assert.deepEqual(errors, []);
  console.log('PASS: Speech key/Entra controls, LLM-disabled behavior, test-before-save, key clearing, tenant submission and reset.');
} finally {
  await browser.close();
}

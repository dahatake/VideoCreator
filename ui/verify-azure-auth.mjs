import assert from 'node:assert/strict';
import { chromium } from 'playwright';

// Settings → Microsoft Foundry (the only Azure connection; captions use the same resource): authentication comes first,
// the other method's inputs are hidden, test-before-save, key clearing, tenant submission and reset. The API is mocked.
const base = process.argv[2] ?? 'http://127.0.0.1:18779';
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ locale: 'ja-JP', viewport: { width: 1440, height: 1400 } });
const errors = [], sent = [];
page.on('pageerror', e => errors.push(e.message));
const endpoint = 'https://shared.cognitiveservices.azure.com/openai/v1/';
const configured = { source: 'config', endpoint, deployment: 'gpt-chat-latest', auth: 'entra' };
const settings = {
  llm_provider: 'azure-openai', local_only: false, copilot_cli: null, asr_configured: true,
  azure_configured: true, azure_deployment: 'gpt-chat-latest', secret_protection: 'dpapi',
  transmission: { llm_disabled: false, asr_endpoint: 'https://shared.cognitiveservices.azure.com', authentication: 'Microsoft Entra ID', llm_destination: 'https://shared.cognitiveservices.azure.com' },
  speech: { model: 'MAI-Transcribe-2', mai_regions: ['southeastasia'] },
  foundry: configured,
  copilot: { source: 'config', auth: 'user' },
};
try {
  await page.route('**/api/settings', route => route.fulfill({ json: settings }));
  await page.route('**/api/settings/drafts**', route => route.fulfill(route.request().method() === 'GET' ? { json: {} } : { status: 204 }));
  await page.route(/\/api\/settings\/ai\/foundry(?:\/test)?$/, async route => {
    const request = route.request();
    if (request.method() === 'DELETE') {
      settings.foundry = configured;
      return route.fulfill({ json: { foundry: settings.foundry } });
    }
    const body = request.postDataJSON();
    sent.push({ method: request.method(), body });
    const test = { ok: true, steps: [{ id: 'chat', ok: true, ms: 10 }, { id: 'speech', ok: true, ms: 10, model: 'MAI-Transcribe-2' }], diagnosis: null, endpoint };
    if (request.method() === 'POST') return route.fulfill({ json: test });
    settings.foundry = { source: 'app', endpoint, deployment: body.deployment, auth: body.auth, tenant_id: body.tenant_id, has_api_key: body.auth === 'key', tested_at: new Date().toISOString() };
    return route.fulfill({ json: { test, foundry: settings.foundry } });
  });
  await page.goto(base);
  await page.locator('#settings-btn').click();
  await page.locator('#fEndpoint').waitFor();

  // one Azure connection only: no Azure Speech group, no second Azure picker
  assert.deepEqual(await page.locator('#aiFlash ~ details.setgroup > summary h3').allInnerTexts(), ['Microsoft Foundry', 'GitHub Copilot']);
  assert.equal(await page.locator('#aiSpeech').count(), 0);
  assert.equal(await page.locator('#sPick').count(), 0);
  assert.equal(await page.locator('#fPick').count(), 1);

  // order: authentication first, then the picker and endpoint
  const order = await page.locator('#aiFoundry').evaluate(root => ['#fAuth', '#fTenant', '#fKey', '#fPick', '#fEndpoint'].map(sel => {
    const el = root.querySelector(sel); return el.getBoundingClientRect().top + scrollY;
  }));
  assert.ok(order[0] < order[3] && order[0] < order[4], `authentication comes before the picker and endpoint (${order})`);

  assert.equal(await page.locator('#fKeyBox').isVisible(), false);
  assert.equal(await page.locator('#fEntraBox').isVisible(), true);
  assert.equal(await page.locator('#fTest').evaluate(el => !!el.disabled), false);

  await page.locator('#fAuth fluent-radio[value="key"]').click();
  await page.locator('#fKeyBox').waitFor({ state: 'visible' });
  assert.equal(await page.locator('#fEntraBox').isVisible(), false, 'Entra inputs are hidden while the key is selected');
  assert.equal(await page.locator('#fKey').getAttribute('type'), 'password');
  await page.locator('#fKey').evaluate(el => { el.value = 'ui-test-key-not-a-real-secret'; });
  await page.locator('#fTest').click();
  await page.waitForFunction(() => document.querySelector('#fResult')?.textContent.includes('合格'));
  assert.equal(sent.at(-1).method, 'POST');
  assert.equal(sent.at(-1).body.auth, 'key');
  assert.equal(sent.at(-1).body.api_key, 'ui-test-key-not-a-real-secret');
  assert.equal(settings.foundry.source, 'config', 'test does not save');
  await page.locator('#fSave').click();
  await page.locator('#fForget').waitFor();
  assert.equal(sent.at(-1).method, 'PUT');
  assert.equal(await page.locator('#fKey').evaluate(el => el.value), '', 'saved key is cleared');
  assert.ok(!(await page.locator('#settings-body').innerText()).includes('ui-test-key-not-a-real-secret'));

  await page.locator('#fAuth fluent-radio[value="entra"]').click();
  await page.locator('#fEntraBox').waitFor({ state: 'visible' });
  assert.equal(await page.locator('#fKeyBox').isVisible(), false, 'key input is hidden while Entra ID is selected');
  await page.locator('#fTenant').evaluate(el => { el.value = '16b3c013-d300-468d-ac64-7eda0820b6d3'; });
  await page.locator('#fSave').click();
  await page.waitForFunction(() => document.querySelector('#fKey')?.getAttribute('placeholder') === null && !document.querySelector('#fSave')?.disabled);
  assert.equal(sent.at(-1).body.auth, 'entra');
  assert.equal(sent.at(-1).body.tenant_id, '16b3c013-d300-468d-ac64-7eda0820b6d3');
  assert.equal(sent.at(-1).body.api_key, '', 'the key field is cleared, so no key is sent for Entra ID');
  assert.equal(settings.foundry.has_api_key, false);

  page.once('dialog', dialog => dialog.accept());
  await page.locator('#fForget').click();
  await page.locator('#fForget').waitFor({ state: 'detached' });
  assert.equal(settings.foundry.source, 'config');
  assert.deepEqual(errors, []);
  console.log('PASS: Foundry authentication first, other method hidden, test-before-save, key clearing, tenant submission and reset; no second Azure connection.');
} finally {
  await browser.close();
}

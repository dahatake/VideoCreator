import assert from 'node:assert/strict';
import { chromium } from 'playwright';

// "Azure から選択" in Settings (the only Azure picker): ARM listing is mocked; checks selection → inputs, Entra switch, warnings, accessible names.
const base = process.argv[2] ?? 'http://127.0.0.1:18779';
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ locale: 'ja-JP', viewport: { width: 1440, height: 1400 } });
const errors = [], calls = [];
page.on('pageerror', e => errors.push(e.message));
const settings = {
  llm_provider: 'none', local_only: false, copilot_cli: null, asr_configured: false, azure_configured: false, secret_protection: 'dpapi',
  transmission: { llm_disabled: false },
  speech: { model: 'MAI-Transcribe-2', mai_regions: ['eastus'] },
  foundry: { source: 'none', auth: 'key' },
  copilot: { source: 'config', auth: 'user' },
};
const account = (name, extra) => ({
  id: `/subscriptions/s1/resourceGroups/rg-${name}/providers/Microsoft.CognitiveServices/accounts/${name}`, name, kind: 'AIServices', location: 'japaneast',
  subscription_id: 's1', subscription_name: 'Main', resource_group: `rg-${name}`, endpoint: `https://${name}.cognitiveservices.azure.com/`,
  custom_domain: true, key_auth_disabled: true, provisioning_state: 'Succeeded', speech: true, foundry: true, can_transcribe: true, can_chat: true, can_embed: false, ...extra,
});
const catalog = {
  subscriptions: [{ subscription_id: 's1', display_name: 'Main', state: 'Enabled' }, { subscription_id: 's2', display_name: 'Locked', state: 'Enabled' }],
  accounts: [
    account('ai-both'),
    account('ai-other', { key_auth_disabled: false }),
    account('speech-regional', { kind: 'SpeechServices', foundry: false, custom_domain: false, key_auth_disabled: false, can_transcribe: false, can_chat: null, can_embed: null,
      endpoint: 'https://japaneast.api.cognitive.microsoft.com/' }),
  ],
  issues: [{ scope: 'Locked', code: 'forbidden', message: 'no read' }],
};
const deployments = [
  { name: 'emb', model: 'text-embedding-3-large', model_version: '1', sku: 'GlobalStandard', provisioning_state: 'Succeeded', chat: false, embeddings: true },
  { name: 'gpt-4.1', model: 'gpt-4.1', model_version: '2025-04-14', sku: 'GlobalStandard', provisioning_state: 'Succeeded', chat: true, embeddings: false },
  { name: 'image-prod', model: 'FLUX-1.1-pro', provisioning_state: 'Succeeded', chat: false, embeddings: false },
  { name: 'broken', model: 'gpt-4o', provisioning_state: 'Failed', chat: true, embeddings: false },
];
try {
  await page.route('**/api/settings', route => route.fulfill({ json: settings }));
  await page.route('**/api/settings/drafts**', route => route.fulfill(route.request().method() === 'GET' ? { json: {} } : { status: 204 }));
  await page.route('**/api/settings/ai/azure/resources', route => { calls.push(['resources', route.request().postDataJSON()]); return route.fulfill({ json: catalog }); });
  let releaseOther;
  const otherPending = new Promise(resolve => { releaseOther = resolve; });
  let saveFoundry = false, speechProbeOk = true;
  await page.route('**/api/settings/ai/azure/deployments', async route => {
    const body = route.request().postDataJSON();
    calls.push(['deployments', body]);
    if (body.account_id === catalog.accounts[1].id) {
      await otherPending;
      return route.fulfill({ json: { deployments: [{ ...deployments[1], name: 'other-chat' }] } });
    }
    return route.fulfill({ json: { deployments } });
  });
  await page.route('**/api/settings/ai/foundry/test', route => {
    calls.push(['test', route.request().postDataJSON()]);
    return speechProbeOk
      ? route.fulfill({ json: { ok: true, steps: [{ id: 'chat', ok: true, ms: 1 }, { id: 'speech', ok: true, ms: 1, model: 'MAI-Transcribe-2' }] } })
      : route.fulfill({ json: { ok: false, steps: [{ id: 'chat', ok: true, ms: 1 }, { id: 'speech', ok: false, ms: 1, diagnosis: { code: 'speech_forbidden' } }], diagnosis: { code: 'speech_forbidden' } } });
  });
  await page.route('**/api/settings/ai/foundry', route => {
    calls.push(['save', route.request().postDataJSON()]);
    if (saveFoundry) {
      settings.foundry = { source: 'app', auth: 'entra' };
      return route.fulfill({ json: { test: { ok: true, steps: [{ id: 'chat', ok: true, ms: 1 }, { id: 'speech', ok: true, ms: 1, model: 'standard' }] } } });
    }
    return route.fulfill({ status: 422, json: { ok: false, steps: [] } });
  });
  await page.goto(base);
  await page.locator('#settings-btn').click();
  await page.locator('#fPick').waitFor();
  assert.equal(await page.locator('#sPick').count(), 0, 'there is no second Azure picker');
  assert.equal(await page.locator('#aiSpeech').count(), 0, 'there is no separate Azure Speech connection');
  assert.equal(await page.locator('#fKeyBox').isVisible(), true, 'saved key auth shows the key field on first render');
  assert.equal(await page.locator('#fEntraBox').isVisible(), false);

  await page.locator('#fTenant').evaluate(el => { el.value = 'contoso.onmicrosoft.com'; });
  await page.locator('#fPick').click();
  await page.locator('#fDepBox').waitFor({ state: 'visible' });
  assert.deepEqual(calls[0], ['resources', { tenant_id: 'contoso.onmicrosoft.com' }]);
  assert.match(await page.locator('#fPickStatus').innerText(), /2 件のリソース（2 件のサブスクリプション/, 'Speech-only resource is not offered');
  assert.equal(await page.locator('#aiFoundry').getByLabel('リソース').evaluate(el => el.id), 'fResource', 'select has an accessible name');
  assert.equal(calls.at(-1)[0], 'deployments');
  assert.equal(calls.at(-1)[1].account_id, catalog.accounts[0].id);
  assert.equal(await page.locator('#fEndpoint').evaluate(el => el.value), 'https://ai-both.cognitiveservices.azure.com/');
  assert.equal(await page.locator('#fEntraBox').isVisible(), true, 'key auth disabled → switched to Entra ID');
  assert.equal(await page.locator('#fKeyBox').isVisible(), false);
  const note = await page.locator('#fPickNote').innerText();
  assert.ok(note.includes('Locked'), 'partial failure is shown');
  assert.ok(note.includes('利用権限があります') && note.includes('disableLocalAuth'), note);
  assert.ok(note.includes('埋め込みの利用権限が見つかりません'));
  assert.equal(await page.locator('#fDeployment').evaluate(el => el.value), 'gpt-4.1');
  assert.equal(await page.locator('#fEmbedding').evaluate(el => el.value), '');
  for (const id of ['fDeployment', 'fEmbedding', 'fImage'])
    assert.equal(await page.locator(`#${id}Manual`).isVisible(), false, `${id} is not duplicated in Entra mode`);
  assert.equal(await page.locator('#fEndpoint').isVisible(), true);
  assert.deepEqual(await page.locator('#fChatDep option').evaluateAll(o => o.map(x => x.value)), ['gpt-4.1'], 'failed deployments are hidden');
  await page.locator('#fEmbDep').selectOption('emb');
  await page.locator('#fEmbDep').selectOption('');
  await page.locator('#fImageDep').selectOption('image-prod');
  assert.equal(await page.locator('#fEmbedding').evaluate(el => el.value), '');

  await page.locator('#fPick').click();
  await page.locator('#fDepBox').waitFor({ state: 'visible' });
  assert.equal(calls.filter(c => c[0] === 'resources').length, 1, 'same tenant → cached');
  const selection = { endpoint: catalog.accounts[0].endpoint, deployment: 'gpt-4.1', embedding_deployment: '', image_deployment: 'image-prod', auth: 'entra', tenant_id: 'contoso.onmicrosoft.com' };
  for (const [button, call] of [['fTest', 'test'], ['fSave', 'save']]) {
    const sent = page.waitForRequest(r => r.url().endsWith(call === 'save' ? '/foundry' : '/foundry/test') && ['PUT', 'POST'].includes(r.method()));
    await page.locator(`#${button}`).click();
    const { api_key, ...body } = (await sent).postDataJSON();
    assert.ok(!api_key, 'no key is sent for Entra ID');
    assert.deepEqual(body, selection);
  }

  // captions need the same resource: a failing speech step fails the whole test
  speechProbeOk = false;
  await page.locator('#fTest').click();
  await page.locator('#fResult fluent-message-bar[intent="error"]').first().waitFor();
  assert.doesNotMatch(await page.locator('#fResult').innerText(), /接続テストに合格しました/);
  speechProbeOk = true;

  await page.locator('#fResource').selectOption(catalog.accounts[1].id);
  await page.locator('#fDeploymentManual').waitFor({ state: 'visible' });
  await page.locator('#fResource').selectOption(catalog.accounts[0].id);
  await page.locator('#fDepBox').waitFor({ state: 'visible' });
  const late = page.waitForResponse(r => r.url().endsWith('/deployments') && r.request().postDataJSON().account_id === catalog.accounts[1].id);
  releaseOther();
  await (await late).finished();
  assert.equal(await page.locator('#fChatDep').inputValue(), 'gpt-4.1');
  assert.equal(await page.locator('#fDeploymentManual').isVisible(), false);
  await page.locator('#fResource').selectOption(catalog.accounts[1].id);
  await page.locator('#fChatDep').waitFor();
  assert.equal(await page.locator('#fChatDep').inputValue(), 'other-chat');
  assert.equal(await page.locator('#fEmbeddingManual').isVisible(), true, 'manual inputs remain for model types with no deployments');
  await page.locator('#fAuth fluent-radio[value="key"]').click();
  await page.locator('#fDeploymentManual').waitFor();
  await page.locator('#fResource').selectOption('');
  for (const id of ['fDeployment', 'fEmbedding', 'fImage'])
    assert.equal(await page.locator(`#${id}Manual`).isVisible(), true, 'manual inputs return without a picker selection');

  saveFoundry = true;
  await page.locator('#fSave').click();
  await page.locator('#aiFlash').getByText(/Microsoft Foundry の接続を保存/).waitFor();
  assert.match(await page.locator('#aiFlash').innerText(), /対応リージョン/, 'a region without MAI-Transcribe-2 is explained');
  assert.equal(await page.locator('#sPick').count(), 0);
  assert.deepEqual(errors, []);
  console.log('PASS: Azure picker (single Foundry connection), visibility, persistence, and caption speech check.');
} finally {
  await browser.close();
}

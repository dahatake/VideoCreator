import assert from 'node:assert/strict';
import { chromium } from 'playwright';

// "Azure から選択" in Settings: ARM listing is mocked; checks selection → inputs, Entra switch, warnings, accessible names.
const base = process.argv[2] ?? 'http://127.0.0.1:18779';
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ locale: 'ja-JP', viewport: { width: 1440, height: 1100 } });
const errors = [], calls = [];
page.on('pageerror', e => errors.push(e.message));
const settings = {
  llm_provider: 'none', local_only: false, copilot_cli: null, asr_configured: false, azure_configured: false, secret_protection: 'dpapi',
  transmission: { llm_disabled: false },
  speech: { source: 'none', auth: 'key' },
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
    return route.fulfill({ json: { ok: true, steps: [{ id: 'chat', ok: true, ms: 1 },
      { id: 'speech', ok: speechProbeOk, ms: 1, ...(speechProbeOk ? {} : { diagnosis: { code: 'speech_forbidden' } }) }] } });
  });
  await page.route('**/api/settings/ai/foundry', route => {
    calls.push(['save', route.request().postDataJSON()]);
    if (saveFoundry) {
      settings.foundry = { source: 'app', auth: 'entra' };
      settings.speech = { source: 'app', endpoint: 'https://dedicated.cognitiveservices.azure.com/', auth: 'entra',
        foundry_available: true, use_foundry: false, separate: { source: 'app', endpoint: 'https://dedicated.cognitiveservices.azure.com/', auth: 'entra' } };
      return route.fulfill({ json: { test: { ok: true, steps: [{ id: 'chat', ok: true, ms: 1 }, { id: 'speech', ok: true, ms: 1 }] } } });
    }
    return route.fulfill({ status: 422, json: { ok: false, steps: [] } });
  });
  await page.route('**/api/settings/ai/speech/source', route => {
    const body = route.request().postDataJSON();
    calls.push(['speech-source', body]);
    settings.speech = { ...settings.speech, use_foundry: body.use_foundry, source: body.use_foundry ? 'foundry' : 'app',
      endpoint: body.use_foundry ? catalog.accounts[0].endpoint : settings.speech.separate.endpoint };
    return route.fulfill({ status: 204 });
  });
  await page.goto(base);
  await page.locator('#settings-btn').click();
  await page.locator('#sPick').waitFor();
  assert.equal(await page.locator('#sKeyBox').isVisible(), true, 'saved key auth shows the key field on first render');
  assert.equal(await page.locator('#fKeyBox').isVisible(), true);
  assert.equal(await page.locator('#sEntraBox').isVisible(), false);

  await page.locator('#sTenant').evaluate(el => { el.value = 'contoso.onmicrosoft.com'; });
  await page.locator('#sPick').click();
  await page.locator('#sResBox').waitFor({ state: 'visible' });
  assert.deepEqual(calls[0], ['resources', { tenant_id: 'contoso.onmicrosoft.com' }]);
  assert.match(await page.locator('#sPickStatus').innerText(), /3 件のリソース（2 件のサブスクリプション/);
  assert.equal(await page.getByLabel('リソース').first().evaluate(el => el.id), 'sResource', 'select has an accessible name');
  assert.equal(await page.locator('#sEndpoint').evaluate(el => el.value), 'https://ai-both.cognitiveservices.azure.com/');
  assert.equal(await page.locator('#sEntraBox').isVisible(), true, 'key auth disabled → switched to Entra ID');
  assert.equal(await page.locator('#sKeyBox').isVisible(), false);
  const sNote = await page.locator('#sPickNote').innerText();
  assert.ok(sNote.includes('Locked'), 'partial failure is shown');
  assert.ok(sNote.includes('利用権限があります') && sNote.includes('disableLocalAuth'), sNote);

  await page.locator('#sResource').selectOption(catalog.accounts[2].id);
  const regional = await page.locator('#sPickNote').innerText();
  assert.equal(await page.locator('#sEndpoint').evaluate(el => el.value), 'https://japaneast.api.cognitive.microsoft.com/');
  assert.ok((await page.locator('#sResource').evaluate(el => el.selectedOptions[0].textContent)).includes('—'), 'missing role is marked in the option');
  assert.ok(regional.includes('Cognitive Services Speech User') && regional.includes('カスタムドメインがない'), regional);

  await page.locator('#fPick').click();
  await page.locator('#fDepBox').waitFor({ state: 'visible' });
  assert.equal(calls.filter(c => c[0] === 'resources').length, 2, 'different tenant field → separate request');
  assert.equal(await page.locator('#fResource option').evaluateAll(o => o.filter(x => x.textContent.includes('·')).length), 2, 'Speech-only resource is not offered for Foundry');
  assert.equal(calls.at(-1)[0], 'deployments');
  assert.equal(calls.at(-1)[1].account_id, catalog.accounts[0].id);
  assert.equal(await page.locator('#fEndpoint').evaluate(el => el.value), 'https://ai-both.cognitiveservices.azure.com/');
  assert.equal(await page.locator('#fDeployment').evaluate(el => el.value), 'gpt-4.1');
  assert.equal(await page.locator('#fEmbedding').evaluate(el => el.value), '');
  for (const id of ['fDeployment', 'fEmbedding', 'fImage'])
    assert.equal(await page.locator(`#${id}Manual`).isVisible(), false, `${id} is not duplicated in Entra mode`);
  assert.equal(await page.locator('#fEndpoint').isVisible(), true);
  assert.deepEqual(await page.locator('#fChatDep option').evaluateAll(o => o.map(x => x.value)), ['gpt-4.1'], 'failed deployments are hidden');
  assert.equal(await page.locator('#fEntraBox').isVisible(), true);
  assert.ok((await page.locator('#fPickNote').innerText()).includes('埋め込みの利用権限が見つかりません'));
  await page.locator('#fEmbDep').selectOption('emb');
  await page.locator('#fEmbDep').selectOption('');
  await page.locator('#fImageDep').selectOption('image-prod');
  assert.equal(await page.locator('#fEmbedding').evaluate(el => el.value), '');

  await page.locator('#fPick').click();
  await page.locator('#fDepBox').waitFor({ state: 'visible' });
  assert.equal(calls.filter(c => c[0] === 'resources').length, 2, 'same tenant → cached');
  const selection = { endpoint: catalog.accounts[0].endpoint, deployment: 'gpt-4.1', embedding_deployment: '', image_deployment: 'image-prod', auth: 'entra', tenant_id: '' };
  for (const [button, call] of [['fTest', 'test'], ['fSave', 'save']]) {
    const sent = page.waitForRequest(r => r.url().endsWith(call === 'save' ? '/foundry' : '/foundry/test') && ['PUT', 'POST'].includes(r.method()));
    await page.locator(`#${button}`).click();
    const { api_key, ...body } = (await sent).postDataJSON();
    assert.ok(!api_key, 'no key is sent for Entra ID');
    assert.deepEqual(body, selection);
    assert.equal(await page.locator('#sSource').count(), 0, 'test alone does not enable a new source');
  }
  speechProbeOk = false;
  await page.locator('#fTest').click();
  await page.locator('#fResult fluent-message-bar[intent="warning"]').first().waitFor();
  assert.match(await page.locator('#fResult').innerText(), /Speech を確認できませんでした/);
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
  await page.locator('#sSource').waitFor();
  assert.equal(await page.locator('#sSource').inputValue(), 'separate');
  await page.locator('#sSource').selectOption('foundry');
  assert.equal(await page.locator('#sSeparate').isVisible(), true, 'unapplied choice does not hide the current form');
  await page.locator('#sSourceSave').click();
  await page.locator('#sSeparate').waitFor({ state: 'hidden' });
  assert.deepEqual(calls.at(-1), ['speech-source', { use_foundry: true }]);
  assert.match(await page.locator('#aiSpeech').innerText(), /送信先: https:\/\/ai-both/);
  const previousSource = await page.locator('#sSource').elementHandle();
  await page.locator('#settings-close').click();
  await page.locator('#settings-dialog').waitFor({ state: 'hidden' });
  await page.locator('#settings-btn').click();
  await previousSource.waitForElementState('hidden');
  await page.waitForFunction(el => !el.isConnected, previousSource);
  await page.locator('#sSource').waitFor();
  assert.equal(await page.locator('#sSource').inputValue(), 'foundry');
  await page.locator('#sSource').selectOption('separate');
  const sourceRequest = page.waitForRequest(r => r.url().endsWith('/speech/source') && r.postDataJSON()?.use_foundry === false);
  await page.locator('#sSourceSave').click();
  await sourceRequest;
  await page.locator('#sSeparate').waitFor();
  assert.equal(await page.locator('#sEndpoint').evaluate(el => el.value), 'https://dedicated.cognitiveservices.azure.com/');
  assert.deepEqual(errors, []);
  console.log('PASS: Azure picker and Speech source selection, visibility, persistence, and destination.');
} finally {
  await browser.close();
}

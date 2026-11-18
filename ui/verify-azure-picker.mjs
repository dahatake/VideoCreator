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
    account('speech-regional', { kind: 'SpeechServices', foundry: false, custom_domain: false, key_auth_disabled: false, can_transcribe: false, can_chat: null, can_embed: null,
      endpoint: 'https://japaneast.api.cognitive.microsoft.com/' }),
  ],
  issues: [{ scope: 'Locked', code: 'forbidden', message: 'no read' }],
};
const deployments = [
  { name: 'emb', model: 'text-embedding-3-large', model_version: '1', sku: 'GlobalStandard', provisioning_state: 'Succeeded', chat: false, embeddings: true },
  { name: 'gpt-4.1', model: 'gpt-4.1', model_version: '2025-04-14', sku: 'GlobalStandard', provisioning_state: 'Succeeded', chat: true, embeddings: false },
  { name: 'broken', model: 'gpt-4o', provisioning_state: 'Failed', chat: true, embeddings: false },
];
try {
  await page.route('**/api/settings', route => route.fulfill({ json: settings }));
  await page.route('**/api/settings/ai/azure/resources', route => { calls.push(['resources', route.request().postDataJSON()]); return route.fulfill({ json: catalog }); });
  await page.route('**/api/settings/ai/azure/deployments', route => { calls.push(['deployments', route.request().postDataJSON()]); return route.fulfill({ json: { deployments } }); });
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
  assert.match(await page.locator('#sPickStatus').innerText(), /2 件のリソース（2 件のサブスクリプション/);
  assert.equal(await page.getByLabel('リソース').first().evaluate(el => el.id), 'sResource', 'select has an accessible name');
  assert.equal(await page.locator('#sEndpoint').evaluate(el => el.value), 'https://ai-both.cognitiveservices.azure.com/');
  assert.equal(await page.locator('#sEntraBox').isVisible(), true, 'key auth disabled → switched to Entra ID');
  assert.equal(await page.locator('#sKeyBox').isVisible(), false);
  const sNote = await page.locator('#sPickNote').innerText();
  assert.ok(sNote.includes('Locked'), 'partial failure is shown');
  assert.ok(sNote.includes('利用権限があります') && sNote.includes('disableLocalAuth'), sNote);

  await page.locator('#sResource').selectOption(catalog.accounts[1].id);
  const regional = await page.locator('#sPickNote').innerText();
  assert.equal(await page.locator('#sEndpoint').evaluate(el => el.value), 'https://japaneast.api.cognitive.microsoft.com/');
  assert.ok((await page.locator('#sResource').evaluate(el => el.selectedOptions[0].textContent)).includes('—'), 'missing role is marked in the option');
  assert.ok(regional.includes('Cognitive Services Speech User') && regional.includes('カスタムドメインがない'), regional);

  await page.locator('#fPick').click();
  await page.locator('#fDepBox').waitFor({ state: 'visible' });
  assert.equal(calls.filter(c => c[0] === 'resources').length, 2, 'different tenant field → separate request');
  assert.deepEqual(await page.locator('#fResource option').evaluateAll(o => o.map(x => x.textContent).filter(t => t.includes('·')).length), 1, 'Speech-only resource is not offered for Foundry');
  assert.equal(calls.at(-1)[0], 'deployments');
  assert.equal(calls.at(-1)[1].account_id, catalog.accounts[0].id);
  assert.equal(await page.locator('#fEndpoint').evaluate(el => el.value), 'https://ai-both.cognitiveservices.azure.com/');
  assert.equal(await page.locator('#fDeployment').evaluate(el => el.value), 'gpt-4.1');
  assert.equal(await page.locator('#fEmbedding').evaluate(el => el.value), 'emb');
  assert.deepEqual(await page.locator('#fChatDep option').evaluateAll(o => o.map(x => x.value)), ['gpt-4.1'], 'failed deployments are hidden');
  assert.equal(await page.locator('#fEntraBox').isVisible(), true);
  assert.ok((await page.locator('#fPickNote').innerText()).includes('埋め込みの利用権限が見つかりません'));
  await page.locator('#fEmbDep').selectOption('');
  assert.equal(await page.locator('#fEmbedding').evaluate(el => el.value), '');

  await page.locator('#fPick').click();
  await page.waitForFunction(() => !document.querySelector('#fPick').disabled);
  assert.equal(calls.filter(c => c[0] === 'resources').length, 2, 'same tenant → cached');
  assert.deepEqual(errors, []);
  console.log('PASS: Azure picker lists resources, fills endpoint/deployments, switches to Entra ID, shows role/domain/partial-failure notes, caches per tenant.');
} finally {
  await browser.close();
}

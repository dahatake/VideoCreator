// Real settings UI and Fluent controls, isolated from credentials and external AI services.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import axe from 'axe-core';

const root = fileURLToPath(new URL('../src/VideoCreator.App/wwwroot/', import.meta.url));
const server = createServer(async (request, response) => {
  const file = path.resolve(root, `.${new URL(request.url, 'http://localhost').pathname.replace(/\/$/, '/index.html')}`);
  if (!file.startsWith(root)) { response.writeHead(403); response.end(); return; }
  try {
    const data = await fs.readFile(file);
    response.writeHead(200, {
      'Content-Type': { '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.html': 'text/html' }[path.extname(file)] ?? 'application/octet-stream',
    });
    response.end(data);
  } catch (error) {
    response.writeHead(error.code === 'ENOENT' ? 404 : 500);
    response.end(error.message);
  }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
let browser;
try {
  browser = await chromium.launch({ channel: 'msedge', headless: true });
  for (const language of ['ja', 'en', 'ko', 'zh-CN']) {
    const page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const settings = {
      llm_provider: 'none', secret_protection: 'dpapi', copilot_cli: 'fixture', azure_configured: false, azure_deployment: 'fixture',
      foundry: { source: 'none', auth: 'entra' },
      copilot: { source: 'none', auth: 'user', model: 'fixture' },
    };
    const writes = [];
    await page.route('**/api/**', async route => {
      const request = route.request(), url = new URL(request.url());
      if (request.method() === 'PUT' && url.pathname === '/api/settings') {
        Object.assign(settings, request.postDataJSON());
        writes.push(request.postDataJSON());
      }
      if (url.pathname === '/api/settings/ai/copilot/test') {
        writes.push(request.postDataJSON());
        return route.fulfill({ json: { ok: true, steps: [] } });
      }
      const fixtures = {
        '/api/settings': settings,
        '/api/health': { version: 'test', informational_version: 'fixture', missing_filters: [], presets: 0, llm_provider: settings.llm_provider, asr: 'none' },
        '/api/projects': [],
        '/api/settings/projects-dir': { source: 'default', path: 'C:\\Videos', projects: 0 },
        '/api/settings/ai/copilot/models': {
          account: { authenticated: true, login: 'fixture' },
          models: [
            { id: 'fixture', name: 'Fixture', reasoning_efforts: ['low', 'medium', 'high'], default_reasoning_effort: 'medium', default_context_tokens: 200000, long_context_tokens: 1000000, prices: { input: 2 }, long_context_prices: { input: 20 } },
            { id: 'fixed', name: 'Fixed', reasoning_efforts: [], default_context_tokens: 200000 },
          ],
        },
      };
      if (!(url.pathname in fixtures)) {
        errors.push(`Unexpected API request: ${request.method()} ${url.pathname}`);
        return route.fulfill({ status: 404, json: {} });
      }
      return route.fulfill({ json: fixtures[url.pathname] });
    });
    await page.goto(`http://127.0.0.1:${server.address().port}`);
    await page.locator('#ui-language').selectOption(language);
    await page.waitForFunction(value => document.documentElement.lang === value && !document.querySelector('#ui-language').disabled, language);
    await page.locator('#settings-btn').click();
    await page.locator('#aiCopilot').evaluate(element => { element.open = true; });
    await page.locator('#cEffort fluent-radio').last().waitFor();
    await page.addScriptTag({ content: axe.source });
    const audit = async () => {
      const violations = await page.evaluate(async () => (await axe.run(document.querySelector('#settings-body'))).violations);
      assert.deepEqual(violations.map(v => ({ id: v.id, nodes: v.nodes.map(n => n.html) })), [], `${language}: axe`);
    };
    await audit();

    // Read the browser's accessibility tree, not just component properties.
    const cdp = await page.context().newCDPSession(page);
    const { nodes } = await cdp.send('Accessibility.getFullAXTree');
    const radios = nodes.filter(node => !node.ignored && node.role?.value === 'radio');
    assert.equal(radios.length, await page.locator('#settings-body fluent-radio').count(), `${language}: all settings radios exposed`);
    assert.ok(radios.every(node => node.name?.value), `${language}: every radio has a name`);
    assert.equal(radios.filter(node => node.properties?.some(p => p.name === 'checked' && p.value.value === 'true')).length, 4);
    assert.ok(radios.some(node => node.properties?.some(p => p.name === 'disabled' && p.value.value === true)));
    await cdp.detach();

    const selectWithKeyboard = async (group, value, key) => {
      await page.locator(`#${group} fluent-radio[value="${value}"]`).focus();
      await page.keyboard.press(key);
    };
    await selectWithKeyboard('fAuth', 'entra', 'End');
    assert.equal(await page.locator('#fAuth').getByRole('radio', { checked: true }).getAttribute('value'), 'key');
    assert.equal(await page.locator('#fKeyBox').isVisible(), true);
    await page.keyboard.press('Home');
    assert.equal(await page.locator('#fEntraBox').isVisible(), true);
    await selectWithKeyboard('cAuth', 'user', 'ArrowDown');
    assert.equal(await page.locator('#cTokenBox').isVisible(), true);
    await page.keyboard.press('ArrowUp');
    assert.equal(await page.locator('#cTokenBox').isVisible(), false);
    await page.locator('#cLoad').click();
    await page.waitForFunction(() => document.querySelectorAll('#cModel option').length === 3);

    await selectWithKeyboard('cEffort', 'low', 'ArrowRight');
    assert.equal(await page.locator('#cEffort').getByRole('radio', { checked: true }).getAttribute('value'), 'medium');
    assert.equal(await page.locator('#cPrices dd').first().textContent(), '2');
    await selectWithKeyboard('cTier', 'default', 'ArrowRight');
    await page.waitForFunction(() => document.querySelector('#cTier fluent-radio[value="long_context"]')?.ariaChecked === 'true');
    assert.equal(await page.locator('#cTier fluent-radio[value="long_context"]').evaluate(element => document.activeElement === element), true);
    assert.equal(await page.locator('#cPrices dd').first().textContent(), '20');
    await page.locator('#cTest').click();
    await page.locator('#cResult fluent-message-bar[intent=success]').waitFor();
    assert.equal(writes.at(-1).reasoning_effort, 'medium');
    assert.equal(writes.at(-1).context_tier, 'long_context');
    await audit();
    await page.locator('#cModel').selectOption('fixed');
    assert.equal(await page.locator('#cEffort').count(), 0);
    await page.locator('#cModel').selectOption('fixture');
    await page.locator('#cEffort fluent-radio').last().waitFor();
    await audit();

    await selectWithKeyboard('llm', 'none', 'ArrowUp');
    assert.equal(await page.locator('#llm').getByRole('radio', { checked: true }).getAttribute('value'), 'copilot');
    await page.keyboard.press('ArrowUp');
    assert.equal(await page.locator('#llm').getByRole('radio', { checked: true }).getAttribute('value'), 'none');
    assert.ok(!writes.some(body => body.llm_provider === 'azure-openai'), 'disabled provider skipped');
    await page.locator('#pdirMove').evaluate(group => { group.disabled = true; });
    assert.deepEqual(await page.locator('#pdirMove fluent-radio').evaluateAll(radios => radios.map(r => r.ariaDisabled)), ['true', 'true']);
    await page.locator('#pdirMove').evaluate(group => { group.disabled = false; group.value = 'keep'; });
    assert.equal(await page.locator('#pdirMove').getByRole('radio', { checked: true }).getAttribute('value'), 'keep');
    await audit();
    assert.deepEqual(errors, []);
    await page.close();
    console.log(`PASS ${language}: axe, accessible names/states, keyboard selection, disabled skip and dynamic options`);
  }
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}

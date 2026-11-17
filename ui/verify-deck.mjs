// Uses caller-supplied recordings and a presentation file on an isolated running app; converting .pptx uses the local PowerPoint.
// Verifies: deck upload from the screen asset, slide-based scene suggestions preselected with slide thumbnails, saving the confirmed selection.
import assert from 'node:assert/strict';
import { chromium } from 'playwright';

const [base = 'http://127.0.0.1:8799', mainPath, screenPath, deckPath] = process.argv.slice(2);
assert.ok(mainPath && screenPath && deckPath, 'pass main, screen and presentation paths');
const request = async (path, method = 'GET', body) => {
  const response = await fetch(`${base}/api${path}`, {
    method, headers: { 'X-VideoCreator': '1', 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  assert.ok(response.ok, `${method} ${path}: ${await response.clone().text()}`);
  return response.json();
};
const project = await request('/projects', 'POST', { name: 'PiP deck verification' });
const main = await request(`/projects/${project.id}/assets`, 'POST', { path: mainPath });
const screen = await request(`/projects/${project.id}/assets`, 'POST', { path: screenPath });
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
const errors = [];
page.on('pageerror', e => errors.push(e.message));
try {
  await page.goto(`${base}/#/p/${project.id}`);
  await page.locator(`.role-pick[data-asset="${main.id}"] [data-role="main"]`).click();
  await page.locator(`.role-pick[data-asset="${screen.id}"] [data-role="screen"]`).click();
  const deckInput = page.locator(`input.deck-file[data-asset="${screen.id}"]`);
  await deckInput.waitFor();
  await deckInput.setInputFiles(deckPath);
  await page.locator(`.deck-del[data-asset="${screen.id}"]`).waitFor({ timeout: 600_000 });
  let saved = (await request(`/projects/${project.id}`)).assets.find(a => a.id === screen.id);
  assert.ok(saved.deck?.slides.length > 0, 'slides rendered');
  assert.ok(saved.slide_spans.length > 0, 'slides matched in the recording');
  await page.locator(`.pip-scenes[data-asset="${screen.id}"]`).click();
  const list = page.locator(`.pip-scene-list[data-asset="${screen.id}"]`);
  await list.locator('.pip-scene').first().waitFor({ timeout: 300_000 });
  const checked = list.locator('.pip-scene').filter({ has: page.locator('.pip-use:checked') });
  assert.ok(await checked.count() >= 1, 'slide scenes are preselected');
  assert.ok(await checked.locator('img[src*="/slides/"]').count() >= 1, 'slide thumbnails shown');
  const thumb = await checked.locator('img[src*="/slides/"]').first().evaluate(img => img.decode().then(() => img.naturalWidth));
  assert.ok(thumb > 0, 'slide thumbnail loads');
  await list.locator('.pip-save').click();
  await list.locator('.pip-message fluent-message-bar[intent="success"]').waitFor();
  saved = (await request(`/projects/${project.id}`)).assets.find(a => a.id === screen.id);
  assert.equal(saved.pip_scenes.length, await checked.count());
  await page.screenshot({ path: process.env.SHOT ?? 'verify-deck.png', fullPage: true });
  assert.deepEqual(errors, []);
  console.log(JSON.stringify({ project: project.id, main: main.id, screen: screen.id, slides: saved.deck.slides.length, spans: saved.slide_spans, scenes: saved.pip_scenes }));
  console.log('PASS deck upload, slide matching, preselected slide scenes with thumbnails, saved selection');
} finally {
  await browser.close();
}

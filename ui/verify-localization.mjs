import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { catalogs, setLanguage, trUi, formatDate, getLanguage, VIDEO_LANGUAGES } from '../src/VideoCreator.App/wwwroot/js/i18n.js';
import { localizePreset, localizeQa, serverText, errorMessage, libraryTrackName } from '../src/VideoCreator.App/wwwroot/js/server-i18n.js';
import { presetDisplays, qaNames, qaRecommendations, serverMessages, serverPatterns } from '../src/VideoCreator.App/wwwroot/js/server-resources.js';

const root = new URL('../src/VideoCreator.App/wwwroot/', import.meta.url);
const source = fs.readFileSync(new URL('js/app.js', root), 'utf8');
const shell = fs.readFileSync(new URL('index.html', root), 'utf8');
execFileSync(process.execPath, ['--check', fileURLToPath(new URL('js/app.js', root))]);
const placeholders = text => [...text.matchAll(/\{(\d+)\}/g)].map(m => Number(m[1])).sort().join(',');
for (const [locale, catalog] of Object.entries(catalogs)) {
  assert.deepEqual(Object.keys(catalog).sort(), Object.keys(catalogs.ja).sort(), `${locale} catalog keys`);
  for (const [key, value] of Object.entries(catalog)) {
    assert.equal(typeof value, 'string', `${locale}.${key}`);
    assert.ok(value.trim(), `${locale}.${key} empty`);
    assert.equal(placeholders(value), placeholders(catalogs.ja[key]), `${locale}.${key} placeholders`);
  }
}
for (const match of source.matchAll(/trUi\('([^']+)'/g)) assert.ok(catalogs.ja[match[1]], match[1]);
for (const match of shell.matchAll(/data-i18n(?:-aria)?="([^"]+)"/g)) assert.ok(catalogs.ja[match[1]], match[1]);
assert.ok(!source.includes('MutationObserver'));
assert.ok(!source.includes("toLocaleString('ja-JP'"));
assert.ok(!/[\u3040-\u30ff\u3400-\u9fff]/.test(source.replace(/^\s*\/\/.*$/gm, '')), 'no untranslated Japanese source literals');
assert.deepEqual(VIDEO_LANGUAGES, ['ja-JP', 'en-US', 'zh-CN', 'ko-KR']);

for (const rows of [qaNames, qaRecommendations]) {
  for (const [key, values] of Object.entries(rows)) assert.equal(values.length, 4, key);
}
for (const row of serverMessages) {
  assert.equal(row.length, 4, row[0]);
  row.forEach(value => assert.ok(value.trim(), row[0]));
}
for (const [pattern, values] of serverPatterns) {
  assert.equal(values.length, 4, pattern.toString());
  values.forEach(value => assert.equal(placeholders(value), placeholders(values[0]), pattern.toString()));
}
for (const name of fs.readdirSync(new URL('../presets/', import.meta.url)).filter(name => /^P\d+\.json$/.test(name))) {
  const preset = JSON.parse(fs.readFileSync(new URL(`../presets/${name}`, import.meta.url), 'utf8'));
  for (const locale of ['en', 'zh-CN', 'ko']) {
    assert.ok(presetDisplays[preset.id]?.[locale]?.length >= 6, `${preset.id}: display metadata for ${locale}`);
  }
}
for (const locale of ['ja', 'en', 'zh-CN', 'ko']) {
  assert.ok(setLanguage(locale));
  assert.equal(getLanguage(), locale);
  assert.equal(trUi('m121', ['<b>{0} & 日本語</b>']), catalogs[locale].m121.replace('{0}', '<b>{0} & 日本語</b>'), 'single-pass interpolation');
  assert.equal(formatDate('not-a-date'), '—');
  assert.equal(formatDate('2026-11-11T00:00:00Z', { dateStyle: 'short', timeZone: 'UTC' }), new Intl.DateTimeFormat({ ja: 'ja-JP', en: 'en-US', 'zh-CN': 'zh-CN', ko: 'ko-KR' }[locale], { dateStyle: 'short', timeZone: 'UTC' }).format(new Date('2026-11-11T00:00:00Z')));
  const raw = { id: 'P4', name: '切り抜き（長尺→30秒）', card: { opening: 'original' }, examples: [{ title: 'original', url: 'https://example.test/' }] };
  assert.equal(localizePreset(raw).name, locale === 'ja' ? raw.name : presetDisplays.P4[locale][0]);
  assert.equal(raw.card.opening, 'original', 'API preset is not mutated');
  const qa = { check_id: 'H-07', message: 'テロップの未出現語', status: 'fail', measured: '17', threshold: '0 ungrounded claims', items: [{ target: 't1', detail: '「日本語のメディア」が発話にありません' }], advice: { repair: 'ai', recommendation: 'original' } };
  const display = localizeQa(qa);
  assert.equal(display.status, 'fail');
  assert.equal(display.measured, '17');
  assert.ok(display.items[0].detail.includes('日本語のメディア'), 'quoted media words preserved');
  if (locale !== 'ja') {
    assert.ok(!display.items[0].detail.includes('が発話にありません'), 'diagnostic phrase translated without changing quoted words');
    const check = (measured, threshold = 'safe zone') => localizeQa({ check_id: 'S-15', measured, threshold });
    const loudness = check('BGM なし（下位10% 99.0 LU・12 秒分）・声のない所の BGM は声より 10.0 LU 小さい（設定 10）');
    assert.ok(!/[\u3040-\u30ff]/.test(loudness.measured), 'composite loudness diagnostic translated');
    assert.ok(loudness.measured.includes('99.0') && loudness.measured.includes('12') && loudness.measured.includes('10.0'), 'numeric evidence preserved');
    for (const diagnostic of ['t1 25字', 't2 行頭「、」', '文字が安全領域からはみ出しています（x-1–1234, y5–1900）', '本編 30.00s / 全体 35.00s (ffprobe)・計画 36.00s と不一致']) {
      assert.ok(!/[\u3040-\u30ff]/.test(check(diagnostic).measured), `known diagnostic: ${diagnostic}`);
    }
    const prices = serverText('Azure Retail Prices API (JPY, test-region, 2030-01-02): Fast Transcription ¥12.34/時間, model-v2 入力 ¥56.78/1M・出力 ¥9,012.34/1M トークン');
    for (const value of ['test-region', '2030-01-02', '12.34', 'model-v2', '56.78', '9,012.34']) assert.ok(prices.includes(value), 'price evidence preserved');
  }
  assert.equal(libraryTrackName({ source: 'user', name: '未承認' }), '未承認', 'user filenames never translated');
  assert.equal(libraryTrackName({ id: 'upbeat', source: 'library', name: 'アップテンポ（120 BPM）' }), trUi('bgm.upbeat'));
  assert.equal(serverText('user-authored <text>'), 'user-authored <text>');
  assert.ok(errorMessage({ code: 'E-RIGHTS-REQUIRED', message: '素材の権利確認が必要です' }));
  assert.equal(setLanguage('unsupported'), false);
}
setLanguage('ja');
console.log(`PASS: ${Object.keys(catalogs.ja).length} resources × 4 locales; placeholder parity, shell coverage, stable mappings, media preservation and Intl dates.`);

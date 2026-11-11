import japanese from './ja.js';
import { translated } from './translations.js';
import { additional } from './locale-extra.js';

export const UI_LANGUAGES = Object.freeze({ ja: '日本語', en: 'English', 'zh-CN': '简体中文', ko: '한국어' });
export const VIDEO_LANGUAGES = Object.freeze(['ja-JP', 'en-US', 'zh-CN', 'ko-KR']);
export const catalogs = { ja: { ...japanese }, en: {}, 'zh-CN': {}, ko: {} };
for (const row of translated.split('\n').filter(row => /^\d+\|/.test(row))) {
  const [id, ...values] = row.split('|');
  const key = `m${id.padStart(3, '0')}`;
  ['en', 'zh-CN', 'ko'].forEach((locale, i) => { catalogs[locale][key] = values[i]; });
}
for (const [key, values] of Object.entries(additional)) {
  ['ja', 'en', 'zh-CN', 'ko'].forEach((locale, i) => { catalogs[locale][key] = values[i]; });
}
let language = 'ja';
try {
  const saved = globalThis.localStorage?.getItem('vc.uiLanguage');
  if (Object.hasOwn(UI_LANGUAGES, saved)) language = saved;
} catch { /* Storage can be disabled by browser policy. */ }
export const getLanguage = () => language;
export const intlLocale = () => ({ ja: 'ja-JP', en: 'en-US', 'zh-CN': 'zh-CN', ko: 'ko-KR' })[language];
export function setLanguage(value) {
  if (!Object.hasOwn(UI_LANGUAGES, value)) return false;
  language = value;
  try { globalThis.localStorage?.setItem('vc.uiLanguage', value); } catch { /* In-memory switching still works. */ }
  if (globalThis.document) document.documentElement.lang = value;
  return true;
}
// Single-pass interpolation: placeholder-like text inside a user's value is never interpreted again.
export function trUi(key, values = []) {
  const template = catalogs[language][key] ?? catalogs.ja[key];
  if (template == null) throw new Error(`Unknown UI resource: ${key}`);
  return template.replace(/\{(\d+)\}/g, (_, i) => String(values[Number(i)] ?? ''));
}
export function formatDate(value, options) {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return '—';
  return new Intl.DateTimeFormat(intlLocale(), options ?? { dateStyle: 'short', timeStyle: 'medium' }).format(date);
}
export const formatNumber = value => new Intl.NumberFormat(intlLocale()).format(value);
export function applyShell(root = document) {
  document.documentElement.lang = language;
  root.querySelectorAll('[data-i18n]').forEach(el => { el.textContent = trUi(el.dataset.i18n); });
  root.querySelectorAll('[data-i18n-aria]').forEach(el => { el.setAttribute('aria-label', trUi(el.dataset.i18nAria)); });
}

import { getLanguage, trUi, catalogs } from './i18n.js';
import { presetDisplays, qaNames, qaRecommendations, serverMessages, serverPatterns } from './server-resources.js';

const localeIndex = () => ({ ja: 0, en: 1, 'zh-CN': 2, ko: 3 })[getLanguage()];
const pick = values => values[localeIndex()];
const exactMessages = new Map(serverMessages.map(values => [values[0], values]));
const japaneseKeys = new Map(Object.entries(catalogs.ja).map(([key, text]) => [text, key]));
const interpolate = (text, values) => text.replace(/\{(\d+)\}/g, (_, i) => String(values[Number(i)] ?? '1'));

// Only known server-owned diagnostics enter this boundary. Media text never does.
export function serverText(value, fallback) {
  if (value == null) return '';
  const text = String(value);
  if (getLanguage() === 'ja') return text;
  const exact = exactMessages.get(text);
  if (exact) return pick(exact);
  const key = japaneseKeys.get(text);
  if (key) return trUi(key);
  for (const [pattern, values] of serverPatterns) {
    const match = text.match(pattern);
    if (match) return interpolate(pick(values), match.slice(1));
  }
  const prices = text.match(/^(Azure Retail Prices API \([^)]+\)): Fast Transcription ¥([\d,.]+)\/時間, (.+) 入力 ¥([\d,.]+)\/1M・出力 ¥([\d,.]+)\/1M トークン$/);
  if (prices) return trUi('server.priceBasis', prices.slice(1));
  // Unknown diagnostics remain inspectable; do not guess at or translate embedded media text.
  return fallback ?? text;
}

export function localizePreset(preset) {
  const values = presetDisplays[preset.id]?.[getLanguage()];
  if (!values) return preset;
  const [name, genre, opening, tempo, telop, sound, ...examples] = values;
  return {
    ...preset, name, genre,
    card: preset.card ? { ...preset.card, opening, tempo, telop, sound } : preset.card,
    unavailable_reason: preset.unavailable_reason ? serverText(preset.unavailable_reason, trUi('m021')) : preset.unavailable_reason,
    examples: (preset.examples ?? []).map((x, i) => ({ ...x, title: examples[i] ?? x.title })),
  };
}
export function localizeQa(q) {
  if (getLanguage() === 'ja') return q;
  return {
    ...q,
    message: qaNames[q.check_id] ? pick(qaNames[q.check_id]) : serverText(q.message),
    measured: ['H-06', 'H-07', 'S-07'].includes(q.check_id) ? q.measured : diagnosticText(q.measured),
    threshold: diagnosticText(q.threshold),
    stage: q.stage && catalogs.ja[`stage.${q.stage}`] ? trUi(`stage.${q.stage}`) : q.stage,
    items: q.items?.map(item => ({ ...item, detail: diagnosticText(item.detail), note: diagnosticText(item.note) })),
    advice: q.advice ? {
      ...q.advice,
      recommendation: qaRecommendations[q.check_id] ? pick(qaRecommendations[q.check_id]) : serverText(q.advice.recommendation),
      remaining: diagnosticText(q.advice.remaining),
    } : q.advice,
  };
}
function diagnosticText(value) {
  const text = String(value ?? '');
  if (getLanguage() === 'ja') return text;
  const loudness = text.match(/^(.+)（下位10% ([\d.]+) LU・([\d.]+) 秒分）(.*)$/);
  if (loudness) return serverText(`${serverText(loudness[1])}（下位10% ${loudness[2]} LU・${loudness[3]} 秒分）${serverText(loudness[4])}`);
  const discrepancy = text.match(/^(.+)(・計画 [\d.]+s と不一致)$/);
  if (discrepancy) return serverText(discrepancy[1]) + serverText(discrepancy[2]);
  const faceProtection = text.match(/^(.+)・(顔保護のため演出テロップ \d+ 件を非表示)$/);
  if (faceProtection) return serverText(faceProtection[1]) + ' · ' + serverText(faceProtection[2]);
  const direct = serverText(text);
  if (direct !== text) return direct;
  // These delimiters and t*/i* prefixes are diagnostic structure, not general text-replacement rules.
  if (text.includes(' / ')) return text.split(' / ').map(part => serverText(part)).join(' / ');
  if (text.includes(', ')) return text.split(', ').map(part => diagnosticText(part)).join(', ');
  const prefix = text.match(/^([ti][\w-]*)(: | )(.+)$/);
  if (prefix) return prefix[1] + prefix[2] + serverText(prefix[3]);
  return text;
}
export function jobMessage(job) {
  const state = job.stage ?? job.status ?? job.state;
  const labels = {
    queued: 'server.queued', ingesting: 'm010', transcribing: 'm011', analyzing: 'm012',
    selecting: 'm013', planning: 'm014', previewing: 'm015',
    ready: 'm378', done: 'm378', failed: 'm379', cancelled: 'm380', rendering: 'm462',
  };
  return serverText(job.message, labels[state] ? trUi(labels[state]) : undefined);
}
export function errorMessage(error) {
  if (getLanguage() === 'ja') return error.message ?? trUi('server.error');
  const keys = {
    'E-VALIDATION': 'server.validation', 'E-RIGHTS-REQUIRED': 'm016', 'E-ING-UNSUPPORTED': 'm017',
    'E-ASR-PROVIDER': 'm018', 'E-SOL-INFEASIBLE': 'm019', 'E-QA-HARDFAIL': 'm020',
    'E-PRESET-UNAVAILABLE': 'm021', 'E-RND-FFMPEG': 'm022', 'E-PROJECT-BUSY': 'server.busy',
    'E-FILE-IN-USE': 'm024', 'E-PLAN-STALE': 'm719', CANCELLED: 'm380',
  };
  return serverText(error.message, keys[error.code] ? trUi(keys[error.code]) : trUi('server.error')) || trUi('server.error');
}
export function libraryTrackName(track) {
  // A user track's name is its filename; never localize it.
  if (track.source !== 'library') return track.name;
  return catalogs.ja[`bgm.${track.id}`] ? trUi(`bgm.${track.id}`) : serverText(track.name);
}

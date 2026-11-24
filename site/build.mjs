// Builds the GitHub Pages site from the repository documentation (the single source of truth).
// Usage: node build.mjs   (env: SITE_URL, GOOGLE_SITE_VERIFICATION, BING_SITE_VERIFICATION)
import { cpSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { Marked } from 'marked';
import { languages, t } from './src/i18n.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');
const out = path.join(here, 'dist');
const siteUrl = (process.env.SITE_URL || 'https://dahatake.github.io/VideoCreator').replace(/\/+$/, '');
const basePath = new URL(siteUrl + '/').pathname;
const repoUrl = 'https://github.com/dahatake/VideoCreator';
const branch = 'main';
const version = readFileSync(path.join(root, 'Directory.Build.props'), 'utf8').match(/<VersionPrefix>([^<]+)</)?.[1] ?? '';
const problems = [];
const warn = message => problems.push(message);

// ---------- helpers ----------
const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const posix = p => p.split(path.sep).join('/');
const abs = url => `${siteUrl}/${url}`;
const rel = (from, to) => (from ? '../'.repeat(from.split('/').filter(Boolean).length) : './') + to;
const ldJson = data => `<script type="application/ld+json">${JSON.stringify(data).replace(/</g, '\\u003c')}</script>`;
const slugify = text => text.replace(/\[([^\]]*)\]\([^)]*\)/g, '$1').replace(/<[^>]*>/g, '')
  .toLowerCase().replace(/[^\p{L}\p{N}\p{M}_ -]/gu, '').replace(/ /g, '-');
const plain = text => text.replace(/!\[[^\]]*\]\([^)]*\)/g, '').replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
  .replace(/<[^>]*>/g, '').replace(/[*_`~]/g, '').replace(/\s+/g, ' ').trim();
const cjk = lang => lang !== 'en';
const truncate = (text, lang) => {
  const max = cjk(lang) ? 118 : 158;
  if (text.length <= max) return text;
  let cut = text.slice(0, max - 1);
  if (!cjk(lang)) cut = cut.replace(/\s+\S*$/, '');
  return cut.replace(/[、。,.;:（(]+$/, '') + '…';
};
const gitDates = new Map();
const lastModified = file => {
  if (!gitDates.has(file)) {
    let date = '';
    try { date = execFileSync('git', ['log', '-1', '--format=%cI', '--', file], { cwd: root, encoding: 'utf8' }).trim(); } catch { /* not a git checkout */ }
    gitDates.set(file, date || new Date(statSync(path.join(root, file)).mtime).toISOString());
  }
  return gitDates.get(file);
};
const latest = dates => dates.filter(Boolean).sort().at(-1);
const write = (file, content) => {
  const target = path.join(out, file);
  mkdirSync(path.dirname(target), { recursive: true });
  writeFileSync(target, content);
};
const imageSize = file => {
  const data = readFileSync(file);
  if (file.endsWith('.png')) return { width: data.readUInt32BE(16), height: data.readUInt32BE(20) };
  if (file.endsWith('.svg')) {
    const head = data.toString('utf8', 0, 4000).match(/<svg\b[^>]*>/)?.[0] ?? '';
    const num = name => Number.parseFloat(head.match(new RegExp(`\\s${name}="([\\d.]+)(?:px)?"`))?.[1]);
    const box = head.match(/viewBox="[\d.\s-]*?([\d.]+)\s+([\d.]+)"/);
    const width = num('width') || Number(box?.[1]);
    const height = num('height') || Number(box?.[2]);
    if (width && height) return { width: Math.round(width), height: Math.round(height) };
  }
  return null;
};

// ---------- page registry ----------
const groups = {
  users: ['start', 'quickstart', 'patterns', 'screens', 'troubleshooting', 'guide'],
  engineers: ['technical-reference', 'configuration-api', 'azure-access', 'overview', 'verification'],
  background: ['prd', 'implementation', 'analytics', 'documentation-brief'],
};
const guideFiles = {
  quickstart: 'quickstart.md', screens: 'screens.md', troubleshooting: 'troubleshooting.md', guide: 'README.md',
  'technical-reference': 'technical-reference.md', 'configuration-api': 'configuration-api.md', 'azure-access': 'azure-access.md',
  verification: 'verification.md', overview: 'overview.md', 'documentation-brief': 'documentation-brief.md',
};
const docFiles = { prd: 'PRD.md', implementation: 'IMPLEMENTATION.md', analytics: 'VideoCreator-AnalyticsReport.md' };
const pagePath = id => ({ start: 'guide/start/', guide: 'guide/', patterns: 'patterns/', users: 'users/', engineers: 'engineers/', home: '' })[id]
  ?? (docFiles[id] ? `docs/${id}/` : `guide/${id}/`);
const sourceOf = (id, lang) => {
  if (id === 'start') return lang === 'ja' ? 'README.md' : `users-guide/${lang}/start.md`;
  if (guideFiles[id]) return lang === 'ja' ? `users-guide/${guideFiles[id]}` : `users-guide/${lang}/${guideFiles[id]}`;
  if (docFiles[id]) return lang === 'ja' ? `docs/${docFiles[id]}` : `users-guide/${lang}/docs/${docFiles[id]}`;
  return null;
};
const groupOf = id => Object.keys(groups).find(g => groups[g].includes(id));
const docIds = Object.values(groups).flat().filter(id => id !== 'patterns');
const urlOf = (id, lang) => languages.find(l => l.code === lang).prefix + pagePath(id);

const pageBySource = new Map();
for (const { code } of languages) for (const id of docIds) {
  const src = sourceOf(id, code);
  if (existsSync(path.join(root, src))) pageBySource.set(src, { id, lang: code });
  else warn(`missing source ${src}; ${code}/${id} is not generated`);
}
const exists = (id, lang) => id === 'patterns' || ['home', 'users', 'engineers'].includes(id) || pageBySource.has(sourceOf(id, lang));

// ---------- markdown ----------
const media = new Map();
function resolveTarget(href, srcFile, fromUrl, isImage) {
  if (!href || /^(?:[a-z][a-z\d+.-]*:|\/\/)/i.test(href)) return href;
  if (href.startsWith('#')) return href;
  const [rawPath, hash = ''] = href.split('#');
  let decoded = rawPath;
  try { decoded = decodeURI(rawPath); } catch { /* keep raw */ }
  const repoPath = path.posix.normalize(path.posix.join(path.posix.dirname(srcFile), decoded));
  const suffix = hash ? `#${hash}` : '';
  const page = pageBySource.get(repoPath);
  if (page) return rel(fromUrl, urlOf(page.id, page.lang)) + suffix;
  const file = path.join(root, repoPath);
  if (repoPath.startsWith('..') || !existsSync(file)) {
    warn(`${srcFile}: unresolved link ${href}`);
    return href;
  }
  if (statSync(file).isDirectory()) return `${repoUrl}/tree/${branch}/${encodeURI(repoPath)}${suffix}`;
  if (isImage || /\.(?:svg|png|jpe?g|gif|webp)$/i.test(repoPath)) {
    const target = `media/${repoPath}`;
    if (!media.has(target)) media.set(target, { file, size: imageSize(file) });
    return rel(fromUrl, encodeURI(target));
  }
  return `${repoUrl}/blob/${branch}/${encodeURI(repoPath)}${suffix}`;
}

function createRenderer(ctx) {
  const marked = new Marked({ gfm: true });
  marked.use({
    renderer: {
      heading({ tokens, depth, text }) {
        const base = slugify(text);
        const count = ctx.seen.get(base) ?? 0;
        ctx.seen.set(base, count + 1);
        const id = count ? `${base}-${count}` : base;
        const inner = this.parser.parseInline(tokens);
        ctx.headings.push({ depth, id, text: plain(text) });
        return `<h${depth} id="${esc(id)}">${inner}<a class="anchor" href="#${esc(id)}" aria-label="${esc(plain(text))}">#</a></h${depth}>\n`;
      },
      code({ text, lang }) {
        if (lang === 'mermaid') { ctx.mermaid = true; return `<div class="mermaid-wrap"><pre class="mermaid">${esc(text)}</pre></div>\n`; }
        const language = (lang || '').split(/\s/)[0];
        return `<div class="code"><pre tabindex="0"${language ? ` data-lang="${esc(language)}"` : ''}><code${language ? ` class="language-${esc(language)}"` : ''}>${esc(text)}</code></pre></div>\n`;
      },
      image({ href, title, text }) {
        const info = media.get(decodeURI(href.replace(/^(?:\.\.\/)+|^\.\//, '')));
        const size = info?.size ? ` width="${info.size.width}" height="${info.size.height}"` : '';
        return `<a class="zoom" href="${esc(href)}" aria-label="${esc(t[ctx.lang].ui.openImage)}: ${esc(text)}"><img src="${esc(href)}" alt="${esc(text)}"${title ? ` title="${esc(title)}"` : ''}${size} loading="lazy" decoding="async"></a>`;
      },
    },
  });
  // walkTokens only runs inside parse(); we lex and parse separately, so links are rewritten here.
  ctx.lex = markdown => {
    const tokens = marked.lexer(markdown);
    marked.walkTokens(tokens, token => {
      if (token.type === 'link' || token.type === 'image') token.href = resolveTarget(token.href, ctx.src, ctx.url, token.type === 'image');
      if (token.type === 'html' || token.type === 'tag') {
        token.text = token.text.replace(/\b(href|src)=(["'])([^"']+)\2/gi,
          (_, attr, quote, value) => `${attr}=${quote}${resolveTarget(value, ctx.src, ctx.url, attr.toLowerCase() === 'src')}${quote}`);
      }
    });
    return tokens;
  };
  return marked;
}

function prepare(markdown) {
  return markdown.replace(/<!--\s*language-nav\s*-->[\s\S]*?<!--\s*\/language-nav\s*-->/g, '');
}

function loadDoc(id, lang) {
  const src = sourceOf(id, lang);
  const url = urlOf(id, lang);
  const ctx = { src, url, lang, seen: new Map(), headings: [], mermaid: false };
  const marked = createRenderer(ctx);
  const tokens = ctx.lex(prepare(readFileSync(path.join(root, src), 'utf8')));
  const h1Index = tokens.findIndex(token => token.type === 'heading' && token.depth === 1);
  const h1Text = h1Index >= 0 ? plain(tokens[h1Index].text) : '';
  // A bare product name (README/overview) or an untranslated h1 would duplicate titles across pages and languages.
  const title = !h1Text || h1Text === 'VideoCreator' ? t[lang].pages[id]
    : lang !== 'en' && !/[^\x00-\x7F—–]/.test(h1Text) ? `${t[lang].pages[id]} — ${h1Text.replace(/^VideoCreator\s*[—-]\s*/, '')}` : h1Text;
  if (h1Index >= 0) {
    const h1 = slugify(tokens[h1Index].text);
    ctx.h1Slug = h1;
    ctx.seen.set(h1, 1);
    tokens.splice(h1Index, 1);
    // Drop the per-file breadcrumb / language lines; the site renders its own navigation.
    let i = h1Index;
    while (i < tokens.length) {
      const token = tokens[i];
      const linkOnly = token.type === 'paragraph' && token.text.replace(/!?\[[^\]]*\]\([^)]*\)/g, '').replace(/[\s/|｜]/g, '') === '';
      if (token.type === 'space' || linkOnly) tokens.splice(i, 1);
      else break;
    }
  }
  const minimum = cjk(lang) ? 70 : 120;
  let summary = '';
  for (const token of tokens) {
    if (summary.length >= minimum) break;
    if (token.type === 'paragraph' || token.type === 'blockquote') {
      const piece = plain(token.text);
      if (piece.length > 20) summary += (summary ? ' ' : '') + piece;
    }
  }
  const description = truncate(summary || t[lang].pages[id], lang);
  const text = plain(tokens.map(token => token.raw).join(' '));
  const readMinutes = Math.max(1, Math.round(cjk(lang) ? text.length / 500 : text.split(' ').length / 220));
  return { id, lang, src, url, title, description, tokens, ctx, marked, readMinutes, modified: lastModified(src) };
}

function renderBody(doc) {
  doc.ctx.seen = new Map(doc.ctx.h1Slug ? [[doc.ctx.h1Slug, 1]] : []);
  doc.ctx.headings = [];
  let html = doc.marked.parser(doc.tokens);
  html = html.replace(/<table>/g, '<div class="table-wrap" tabindex="0" role="region"><table>').replace(/<\/table>/g, '</table></div>')
    .replace(/<a href="(https?:\/\/[^"]+)"/g, '<a href="$1" rel="noopener"');
  return html;
}

// ---------- data ----------
const docs = new Map();
for (const { code } of languages) for (const id of docIds) if (exists(id, code)) docs.set(`${code}/${id}`, loadDoc(id, code));
for (const doc of docs.values()) {
  doc.html = renderBody(doc);
  doc.headings = doc.ctx.headings;
}

const headingAnchor = (id, lang, spec) => {
  if (!spec) return '';
  const target = docs.get(`${lang}/${id}`);
  const ja = docs.get(`ja/${id}`);
  if (!target || !ja) return '';
  let heading;
  if (typeof spec === 'string') {
    const index = ja.headings.findIndex(h => h.text === spec);
    if (index < 0) throw new Error(`heading "${spec}" not found in ja/${id}`);
    heading = target.headings[index];
  } else {
    heading = target.headings.find(h => new RegExp(spec[lang]).test(h.text));
  }
  if (!heading) throw new Error(`heading ${JSON.stringify(spec)} not found in ${lang}/${id}`);
  return `#${heading.id}`;
};

const { presetDisplays } = await import(pathToFileURL(path.join(root, 'src/VideoCreator.App/wwwroot/js/server-resources.js')).href);
const presets = readdirSync(path.join(root, 'presets')).filter(f => /^P\d+\.json$/.test(f))
  .map(f => ({ file: `presets/${f}`, ...JSON.parse(readFileSync(path.join(root, 'presets', f), 'utf8')) }))
  .filter(p => p.available !== false)
  .sort((a, b) => Number(a.id.slice(1)) - Number(b.id.slice(1)));
const presetText = (preset, lang) => {
  if (lang === 'ja') {
    return { name: preset.name, genre: preset.genre, opening: preset.card?.opening, tempo: preset.card?.tempo, telop: preset.card?.telop, sound: preset.card?.sound,
      example: preset.examples?.[0]?.title };
  }
  const row = presetDisplays[preset.id]?.[lang];
  if (!row) throw new Error(`presetDisplays.${preset.id}.${lang} is missing`);
  return { name: row[0], genre: row[1], opening: row[2], tempo: row[3], telop: row[4], sound: row[5], example: row[6] ?? null };
};
const presetsModified = latest([...presets.map(p => lastModified(p.file)), lastModified('src/VideoCreator.App/wwwroot/js/server-resources.js')]);

// ---------- layout ----------
const icons = {
  logo: '<svg viewBox="0 0 32 32" aria-hidden="true"><defs><linearGradient id="lg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#7c5cff"/><stop offset=".55" stop-color="#3aa0ff"/><stop offset="1" stop-color="#2fe3c4"/></linearGradient></defs><rect x="2" y="2" width="28" height="28" rx="8" fill="url(#lg)"/><path d="M12 9.5v9l7.5-4.5z" fill="#fff"/><rect x="8" y="21.5" width="16" height="2.4" rx="1.2" fill="#fff" opacity=".9"/><rect x="11" y="25" width="10" height="2" rx="1" fill="#fff" opacity=".6"/></svg>',
  search: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="10.5" cy="10.5" r="6.5" fill="none" stroke="currentColor" stroke-width="2"/><path d="m15.5 15.5 5 5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>',
  globe: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" stroke-width="1.8"/><path d="M3 12h18M12 3c2.6 2.6 3.8 5.6 3.8 9s-1.2 6.4-3.8 9c-2.6-2.6-3.8-5.6-3.8-9S9.4 5.6 12 3z" fill="none" stroke="currentColor" stroke-width="1.8"/></svg>',
  theme: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3a9 9 0 1 0 9 9A7 7 0 0 1 12 3z" fill="currentColor"/></svg>',
  github: '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M12 2a10 10 0 0 0-3.16 19.49c.5.09.68-.22.68-.48v-1.7c-2.78.6-3.37-1.34-3.37-1.34-.45-1.15-1.11-1.46-1.11-1.46-.91-.62.07-.61.07-.61 1 .07 1.53 1.03 1.53 1.03.9 1.52 2.34 1.08 2.91.83.09-.65.35-1.09.63-1.34-2.22-.25-4.55-1.11-4.55-4.94 0-1.09.39-1.98 1.03-2.68-.1-.25-.45-1.27.1-2.65 0 0 .84-.27 2.75 1.02a9.5 9.5 0 0 1 5 0c1.91-1.29 2.75-1.02 2.75-1.02.55 1.38.2 2.4.1 2.65.64.7 1.03 1.59 1.03 2.68 0 3.84-2.34 4.68-4.57 4.93.36.31.68.92.68 1.85v2.74c0 .27.18.58.69.48A10 10 0 0 0 12 2z"/></svg>',
  menu: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>',
  arrow: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h13m-5-6 6 6-6 6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  mic: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="3" width="6" height="11" rx="3" fill="none" stroke="currentColor" stroke-width="1.8"/><path d="M5.5 11a6.5 6.5 0 0 0 13 0M12 17.5V21" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>',
  sparkle: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9zM18.5 15.5l.8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8z" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"/></svg>',
  cut: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="6" cy="6.5" r="2.8" fill="none" stroke="currentColor" stroke-width="1.8"/><circle cx="6" cy="17.5" r="2.8" fill="none" stroke="currentColor" stroke-width="1.8"/><path d="M8.3 8.2 20 18M8.3 15.8 20 6" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>',
  text: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="4" width="18" height="16" rx="3" fill="none" stroke="currentColor" stroke-width="1.8"/><path d="M7 14h10M8.5 17h7" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>',
  shield: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3l7.5 3v5.5c0 4.6-3.2 8.1-7.5 9.5-4.3-1.4-7.5-4.9-7.5-9.5V6z" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/><path d="m8.5 12 2.4 2.4 4.6-4.8" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  file: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 3h8l4 4v14H6z" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/><path d="m10.5 11 4 2.5-4 2.5z" fill="currentColor"/></svg>',
  stack: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="5" width="7" height="14" rx="2" fill="none" stroke="currentColor" stroke-width="1.8"/><rect x="14" y="5" width="7" height="14" rx="2" fill="none" stroke="currentColor" stroke-width="1.8"/><path d="M10 12h4" stroke="currentColor" stroke-width="1.8"/></svg>',
  sync: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 12a8 8 0 0 1 13.7-5.6L20 8.5M20 4v4.5h-4.5M20 12a8 8 0 0 1-13.7 5.6L4 15.5M4 20v-4.5h4.5" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  pc: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="4" width="18" height="12" rx="2" fill="none" stroke="currentColor" stroke-width="1.8"/><path d="M9 20h6M12 16v4" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>',
  cloud: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 18a4.5 4.5 0 0 1-.6-9 6 6 0 0 1 11.4 1.6A3.8 3.8 0 0 1 17.5 18z" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/></svg>',
  send: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 12 20 4l-4 16-4-6.5z" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/></svg>',
  user: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="8" r="4" fill="none" stroke="currentColor" stroke-width="1.8"/><path d="M4 21a8 8 0 0 1 16 0" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>',
  code: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m8 7-5 5 5 5M16 7l5 5-5 5M14 4l-4 16" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>',
};
const featureIcons = ['mic', 'sparkle', 'cut', 'text', 'shield', 'file', 'stack', 'sync'];

function alternates(id) {
  return languages.filter(l => exists(id, l.code)).map(l => ({ ...l, url: urlOf(id, l.code) }));
}

function head({ lang, url, title, description, id, type = 'website', modified, noindex = false, jsonLd = [], extraHead = '' }) {
  const L = languages.find(l => l.code === lang);
  const alts = noindex ? [] : alternates(id);
  const xDefault = alts.find(a => a.code === 'en') ?? alts[0];
  const verify = url === '' ? [
    process.env.GOOGLE_SITE_VERIFICATION && `<meta name="google-site-verification" content="${esc(process.env.GOOGLE_SITE_VERIFICATION)}">`,
    process.env.BING_SITE_VERIFICATION && `<meta name="msvalidate.01" content="${esc(process.env.BING_SITE_VERIFICATION)}">`,
  ].filter(Boolean).join('\n') : '';
  const fullTitle = title.includes('VideoCreator') ? title : `${title} | VideoCreator`;
  const image = abs(`assets/og/og-${lang}.png`);
  return `<!doctype html>
<html lang="${lang}" data-root="${esc(noindex ? basePath : rel(url, ''))}" data-lang="${lang}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(fullTitle)}</title>
<meta name="description" content="${esc(description)}">
${noindex ? '<meta name="robots" content="noindex">' : `<link rel="canonical" href="${esc(abs(url))}">`}
${alts.map(a => `<link rel="alternate" hreflang="${a.code}" href="${esc(abs(a.url))}">`).join('\n')}
${xDefault ? `<link rel="alternate" hreflang="x-default" href="${esc(abs(xDefault.url))}">` : ''}
<meta name="theme-color" content="#0b0d1a">
<meta name="color-scheme" content="dark light">
<meta property="og:type" content="${type}">
<meta property="og:site_name" content="VideoCreator">
<meta property="og:title" content="${esc(fullTitle)}">
<meta property="og:description" content="${esc(description)}">
<meta property="og:url" content="${esc(abs(url))}">
<meta property="og:image" content="${esc(image)}">
<meta property="og:image:width" content="1200">
<meta property="og:image:height" content="630">
<meta property="og:image:alt" content="${esc(t[lang].home.title)}">
<meta property="og:locale" content="${L.og}">
${languages.filter(l => l.code !== lang && alts.some(a => a.code === l.code)).map(l => `<meta property="og:locale:alternate" content="${l.og}">`).join('\n')}
${modified && type === 'article' ? `<meta property="article:modified_time" content="${esc(modified)}">` : ''}
<meta name="twitter:card" content="summary_large_image">
${verify}
<link rel="icon" href="${noindex ? basePath : rel(url, '')}assets/favicon.svg" type="image/svg+xml">
<link rel="apple-touch-icon" href="${noindex ? basePath : rel(url, '')}assets/apple-touch-icon.png">
<link rel="stylesheet" href="${noindex ? basePath : rel(url, '')}assets/site.css">
<script>(()=>{try{const s=localStorage.getItem('vc-theme');if(s)document.documentElement.dataset.theme=s;}catch{}document.documentElement.classList.add('js');})();</script>
<script src="${noindex ? basePath : rel(url, '')}assets/site.js" defer></script>
${jsonLd.map(ldJson).join('\n')}
${extraHead}
</head>`;
}

function topbar(lang, url, id) {
  const T = t[lang];
  const r = rel(url, '');
  const L = languages.find(l => l.code === lang);
  const alts = alternates(id);
  const nav = [['users', T.nav.users], ['engineers', T.nav.engineers], ['patterns', T.nav.patterns]];
  return `<a class="skip" href="#main">${esc(T.ui.skip)}</a>
<header class="topbar">
  <a class="brand" href="${r}${L.prefix}" aria-label="VideoCreator ${esc(T.pages.home)}">${icons.logo}<span>VideoCreator</span></a>
  <nav class="topnav" aria-label="${esc(T.ui.menu)}">
    ${nav.map(([pid, label]) => `<a href="${r}${urlOf(pid, lang)}"${pid === id || groupOf(id) === pid || (pid === 'engineers' && groupOf(id) === 'background') ? ' aria-current="page"' : ''}>${esc(label)}</a>`).join('\n    ')}
  </nav>
  <div class="tools">
    <button class="icon-btn search-open" type="button" title="${esc(T.ui.searchHint)}">${icons.search}<span class="sr">${esc(T.ui.search)}</span><kbd aria-hidden="true">Ctrl K</kbd></button>
    <details class="lang-menu">
      <summary class="icon-btn" title="${esc(T.ui.language)}">${icons.globe}<span><span class="sr">${esc(T.ui.language)}: </span>${esc(L.name)}</span></summary>
      <ul>
        ${alts.map(a => `<li><a href="${r}${a.url}" hreflang="${a.code}" lang="${a.code}" data-lang-switch="${a.code}"${a.code === lang ? ' aria-current="true"' : ''}>${esc(a.name)}</a></li>`).join('\n        ')}
      </ul>
    </details>
    <button class="icon-btn theme-toggle" type="button" aria-label="${esc(T.ui.theme)}" title="${esc(T.ui.theme)}">${icons.theme}</button>
    <a class="icon-btn" href="${repoUrl}" rel="noopener" aria-label="${esc(T.ui.repo)}">${icons.github}</a>
    <button class="icon-btn menu-toggle" type="button" aria-label="${esc(T.ui.menu)}" aria-expanded="false" aria-controls="sidebar">${icons.menu}</button>
  </div>
</header>
<div class="lang-suggest" hidden data-suggest='${esc(JSON.stringify(Object.fromEntries(alts.filter(a => a.code !== lang).map(a => [a.code, { url: r + a.url, text: t[a.code].ui.readIn }]))))}'></div>`;
}

function sidebar(lang, url, id) {
  const T = t[lang];
  const r = rel(url, '');
  const link = pid => exists(pid, lang) ? `<li><a href="${r}${urlOf(pid, lang)}"${pid === id ? ' aria-current="page"' : ''}>${esc(T.pages[pid])}</a></li>` : '';
  return `<nav class="sidebar" id="sidebar" aria-label="${esc(T.ui.menu)}">
  <a class="side-hub" href="${r}${urlOf('users', lang)}"${id === 'users' ? ' aria-current="page"' : ''}>${icons.user}${esc(T.pages.users)}</a>
  <ul>${groups.users.map(link).join('')}</ul>
  <a class="side-hub" href="${r}${urlOf('engineers', lang)}"${id === 'engineers' ? ' aria-current="page"' : ''}>${icons.code}${esc(T.pages.engineers)}</a>
  <ul>${groups.engineers.map(link).join('')}</ul>
  <p class="side-group">${esc(T.groups.background)}</p>
  <ul>${groups.background.map(link).join('')}</ul>
</nav>`;
}

function footer(lang, url) {
  const T = t[lang];
  const r = rel(url, '');
  return `<footer class="footer">
  <div class="footer-inner">
    <a class="brand" href="${r}${languages.find(l => l.code === lang).prefix}">${icons.logo.replace('id="lg"', 'id="lgf"').replace('url(#lg)', 'url(#lgf)')}<span>VideoCreator</span></a>
    <p>${esc(T.ui.footer)}</p>
    <ul>
      <li><a href="${repoUrl}" rel="noopener">${esc(T.ui.repo)}</a></li>
      <li><a href="${repoUrl}/releases" rel="noopener">${esc(T.ui.releases)}</a></li>
      <li><a href="${repoUrl}/blob/${branch}/LICENSE" rel="noopener">${esc(T.ui.license)}</a></li>
      <li><a href="${r}${urlOf('users', lang)}">${esc(T.pages.users)}</a></li>
      <li><a href="${r}${urlOf('engineers', lang)}">${esc(T.pages.engineers)}</a></li>
    </ul>
    <p class="fine">${version ? `v${esc(version)} · ` : ''}MIT License</p>
  </div>
</footer>
<dialog class="search-dialog" aria-label="${esc(T.ui.search)}">
  <form method="dialog" class="search-box" role="search">
    ${icons.search}
    <input type="search" placeholder="${esc(T.ui.searchPlaceholder)}" aria-label="${esc(T.ui.search)}" autocomplete="off" data-empty="${esc(T.ui.noResults)}">
    <button class="icon-btn" value="close" aria-label="${esc(T.ui.close)}">Esc</button>
  </form>
  <ul class="search-results" role="listbox" aria-label="${esc(T.ui.search)}"></ul>
</dialog>
<div class="i18n" hidden data-copy="${esc(T.ui.copy)}" data-copied="${esc(T.ui.copied)}" data-motion="${esc(T.ui.motion)}" data-motion-on="${esc(T.ui.motionOn)}"></div>`;
}

const breadcrumbLd = (lang, trail) => ({
  '@context': 'https://schema.org', '@type': 'BreadcrumbList',
  itemListElement: trail.map((item, i) => ({ '@type': 'ListItem', position: i + 1, name: item.name, item: abs(item.url) })),
});
const hubOf = id => (groupOf(id) === 'users' ? 'users' : 'engineers');
const trailOf = (id, lang) => {
  const T = t[lang];
  const trail = [{ name: T.pages.home, url: urlOf('home', lang) }];
  if (!['home', 'users', 'engineers'].includes(id)) trail.push({ name: T.pages[hubOf(id)], url: urlOf(hubOf(id), lang) });
  if (id !== 'home') trail.push({ name: T.pages[id], url: urlOf(id, lang) });
  return trail;
};
const crumbs = (id, lang, url) => `<nav class="crumbs" aria-label="${esc(t[lang].ui.breadcrumb)}"><ol>${trailOf(id, lang)
  .map((c, i, all) => i === all.length - 1 ? `<li aria-current="page">${esc(c.name)}</li>` : `<li><a href="${rel(url, c.url)}">${esc(c.name)}</a></li>`).join('')}</ol></nav>`;

const order = [...groups.users, ...groups.engineers, ...groups.background];
const formatDate = (iso, lang) => new Intl.DateTimeFormat(lang, { year: 'numeric', month: 'long', day: 'numeric', timeZone: 'Asia/Tokyo' }).format(new Date(iso));

function docPage(doc) {
  const { id, lang, url } = doc;
  const T = t[lang];
  const available = order.filter(pid => exists(pid, lang));
  const index = available.indexOf(id);
  const prev = available[index - 1];
  const next = available[index + 1];
  const toc = doc.headings.filter(h => h.depth === 2 || h.depth === 3);
  const jsonLd = [
    { '@context': 'https://schema.org', '@type': 'TechArticle', headline: doc.title, description: doc.description, inLanguage: lang,
      dateModified: doc.modified, url: abs(url), image: abs(`assets/og/og-${lang}.png`),
      isPartOf: { '@type': 'WebSite', name: 'VideoCreator', url: abs(languages.find(l => l.code === lang).prefix) },
      about: { '@type': 'SoftwareApplication', name: 'VideoCreator' } },
    breadcrumbLd(lang, trailOf(id, lang)),
  ];
  return `${head({ lang, url, id, title: doc.title, description: doc.description, type: 'article', modified: doc.modified, jsonLd })}
<body class="page-doc">
<div class="aurora" aria-hidden="true"><i></i><i></i><i></i></div>
${topbar(lang, url, id)}
<div class="read-progress" aria-hidden="true"><span></span></div>
<div class="doc-layout">
${sidebar(lang, url, id)}
<main id="main" class="doc-main">
  ${crumbs(id, lang, url)}
  <header class="doc-hero">
    <p class="eyebrow">${esc(T.groups[groupOf(id)])}</p>
    <h1>${esc(doc.title)}</h1>
    <p class="doc-meta"><span>${esc(T.ui.updated)}: <time datetime="${esc(doc.modified)}">${esc(formatDate(doc.modified, lang))}</time></span><span>${lang === 'en' ? `${doc.readMinutes} ${esc(T.ui.minutes)}` : `${doc.readMinutes}${esc(T.ui.minutes)}`}</span><a href="${repoUrl}/blob/${branch}/${encodeURI(doc.src)}" rel="noopener">${esc(T.ui.source)}</a></p>
  </header>
  <article class="prose">
${doc.html}
  </article>
  <nav class="pager" aria-label="${esc(T.ui.prev)} / ${esc(T.ui.next)}">
    ${prev ? `<a class="glow" rel="prev" href="${rel(url, urlOf(prev, lang))}"><small>${esc(T.ui.prev)}</small><span>${esc(T.pages[prev])}</span></a>` : '<span></span>'}
    ${next ? `<a class="glow next" rel="next" href="${rel(url, urlOf(next, lang))}"><small>${esc(T.ui.next)}</small><span>${esc(T.pages[next])}</span></a>` : '<span></span>'}
  </nav>
  <p class="edit-link"><a href="${repoUrl}/edit/${branch}/${encodeURI(doc.src)}" rel="noopener">${esc(T.ui.edit)}</a></p>
</main>
${toc.length ? `<aside class="toc" aria-label="${esc(T.ui.toc)}"><p>${esc(T.ui.toc)}</p><ol>${toc.map(h => `<li class="d${h.depth}"><a href="#${esc(h.id)}">${esc(h.text)}</a></li>`).join('')}</ol><a class="to-top" href="#main">↑ ${esc(T.ui.top)}</a></aside>` : ''}
</div>
${footer(lang, url)}
</body>
</html>
`;
}

// ---------- shared widgets ----------
function durationChart(lang, url, compact = false) {
  const T = t[lang];
  const max = Math.ceil(Math.max(...presets.map(p => p.target.duration_sec[1])) / 15) * 15;
  const ticks = Array.from({ length: max / 15 + 1 }, (_, i) => i * 15);
  const rows = presets.map(p => {
    const [a, b] = p.target.duration_sec;
    const name = presetText(p, lang).name;
    return `<li class="bar-row" data-id="${p.id}" data-min="${a}" data-len="${b}" style="--a:${(a / max * 100).toFixed(2)}%;--b:${(b / max * 100).toFixed(2)}%">
      <a class="bar-label" href="${rel(url, urlOf('patterns', lang))}#${p.id.toLowerCase()}"><b>${p.id}</b> <span>${esc(name)}</span></a>
      <span class="bar-track"><span class="bar" tabindex="0" aria-label="${esc(`${p.id} ${name}: ${a}–${b}${T.ui.seconds}`)}"><span class="bar-value">${a}–${b}${esc(T.ui.seconds)}</span></span></span>
    </li>`;
  }).join('\n');
  return `<figure class="chart reveal${compact ? ' compact' : ''}">
  <figcaption><strong>${esc(T.patterns.chartTitle)}</strong><span>${esc(T.patterns.chartDesc)}</span></figcaption>
  ${compact ? '' : `<div class="chart-tools" role="group" aria-label="${esc(T.patterns.chartTitle)}"><button type="button" class="chip" data-sort="id" aria-pressed="true">${esc(T.patterns.sortId)}</button><button type="button" class="chip" data-sort="len" aria-pressed="false">${esc(T.patterns.sortLen)}</button></div>`}
  <div class="axis" aria-hidden="true">${ticks.map(v => `<span style="--x:${(v / max * 100).toFixed(2)}%">${v}</span>`).join('')}</div>
  <ol class="bars">${rows}</ol>
  <details class="as-table"><summary>${esc(T.ui.table)}</summary>
    <div class="table-wrap"><table><thead><tr><th scope="col">ID</th><th scope="col">${esc(T.patterns.fields.genre)}</th><th scope="col">${esc(T.patterns.fields.duration)} (${esc(T.ui.seconds)})</th><th scope="col">${esc(T.patterns.fields.asl)} (${esc(T.ui.seconds)})</th></tr></thead>
    <tbody>${presets.map(p => `<tr><th scope="row">${p.id} ${esc(presetText(p, lang).name)}</th><td>${esc(presetText(p, lang).genre)}</td><td>${p.target.duration_sec.join('–')}</td><td>${p.cut.target_asl_sec.join('–')}</td></tr>`).join('')}</tbody></table></div>
  </details>
</figure>`;
}

function hubLink(item, lang, url) {
  const target = urlOf(item.page, lang);
  return `${rel(url, target)}${headingAnchor(item.page, lang, item.heading)}`;
}

// ---------- home ----------
function homePage(lang) {
  const T = t[lang];
  const H = T.home;
  const url = urlOf('home', lang);
  const r = rel(url, '');
  const screens = ['screen-settings', 'screen-assets', 'screen-generation', 'screen-candidates', 'screen-export'];
  const shot = name => {
    const target = `media/users-guide/images/${name}.svg`;
    const file = path.join(root, `users-guide/images/${name}.svg`);
    if (!media.has(target)) media.set(target, { file, size: imageSize(file) });
    return { src: r + target, ...media.get(target).size };
  };
  const words = H.scene.words.map((w, i) => `<span style="--i:${i}">${esc(w)}</span>`).join('');
  const wave = Array.from({ length: 44 }, (_, i) => `<i style="--h:${(0.25 + Math.abs(Math.sin(i * 1.7) * Math.cos(i * 0.45))).toFixed(2)};--i:${i}"></i>`).join('');
  const clips = [3, 5, 2, 6, 4, 3, 5, 2].map((w, i) => `<i style="--w:${w}" class="${[1, 3, 6].includes(i) ? 'pick' : ''}"></i>`).join('');
  const jsonLd = [
    { '@context': 'https://schema.org', '@type': 'WebSite', name: 'VideoCreator', url: abs(url), inLanguage: lang, description: H.description },
    { '@context': 'https://schema.org', '@type': 'SoftwareApplication', name: 'VideoCreator', applicationCategory: 'MultimediaApplication',
      operatingSystem: 'Windows 10, Windows 11, macOS', softwareVersion: version || undefined, description: H.description, inLanguage: lang,
      url: abs(url), downloadUrl: `${repoUrl}/releases`, license: 'https://opensource.org/licenses/MIT', image: abs(`assets/og/og-${lang}.png`),
      screenshot: abs('media/users-guide/images/screen-candidates.svg'),
      offers: { '@type': 'Offer', price: '0', priceCurrency: 'USD' } },
    { '@context': 'https://schema.org', '@type': 'SoftwareSourceCode', name: 'VideoCreator', codeRepository: repoUrl,
      programmingLanguage: ['C#', 'JavaScript'], license: 'https://opensource.org/licenses/MIT' },
  ];
  return `${head({ lang, url, id: 'home', title: H.title, description: H.description, modified: latest([...docs.values()].map(d => d.modified)), jsonLd })}
<body class="page-home">
<div class="aurora" aria-hidden="true"><i></i><i></i><i></i></div>
${topbar(lang, url, 'home')}
<main id="main">
<section class="hero">
  <div class="hero-copy">
    <p class="eyebrow">${esc(H.eyebrow)}${version ? ` · v${esc(version)}` : ''}</p>
    <h1><span>${esc(H.h1a)}</span><span class="grad">${esc(H.h1b)}</span></h1>
    <p class="lead">${esc(H.lead)}</p>
    <div class="cta">
      <a class="btn primary" href="${r}${urlOf('quickstart', lang)}">${esc(H.ctaUser)}${icons.arrow}</a>
      <a class="btn" href="${r}${urlOf('engineers', lang)}">${esc(H.ctaDev)}</a>
      <a class="btn ghost" href="${repoUrl}" rel="noopener">${icons.github}${esc(H.ctaRepo)}</a>
    </div>
    <ul class="facts">${H.facts.map(f => `<li>${esc(f)}</li>`).join('')}</ul>
  </div>
  <div class="studio-wrap">
    <div class="studio" data-stage="4" aria-hidden="true">
      <div class="studio-src"><div class="src-frame"><b></b><b></b><b></b><span class="playhead"></span></div><span class="tag">${esc(H.scene.source)}</span></div>
      <div class="wave">${wave}</div>
      <div class="words">${words}</div>
      <div class="track">${clips}</div>
      <div class="phone"><div class="screen"><b></b><b></b><span class="cap">${esc(H.scene.caption)}</span><span class="qa">✓ ${esc(H.scene.qa)}</span><span class="ring"><svg viewBox="0 0 36 36"><circle cx="18" cy="18" r="15"/><circle class="fill" cx="18" cy="18" r="15"/></svg></span></div><span class="tag">${esc(H.scene.short)}</span></div>
      <p class="status">${H.scene.status.map((s, i) => `<span data-s="${i}">${esc(s)}</span>`).join('')}</p>
    </div>
    <button class="motion-toggle" type="button" aria-pressed="false">${esc(T.ui.motion)}</button>
  </div>
</section>

<section class="section steps-section" aria-labelledby="steps-title">
  <header class="section-head reveal"><h2 id="steps-title">${esc(H.stepsTitle)}</h2><p>${esc(H.stepsLead)}</p></header>
  <div class="steps reveal">
    <ol class="step-tabs">${H.steps.map((s, i) => `<li><button type="button" class="step-tab" id="step-tab-${i}" aria-controls="step-${i}" aria-selected="${i === 0}"><span class="num">${i + 1}</span><span>${esc(s.t)}</span><span class="meter" aria-hidden="true"></span></button></li>`).join('')}</ol>
    <div class="step-panels">${H.steps.map((s, i) => {
      const img = shot(screens[i]);
      return `<section class="step-panel" id="step-${i}" aria-labelledby="step-tab-${i}"><div class="step-text"><h3><span class="num">${i + 1}</span>${esc(s.t)}</h3><p>${esc(s.d)}</p><a href="${r}${urlOf('quickstart', lang)}">${esc(T.pages.quickstart)}${icons.arrow}</a></div><a class="shot zoom" href="${img.src}"><img src="${img.src}" alt="${esc(s.t)}" width="${img.width}" height="${img.height}" loading="lazy" decoding="async"></a></section>`;
    }).join('')}</div>
  </div>
</section>

<section class="section" aria-labelledby="features-title">
  <header class="section-head reveal"><h2 id="features-title">${esc(H.featuresTitle)}</h2><p>${esc(H.featuresLead)} <a href="${r}${urlOf('overview', lang)}">${esc(T.pages.overview)}</a></p></header>
  <ul class="features">${H.features.map((f, i) => `<li class="feature glow reveal" style="--d:${i}"><span class="ico">${icons[featureIcons[i]]}</span><h3>${esc(f.t)}</h3><p>${esc(f.d)}</p></li>`).join('')}</ul>
</section>

<section class="section patterns-teaser" aria-labelledby="patterns-title">
  <header class="section-head reveal"><h2 id="patterns-title">${esc(H.patternsTitle)}</h2><p>${esc(H.patternsLead)}</p></header>
  ${durationChart(lang, url, true)}
  <p class="center reveal"><a class="btn" href="${r}${urlOf('patterns', lang)}">${esc(H.patternsCta)}${icons.arrow}</a></p>
</section>

<section class="section" aria-labelledby="aud-title">
  <header class="section-head reveal"><h2 id="aud-title">${esc(H.audiencesTitle)}</h2></header>
  <div class="audiences">
    <a class="audience glow reveal" href="${r}${urlOf('users', lang)}"><span class="ico">${icons.user}</span><h3>${esc(H.users.t)}</h3><p>${esc(H.users.d)}</p><span class="go">${icons.arrow}</span></a>
    <a class="audience glow reveal" href="${r}${urlOf('engineers', lang)}" style="--d:1"><span class="ico">${icons.code}</span><h3>${esc(H.engineers.t)}</h3><p>${esc(H.engineers.d)}</p><span class="go">${icons.arrow}</span></a>
  </div>
</section>

<section class="section privacy" aria-labelledby="privacy-title">
  <header class="section-head reveal"><h2 id="privacy-title">${esc(H.privacyTitle)}</h2><p>${esc(H.privacyLead)}</p></header>
  <ol class="flow reveal">${H.flow.map((f, i) => `<li class="flow-node n${i}"><span class="ico">${icons[['pc', 'cloud', 'sparkle', 'send'][i]]}</span><h3>${esc(f.t)}</h3><p>${esc(f.d)}</p></li>`).join('')}</ol>
  <p class="note reveal">${esc(H.privacyNote)} <a href="${r}${urlOf('azure-access', lang)}">${esc(T.pages['azure-access'])}</a></p>
</section>

<section class="final reveal">
  <h2>${esc(H.finalTitle)}</h2>
  <p>${esc(H.finalLead)}</p>
  <div class="cta"><a class="btn primary" href="${r}${urlOf('quickstart', lang)}">${esc(H.ctaUser)}${icons.arrow}</a><a class="btn" href="${r}${urlOf('engineers', lang)}">${esc(H.ctaDev)}</a></div>
</section>
</main>
${footer(lang, url)}
</body>
</html>
`;
}

// ---------- hubs ----------
function hubPage(id, lang) {
  const T = t[lang];
  const H = T[id];
  const url = urlOf(id, lang);
  const sourcePages = H.sections.flatMap(s => s.items.map(i => i.page)).filter(p => docs.has(`${lang}/${p}`));
  const modified = latest(sourcePages.map(p => docs.get(`${lang}/${p}`).modified).concat(id === 'users' ? [presetsModified] : []));
  let map = '';
  if (id === 'engineers') {
    const doc = docs.get(`${lang}/technical-reference`);
    const anchor = headingAnchor('technical-reference', lang, H.mapHeading).slice(1);
    const start = doc.tokens.findIndex(tk => tk.type === 'heading' && doc.headings.length && slugify(tk.text) === anchor);
    const table = doc.tokens.slice(start).find(tk => tk.type === 'table');
    if (!table) throw new Error(`customization table not found in ${lang}/technical-reference`);
    const ctx = { src: doc.src, url, lang, seen: new Map(), headings: [], mermaid: false };
    const marked = createRenderer(ctx);
    const html = marked.parser(ctx.lex(table.raw))
      .replace('<table>', '<div class="table-wrap" tabindex="0" role="region"><table class="map-table">').replace('</table>', '</table></div>')
      .replace(/<a href="(https?:\/\/[^"]+)"/g, '<a href="$1" rel="noopener"');
    map = `<section class="section map reveal" aria-labelledby="map-title">
  <header class="section-head"><h2 id="map-title">${esc(H.mapTitle)}</h2><p>${esc(H.mapLead)} <a href="${rel(url, urlOf('technical-reference', lang))}#${esc(anchor)}">${esc(T.pages['technical-reference'])}</a></p></header>
  <label class="filter">${icons.search}<input type="search" placeholder="${esc(H.filter)}" aria-label="${esc(H.filter)}" data-filter-table=".map-table"></label>
  <p class="filter-count" aria-live="polite" data-template="{n} ${esc(H.filterCount)}"></p>
  ${html}
</section>`;
  }
  const jsonLd = [
    { '@context': 'https://schema.org', '@type': 'CollectionPage', name: H.title, description: H.description, inLanguage: lang, url: abs(url), dateModified: modified },
    breadcrumbLd(lang, trailOf(id, lang)),
  ];
  return `${head({ lang, url, id, title: H.title, description: H.description, modified, jsonLd })}
<body class="page-hub hub-${id}">
<div class="aurora" aria-hidden="true"><i></i><i></i><i></i></div>
${topbar(lang, url, id)}
<div class="doc-layout wide">
${sidebar(lang, url, id)}
<main id="main" class="doc-main">
  ${crumbs(id, lang, url)}
  <header class="doc-hero hub-hero">
    <p class="eyebrow">${icons[id === 'users' ? 'user' : 'code']}${esc(T.pages[id])}</p>
    <h1>${esc(H.title.split(' — ')[1] ?? H.title)}</h1>
    <p class="lead">${esc(H.lead)}</p>
  </header>
  ${H.sections.map((s, si) => `<section class="hub-section reveal" aria-labelledby="hs-${si}"><h2 id="hs-${si}">${esc(s.t)}</h2><ul class="hub-cards">${s.items.filter(item => exists(item.page, lang)).map(item => `<li><a class="hub-card glow" href="${hubLink(item, lang, url)}"><h3>${esc(item.t)}</h3><p>${esc(item.d)}</p><span class="where">${esc(T.pages[item.page])}${icons.arrow}</span></a></li>`).join('')}</ul></section>`).join('\n  ')}
  ${map}
</main>
</div>
${footer(lang, url)}
</body>
</html>
`;
}

// ---------- patterns ----------
function patternsPage(lang) {
  const T = t[lang];
  const P = T.patterns;
  const F = P.fields;
  const url = urlOf('patterns', lang);
  const genres = [...new Set(presets.map(p => presetText(p, lang).genre))];
  const cards = presets.map((p, i) => {
    const x = presetText(p, lang);
    const asl = p.cut.target_asl_sec;
    const avg = (asl[0] + asl[1]) / 2;
    const ex = p.examples?.[0];
    return `<li class="pattern glow reveal" id="${p.id.toLowerCase()}" data-genre="${esc(x.genre)}" style="--d:${i % 4};--cut:${avg.toFixed(2)}s">
  <div class="preview" aria-hidden="true"><div class="frame"><i></i><i></i><i></i><span class="cap"></span></div></div>
  <div class="pattern-body">
    <p class="pid">${p.id}${p.id === 'P4' ? `<span class="badge">${esc(P.recommended)}</span>` : ''}</p>
    <h2>${esc(x.name)}</h2>
    <p class="genre">${esc(x.genre)}</p>
    <dl class="facts-grid">
      <div><dt>${esc(F.duration)}</dt><dd>${p.target.duration_sec.join('–')}${esc(T.ui.seconds)}</dd></div>
      <div><dt>${esc(F.aspect)}</dt><dd>${esc(p.target.aspect)}</dd></div>
      <div><dt>${esc(F.opening)}</dt><dd>${esc(x.opening)}</dd></div>
      <div><dt>${esc(F.tempo)}</dt><dd>${esc(x.tempo)}</dd></div>
      <div><dt>${esc(F.telop)}</dt><dd>${esc(x.telop)}</dd></div>
      <div><dt>${esc(F.sound)}</dt><dd>${esc(x.sound)}</dd></div>
    </dl>
    <details class="more">
      <summary>${esc(F.hook)} · ${esc(F.asl)} · ${esc(F.loudness)}</summary>
      <dl class="facts-grid small">
        <div><dt>${esc(F.hook)}</dt><dd>${p.hook?.max_sec ?? '—'}${esc(T.ui.seconds)}</dd></div>
        <div><dt>${esc(F.asl)}</dt><dd>${asl.join('–')}${esc(T.ui.seconds)}</dd></div>
        <div><dt>${esc(F.caption)}</dt><dd>${p.telop?.caption?.max_chars_per_line ?? '—'}</dd></div>
        <div><dt>${esc(F.loudness)}</dt><dd>${p.audio?.loudnorm ? `${p.audio.loudnorm.i} LUFS / ${p.audio.loudnorm.tp} dBTP` : '—'}</dd></div>
        ${ex ? `<div><dt>${esc(F.example)}</dt><dd><a href="${esc(ex.url)}" rel="noopener nofollow">${esc(x.example ?? ex.title)}</a></dd></div>` : ''}
        <div><dt>${esc(F.version)}</dt><dd>${esc(p.version)} · <a href="${repoUrl}/blob/${branch}/${p.file}" rel="noopener">${esc(F.source)}</a></dd></div>
      </dl>
    </details>
  </div>
</li>`;
  }).join('\n');
  const jsonLd = [
    { '@context': 'https://schema.org', '@type': 'CollectionPage', name: P.title, description: P.description, inLanguage: lang, url: abs(url), dateModified: presetsModified,
      mainEntity: { '@type': 'ItemList', numberOfItems: presets.length,
        itemListElement: presets.map((p, i) => ({ '@type': 'ListItem', position: i + 1, name: `${p.id} ${presetText(p, lang).name}`, url: `${abs(url)}#${p.id.toLowerCase()}` })) } },
    breadcrumbLd(lang, trailOf('patterns', lang)),
  ];
  return `${head({ lang, url, id: 'patterns', title: P.title, description: P.description, modified: presetsModified, jsonLd })}
<body class="page-hub page-patterns">
<div class="aurora" aria-hidden="true"><i></i><i></i><i></i></div>
${topbar(lang, url, 'patterns')}
<div class="doc-layout wide">
${sidebar(lang, url, 'patterns')}
<main id="main" class="doc-main">
  ${crumbs('patterns', lang, url)}
  <header class="doc-hero hub-hero">
    <p class="eyebrow">${esc(T.groups.users)}</p>
    <h1>${esc(P.title.split(' — ')[0])}</h1>
    <p class="lead">${esc(P.lead)}</p>
  </header>
  ${durationChart(lang, url)}
  <div class="genre-filter reveal" role="group" aria-label="${esc(P.filterLabel)}">
    <button type="button" class="chip" data-genre="" aria-pressed="true">${esc(P.all)}</button>
    ${genres.map(g => `<button type="button" class="chip" data-genre="${esc(g)}" aria-pressed="false">${esc(g)}</button>`).join('')}
  </div>
  <ol class="patterns">${cards}</ol>
</main>
</div>
${footer(lang, url)}
</body>
</html>
`;
}

function notFoundPage() {
  const lang = 'ja';
  const blocks = languages.map(({ code, prefix }) => `<section lang="${code}"><h2>${esc(t[code].ui.notFound)}</h2><p>${esc(t[code].ui.notFoundBody)}</p><a class="btn" href="${basePath}${prefix}">${esc(t[code].ui.home)}</a></section>`).join('');
  return `${head({ lang, url: '404.html', id: 'home', title: 'Not found', description: languages.map(l => t[l.code].ui.notFoundBody).join(' '), noindex: true }).replace('<head>', `<head>\n<base href="${basePath}">`)}
<body class="page-404">
<div class="aurora" aria-hidden="true"><i></i><i></i><i></i></div>
<main id="main" class="nf"><h1>404</h1>${blocks}</main>
</body>
</html>
`;
}

// ---------- build ----------
rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });
const pages = [];
for (const doc of docs.values()) {
  write(`${doc.url}index.html`, docPage(doc));
  pages.push({ id: doc.id, lang: doc.lang, url: doc.url, modified: doc.modified, hasMermaid: doc.ctx.mermaid });
}
for (const { code } of languages) {
  for (const [id, render] of [['home', homePage], ['users', l => hubPage('users', l)], ['engineers', l => hubPage('engineers', l)], ['patterns', patternsPage]]) {
    const url = urlOf(id, code);
    write(`${url}index.html`, render(code));
    const modified = id === 'patterns' ? presetsModified : latest([...docs.values()].filter(d => d.lang === code).map(d => d.modified));
    pages.push({ id, lang: code, url, modified });
  }
  const entries = [];
  for (const id of ['users', 'engineers', 'patterns']) entries.push({ t: t[code].pages[id], u: urlOf(id, code), g: t[code].pages[id] });
  for (const doc of [...docs.values()].filter(d => d.lang === code)) {
    entries.push({ t: doc.title, u: doc.url, g: t[code].pages[doc.id] });
    for (const h of doc.headings.filter(h => h.depth <= 3)) entries.push({ t: h.text, u: `${doc.url}#${h.id}`, g: t[code].pages[doc.id] });
  }
  for (const p of presets) entries.push({ t: `${p.id} ${presetText(p, code).name}`, u: `${urlOf('patterns', code)}#${p.id.toLowerCase()}`, g: t[code].pages.patterns });
  write(`assets/search-${code}.json`, JSON.stringify(entries));
}
write('404.html', notFoundPage());

for (const [target, { file }] of media) {
  mkdirSync(path.dirname(path.join(out, target)), { recursive: true });
  cpSync(file, path.join(out, target));
}
cpSync(path.join(here, 'src/assets'), path.join(out, 'assets'), { recursive: true });
cpSync(path.join(here, 'static'), path.join(out, 'assets'), { recursive: true });
for (const lang of languages) if (!existsSync(path.join(out, `assets/og/og-${lang.code}.png`))) throw new Error(`missing OG image assets/og/og-${lang.code}.png (run node tools/make-og.mjs)`);
if (pages.some(p => p.hasMermaid)) {
  mkdirSync(path.join(out, 'assets/vendor'), { recursive: true });
  cpSync(path.join(here, 'node_modules/mermaid/dist/mermaid.min.js'), path.join(out, 'assets/vendor/mermaid.min.js'));
}

const byId = Map.groupBy(pages, p => p.id);
const sitemap = `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">
${pages.map(p => {
  const alts = byId.get(p.id);
  const xDefault = alts.find(a => a.lang === 'en') ?? alts[0];
  return `<url><loc>${esc(abs(p.url))}</loc>${p.modified ? `<lastmod>${esc(p.modified)}</lastmod>` : ''}
${alts.map(a => `  <xhtml:link rel="alternate" hreflang="${a.lang}" href="${esc(abs(a.url))}"/>`).join('\n')}
  <xhtml:link rel="alternate" hreflang="x-default" href="${esc(abs(xDefault.url))}"/></url>`;
}).join('\n')}
</urlset>
`;
write('sitemap.xml', sitemap);
write('robots.txt', `User-agent: *\nAllow: /\n\nSitemap: ${abs('sitemap.xml')}\n`);
write('.nojekyll', '');
write('build-info.json', JSON.stringify({ siteUrl, version, pages: pages.length, media: media.size, warnings: problems }, null, 2));

console.log(`Built ${pages.length} pages, ${media.size} media files -> ${posix(path.relative(root, out))} (${siteUrl})`);
if (problems.length) {
  console.log(`${problems.length} warning(s):`);
  for (const p of problems) console.log(`  ${process.env.GITHUB_ACTIONS ? '::warning::' : ''}${p}`);
}

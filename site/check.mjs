// Quality gate for site/dist: SEO metadata, hreflang reciprocity, internal links/anchors, sitemap coverage.
// Exits non-zero on any error so the Pages workflow never publishes a broken site.
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const dist = path.join(here, 'dist');
const info = JSON.parse(readFileSync(path.join(dist, 'build-info.json'), 'utf8'));
const siteUrl = info.siteUrl;
const errors = [];
const fail = (file, message) => errors.push(`${file}: ${message}`);

const walk = dir => readdirSync(dir).flatMap(name => {
  const full = path.join(dir, name);
  return statSync(full).isDirectory() ? walk(full) : [full];
});
const files = walk(dist);
const htmlFiles = files.filter(f => f.endsWith('.html'));
const relOf = f => path.relative(dist, f).split(path.sep).join('/');
const urlOfFile = f => relOf(f).replace(/index\.html$/, '');
const decode = s => s.replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>');
const attr = (tag, name) => { const m = tag.match(new RegExp(`\\s${name}="([^"]*)"`)); return m ? decode(m[1]) : null; };
const metas = html => [...html.matchAll(/<meta\s[^>]*>/g)].map(m => m[0]);
const meta = (html, key, value) => metas(html).find(tag => attr(tag, key) === value);

const pages = new Map();
for (const file of htmlFiles) {
  const html = readFileSync(file, 'utf8');
  const ids = new Set();
  for (const m of html.matchAll(/\sid="([^"]+)"/g)) {
    const id = decode(m[1]);
    if (ids.has(id)) fail(relOf(file), `duplicate id "${id}"`);
    ids.add(id);
  }
  pages.set(relOf(file), { file, html, ids, url: urlOfFile(file) });
}

const titles = new Map();
const descriptions = new Map();
const indexable = [];
for (const [name, page] of pages) {
  const { html, url } = page;
  const notFound = name === '404.html';
  const lang = html.match(/<html lang="([^"]+)"/)?.[1];
  if (!lang) fail(name, 'missing <html lang>');
  const h1 = (html.match(/<h1[\s>]/g) || []).length;
  if (h1 !== 1) fail(name, `expected exactly one <h1>, found ${h1}`);
  const title = decode(html.match(/<title>([^<]*)<\/title>/)?.[1] ?? '');
  const description = attr(meta(html, 'name', 'description') ?? '', 'content');
  if (!title) fail(name, 'missing <title>');
  if (!description) fail(name, 'missing meta description');
  else if (description.length < 50) fail(name, `description too short (${description.length})`);
  for (const tag of html.match(/<img\s[^>]*>/g) || []) if (attr(tag, 'alt') === null) fail(name, `img without alt: ${tag.slice(0, 80)}`);
  for (const m of html.matchAll(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/g)) {
    try { JSON.parse(m[1]); } catch (e) { fail(name, `invalid JSON-LD: ${e.message}`); }
  }
  if (notFound) {
    if (!meta(html, 'name', 'robots')?.includes('noindex')) fail(name, '404 must be noindex');
    continue;
  }
  indexable.push(page);
  if (titles.has(title)) fail(name, `duplicate title "${title}" (also ${titles.get(title)})`);
  titles.set(title, name);
  if (description && descriptions.has(description)) fail(name, `duplicate description (also ${descriptions.get(description)})`);
  descriptions.set(description, name);
  const expected = `${siteUrl}/${url}`;
  const canonical = attr(html.match(/<link rel="canonical"[^>]*>/)?.[0] ?? '', 'href');
  if (canonical !== expected) fail(name, `canonical ${canonical} != ${expected}`);
  for (const [key, value] of [['property', 'og:title'], ['property', 'og:description'], ['property', 'og:url'], ['property', 'og:image'], ['property', 'og:locale'], ['name', 'twitter:card']]) {
    if (!meta(html, key, value)) fail(name, `missing ${value}`);
  }
  if (attr(meta(html, 'property', 'og:url') ?? '', 'content') !== expected) fail(name, 'og:url differs from canonical');
  const image = attr(meta(html, 'property', 'og:image') ?? '', 'content');
  if (image && !existsSync(path.join(dist, image.replace(`${siteUrl}/`, '')))) fail(name, `og:image not found: ${image}`);
  const alternates = [...html.matchAll(/<link rel="alternate" hreflang="([^"]+)" href="([^"]+)">/g)].map(m => ({ lang: m[1], href: decode(m[2]) }));
  page.alternates = alternates;
  if (!alternates.some(a => a.lang === lang && a.href === expected)) fail(name, 'hreflang set does not include itself');
  if (!alternates.some(a => a.lang === 'x-default')) fail(name, 'missing hreflang x-default');
}

// hreflang reciprocity: every alternate must exist and list this page back.
const byUrl = new Map(indexable.map(p => [`${siteUrl}/${p.url}`, p]));
for (const page of indexable) {
  for (const alt of page.alternates.filter(a => a.lang !== 'x-default')) {
    const other = byUrl.get(alt.href);
    if (!other) { fail(relOf(page.file), `hreflang target missing: ${alt.href}`); continue; }
    if (!other.alternates.some(a => a.href === `${siteUrl}/${page.url}`)) fail(relOf(page.file), `hreflang not reciprocal with ${alt.href}`);
  }
}

// internal links and anchors
let checkedLinks = 0;
for (const [name, page] of pages) {
  const baseUrl = new URL(name === '404.html' ? `${siteUrl}/` : `${siteUrl}/${page.url}`);
  const html = page.html.replace(/<script type="application\/ld\+json">[\s\S]*?<\/script>/g, '').replace(/<code>[\s\S]*?<\/code>/g, '');
  for (const m of html.matchAll(/\s(?:href|src)="([^"]*)"/g)) {
    const raw = decode(m[1]);
    if (!raw || /^(?:mailto|tel|data|javascript):/i.test(raw)) continue;
    if (/^https?:/i.test(raw) && !raw.startsWith(`${siteUrl}/`)) continue;
    checkedLinks++;
    let target;
    try { target = new URL(raw, baseUrl); } catch { fail(name, `bad URL ${raw}`); continue; }
    if (!target.href.startsWith(`${siteUrl}/`)) { fail(name, `link escapes the site: ${raw}`); continue; }
    let relPath = decodeURIComponent(target.pathname.slice(new URL(`${siteUrl}/`).pathname.length));
    if (relPath === '' || relPath.endsWith('/')) relPath += 'index.html';
    const filePath = path.join(dist, relPath);
    if (!existsSync(filePath)) { fail(name, `broken link ${raw}`); continue; }
    if (target.hash && target.hash.length > 1) {
      const targetPage = pages.get(relPath);
      const id = decodeURIComponent(target.hash.slice(1));
      if (targetPage && !targetPage.ids.has(id)) fail(name, `missing anchor ${raw}`);
    }
  }
}

// sitemap & robots
const sitemap = readFileSync(path.join(dist, 'sitemap.xml'), 'utf8');
const locs = new Set([...sitemap.matchAll(/<loc>([^<]+)<\/loc>/g)].map(m => decode(m[1])));
for (const page of indexable) if (!locs.has(`${siteUrl}/${page.url}`)) fail('sitemap.xml', `missing ${page.url || '(home)'}`);
for (const loc of locs) if (!byUrl.has(loc)) fail('sitemap.xml', `loc without page: ${loc}`);
if (!/<lastmod>\d{4}-\d{2}-\d{2}T/.test(sitemap)) fail('sitemap.xml', 'lastmod missing');
if (!readFileSync(path.join(dist, 'robots.txt'), 'utf8').includes(`Sitemap: ${siteUrl}/sitemap.xml`)) fail('robots.txt', 'sitemap line missing');
if (info.warnings?.length) for (const w of info.warnings) fail('build', w);

console.log(`Checked ${pages.size} HTML files, ${checkedLinks} internal links, ${locs.size} sitemap URLs.`);
if (errors.length) {
  console.error(`${errors.length} error(s):`);
  for (const e of errors.slice(0, 200)) console.error(`  ${process.env.GITHUB_ACTIONS ? '::error::' : ''}${e}`);
  process.exit(1);
}
console.log('All checks passed.');

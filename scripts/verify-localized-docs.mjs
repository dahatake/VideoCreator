import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const originals = execFileSync('git', ['ls-files', '*.md'], { cwd: root, encoding: 'utf8' })
  .trim().split(/\r?\n/).filter(f => !f.startsWith('locales/'));
const languages = ['en', 'zh-CN', 'ko'];
const problems = [];
for (const language of languages) {
  for (const original of originals) {
    const file = path.join(root, 'locales', language, original);
    if (!existsSync(file)) { problems.push(`${language}: missing ${original}`); continue; }
    const source = readFileSync(path.join(root, original), 'utf8');
    const translated = readFileSync(file, 'utf8');
    if (Buffer.byteLength(translated) < Buffer.byteLength(source) * 0.55)
      problems.push(`${language}/${original}: translation is suspiciously short; check for omitted prose`);
    const headings = text => [...text.matchAll(/^#{1,6}\s+\S/gm)].length;
    const rows = text => [...text.matchAll(/^\|/gm)].length;
    const fences = text => [...text.matchAll(/^```/gm)].length;
    for (const [label, count] of [['headings', headings], ['table rows', rows], ['code fences', fences]]) {
      if (count(translated) < count(source))
        problems.push(`${language}/${original}: ${count(translated)} ${label}; source has ${count(source)}`);
    }
    const requirementIds = text => new Set(text.match(/\b(?:FR-[A-Z]+-\d+|NFR-[A-Z]+-\d+|AT-\d+|UX-\d+|H-\d+|S-\d+)\b/g) ?? []);
    const translatedIds = requirementIds(translated);
    for (const id of requirementIds(source))
      if (!translatedIds.has(id)) problems.push(`${language}/${original}: missing requirement identifier ${id}`);
    const prose = translated.replace(/^```[^\n]*\n[\s\S]*?^```[^\n]*$/gm, '');
    for (const match of prose.matchAll(/!?\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g)) {
      const target = match[1].split('#')[0];
      if (!target || /^[a-z][a-z0-9+.-]*:|^\/\//i.test(target) || target.includes('<')) continue;
      const destination = path.resolve(path.dirname(file), decodeURIComponent(target));
      if (!existsSync(destination))
        problems.push(`${language}/${original}: unresolved link ${target}`);
    }
  }
}
assert.equal(problems.length, 0, problems.join('\n'));
console.log(`Verified ${originals.length} documents × ${languages.length} languages: inventory, structure and local links.`);

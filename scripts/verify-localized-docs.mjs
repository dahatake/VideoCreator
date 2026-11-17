import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const languages = ['en', 'ko', 'zh-CN'];
const walk = directory => readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
  const file = path.join(directory, entry.name);
  return entry.isDirectory() ? walk(file) : [file];
});
const display = file => path.relative(root, file).split(path.sep).join('/');
const withoutCode = text => {
  let fence;
  return text.split(/\r?\n/).filter(line => {
    const marker = line.match(/^\s{0,3}(`{3,}|~{3,})/);
    if (marker) {
      if (!fence) fence = marker[1];
      else if (marker[1][0] === fence[0] && marker[1].length >= fence.length) fence = undefined;
      return false;
    }
    return !fence;
  }).join('\n');
};
const headings = markdown => {
  const seen = new Map();
  return [...withoutCode(markdown).matchAll(/^#{1,6}\s+(.+)$/gm)].map(match => {
    const id = match[1].replace(/\[([^\]]*)\]\([^)]*\)/g, '$1').replace(/<[^>]*>/g, '')
      .toLowerCase().replace(/[^\p{L}\p{N}\p{M}_ -]/gu, '').replace(/ /g, '-');
    const count = seen.get(id) ?? 0;
    seen.set(id, count + 1);
    return count ? `${id}-${count}` : id;
  });
};
const destinations = markdown => {
  const text = withoutCode(markdown).replace(/`+[^`\n]*`+/g, '');
  const inline = [...text.matchAll(/!?\[[^\]\n]*\]\(\s*(<[^>]+>|[^)\s]+)(?:\s+["'][^"']*["'])?\s*\)/g)]
    .map(match => match[1].replace(/^<|>$/g, ''));
  const definitions = [...text.matchAll(/^\s{0,3}\[[^\]]+\]:\s*(<[^>]+>|\S+)/gm)]
    .map(match => match[1].replace(/^<|>$/g, ''));
  const html = [...text.matchAll(/<(?:a|img)\b[^>]*\b(?:href|src)=["']([^"']+)["']/gi)]
    .map(match => match[1]);
  return [...inline, ...definitions, ...html];
};
const localized = (source, language) => path.join(root, 'users-guide', language,
  source === 'README.md' ? 'start.md' :
    source.startsWith(`users-guide${path.sep}`) ? source.slice('users-guide'.length + 1) : source);

export function verifyLocalizedDocs() {
  const guide = path.join(root, 'users-guide');
  // Read the working tree, including new/untracked files. Git's index is not the inventory.
  const guideSources = readdirSync(guide, { withFileTypes: true })
    .filter(entry => entry.isFile() && entry.name.endsWith('.md'))
    .map(entry => path.join('users-guide', entry.name));
  const rootSources = readdirSync(root, { withFileTypes: true })
    .filter(entry => entry.isFile() && entry.name.endsWith('.md')).map(entry => entry.name);
  const originals = [...rootSources, ...guideSources,
    ...['docs', 'work'].flatMap(directory => walk(path.join(root, directory))
      .filter(file => file.endsWith('.md')).map(file => path.relative(root, file)))];
  const problems = [];
  const expected = new Set(originals.map(source => path.join(root, source)));
  const counts = [
    ['headings', text => [...withoutCode(text).matchAll(/^#{1,6}\s+\S/gm)].length],
    ['table rows', text => [...withoutCode(text).matchAll(/^\|/gm)].length],
    ['code fences', text => [...text.matchAll(/^\s{0,3}(?:```|~~~)/gm)].length],
  ];
  const requirementIds = text => new Set(text.match(/\b(?:FR-[A-Z]+-\d+|NFR-[A-Z]+-\d+|AT-\d+|UX-\d+|H-\d+|S-\d+)\b/g) ?? []);
  const content = file => readFileSync(file, 'utf8');
  for (const language of languages) {
    for (const source of originals) {
      const file = localized(source, language);
      expected.add(file);
      if (!existsSync(file)) {
        problems.push(`${display(file)}: missing translation of ${source}`);
        continue;
      }
      const original = content(path.join(root, source));
      const translated = content(file);
      // The preserved detailed overview is intentionally independent of the simplified entry.
      if (source !== path.join('users-guide', 'overview.md')) {
        if (Buffer.byteLength(translated) < Buffer.byteLength(original) * 0.55)
          problems.push(`${display(file)}: translation is suspiciously short`);
        for (const [label, count] of counts) {
          if (count(translated) < count(original))
            problems.push(`${display(file)}: ${count(translated)} ${label}; source has ${count(original)}`);
        }
      }
      const ids = requirementIds(translated);
      for (const id of requirementIds(original)) {
        if (!ids.has(id)) problems.push(`${display(file)}: missing requirement identifier ${id}`);
      }
    }
  }
  for (const file of walk(guide).filter(file => file.endsWith('.md'))) {
    if (!expected.has(file)) problems.push(`${display(file)}: document has no four-language source mapping`);
  }
  if (existsSync(path.join(root, 'locales'))) problems.push('Legacy locales directory still exists');
  let links = 0;
  for (const file of expected) {
    if (!existsSync(file)) continue;
    const markdown = content(file);
    for (const url of destinations(markdown)) {
      if (/^[a-z][a-z0-9+.-]*:|^\/\//i.test(url) || url.includes('<')) continue;
      const [target, fragment] = url.split('#');
      const destination = target ? path.resolve(path.dirname(file), decodeURIComponent(target.split('?')[0])) : file;
      links++;
      if (!existsSync(destination)) problems.push(`${display(file)}: unresolved link ${url}`);
      else if (fragment && statSync(destination).isFile() && destination.endsWith('.md')) {
        const targetText = content(destination);
        const anchors = [...headings(targetText),
          ...[...targetText.matchAll(/<(?:a|h[1-6])\b[^>]*(?:id|name)=["']([^"']+)["']/gi)].map(match => match[1])];
        if (!anchors.includes(decodeURIComponent(fragment)))
          problems.push(`${display(file)}: unresolved heading ${url}`);
      }
    }
  }
  // The navigation contract is exact: switching languages must retain document identity.
  for (const source of originals) {
    const peers = [path.join(root, source), ...languages.map(language => localized(source, language))];
    for (const file of peers) {
      if (!existsSync(file)) continue;
      const navigation = content(file).match(/<!-- language-nav -->([\s\S]*?)<!-- \/language-nav -->/);
      if (!navigation) {
        problems.push(`${display(file)}: missing language navigation`);
        continue;
      }
      const targets = destinations(navigation[1]).map(url => path.resolve(path.dirname(file), url));
      if (targets.length !== 4 || peers.some(peer => !targets.includes(peer)))
        problems.push(`${display(file)}: language navigation must link to the same document in all four languages`);
    }
  }
  assert.equal(problems.length, 0, problems.join('\n'));
  return { files: [...expected], links, originals: originals.length };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const result = verifyLocalizedDocs();
  console.log(`PASS: ${result.originals} source documents × 4 languages; full working-tree inventory, structure, ${result.links} local links/anchors and same-document language mappings.`);
}

// Rasterises the SVG app icon to PNGs and a multi-size ICO (PNG-compressed entries, Vista+ format).
// Usage: node make-icons.mjs
import { Resvg } from '@resvg/resvg-js';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const wwwroot = join(here, '..', 'src', 'VideoCreator.App', 'wwwroot');
const svg = readFileSync(join(wwwroot, 'img', 'icon.svg'), 'utf8');
mkdirSync(join(wwwroot, 'img'), { recursive: true });

const render = (size) => new Resvg(svg, { fitTo: { mode: 'width', value: size }, background: 'rgba(0,0,0,0)' }).render().asPng();

for (const size of [16, 32, 48, 180, 192, 256, 512]) writeFileSync(join(wwwroot, 'img', `icon-${size}.png`), render(size));

const icoSizes = [16, 24, 32, 48, 64, 256];
const pngs = icoSizes.map(render);
const header = Buffer.alloc(6); header.writeUInt16LE(0, 0); header.writeUInt16LE(1, 2); header.writeUInt16LE(pngs.length, 4);
const dir = Buffer.alloc(16 * pngs.length);
let offset = 6 + dir.length;
pngs.forEach((png, i) => {
  const s = icoSizes[i], o = i * 16;
  dir.writeUInt8(s >= 256 ? 0 : s, o); dir.writeUInt8(s >= 256 ? 0 : s, o + 1);
  dir.writeUInt8(0, o + 2); dir.writeUInt8(0, o + 3);
  dir.writeUInt16LE(1, o + 4); dir.writeUInt16LE(32, o + 6);
  dir.writeUInt32LE(png.length, o + 8); dir.writeUInt32LE(offset, o + 12);
  offset += png.length;
});
writeFileSync(join(wwwroot, 'favicon.ico'), Buffer.concat([header, dir, ...pngs]));
console.log('icons written:', icoSizes.join(','), '+ png 16..512');

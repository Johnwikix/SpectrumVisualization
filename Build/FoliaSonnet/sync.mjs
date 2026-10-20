// Rebuild the checked-in AGPL geometry bundle from an explicit Folia checkout (Node 24+).
// Usage: node Build/FoliaSonnet/sync.mjs <folia-major directory>
import fs from 'node:fs';
import path from 'node:path';
import { stripTypeScriptTypes } from 'node:module';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const here = path.dirname(fileURLToPath(import.meta.url));
const source = path.resolve(process.argv[2] ?? '');
if (!process.argv[2]) throw new Error('Pass the upstream folia-major checkout explicitly.');
const root = path.join(source, 'src/components/visualizer');
const version = JSON.parse(fs.readFileSync(path.join(source, 'package.json'), 'utf8')).version;
const revision = execFileSync('git', ['-C', source, 'rev-parse', '--short=8', 'HEAD'], { encoding: 'utf8' }).trim();
const modules = new Map();
function add(id) {
    if (modules.has(id)) return;
    modules.set(id, '');
    let code;
    if (id === 'sonnetIcons') {
        const original = fs.readFileSync(path.join(root, 'sonnet/sonnetIcons.ts'), 'utf8');
        // SVG/React generation is a browser concern. Preserve only the pure timing/index helpers.
        code = original.slice(original.indexOf('export const buildSonnetIconParticleIndices'), original.indexOf('export const buildSonnetIconDataUrl'));
    } else if (id === 'sonnetBeatFx') {
        code = 'export const resolveSonnetIconOnsetPop = () => 0; export const resolveSonnetIconPopScale = () => 1;';
    } else if (id === 'beatFxLive') {
        code = 'export const isBeatFxReady = () => false;';
    } else if (id === 'sonnetAnimatedGraphics') {
        code = 'export const AnimatedGraphics = NativeGraphics;';
    } else if (id === 'sonnetGlyphLayout') {
        // Frame decoration only needs the pure duration helper; typography is compiled by native-layout.js.
        const original = fs.readFileSync(path.join(root, 'sonnet/sonnetGlyphLayout.ts'), 'utf8');
        const first = original.indexOf('export const resolveSonnetGlyphMotionDuration');
        code = stripTypeScriptTypes(original.slice(first, original.indexOf('\n};', first) + 3), { mode: 'strip' });
    } else {
        const file = id === 'visualizerSeedHash' ? path.join(root, id + '.ts') : path.join(root, 'sonnet', id + '.ts');
        code = stripTypeScriptTypes(fs.readFileSync(file, 'utf8'), { mode: 'strip' });
    }
    if (id === 'sonnetIcons') code = stripTypeScriptTypes(code, { mode: 'strip' });
    const exports = [];
    code = code.replace(/export\s*\{([\s\S]*?)\}\s*from\s*['"]([^'"]+)['"];?/g, (_, names, specifier) => {
        const dependency = path.basename(specifier);
        add(dependency);
        return names.replace(/\/\/[^\n]*/g, '').split(',').filter(x => x.trim()).map(name => {
            const [original, alias] = name.trim().split(/\s+as\s+/);
            return `exports.${alias ?? original} = require(${JSON.stringify(dependency)}).${original};`;
        }).join('\n');
    });
    code = code.replace(/import\s*\{([\s\S]*?)\}\s*from\s*['"]([^'"]+)['"];?/g, (_, names, specifier) => {
        const dependency = path.basename(specifier);
        add(dependency);
        const bindings = names.split(',').map(x => x.trim().replace(/\s+as\s+/, ': ')).filter(Boolean).join(', ');
        return `const { ${bindings} } = require(${JSON.stringify(dependency)});`;
    });
    code = code.replace(/export\s+(const|function|class)\s+(\w+)/g, (_, kind, name) => {
        exports.push(name);
        return `${kind} ${name}`;
    });
    if (/^\s*(?:import|export)\s/m.test(code)) throw new Error('Unsupported module syntax in ' + id);
    modules.set(id, code.replace(/[ \t]+$/gm, '') + '\n' + exports.map(name => `exports.${name} = ${name};`).join('\n'));
}
// The viewport overlay is native; imported transition math remains available for reference checks.
for (const id of ['sonnetShotMg', 'sonnetTransitions', 'sonnetTextFixedGeo', 'sonnetFrameDecor']) add(id);
const adapter = fs.readFileSync(path.join(here, 'adapter.js'), 'utf8');
const table = [...modules].map(([id, code]) => `${JSON.stringify(id)}: function(exports, require) {\n${code}\n}`).join(',\n');
const output = `// Generated from Folia ${version}, upstream ${revision}. AGPL-3.0; see External/Folia/NOTICE.md.\n`
    + '// Regenerate with Build/FoliaSonnet/sync.mjs. CPU preparation only; never execute per frame.\n'
    + 'var FoliaSonnetGraphics = (function() {\n' + adapter.replace('/* MODULES */', table) + '\n})();\n';
fs.writeFileSync(path.join(here, '../../External/Folia/sonnet-graphics.js'), output);
console.log(`Bundled ${modules.size} modules, ${output.length} characters.`);

// Rebuild the original non-AI theme/keyword helpers from an explicit checkout, using Node 24+.
import fs from 'node:fs';
import path from 'node:path';
import { stripTypeScriptTypes } from 'node:module';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

if (!process.argv[2]) throw new Error('Pass the upstream folia-major checkout explicitly.');
const source = path.resolve(process.argv[2]);
const here = path.dirname(fileURLToPath(import.meta.url));
const modules = new Map();
function add(relative) {
    const id = relative.replaceAll('\\', '/');
    if (modules.has(id)) return id;
    modules.set(id, '');
    let code = stripTypeScriptTypes(fs.readFileSync(path.join(source, id), 'utf8'), { mode: 'strip' });
    const exported = [];
    code = code.replace(/import\s*\{([\s\S]*?)\}\s*from\s*['"]([^'"]+)['"];?/g, (_, names, specifier) => {
        if (!specifier.startsWith('.')) throw new Error(`Nonlocal dependency ${specifier}`);
        const dependency = add(path.posix.normalize(path.posix.join(path.posix.dirname(id), specifier)) + '.ts');
        return `const { ${names.trim().replace(/\s+as\s+/g, ': ')} } = require(${JSON.stringify(dependency)});`;
    });
    code = code.replace(/export\s+(const|function|class)\s+(\w+)/g, (_, kind, name) => {
        exported.push(name);
        return `${kind} ${name}`;
    });
    if (id.endsWith('/colorExtractor.ts')) exported.push('extractVibrantColorsFromPixels');
    if (/^\s*(?:import|export)\s/m.test(code)) throw new Error('Unsupported syntax in ' + id);
    modules.set(id, code + '\n' + exported.map(name => `exports.${name} = ${name};`).join('\n'));
    return id;
}
for (const file of ['src/utils/builtinTheme/generateBuiltinDualTheme.ts', 'src/utils/colorExtractor.ts', 'src/components/visualizer/wordColoring.ts']) add(file);
const revision = process.argv[3] ?? execFileSync('git', ['-C', source, 'rev-parse', '--short=8', 'HEAD'], { encoding: 'utf8' }).trim();
const table = [...modules].map(([id, code]) => `${JSON.stringify(id)}: function(exports, require) {\n${code}\n}`).join(',\n');
const adapter = fs.readFileSync(path.join(here, 'adapter.js'), 'utf8');
fs.writeFileSync(path.join(here, '../../External/Folia/themes.js'), `// Folia ${revision}, AGPL-3.0. Regenerate with Build/FoliaThemes/sync.mjs. Cold paths only.\nvar FoliaThemes = (function() {\n${adapter.replace('/* MODULES */', table)}\n})();\n`);
console.log(`Bundled ${modules.size} non-AI theme modules from ${revision}.`);

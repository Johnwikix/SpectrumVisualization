import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, resolve } from 'node:path';
import { execFileSync } from 'node:child_process';
import { readFile, writeFile } from 'node:fs/promises';
import { brotliCompressSync, constants, gunzipSync } from 'node:zlib';

const revision = '847bb51dd80316b4d94461d9d838db50d31da2c8';
if (!process.argv[4]) throw new Error('Usage: node Build/FoliaMachi/sync.mjs <folia-major-checkout> <rainy-corner.json> <model-directory> [chromium-executable]');
const upstream = resolve(process.argv[2]);
if (execFileSync('git', ['--git-dir', resolve(upstream, '.git'), 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim() !== revision) {
    throw new Error(`Machi assets require folia-major ${revision}`);
}

const here = dirname(fileURLToPath(import.meta.url));
const requireUpstream = createRequire(resolve(upstream, 'package.json'));
const { build } = await import(pathToFileURL(requireUpstream.resolve('vite')));
const { chromium } = requireUpstream('playwright');
const sourceFile = resolve(here, 'export-scene.mjs');
const sourceId = sourceFile.replaceAll('\\', '/');
const importRoot = resolve(upstream, 'src/components/visualizer/machi').replaceAll('\\', '/');
const source = await readFile(sourceFile, 'utf8');
const donor = JSON.parse(await readFile(resolve(process.argv[3]), 'utf8'));
const bundle = await build({
    root: upstream,
    configFile: false,
    logLevel: 'error',
    define: { 'process.env.NODE_ENV': '"production"' },
    resolve: { alias: { three: resolve(upstream, 'node_modules/three'),
        'spectrum-models': resolve(here, 'fit-vehicles.mjs'), 'spectrum-surfaces': resolve(here, 'surface-tiles.mjs'),
        'spectrum-colors': resolve(here, 'scene-colors.mjs') } },
    plugins: [{
        name: 'spectrum-machi-export',
        enforce: 'pre',
        resolveId(id) {
            if (id === 'virtual:folia-locale-zh-TW') return '\0' + id;
        },
        load(id) {
            if (id === '\0virtual:folia-locale-zh-TW') return 'export default {};';
            return id === sourceId ? source.replaceAll(/from '([.][^']+)'/g,
                (_, specifier) => `from '${resolve(importRoot, specifier).replaceAll('\\', '/')}'`) : undefined;
        },
        transform(code, id) {
            // WebGL's static merge discards named prop boundaries. The exporter handles
            // flattening and indexing after the designated Blender props are fitted.
            if (id.endsWith('/machi/engine/geometry/shapes.ts')) {
                const signature = 'export const bakeStatic = (root: THREE.Object3D) => {';
                if (!code.includes(signature)) throw new Error('The upstream static mesh collector has changed.');
                return code.replace(signature, signature + '\n    if (root) return;');
            }
        },
    }],
    build: {
        write: false,
        minify: false,
        lib: { entry: sourceFile, name: 'SpectrumMachi', formats: ['iife'] },
    },
});
const { code } = bundle[0].output.find(output => output.type === 'chunk');

// Chromium supplies Canvas 2D for the upstream signs. No upstream app, WebGL
// renderer, browser window, server, or network source is needed by this export.
const browser = await chromium.launch({ executablePath: process.argv[5] });
const weatherDefinitions = [];
const exported = [];
try {
    const page = await browser.newPage({ offline: true });
    page.on('pageerror', error => console.error(error));
    const origin = 'https://machi-export.invalid/';
    await page.route(origin, route => route.fulfill({ contentType: 'text/html', body: '<!doctype html>' }));
    await page.goto(origin);
    await page.addScriptTag({ content: code });
    for (const id of ['konbini', 'station', 'kowloon']) {
        const modelIds = id === 'station' ? ['traffic', 'sedan', 'taxi', 'kei', 'ad-truck'] :
            id === 'konbini' ? ['traffic'] : ['scooter'];
        const models = Object.fromEntries(await Promise.all([id, ...modelIds].map(async name =>
            [name, JSON.parse(await readFile(resolve(process.argv[4], name + '.json'), 'utf8'))])));
        const result = await page.evaluate(({ id, donor, models }) => SpectrumMachi.exportScene(id, donor, models),
            { id, donor: id === 'konbini' ? donor : null, models });
        const binary = gunzipSync(Buffer.from(result.binary, 'base64'));
        const compressed = brotliCompressSync(binary, {
            params: { [constants.BROTLI_PARAM_QUALITY]: 9, [constants.BROTLI_PARAM_SIZE_HINT]: binary.length },
        });
        exported.push([id, compressed]);
        console.log(JSON.stringify({ id, ...result.stats, bytes: compressed.length,
            weather: [result.weather.roofs.length, result.weather.drips.length, result.weather.glints.length] }));
        weatherDefinitions.push(writeWeather(id, result.weather));
    }
} finally {
    await browser.close();
}

for (const [id, bytes] of exported)
    await writeFile(resolve(here, '../../External/Folia/Machi', `${id}.machi.br`), bytes);
await writeFile(resolve(here, '../../Effects/Folia/FoliaMachi.WeatherData.cs'),
    `// Generated by Build/FoliaMachi/sync.mjs.\nusing System.Numerics;\n\nnamespace WinExSpectrumTest.Effects.Folia;\n\ninternal sealed partial class FoliaMachi\n{\n${weatherDefinitions.join('\n\n')}\n}\n`);

function writeWeather(id, weather) {
    const entries = values => values.map(x => {
        if (!Number.isFinite(x)) throw new Error('Non-finite baked Machi atmosphere value');
        return `${Number(x.toPrecision(9))}f`;
    }).join(', ');
    const rows = (values, pattern) => values.map(x => '            ' + pattern(x)).join(',\n');
    const roof = ({ x0, z0, x1, z1, y }) => `new(${entries([x0, z0, x1, z1, y])})`;
    const drip = ({ x, z, from, to, period, phase }) => `new(${entries([x, z, from, to, period, phase])})`;
    const glint = ({ position, radius }) => `new(new Vector3(${entries(position)}), ${entries([radius])})`;
    const puff = values => `new(new(${entries(values.slice(0,4))}), new(${entries(values.slice(4))}))`;
    const name = id[0].toUpperCase() + id.slice(1) + 'Weather';
    return `    private static readonly WeatherLayout ${name} = new(\n        ${entries([weather.half, weather.groundY, weather.windYaw, weather.windSlant])},\n        [\n${rows(weather.roofs, roof)}\n        ],\n        [\n${rows(weather.drips, drip)}\n        ],\n        [\n${rows(weather.glints, glint)}\n        ],\n        [\n${rows(weather.steam, puff)}\n        ]);`;
}

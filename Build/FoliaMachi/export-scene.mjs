import * as THREE from 'three';
import { MACHI_SCENES } from './scenes/index';
import { createVisualizerRng } from '../visualizerSeedHash';
import { MachiColorBlend, MachiMaterials } from './engine/materials';
import { resolveMachiPalette } from './engine/palette';
import { createMachiSnowUniforms } from './engine/snow';
import { MACHI_GLOSS, machiGlossFor } from './engine/sheen';

const atlasWidth = 2048;
const stride = 32;
const ignored = new Set(['Steam', 'LightCones', 'ReflectionWindow', 'Hologram/beam', 'HeadlightPool', 'TaxiLamp']);
const panelHeight = 560;

const sceneLabels = {
    konbini: 'KONBINI',
    station: 'FOLIA SERVICE',
    kowloon: 'KOWLOON',
};
const theme = {
    provider: 'preset',
    name: '',
    backgroundColor: '#101722',
    primaryColor: '#d6e4ef',
    secondaryColor: '#61d9b8',
    accentColor: '#ef835a',
    wordColors: [],
};

const float = (view, offset, value) => view.setFloat32(offset, value, true);
const uint = (view, offset, value) => view.setUint32(offset, value, true);
const unorm = value => Math.round(65535 * THREE.MathUtils.clamp(value, 0, 1));
const srgbByte = value => Math.round(255 * THREE.MathUtils.clamp(value <= .0031308 ? value * 12.92 : 1.055 * value ** (1 / 2.4) - .055, 0, 1));
const unorm4 = value => Math.round(15 * THREE.MathUtils.clamp(value, 0, 1));

function packedSurface(material) {
    const authored = material.userData.rainyCorner ?? (material.isMeshStandardMaterial ? material : null);
    if (authored) return (unorm4(authored.roughness) << 4) | unorm4(authored.metalness);

    const role = machiGlossFor(material.name.split(':').at(-1));
    let roughness = .72;
    let metalness = 0;
    if (role === MACHI_GLOSS.paint) roughness = .29;
    if (role === MACHI_GLOSS.glass) roughness = .18;
    if (role === MACHI_GLOSS.ground) roughness = .75;
    if (role === MACHI_GLOSS.metal) { roughness = .35; metalness = .66; }
    if (role === MACHI_GLOSS.chrome) { roughness = .16; metalness = .94; }
    return (unorm4(roughness) << 4) | unorm4(metalness);
}

function fitRainyCorner(scene, donor, materials, palette) {
    const applied = {};
    const presets = {
        white: 'shelf', steel: 'metal', metal: 'trim', yellow: 'caution', rubber: 'rubber',
        wood: 'counter', black: 'darkMetal', paper: 'interiorWall', 'RC bicycle enamel': 'bike',
        'RC chain steel': 'darkMetal', 'RC saddle leather': 'rubber', 'RC vending backing': 'darkMetal',
    };
    const special = {
        vending: { red: 'vendingBody', green: 'brand2' },
        counter: { white: 'counter' },
        ac: { white: 'acUnit' },
    };
    const materialFor = (category, key) => {
        if (key === 'glass') return materials.glass;
        if (key === 'RC vending LED diffuser' || key === 'cyan_light') return materials.glow('vendingGlow');
        if (key === 'warm_light') return materials.glow('lampHead');
        const color = special[category]?.[key] ?? presets[key];
        if (color) return materials.toon(color);
        const fallback = key === 'orange' ? palette.products[2] : palette.products[0];
        const material = materials.track(new THREE.MeshToonMaterial({ color: fallback }));
        material.name = `donor:${key}`;
        return material;
    };
    const operations = [
        { category: 'vending', name: /^Vending[01]$/ },
        { category: 'shelf', name: /^Shelf\d+$/ },
        { category: 'fridgeRack', name: /^Fridge$/, part: /^Rack$/, individual: true },
        { category: 'fridgeHandle', name: /^Fridge$/, part: /^Handle$/, individual: true },
        { category: 'counter', name: /^Counter$/ },
        { category: 'ac', name: /^(RoofAc|GroundAc)\d$/, part: /^(Body|Feet|Grille|Pipe)$/ },
        { category: 'bike', name: /^Bicycle\d+$/ },
        { category: 'lamp', name: /^StreetLamp$/ },
    ];
    for (const { category, name, part, individual } of operations) {
        const targets = [];
        scene.root.traverse(object => {
            if (!object.isGroup || !name.test(object.name)) return;
            const chosen = object.children.filter(child => !part || part.test(child.name));
            if (individual) targets.push(...chosen.map(child => ({ target: object, original: [child] })));
            else targets.push({ target: object, original: chosen });
        });
        const template = new THREE.Group();
        const source = donor.groups[category];
        if (!source || targets.length === 0) throw new Error(`RainyCorner ${category} is missing.`);
        for (const [key, data] of Object.entries(source)) {
            const geometry = new THREE.BufferGeometry();
            geometry.setAttribute('position', new THREE.Float32BufferAttribute(data.positions, 3));
            geometry.setAttribute('normal', new THREE.Float32BufferAttribute(data.normals, 3));
            geometry.setIndex(data.indices);
            const material = materialFor(category, key).clone();
            if (data.surface) material.userData.rainyCorner = data.surface;
            template.add(new THREE.Mesh(geometry, materials.track(material)));
        }
        const sourceBounds = new THREE.Box3().setFromObject(template);
        const sourceSize = sourceBounds.getSize(new THREE.Vector3());
        applied[category] = targets.length;
        for (const { target, original } of targets) {
            const saved = new THREE.Group();
            saved.add(...original.map(child => child.clone()));
            const bounds = new THREE.Box3().setFromObject(saved);
            if (bounds.isEmpty()) throw new Error(`No existing mesh bounds for ${target.name}.`);
            const replacement = template.clone();
            replacement.scale.copy(bounds.getSize(new THREE.Vector3()).divide(sourceSize));
            replacement.position.copy(bounds.min).sub(sourceBounds.min.clone().multiply(replacement.scale));
            replacement.name = `RainyCorner/${category}`;
            target.remove(...original);
            target.add(replacement);
        }
    }
    const cans = scene.root.getObjectByName('SampleCans');
    cans?.removeFromParent();
    return applied;
}

function collectScene(scene) {
    const road = new Set(scene.surfaces.road);
    const lot = new Set(scene.surfaces.lot);
    const staticDraws = [];
    const actors = [];
    const textures = new Map();
    let vertexCount = 0;
    let indexCount = 0;
    const color = new THREE.Color();
    const matrix = new THREE.Matrix4();
    scene.root.updateMatrixWorld(true);
    const visit = (object, actor = null) => {
        if ((!object.visible && actor?.kind !== 5) || ignored.has(object.name) || object.name.startsWith('Pool/')) return;
        if (!actor) {
            const kind = object.name === 'Door/left' ? 1 : object.name === 'Door/right' ? 2 :
                object.name === 'Fan' ? 3 : /^Nobori\d+$/.test(object.name) ? 4 : object.name === 'Traffic' ? 5 :
                object.name === 'Hologram/panel' ? 6 : 0;
            if (kind) {
                actor = { kind, draws: [], pivot: object.getWorldPosition(new THREE.Vector3()), axis: object.getWorldDirection(new THREE.Vector3()) };
                actors.push(actor);
            }
        }
        const material = object.material;
        let geo = object.geometry;
        if (object.isMesh && !Array.isArray(material) && geo.attributes.position && geo.attributes.normal && !material.alphaMap) {
            const texture = material.map?.isCanvasTexture ? material.map : null;
            const alternative = object.name === 'Motel/vacancy' ? scene.signs.vacancyFull.texture : null;
            const name = material.name;
            let kind = 0;
            if (name === 'machi:lyricScreen') kind = 2;
            else if (name === 'machi:led') kind = 6;
            else if (object.name === 'Hologram/panel') kind = 8;
            else if (texture) kind = alternative ? 10 : 5;
            else if (name.endsWith(':glass')) {
                kind = 1;
                if (geo.type === 'PlaneGeometry')
                    geo = new THREE.BoxGeometry(geo.parameters.width, geo.parameters.height, .016);
            }
            else if (road.has(object)) kind = 4;
            else if (lot.has(object)) kind = 9;
            else if (name.startsWith('machi:glow:') || object.name === 'Motel/bulbs' || /^(Headlight|Taillight)$/.test(object.name)) kind = 3;

            if (!material.isShaderMaterial || kind !== 0) {
                const count = object.isInstancedMesh ? object.count : 1;
                if (count && texture && !textures.has(texture)) textures.set(texture, null);
                if (count && alternative && !textures.has(alternative)) textures.set(alternative, null);
                const surface = packedSurface(material);
                for (let i = 0; i < count; i += 1) {
                    const transform = object.matrixWorld.clone();
                    if (object.isInstancedMesh) {
                        object.getMatrixAt(i, matrix);
                        transform.multiply(matrix);
                    }
                    if (actor?.kind === 5) transform.elements[13] -= 30;
                    color.copy(material.color ?? material.uniforms?.uColor?.value ?? new THREE.Color(1, 1, 1));
                    if (object.instanceColor) {
                        const instanceColor = new THREE.Color();
                        object.getColorAt(i, instanceColor);
                        color.multiply(instanceColor);
                    }
                    const gain = kind === 3 ? Math.max(1, color.r, color.g, color.b) :
                        kind === 5 || kind === 10 ? (material.isMeshBasicMaterial ? color.r : 0.3) : 1;
                    if (kind === 3) color.multiplyScalar(1 / gain);
                    else if (kind === 5 || kind === 10) color.setRGB(.03, .035, .05);
                    else if (kind === 2) color.set(0x1e1822);
                    else if (kind === 6) color.set(0x201c1a);
                    else if (kind === 8) color.set(0x79d9e8);
                    const draws = actor?.draws ?? staticDraws;
                    const boardCells = material.uniforms?.uCells?.value;
                    const dots = material.uniforms?.uDots?.value;
                    const cells = kind === 2 ? boardCells : kind === 6 ? new THREE.Vector2(Math.floor(dots * material.uniforms.uAspect.value), dots) : null;
                    draws.push({ geo, kind, gain, color: color.clone(), transform, texture, alternative, cells, surface, name: object.name });
                    vertexCount += geo.attributes.position.count;
                    indexCount += geo.index?.count ?? geo.attributes.position.count;
                }
            }
        }
        object.children.forEach(child => visit(child, actor));
    };
    visit(scene.root);
    const indexLength = draws => draws.reduce((count, { geo }) => count + (geo.index?.count ?? geo.attributes.position.count), 0);
    const staticIndexCount = indexLength(staticDraws);
    let firstIndex = staticIndexCount;
    const populated = actors.filter(actor => actor.draws.length);
    for (const actor of populated) {
        actor.firstIndex = firstIndex;
        actor.indexCount = indexLength(actor.draws);
        firstIndex += actor.indexCount;
    }
    return { draws: [...staticDraws, ...populated.flatMap(actor => actor.draws)], actors: populated, staticIndexCount, textures, vertexCount, indexCount };
}

function packSigns(textures) {
    let x = 1;
    let y = 1;
    let rowHeight = 0;
    const allocate = texture => {
        const image = texture.image;
        // Leave a one-pixel edge extension on all sides for bilinear sampling.
        const scale = Math.min(1, (atlasWidth - 4) / image.width);
        const width = Math.max(1, Math.floor(image.width * scale));
        const height = Math.max(1, Math.floor(image.height * scale));
        if (x + width + 1 > atlasWidth) {
            x = 1;
            y += rowHeight + 2;
            rowHeight = 0;
        }
        textures.set(texture, { x, y, width, height });
        x += width + 2;
        rowHeight = Math.max(rowHeight, height);
    };
    [...textures.keys()].sort((a, b) => b.image.height - a.image.height).forEach(allocate);
    const atlas = document.createElement('canvas');
    atlas.width = atlasWidth;
    atlas.height = y + rowHeight + 1;
    const paint = atlas.getContext('2d', { willReadFrequently: true });
    for (const [texture, rect] of textures) {
        const { x, y, width, height } = rect;
        paint.drawImage(texture.image, x, y, width, height);
        paint.drawImage(texture.image, 0, 0, 1, texture.image.height, x - 1, y, 1, height);
        paint.drawImage(texture.image, texture.image.width - 1, 0, 1, texture.image.height, x + width, y, 1, height);
    }
    return paint.getImageData(0, 0, atlas.width, atlas.height);
}

function songSignRects(id, scene) {
    const rects = new Map();
    const named = (name, band, left, right) => rects.set(name, [{ band, left, right, u0: 0, v0: 0, u1: 1, v1: 1 }]);
    if (id === 'konbini') {
        const fascia = scene.signs.fascia.canvas;
        named('Fascia/front', 0, 1.51 * fascia.height / fascia.width, 1 - 1.8 * fascia.height / fascia.width);
        named('Poster0', 1, .04, .96);
    } else if (id === 'station') {
        const canopy = scene.signs.canopy.canvas;
        named('Canopy/sign0', 1, 1.36 * canopy.height / canopy.width, 1 - 2 * canopy.height / canopy.width);
        named('Canopy/sign1', 1, 1.36 * canopy.height / canopy.width, 1 - 2 * canopy.height / canopy.width);
        named('Motel/name', 0, .04, .96);
        named('Pylon/face0', 2, 0, 1);
        named('Pylon/face1', 2, 0, 1);
    } else {
        const canvas = scene.signs.canvas;
        const artistRects = [];
        scene.signs.entries.forEach((entry, index) => {
            if (index !== 0 && entry.placement.role !== 'artist') return;
            const { x, y, width, height } = entry.rect;
            artistRects.push({
                band: index === 0 ? 0 : 1,
                left: index === 0 ? 1.15 * height / width : .03,
                right: index === 0 ? 1 - .91 * height / width : .97,
                u0: x / canvas.width, v0: y / canvas.height,
                u1: (x + width) / canvas.width, v1: (y + height) / canvas.height,
                vertical: index > 0,
            });
        });
        rects.set('Neon', artistRects);
    }
    return rects;
}

function collectLights(scene) {
    const lights = [];
    scene.root.traverse(light => {
        if (!light.isPointLight && !light.isSpotLight) return;
        const position = light.getWorldPosition(new THREE.Vector3());
        const aim = new THREE.Vector3(0, -1, 0);
        if (light.isSpotLight) light.target.getWorldPosition(aim).sub(position).normalize();
        const inner = light.isSpotLight ? Math.cos(light.angle * (1 - light.penumbra)) : 1;
        const outer = light.isSpotLight ? Math.cos(light.angle) : -1;
        lights.push({ name: light.name, values: [...position, ...aim, ...light.color, light.intensity, light.distance, inner, outer] });
    });
    return lights;
}

function writeScene(id, scene, data, signs, lights) {
    const { view: camera, points } = scene.framing;
    const { draws, actors, staticIndexCount, textures, vertexCount, indexCount } = data;
    const signRects = songSignRects(id, scene);
    const pointStart = 52;
    const actorStart = pointStart + 12 * points.length;
    const lightStart = actorStart + 36 * actors.length;
    const vertexStart = lightStart + 52 * lights.length;
    const indexStart = vertexStart + stride * vertexCount;
    const signStart = indexStart + 4 * indexCount;
    const bytes = new Uint8Array(signStart + signs.data.length);
    const view = new DataView(bytes.buffer);
    uint(view, 0, 0x3348434d); // MCH3
    uint(view, 4, vertexCount);
    uint(view, 8, indexCount);
    uint(view, 12, signs.width);
    uint(view, 16, signs.height);
    uint(view, 20, points.length);
    float(view, 24, camera.azimuth);
    float(view, 28, camera.elevation);
    float(view, 32, camera.zoom);
    float(view, 36, scene.weather.half);
    uint(view, 40, actors.length);
    uint(view, 44, staticIndexCount);
    uint(view, 48, lights.length);
    points.forEach(({ x, y, z }, i) => {
        float(view, pointStart + i * 12, x);
        float(view, pointStart + i * 12 + 4, y);
        float(view, pointStart + i * 12 + 8, z);
    });
    actors.forEach(({ kind, firstIndex, indexCount, pivot, axis }, index) => {
        const offset = actorStart + index * 36;
        uint(view, offset, kind);
        uint(view, offset + 4, firstIndex);
        uint(view, offset + 8, indexCount);
        float(view, offset + 12, pivot.x);
        float(view, offset + 16, pivot.y);
        float(view, offset + 20, pivot.z);
        float(view, offset + 24, axis.x);
        float(view, offset + 28, axis.y);
        float(view, offset + 32, axis.z);
    });
    lights.forEach(({ values }, index) => {
        values.forEach((value, field) => float(view, lightStart + index * 52 + field * 4, value));
    });
    let firstVertex = 0;
    let offset = vertexStart;
    let indexOffset = indexStart;
    const point = new THREE.Vector3();
    const normal = new THREE.Vector3();
    const normalMatrix = new THREE.Matrix3();
    const uv = new THREE.Vector2();
    for (const draw of draws) {
        const { geo, kind, gain, color, transform, texture, alternative, cells, surface, name } = draw;
        const { position, normal: normals, uv: uvs, color: vertexColors } = geo.attributes;
        const rect = texture ? textures.get(texture) : null;
        const alternateRect = alternative ? textures.get(alternative) : null;
        const overlays = signRects.get(name) ?? (name.startsWith('Neon/') ? signRects.get('Neon') : null);
        normalMatrix.getNormalMatrix(transform);
        const rgb = [srgbByte(color.r), srgbByte(color.g), srgbByte(color.b)];
        if (texture) texture.updateMatrix();
        for (let i = 0; i < position.count; i += 1) {
            point.fromBufferAttribute(position, i).applyMatrix4(transform);
            normal.fromBufferAttribute(normals, i).applyNormalMatrix(normalMatrix);
            float(view, offset, point.x);
            float(view, offset + 4, point.y);
            float(view, offset + 8, point.z);
            view.setInt16(offset + 12, Math.round(32767 * normal.x), true);
            view.setInt16(offset + 14, Math.round(32767 * normal.y), true);
            view.setInt16(offset + 16, Math.round(32767 * normal.z), true);
            if (uvs && (rect || kind === 2 || kind === 6 || kind === 8)) uv.fromBufferAttribute(uvs, i);
            else uv.set(0, 0);
            let overlay = null;
            let overlayU = cells ? cells.x / 1024 : -1;
            let overlayV = cells ? cells.y / 1024 : -1;
            if (rect) {
                texture.transformUv(uv);
                if (alternateRect) {
                    overlayU = (alternateRect.x + uv.x * alternateRect.width) / signs.width;
                    overlayV = (alternateRect.y + uv.y * alternateRect.height) / signs.height;
                }
                overlay = overlays?.find(area => uv.x >= area.u0 - 1e-6 && uv.x <= area.u1 + 1e-6 &&
                    uv.y >= area.v0 - 1e-6 && uv.y <= area.v1 + 1e-6);
                if (overlay) {
                    const u = (uv.x - overlay.u0) / (overlay.u1 - overlay.u0);
                    const v = (uv.y - overlay.v0) / (overlay.v1 - overlay.v0);
                    overlayU = overlay.vertical ? v : (u - overlay.left) / (overlay.right - overlay.left);
                    overlayV = overlay.band === 2 ? (481 + 78 * v) / panelHeight :
                        (401 + 40 * overlay.band + 38 * (overlay.vertical ? 1 - u : v)) / panelHeight;
                }
                uv.set((rect.x + uv.x * rect.width) / signs.width, (rect.y + uv.y * rect.height) / signs.height);
            } else if (kind === 2) uv.y = (.5 + (1 - uv.y) * 319) / panelHeight;
            else if (kind === 6) uv.y = (320.5 + (1 - uv.y) * 79) / panelHeight;
            else if (kind === 8) uv.y = (480.5 + (1 - uv.y) * 79) / panelHeight;
            else uv.y = 1 - uv.y;
            view.setUint16(offset + 18, unorm(uv.x), true);
            view.setUint16(offset + 20, unorm(uv.y), true);
            bytes[offset + 22] = overlay ? 7 : kind;
            bytes[offset + 23] = Math.min(255, Math.round(gain * 16));
            if (vertexColors) {
                bytes[offset + 24] = srgbByte(color.r * vertexColors.getX(i));
                bytes[offset + 25] = srgbByte(color.g * vertexColors.getY(i));
                bytes[offset + 26] = srgbByte(color.b * vertexColors.getZ(i));
            } else {
                bytes.set(rgb, offset + 24);
            }
            bytes[offset + 27] = surface;
            view.setUint16(offset + 28, unorm(overlayU * .25 + .5), true);
            view.setUint16(offset + 30, unorm(overlayV * .25 + .5), true);
            offset += stride;
        }
        const count = geo.index?.count ?? position.count;
        const reversed = transform.determinant() < 0;
        for (let i = 0; i < count; i += 3) {
            const a = i, b = i + (reversed ? 2 : 1), c = i + (reversed ? 1 : 2);
            uint(view, indexOffset, firstVertex + (geo.index ? geo.index.getX(a) : a));
            uint(view, indexOffset + 4, firstVertex + (geo.index ? geo.index.getX(b) : b));
            uint(view, indexOffset + 8, firstVertex + (geo.index ? geo.index.getX(c) : c));
            indexOffset += 12;
        }
        firstVertex += position.count;
    }
    bytes.set(signs.data, signStart);
    return compactVertices(bytes, vertexStart, indexStart, signStart, vertexCount, indexCount);
}

function compactVertices(bytes, vertexStart, indexStart, signStart, vertexCount, indexCount) {
    const vertices = new Uint32Array(bytes.buffer, vertexStart, vertexCount * stride / 4);
    const compact = new Uint32Array(vertices.length);
    const remap = new Uint32Array(vertexCount);
    const lookup = new Map();
    let unique = 0;
    for (let i = 0; i < vertexCount; i++) {
        const start = i * stride / 4;
        const words = vertices.subarray(start, start + stride / 4);
        const key = words.join('/');
        let index = lookup.get(key);
        if (index === undefined) {
            index = unique++;
            lookup.set(key, index);
            compact.set(words, index * stride / 4);
        }
        remap[i] = index;
    }
    const newIndexStart = vertexStart + unique * stride;
    const result = new Uint8Array(bytes.length - (vertexCount - unique) * stride);
    result.set(bytes.subarray(0, vertexStart));
    result.set(new Uint8Array(compact.buffer, 0, unique * stride), vertexStart);
    result.set(bytes.subarray(signStart), newIndexStart + indexCount * 4);
    const indices = new Uint32Array(bytes.buffer, indexStart, indexCount);
    const resultIndices = new Uint32Array(result.buffer, newIndexStart, indexCount);
    for (let i = 0; i < indexCount; i++) resultIndices[i] = remap[indices[i]];
    uint(new DataView(result.buffer), 4, unique);
    return result;
}

export async function exportScene(id, donor) {
    Math.random = createVisualizerRng(`spectrum-machi:${id}`);
    const definition = MACHI_SCENES[id];
    const song = { seed: 20261119, title: ' ', artist: ' ', album: sceneLabels[id], lines: [], isInstrumental: false };
    const palette = resolveMachiPalette(theme, song.seed, definition.resolveArt);
    const blend = new MachiColorBlend(palette);
    const materials = new MachiMaterials(blend, createMachiSnowUniforms(), id);
    const scene = definition.create({
        song, blend, materials,
        geometryCache: new Map(), previewMode: false, invalidateShadows() {},
    });
    scene.setContent({ song, theme, palette, font: { stack: '"Microsoft YaHei", sans-serif', weight: 700 }, icons: [], fallbacks: [] });
    if (id === 'station') {
        const pylon = scene.signs.pylon.canvas;
        // Draw the actual upstream artwork. Cover the three digit fields with the same
        // background so the per-frame seven-segment overlay can own these pixels.
        scene.signs.paintPylon(0, -1);
        const paint = pylon.getContext('2d');
        const rowHeight = pylon.height * .2;
        paint.fillStyle = '#16191f';
        for (let row = 0; row < 3; row++) {
            const y = pylon.height * .37 + rowHeight * row;
            paint.fillRect(pylon.width * .07, y + rowHeight * .3164, pylon.width * .86, rowHeight * .60);
        }
    }
    const referenceTriangles = donor ? collectScene(scene).indexCount / 3 : null;
    const donorInstances = donor ? fitRainyCorner(scene, donor, materials, palette) : null;
    const data = collectScene(scene);
    const signs = packSigns(data.textures);
    const lights = collectLights(scene);
    const bytes = writeScene(id, scene, data, signs, lights);
    // All three upstream sets use a 20-segment open cone and instanced steam quads.
    const cone = scene.lightCones.mesh.geometry;
    const ring = cone.getAttribute('position');
    if (ring.count !== 42) throw new Error('Unexpected Machi lamp cone topology.');
    const apex = new THREE.Vector3().fromBufferAttribute(ring, 0);
    const target = new THREE.Vector3();
    for (let i = 21; i < 41; ++i) target.add(new THREE.Vector3().fromBufferAttribute(ring, i));
    target.multiplyScalar(1 / 20);
    const radius = target.distanceTo(new THREE.Vector3().fromBufferAttribute(ring, 21));
    const steam = scene.steam.mesh.geometry;
    const sources = steam.getAttribute('aSource'), seeds = steam.getAttribute('aSeed');
    const colorKeys = id === 'konbini' ? ['interiorLight', 'streetLight'] :
        id === 'station' ? ['canopyLight', 'streetLight'] : ['neonPink', 'barLight'];
    const weather = { ...scene.weather, puddles: [...scene.puddles.offset, scene.puddles.amount],
        coneApex: [...apex.toArray(), radius], coneTarget: [...target.toArray(), cone.getAttribute('aGain').getX(0)],
        coneColor: scene.lightCones.mesh.material.uniforms.uColor.value.toArray(),
        glowA: scene.glowA.toArray(), glowB: scene.glowB.toArray(),
        glowColorA: blend.get(colorKeys[0]).toArray(), glowColorB: blend.get(colorKeys[1]).toArray(),
        steam: Array.from({length: sources.count}, (_, i) => [sources.getX(i), sources.getY(i), sources.getZ(i),
            sources.getW(i), seeds.getX(i), seeds.getY(i)]),
        glints: scene.weather.glints.map(({ position, radius }) => ({ position: position.toArray(), radius })),
    };
    scene.dispose();
    const stream = new Blob([bytes]).stream().pipeThrough(new CompressionStream('gzip'));
    const compressed = new Uint8Array(await new Response(stream).arrayBuffer());
    const binary = compressed.toBase64();
    return { binary, weather, stats: {
        draws: data.draws.length, actors: data.actors.map(actor => actor.kind), vertices: new DataView(bytes.buffer).getUint32(4, true),
        triangles: data.indexCount / 3, referenceTriangles, donorInstances,
        lights: lights.length, signs: data.textures.size, atlas: [signs.width, signs.height], unpacked: bytes.length,
    } };
}

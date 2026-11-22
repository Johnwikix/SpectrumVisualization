import * as THREE from 'three';
import { MACHI_SCENES } from './scenes/index';
import { createVisualizerRng } from '../visualizerSeedHash';
import { MachiColorBlend, MachiMaterials } from './engine/materials';
import { resolveMachiPalette } from './engine/palette';
import { createMachiSnowUniforms } from './engine/snow';
import { MACHI_GLOSS, machiGlossFor } from './engine/sheen';
import { fitVehicles } from 'spectrum-models';
import { bakeSurfaces, surfaceRows } from 'spectrum-surfaces';
import { sceneColors } from 'spectrum-colors';

const atlasWidth = 2048;
const stride = 32;
const ignored = new Set(['Steam', 'LightCones', 'ReflectionWindow', 'Hologram/beam', 'HeadlightPool', 'TaxiLamp']);
const panelHeight = 560;
const maxSceneTriangles = 600_000;

function modelFactory(batches, materials) {
    const templates = new Map();
    return name => {
        if (!templates.has(name)) {
            const root = new THREE.Group();
            for (const { material: surface, vertices, indices } of batches[name]) {
                const geometry = new THREE.BufferGeometry();
                const buffer = new THREE.InterleavedBuffer(new Float32Array(Uint8Array.fromBase64(vertices).buffer), 6);
                geometry.setAttribute('position', new THREE.InterleavedBufferAttribute(buffer, 3, 0));
                geometry.setAttribute('normal', new THREE.InterleavedBufferAttribute(buffer, 3, 3));
                geometry.setIndex(new THREE.BufferAttribute(new Uint32Array(Uint8Array.fromBase64(indices).buffer), 1));
                const material = new THREE.MeshStandardMaterial({color: new THREE.Color().fromArray(surface.color),
                    roughness: surface.roughness, metalness: surface.metalness});
                material.name = surface.name;
                material.userData.machiKind = surface.kind;
                if (surface.emission) material.userData.rainyCorner = {...surface, baseColor: surface.color};
                const mesh = new THREE.Mesh(geometry, materials.track(material));
                mesh.name = name + '/' + surface.name;
                root.add(mesh);
            }
            templates.set(name, root);
        }
        return templates.get(name).clone();
    };
}

// The original façades join the same tiled material families as the new streets.
const wallTiles = new Set(['wallA', 'wallB', 'wallC', 'wallD', 'wallE', 'wall']);
const concrete = new Set(['slab', 'concrete', 'curb', 'apron', 'interiorWall']);

const sceneLabels = {
    konbini: 'KONBINI',
    station: 'FOLIA SERVICE',
    kowloon: 'KOWLOON',
};
// These upstream analytic lights stand in for identifiable meshes. A group of
// proxy lights supplies one total flux budget to all of its emitting surfaces.
const fixtureGroups = {
    konbini: [
        { mesh: 'CeilingLights', role: 'ceilingLight', count: 9, downward: true, base: .68,
            lights: ['Lights/interior0', 'Lights/interior1'], tint: [0xffbd89, 1.04] },
        { mesh: 'Lens', donorMesh: 'RainyCorner/lamp/warm_light', role: 'lampHead', count: 1, downward: true, base: .62,
            lights: ['Lights/street'] },
    ],
    station: [
        { mesh: 'Canopy/light', role: 'canopyLight', count: 6, downward: true, base: .68,
            lights: ['Lights/canopy0', 'Lights/canopy1'], tint: [0xb4edff, 1.24] },
        { mesh: 'Lens', role: 'lampHead', count: 1, downward: true, base: .62, lights: ['Lights/lotLamp'] },
    ],
    kowloon: [
        { mesh: 'Bulb', role: 'stallBulb', count: 1, downward: false, base: .62, lights: ['Lights/stall'] },
        { mesh: 'LampLens', role: 'lampHead', count: 1, downward: true, base: .62,
            lights: ['Lights/lamp'], tint: [0x8dcbff, 1.04] },
    ],
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

function fitAcFan(target, replacement, materials) {
    const fan = target.children.find(child => child.name === 'Fan');
    const casing = replacement.getObjectByName('RainyCorner/ac/white');
    const donorGuard = replacement.getObjectByName('RainyCorner/ac/steel');
    if (!fan || !casing || !donorGuard) throw new Error(`Missing air conditioner components for ${target.name}.`);

    // Use the fitted condenser's outlet coordinates, in the unit's local frame.
    // The animation rotates the new propeller in the cavity along this same axis.
    replacement.updateMatrixWorld(true);
    const back = new THREE.Box3().setFromObject(casing).max.z;
    const guard = new THREE.Box3().setFromObject(donorGuard);
    const center = guard.getCenter(new THREE.Vector3());
    const size = guard.getSize(new THREE.Vector3());
    const radius = Math.min(size.x, size.y) * .5;
    const depth = guard.max.z - back;
    if (depth <= 0 || radius <= 0) throw new Error(`No air conditioner outlet clearance for ${target.name}.`);
    donorGuard.removeFromParent();

    const dark = materials.toon('darkMetal');
    const mount = new THREE.Shape();
    mount.absarc(0, 0, radius, 0, Math.PI * 2, false);
    const aperture = new THREE.Path();
    aperture.absarc(0, 0, radius * .88, 0, Math.PI * 2, true);
    mount.holes.push(aperture);
    const shroud = new THREE.Mesh(new THREE.ExtrudeGeometry(mount,
        { depth: depth + .001, curveSegments: 12, bevelEnabled: false }), casing.material);
    shroud.position.set(center.x, center.y, back - .001);
    const recess = new THREE.Mesh(new THREE.CylinderGeometry(radius * .89, radius * .89, depth * .13, 32)
        .rotateX(Math.PI / 2), dark);
    recess.position.set(center.x, center.y, back + depth * .04);
    target.add(shroud, recess);

    for (let index = -4; index <= 4; ++index) {
        const x = index * .192 * radius;
        const halfLength = Math.sqrt((radius * .91) ** 2 - x * x);
        const wire = new THREE.Mesh(new THREE.BoxGeometry(radius * .027, halfLength * 2, depth * .18),
            donorGuard.material);
        wire.position.set(center.x + x, center.y, back + depth * .84);
        target.add(wire);
    }

    fan.clear();
    fan.position.set(center.x, center.y, back + depth * .49);
    const bladeShape = new THREE.Shape();
    const bladeRadius = radius * .85;
    bladeShape.moveTo(.12 * bladeRadius, -.06 * bladeRadius);
    bladeShape.quadraticCurveTo(.49 * bladeRadius, -.25 * bladeRadius, .93 * bladeRadius, -.12 * bladeRadius);
    bladeShape.quadraticCurveTo(.99 * bladeRadius, .04 * bladeRadius, .92 * bladeRadius, .20 * bladeRadius);
    bladeShape.quadraticCurveTo(.61 * bladeRadius, .29 * bladeRadius, .14 * bladeRadius, .10 * bladeRadius);
    bladeShape.closePath();
    const blade = new THREE.ExtrudeGeometry(bladeShape, { depth: depth * .22, curveSegments: 5, bevelEnabled: false })
        .translate(0, 0, -depth * .11);
    const rotor = materials.toon('trim');
    for (let index = 0; index < 5; ++index) {
        const part = new THREE.Mesh(blade, rotor);
        part.rotation.z = index * Math.PI * 2 / 5;
        fan.add(part);
    }
    const hubRadius = radius * .20;
    const hub = new THREE.CylinderGeometry(hubRadius * .9, hubRadius, depth * .32, 16).rotateX(Math.PI / 2);
    fan.add(new THREE.Mesh(hub, dark));
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
    const donorEmitters = { vending: ['RC vending LED diffuser', 'cyan_light'], lamp: ['warm_light'] };
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
        for (const key of donorEmitters[category] ?? []) {
            const surface = source[key]?.surface;
            const validRgb = rgb => rgb?.length === 3 && rgb.every(value => Number.isFinite(value) && value >= 0);
            if (!validRgb(surface?.emission) || !surface.emission.some(value => value > 0) || !validRgb(surface.baseColor))
                throw new Error(`RainyCorner ${category}/${key} lacks physical light data. Re-export with extract-rainy-corner.py.`);
        }
        for (const [key, data] of Object.entries(source)) {
            const geometry = new THREE.BufferGeometry();
            geometry.setAttribute('position', new THREE.Float32BufferAttribute(data.positions, 3));
            geometry.setAttribute('normal', new THREE.Float32BufferAttribute(data.normals, 3));
            geometry.setIndex(data.indices);
            const material = materialFor(category, key).clone();
            if (data.surface) material.userData.rainyCorner = data.surface;
            const mesh = new THREE.Mesh(geometry, materials.track(material));
            mesh.name = `RainyCorner/${category}/${key}`;
            template.add(mesh);
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
            if (category === 'ac') fitAcFan(target, replacement, materials);
            target.remove(...original);
            target.add(replacement);
        }
    }
    const cans = scene.root.getObjectByName('SampleCans');
    cans?.removeFromParent();
    return applied;
}

function fitWindowPlants(root, model) {
    const placeholders = [];
    root.traverse(object => {
        if (object.isMesh && object.name === 'Plant') placeholders.push(object);
    });
    for (const [index, placeholder] of placeholders.entries()) {
        placeholder.geometry.computeBoundingBox();
        const plant = model('window-plant');
        plant.name = 'Window plant';
        plant.position.copy(placeholder.position);
        plant.position.y += placeholder.geometry.boundingBox.min.y + .001;
        plant.rotation.y = index * 2.39996;
        placeholder.parent.add(plant);
        placeholder.removeFromParent();
    }
}

function fitKowloonHandcart(root, materials) {
    const props = root.getObjectByName('Props');
    const surface = root.getObjectByName('Surface/alley');
    const parts = props?.children.filter(part => part.name === 'Cart');
    if (!surface || parts?.length !== 8) throw new Error('Missing Kowloon handcart or alley surface.');

    const [bed, , , firstHandle, secondHandle, firstWheel, secondWheel, foot] = parts;
    const steel = materials.toon('darkMetal');
    const hub = materials.toon('chrome');
    const add = (name, geometry, material, x, y, z) => {
        const part = new THREE.Mesh(geometry, material);
        part.name = 'Cart/' + name;
        part.position.set(x, y, z);
        props.add(part);
        return part;
    };
    props.updateWorldMatrix(true, true);
    const ground = surface.getWorldPosition(new THREE.Vector3()).y;
    const bounds = part => new THREE.Box3().setFromObject(part, true);
    const bedBounds = bounds(bed);
    const railY = bedBounds.min.y - .025;

    // Seat the actual tire vertices on the finished alley plane, including its surface lift.
    for (const wheel of [firstWheel, secondWheel]) {
        wheel.position.y += ground - bounds(wheel).min.y;
        const { x, y, z } = wheel.position;
        add('Hub', new THREE.CylinderGeometry(.071, .071, .084, 20), hub, x, y, z).rotation.x = Math.PI / 2;
    }

    const { x: axleX, y: axleY, z: axleStart } = firstWheel.position;
    const axleEnd = secondWheel.position.z;
    add('Axle', new THREE.CylinderGeometry(.024, .024, axleEnd - axleStart, 12), steel,
        axleX, axleY, (axleStart + axleEnd) / 2).rotation.x = Math.PI / 2;

    const hangerBottom = axleY - .04;
    for (const handle of [firstHandle, secondHandle]) {
        const { z } = handle.position;
        add('Frame rail', new THREE.BoxGeometry(bedBounds.max.x - bedBounds.min.x - .06, .085, .065), bed.material,
            bed.position.x, railY, z);
        add('Axle hanger', new THREE.BoxGeometry(.092, railY - hangerBottom, .05), steel,
            axleX, (railY + hangerBottom) / 2, z);
    }

    const crossWidth = secondHandle.position.z - firstHandle.position.z + .065;
    add('Foot crossmember', new THREE.BoxGeometry(.095, .085, crossWidth), bed.material,
        foot.position.x, railY, foot.position.z);
    const footTop = bounds(foot).max.y;
    const footHeight = foot.geometry.parameters.height;
    const footBase = ground + .012;
    foot.position.y = (footTop + footBase) / 2;
    foot.scale.y = (footTop - footBase) / footHeight;
    add('Foot shoe', new THREE.BoxGeometry(.098, .024, .107), steel,
        foot.position.x, ground + .012, foot.position.z);
}

function collectScene(id, scene, blend) {
    const road = new Set(scene.surfaces.road);
    const lot = new Set(scene.surfaces.lot);
    const staticDraws = [];
    const actors = [];
    const textures = new Map();
    let vertexCount = 0;
    let indexCount = 0;
    const color = new THREE.Color();
    const matrix = new THREE.Matrix4();
    const colors = sceneColors[id];
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
            let kind = material.userData.machiKind ?? 0;
            const fixture = material.userData.rainyCorner;
            if (fixture?.emission) kind = 11;
            else if (object.name === 'Motel/bulbs') kind = 12;
            else if (name === 'machi:lyricScreen') kind = 2;
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
            else if (name.startsWith('machi:glow:') || /^(Headlight|Taillight)$/.test(object.name)) kind = 3;
            else if (kind === 0 && name.startsWith('machi:')) {
                const role = name.split(':').at(-1);
                if (wallTiles.has(role)) kind = 14;
                else if (concrete.has(role)) kind = 15;
                else if (role === 'roof') kind = 16;
            }

            if (!material.isShaderMaterial || kind !== 0) {
                const count = object.isInstancedMesh ? object.count : 1;
                if (count && texture && !textures.has(texture)) textures.set(texture, null);
                if (count && alternative && !textures.has(alternative)) textures.set(alternative, null);
                const surface = packedSurface(material);
                const paint = name.startsWith(id + ':') ? colors.paint[name.split(':').at(-1)] : undefined;
                const glow = kind === 3 ? colors.glow[name.split(':').at(-1)] : undefined;
                for (let i = 0; i < count; i += 1) {
                    const transform = object.matrixWorld.clone();
                    if (object.isInstancedMesh) {
                        object.getMatrixAt(i, matrix);
                        transform.multiply(matrix);
                    }
                    if (actor?.kind === 5) transform.elements[13] -= 30;
                    color.copy(material.color ?? material.uniforms?.uColor?.value ?? new THREE.Color(1, 1, 1));
                    if (kind === 11) color.fromArray(fixture.baseColor);
                    else if (kind === 12) color.copy(blend.get('streetLight'));
                    else if (glow) color.setHex(glow[0]).multiplyScalar(glow[1]);
                    else if (paint !== undefined) color.setHex(paint);
                    if (object.instanceColor) {
                        const instanceColor = new THREE.Color();
                        object.getColorAt(i, instanceColor);
                        color.multiply(instanceColor);
                    }
                    const gain = kind === 3 ? Math.max(1, color.r, color.g, color.b) :
                        kind === 5 || kind === 10 ? (material.isMeshBasicMaterial ? color.r : 0) : 1;
                    if (kind === 3) color.multiplyScalar(1 / gain);
                    else if (kind === 5 || kind === 10) color.setRGB(.03, .035, .05);
                    else if (kind === 2) color.set(0x1e1822);
                    else if (kind === 6) color.set(0x201c1a);
                    else if (kind === 8) color.set(0x79d9e8);
                    const draws = actor?.draws ?? staticDraws;
                    const boardCells = material.uniforms?.uCells?.value;
                    const dots = material.uniforms?.uDots?.value;
                    const cells = kind === 2 ? boardCells : kind === 6 ? new THREE.Vector2(Math.floor(dots * material.uniforms.uAspect.value), dots) : null;
                    draws.push({ geo, kind, gain, color: color.clone(), transform, texture, alternative, cells, surface,
                        emission: fixture?.emission, bulbPhase: kind === 12 ? i % 4 : 0, name: object.name, materialName: name });
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

function packSigns(textures, saturation) {
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
    atlas.height = y + rowHeight + 1 + surfaceRows;
    const paint = atlas.getContext('2d', { willReadFrequently: true });
    for (const [texture, rect] of textures) {
        const { x, y, width, height } = rect;
        paint.drawImage(texture.image, x, y, width, height);
        paint.drawImage(texture.image, 0, 0, 1, texture.image.height, x - 1, y, 1, height);
        paint.drawImage(texture.image, texture.image.width - 1, 0, 1, texture.image.height, x + width, y, 1, height);
    }
    const image = paint.getImageData(0, 0, atlas.width, atlas.height);
    // Canvas RGB is sRGB. Preserve neutral artwork and keep the expanded
    // chroma in gamut before the linear-data surface tiles are written.
    for (let offset = 0; offset < atlasWidth * (atlas.height - surfaceRows) * 4; offset += 4) {
        const r = image.data[offset], g = image.data[offset + 1], b = image.data[offset + 2];
        const minimum = Math.min(r, g, b), maximum = Math.max(r, g, b);
        if (minimum === maximum || image.data[offset + 3] === 0) continue;
        const gray = .2126 * r + .7152 * g + .0722 * b;
        const amount = Math.min(saturation, gray / (gray - minimum), (255 - gray) / (maximum - gray));
        image.data[offset] = gray + (r - gray) * amount;
        image.data[offset + 1] = gray + (g - gray) * amount;
        image.data[offset + 2] = gray + (b - gray) * amount;
    }
    bakeSurfaces(image);
    return image;
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

function emittingFaces(draw, downward) {
    const { geo, transform } = draw;
    const position = geo.getAttribute('position');
    const count = geo.index?.count ?? position.count;
    const active = [], inactive = [];
    const a = new THREE.Vector3(), b = new THREE.Vector3(), c = new THREE.Vector3(), cross = new THREE.Vector3();
    const handedness = Math.sign(transform.determinant());
    let area = 0;
    for (let i = 0; i < count; i += 3) {
        const ai = geo.index ? geo.index.getX(i) : i;
        const bi = geo.index ? geo.index.getX(i + 1) : i + 1;
        const ci = geo.index ? geo.index.getX(i + 2) : i + 2;
        a.fromBufferAttribute(position, ai).applyMatrix4(transform);
        b.fromBufferAttribute(position, bi).applyMatrix4(transform);
        c.fromBufferAttribute(position, ci).applyMatrix4(transform);
        cross.crossVectors(b.sub(a), c.sub(a));
        const twiceArea = cross.length();
        if (twiceArea <= 1e-10 || (downward && cross.y * handedness >= -twiceArea * 1e-6)) {
            inactive.push(ai, bi, ci);
            continue;
        }
        area += twiceArea / 2;
        active.push(ai, bi, ci);
    }
    const withIndices = indices => {
        const part = new THREE.BufferGeometry();
        for (const [name, attribute] of Object.entries(geo.attributes)) part.setAttribute(name, attribute);
        return part.setIndex(indices);
    };
    return { area, active: inactive.length ? withIndices(active) : geo, inactive: inactive.length ? withIndices(inactive) : null };
}

function analyticFlux(light) {
    const [r, g, b, intensity, , inner, outer] = light.values.slice(6);
    // Integrate Three.js' smoothstep cone in cosine space. Its penumbra
    // contributes half the solid angle between its two limits.
    const solidAngle = outer === -1 ? 4 * Math.PI : 2 * Math.PI * (1 - (inner + outer) / 2);
    return [r, g, b].map(channel => channel * intensity * solidAngle);
}

const luminance = ([r, g, b]) => .2126 * r + .7152 * g + .0722 * b;

function tintLight(source, [hex, gain]) {
    const tint = new THREE.Color(hex).toArray();
    const scale = luminance(source) * gain / luminance(tint);
    return tint.map(channel => channel * scale);
}

function transferFixtureLights(id, data, lights) {
    const replaced = new Map();
    // The path tracer gets vending illumination from the diffusers and window
    // spill from the ceiling fixtures through the glass.
    const transferred = new Set(id === 'konbini' ? ['Lights/vending', 'Lights/spill0', 'Lights/spill1'] : []);
    const fixtureStats = [];
    for (const group of fixtureGroups[id]) {
        const draws = data.draws.filter(draw => (draw.name === group.mesh || draw.name === group.donorMesh)
            && draw.materialName === `machi:glow:${group.role}`);
        const sources = lights.filter(light => group.lights.includes(light.name));
        if (draws.length !== group.count || sources.length !== group.lights.length)
            throw new Error(`Unexpected Machi fixture mapping: ${id}/${group.mesh}.`);
        const parts = draws.map(draw => emittingFaces(draw, group.downward));
        const area = parts.reduce((sum, part) => sum + part.area, 0);
        if (parts.some(part => part.area <= 0)) throw new Error(`No emitting surface for ${id}/${group.mesh}.`);
        const flux = [0, 0, 0];
        for (const light of sources) {
            const sourceFlux = analyticFlux(light);
            for (let channel = 0; channel < 3; channel++) flux[channel] += sourceFlux[channel];
            transferred.add(light.name);
        }
        // The hemispherical integral of a one-sided Lambertian emitter is π.
        const diffuse = new THREE.Color().setRGB(group.base, group.base, group.base);
        const donorEmission = group.donorMesh ? draws[0].emission : null;
        // Keep the Blender lamp's chromaticity and use the upstream spotlight
        // only to calibrate its total luminance-weighted flux.
        const calibrated = group.tint ? tintLight(flux, group.tint) : donorEmission?.some(channel => channel > 0)
            ? donorEmission.map(channel => channel * luminance(flux) / luminance(donorEmission)) : flux;
        const emission = calibrated.map(channel => channel / (Math.PI * area));
        for (let index = 0; index < draws.length; index++) {
            const draw = draws[index];
            const part = parts[index];
            const color = draw.emission ? draw.color : diffuse;
            const replacement = [{ ...draw, geo: part.active, kind: 11, gain: 1, color, emission }];
            if (part.inactive) {
                replacement.push({ ...draw, geo: part.inactive, kind: 0, gain: 1, color, emission: undefined });
                data.vertexCount += part.inactive.getAttribute('position').count;
            }
            replaced.set(draw, replacement);
        }
        fixtureStats.push({ mesh: draws[0].name, count: draws.length, area, flux, emission });
    }
    data.draws = data.draws.flatMap(draw => replaced.get(draw) ?? [draw]);
    return { lights: lights.filter(light => !transferred.has(light.name)), fixtureStats };
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
    uint(view, 0, 0x3448434d); // MCH4, with filtered PBR tiles in the atlas.
    uint(view, 4, vertexCount);
    uint(view, 8, indexCount);
    uint(view, 12, signs.width);
    uint(view, 16, signs.height);
    uint(view, 20, points.length);
    float(view, 24, camera.azimuth);
    float(view, 28, camera.elevation);
    float(view, 32, camera.zoom * (id === 'station' ? .77 : .82));
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
            // Untextured physical luminaires store linear emitted RGB separately from albedo.
            if (kind === 11) {
                view.setFloat16(offset + 18, draw.emission[0], true);
                view.setFloat16(offset + 20, draw.emission[1], true);
                view.setFloat16(offset + 28, draw.emission[2], true);
            }
            if (kind === 12) view.setUint16(offset + 18, draw.bulbPhase, true);
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

export async function exportScene(id, donor, batches) {
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
    const donorInstances = donor ? fitRainyCorner(scene, donor, materials, palette) : null;
    const model = modelFactory(batches, materials);
    const vehicles = fitVehicles(scene, id, model);
    if (id === 'kowloon') {
        fitWindowPlants(scene.root, model);
        fitKowloonHandcart(scene.root, materials);
    }
    if (id === 'konbini') {
        const pole = scene.root.getObjectByName('UtilityPole');
        const wire = materials.toon('wire');
        const originals = pole?.children.filter(object => object.material === wire || object.name === 'Insulator');
        if (originals?.length !== 16) throw new Error('Unexpected geometry on the upstream Konbini utility pole.');
        // The Blender model supplies the insulators, supported spans, and service drop.
        for (const object of originals) object.removeFromParent();
    }
    if (id === 'station') {
        const replaced = [];
        scene.root.traverse(object => {
            if (object.name === 'Pump/hose' || object.name === 'Pump/nozzle') replaced.push(object);
        });
        for (const object of replaced) object.removeFromParent();
    }
    scene.root.add(model(id));
    scene.weather.half = 14;
    const data = collectScene(id, scene, blend);
    const signs = packSigns(data.textures, sceneColors[id].signSaturation);
    const { lights, fixtureStats } = transferFixtureLights(id, data, collectLights(scene));
    for (const light of lights) {
        const tint = sceneColors[id].lights[light.name];
        if (tint) light.values.splice(6, 3, ...tintLight(light.values.slice(6, 9), tint));
    }
    if (data.indexCount / 3 > maxSceneTriangles)
        throw new Error(`${id} exceeds the ${maxSceneTriangles}-triangle budget: ${data.indexCount / 3}`);
    const bytes = writeScene(id, scene, data, signs, lights);
    const weather = { ...scene.weather,
        glints: scene.weather.glints.map(({ position, radius }) => ({ position: position.toArray(), radius })),
    };
    scene.dispose();
    const stream = new Blob([bytes]).stream().pipeThrough(new CompressionStream('gzip'));
    const compressed = new Uint8Array(await new Response(stream).arrayBuffer());
    const binary = compressed.toBase64();
    return { binary, weather, stats: {
        draws: data.draws.length, actors: data.actors.map(actor => actor.kind), vertices: new DataView(bytes.buffer).getUint32(4, true),
        triangles: data.indexCount / 3, vehicles, donorInstances,
        lights: lights.length, fixtures: fixtureStats, signs: data.textures.size, atlas: [signs.width, signs.height], unpacked: bytes.length,
    } };
}

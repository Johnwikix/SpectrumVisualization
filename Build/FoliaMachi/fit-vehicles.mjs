import { Box3, CanvasTexture, Group, MeshBasicMaterial, SRGBColorSpace, Vector3 } from 'three';

const vehiclePaint = 'Vehicle paint';
const taxiLivery = 'Taxi livery';
const cargoEnamel = 'Truck cargo enamel';

function findGroups(root, predicate) {
    const groups = [];
    root.traverse(node => {
        if (node.isGroup && predicate(node)) groups.push(node);
    });
    return groups;
}

function requirePart(target, name) {
    const node = target.children.find(child => child.name === name);
    if (!node) throw new Error(`Missing ${target.name || 'Traffic car'}/${name}.`);
    return node;
}

function replacement(models, model, colors) {
    const group = models(model);
    if (!group) throw new Error(`Missing Blender vehicle ${model}.`);
    group.name = `Blender/${model}`;
    const materials = new Map();
    group.traverse(node => {
        if (!node.isMesh) return;
        const original = node.material;
        const color = colors[original.name];
        if (!color) return;
        let material = materials.get(original);
        if (!material) {
            material = original.clone();
            material.color.copy(color);
            materials.set(original, material);
        }
        node.material = material;
    });
    return group;
}

function fit(target, group, retained) {
    target.remove(...target.children.filter(child => !retained.includes(child.name)));
    target.add(group);
}

function fitTaxiSigns(car, andon, faces, vacantMaterial) {
    // Folia puts the live text under Signs, separate from the roof enclosure.
    // Bring all three parts into a common frame before resizing the roof lamp.
    const center = andon.getWorldPosition(new Vector3());
    const point = new Vector3();
    const face = faces.find(node => node.getWorldPosition(point).distanceTo(center) < .35);
    if (!face) throw new Error('The taxi roof lamp has no matching LED face.');
    faces.splice(faces.indexOf(face), 1);
    const lamp = new Group();
    lamp.name = 'Roof lamp assembly';
    lamp.position.set(0, 1.35, -.04);
    car.add(lamp);
    for (const part of [requirePart(car, 'Taxi/andonBase'), andon, face]) lamp.attach(part);
    lamp.scale.set(.65, .54, .76);
    const vacant = requirePart(car, 'Taxi/vacant');
    vacant.material = vacantMaterial;
    vacant.scale.set(.77, .75, 1);
}

function taxiVacancyMaterial() {
    const canvas = document.createElement('canvas');
    canvas.width = 128;
    canvas.height = 48;
    const ctx = canvas.getContext('2d');
    ctx.fillStyle = '#161918';
    ctx.fillRect(0, 0, 128, 48);
    ctx.fillStyle = '#ff4f30';
    ctx.font = 'bold 38px "Microsoft YaHei", sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText('空車', 64, 38);
    const map = new CanvasTexture(canvas);
    map.colorSpace = SRGBColorSpace;
    return new MeshBasicMaterial({map});
}

export function fitVehicles(scene, id, models) {
    const counts = {};
    const add = model => { counts[model] = (counts[model] ?? 0) + 1; };

    if (id === 'konbini' || id === 'station') {
        const traffic = scene.root.getObjectByName('Traffic');
        const car = traffic?.children.find(child => child.isGroup);
        if (!car) throw new Error(`Missing ${id} Traffic actor.`);
        const original = requirePart(car, 'Body');
        const group = replacement(models, 'traffic', { [vehiclePaint]: original.material.color });
        // The native actor drives +X, while the authored car faces +Z.
        group.rotation.y = Math.PI / 2;
        fit(car, group, ['Shadow', 'HeadlightPool', 'TaxiLamp']);
        add('traffic');
    }

    if (id === 'station') {
        scene.root.updateMatrixWorld(true);
        const faces = [];
        scene.root.traverse(node => { if (/^Taxi\d+\/andon$/.test(node.name)) faces.push(node); });
        const vacantMaterial = faces.length ? taxiVacancyMaterial() : null;
        const parked = findGroups(scene.root, group => /^(Stall[0-2]|PumpCar|KeiTruck)$/.test(group.name));
        for (const car of parked) {
            const andon = car.children.find(child => child.name === 'Taxi/andon');
            const model = car.name === 'KeiTruck' ? 'kei' : andon ? 'taxi' : 'sedan';
            const body = requirePart(car, model === 'kei' ? 'Kei/chassis' : 'Car/body');
            const group = replacement(models, model, {
                [vehiclePaint]: body.material.color,
                [taxiLivery]: andon?.material.color,
            });
            fit(car, group, ['Shadow', 'Taxi/andonBase', 'Taxi/andon', 'Taxi/vacant']);
            if (andon) fitTaxiSigns(car, andon, faces, vacantMaterial);
            add(model);
        }

        const truck = scene.root.getObjectByName('Truck');
        if (!truck?.isGroup) throw new Error('Missing station advertising truck.');
        const box = requirePart(truck, 'Truck/box');
        const cab = requirePart(truck, 'Truck/cab');
        box.geometry.computeBoundingBox();
        box.updateMatrix();
        const bounds = new Box3().copy(box.geometry.boundingBox).applyMatrix4(box.matrix);
        const group = replacement(models, 'ad-truck', {
            [vehiclePaint]: cab.material.color,
            [cargoEnamel]: box.material.color,
        });
        group.rotation.y = Math.PI / 2;
        group.position.set(bounds.min.x + 3.475, 0, bounds.getCenter(new Vector3()).z);
        fit(truck, group, ['Truck/bezel', 'Marker', 'Truck/shadow']);
        add('ad-truck');
    }

    if (id === 'kowloon') {
        const scooters = findGroups(scene.root, group => group.name === 'Scooter');
        // The Blender scooters are longer than the upstream props. Park
        // perpendicular to the wall and clear of the gas bottles and crates.
        const bays = [[-2.36, 4.58, Math.PI / 2 - .065], [-2.12, 5.39, Math.PI / 2 + .085]];
        if (scooters.length !== bays.length) throw new Error(`Expected two Kowloon scooters, found ${scooters.length}.`);
        for (const [index, scooter] of scooters.entries()) {
            const body = requirePart(scooter, 'Body');
            const group = replacement(models, 'scooter', { [vehiclePaint]: body.material.color });
            const [x, z, yaw] = bays[index];
            scooter.position.x = x;
            scooter.position.z = z;
            scooter.rotation.y = yaw;
            fit(scooter, group, []);
            add('scooter');
        }
        const [first, second] = scooters.map(scooter => new Box3().setFromObject(scooter, true));
        if (first.intersectsBox(second)) throw new Error('The parked Kowloon scooter meshes overlap.');
    }

    return counts;
}

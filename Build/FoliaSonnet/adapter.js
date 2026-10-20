// Native recording boundary: upstream builders execute once in the cancellable scene worker.
// Curves are flattened to <= 0.4 DIP chord error; earcut shares the existing licensed cold planner.
class NativeNode {
    constructor() {
        this.children = [];
        this.x = this.y = this.rotation = 0;
        this.alpha = 1;
        this.scale = { x: 1, y: 1, set(x, y = x) { this.x = x; this.y = y; } };
        this.position = { set: (x, y) => { this.x = x; this.y = y; } };
        this.anchor = { set() {} };
    }
    addChild(...nodes) { this.children.push(...nodes); return nodes[0]; }
}
class NativeGraphics extends NativeNode {
    constructor(pixi) {
        super();
        this.display = this;
        this.animated = !!pixi;
        this.commands = [];
        this.paths = [];
        this.path = null;
        this.lastX = this.lastY = 0;
    }
    moveTo(x, y) {
        this.path = [[x, y]];
        this.paths.push(this.path);
        this.lastX = x; this.lastY = y;
        return this;
    }
    lineTo(x, y) {
        if (!this.path) this.moveTo(this.lastX, this.lastY);
        this.path.push([x, y]);
        this.lastX = x; this.lastY = y;
        return this;
    }
    curve(points, depth = 0) {
        const a = points[0], b = points.at(-1), dx = b[0] - a[0], dy = b[1] - a[1];
        const length = Math.hypot(dx, dy);
        const error = Math.max(...points.slice(1, -1).map(p => length > 0.00001
            ? Math.abs(dy * (p[0] - a[0]) - dx * (p[1] - a[1])) / length
            : Math.hypot(p[0] - a[0], p[1] - a[1])));
        if (error <= 0.4 || depth >= 12) { this.lineTo(...b); return; }
        const left = [a], right = [b];
        let row = points;
        while (row.length > 1) {
            row = row.slice(0, -1).map((p, i) => [(p[0] + row[i + 1][0]) / 2, (p[1] + row[i + 1][1]) / 2]);
            left.push(row[0]); right.unshift(row.at(-1));
        }
        this.curve(left, depth + 1); this.curve(right, depth + 1);
    }
    quadraticCurveTo(cx, cy, x, y) { this.curve([[this.lastX, this.lastY], [cx, cy], [x, y]]); return this; }
    bezierCurveTo(ax, ay, bx, by, x, y) { this.curve([[this.lastX, this.lastY], [ax, ay], [bx, by], [x, y]]); return this; }
    arc(cx, cy, r, start, end, anticlockwise = false) {
        let sweep = end - start;
        if (anticlockwise && sweep > 0) sweep -= Math.PI * 2;
        else if (!anticlockwise && sweep < 0) sweep += Math.PI * 2;
        const count = Math.max(2, Math.ceil(Math.abs(sweep) / Math.max(0.025, 2 * Math.acos(Math.max(-1, 1 - 0.4 / Math.max(r, 0.4))))));
        if (!this.path) this.moveTo(cx + Math.cos(start) * r, cy + Math.sin(start) * r);
        for (let i = 0; i <= count; i++) {
            const angle = start + sweep * i / count;
            this.lineTo(cx + Math.cos(angle) * r, cy + Math.sin(angle) * r);
        }
        return this;
    }
    circle(x, y, r) {
        // Stable start and direction keep rebuilding/seeking deterministic (upstream used Math.random).
        const start = (this.commands.length * 2.399963229728653) % (Math.PI * 2);
        this.moveTo(x + Math.cos(start) * r, y + Math.sin(start) * r);
        return this.arc(x, y, r, start, start + Math.PI * 2);
    }
    rect(x, y, w, h) {
        this.rectangle = [x, y, w, h];
        return this.moveTo(x, y).lineTo(x + w, y).lineTo(x + w, y + h).lineTo(x, y + h).lineTo(x, y);
    }
    clear() { this.commands = []; this.paths = []; this.path = null; return this; }
    poly(points) {
        if (!points.length) return this;
        this.moveTo(points[0], points[1]);
        for (let i = 2; i < points.length; i += 2) this.lineTo(points[i], points[i + 1]);
        return this.lineTo(points[0], points[1]);
    }
    record(fill, options) {
        if (this.paths.length) this.commands.push({ fill, options, paths: this.paths, rectangle: this.rectangle });
        this.paths = []; this.path = null; this.rectangle = null;
        return this;
    }
    stroke(options) { return this.record(false, options); }
    fill(options) { return this.record(true, options); }
}
class NativeSprite extends NativeGraphics {
    constructor() {
        super();
        this.icon = true;
        // Folia's default Flower icon, in a 24-unit viewbox. Theme SVGs are not part of Spectrum themes.
        for (let i = 0; i < 6; i++) {
            const angle = i * Math.PI / 3;
            this.circle(Math.cos(angle) * 5.5, Math.sin(angle) * 5.5, 4).stroke({ color: 1, width: 1.5, alpha: 1 });
        }
        this.circle(0, 0, 2).stroke({ color: 1, width: 1.5, alpha: 1 });
    }
    set width(value) { this.scale.x = value / 24; }
    set height(value) { this.scale.y = value / 24; }
}
const pixi = {
    Container: NativeNode, Graphics: NativeGraphics, Sprite: NativeSprite,
    Color: { shared: { setValue(value) { return { toNumber: () => value }; } } },
};
const modules = { /* MODULES */ };
const cache = {};
function require(id) {
    if (cache[id]) return cache[id];
    const exports = cache[id] = {};
    if (!modules[id]) throw new Error('Missing Sonnet source module: ' + id);
    modules[id](exports, require);
    return exports;
}
function clipLine(a, b, rect) {
    const [x, y, w, h] = rect, dx = b[0] - a[0], dy = b[1] - a[1];
    const edges = [[-dx, a[0] - x], [dx, x + w - a[0]], [-dy, a[1] - y], [dy, y + h - a[1]]];
    let lo = 0, hi = 1;
    for (const [p, q] of edges) {
        if (Math.abs(p) < 1e-9) { if (q < 0) return null; }
        else if (p < 0) lo = Math.max(lo, q / p);
        else hi = Math.min(hi, q / p);
    }
    return lo <= hi ? [lo, hi] : null;
}
function recordNode(node, layer, output, parent = [1, 0, 0, 1, 0, 0], icon = null) {
    const c = Math.cos(node.rotation), s = Math.sin(node.rotation), sx = node.scale.x, sy = node.scale.y;
    const local = [c * sx, s * sx, -s * sy, c * sy, node.x, node.y];
    const matrix = [parent[0]*local[0]+parent[2]*local[1], parent[1]*local[0]+parent[3]*local[1],
        parent[0]*local[2]+parent[2]*local[3], parent[1]*local[2]+parent[3]*local[3],
        parent[0]*local[4]+parent[2]*local[5]+parent[4], parent[1]*local[4]+parent[3]*local[5]+parent[5]];
    const transform = p => [p[0]*matrix[0]+p[1]*matrix[2]+matrix[4], p[0]*matrix[1]+p[1]*matrix[3]+matrix[5]];
    if (node.commands) {
        let strokes = 0, fills = 0;
        for (const command of node.commands) {
            const i = command.fill ? fills++ : strokes++;
            const delay = node.animated ? ((i * 0.6180339887498949) % 1) * (command.fill ? 0.45 : 0.5) : 0;
            const span = node.animated ? Math.min((command.fill ? 0.4 : 0.32) + (((i * 2654435761) >>> 0) / 4294967296) * (command.fill ? 0.25 : 0.26), 1 - delay) : 0;
            const base = { layer, fill: command.fill, color: command.options.color ?? 1, alpha: command.options.alpha ?? 1,
                width: (command.options.width ?? 1) * Math.sqrt(Math.abs(matrix[0]*matrix[3]-matrix[1]*matrix[2])), delay, span, icon, center: transform([0, 0]) };
            if (command.fill) {
                for (const path of command.paths) {
                    if (path.length < 3) continue;
                    const points = path.map(transform).flat();
                    const indices = FoliaNativeSource.triangulateSonnet(points);
                    output.push({ ...base, points, indices, rectangle: !!command.rectangle && command.paths.length === 1 && path.length === 5 });
                }
            } else {
                let length = 0;
                const lines = [];
                for (const path of command.paths) for (let j = 1; j < path.length; j++) {
                    const a = path[j - 1], b = path[j], distance = Math.hypot(b[0] - a[0], b[1] - a[1]);
                    const clip = node.mask ? clipLine(a, b, node.mask.commands[0].rectangle) : [0, 1];
                    if (clip && distance > 1e-6) lines.push([...transform([a[0]+(b[0]-a[0])*clip[0], a[1]+(b[1]-a[1])*clip[0]]),
                        ...transform([a[0]+(b[0]-a[0])*clip[1], a[1]+(b[1]-a[1])*clip[1]]), length+distance*clip[0], length+distance*clip[1]]);
                    length += distance;
                }
                for (const line of lines) { line[4] /= length || 1; line[5] /= length || 1; }
                if (lines.length) output.push({ ...base, lines });
            }
        }
    }
    for (const child of node.children) if (!node.children.some(n => n.mask === child)) recordNode(child, layer, output, matrix, icon);
}
function geometry(kind, width, height, seed) {
    const mg = require('sonnetShotMg').buildSonnetShotMg(pixi, kind, { primaryColor: 1, secondaryColor: 2 }, width, height, seed,
        new Map([['Flower', {}]]));
    const commands = [];
    if (mg.geoLayer) recordNode(mg.geoLayer, 1, commands);
    if (mg.fixedGeoLayer) recordNode(mg.fixedGeoLayer, 2, commands);
    recordNode(mg.bgLayer, 0, commands);
    const icons = mg.particleLayer.children.filter(n => n.icon);
    for (const child of mg.particleLayer.children) {
        const index = icons.indexOf(child);
        recordNode(child, 3, commands, undefined, index < 0 ? null : [index, icons.length, 0.62 + ((Math.abs(seed + mg.particleLayer.children.indexOf(child) * 17)) % 4) * 0.08]);
    }
    return commands;
}
function compile(json) {
    const input = JSON.parse(json);
    const shots = JSON.parse(FoliaNativeSource.compile(json));
    for (const shot of shots) {
        shot.graphics = geometry(shot.kind, input.width, input.height, shot.seed);
        for (const text of shot.texts) {
            const segment = text.segment, placement = text.placement;
            const spec = require('sonnetFrameDecor').resolveSonnetFrameDecorSpec(segment);
            const dimensions = require('sonnetFrameDecor').resolveSonnetFrameLocalDimensions(placement);
            const seed = segment.text.split('').reduce((a, b) => a + b.charCodeAt(0), 0) + text.segmentIndex * 13;
            const chorus = text.isChorus || seed % 100 < 35;
            const commands = [];
            if (!spec.applied && text.role !== 'decoration' && segment.isWordLike && seed % 100 < (chorus ? 26 : 15)) {
                const node = require('sonnetTextFixedGeo').buildSonnetTextFixedGeo(pixi, { seed, isChorusEffect: chorus,
                    fontSize: text.font, layoutWidth: input.width, theme: { primaryColor: 1, secondaryColor: 2, accentColor: 3 } });
                recordNode(node, 4, commands);
            }
            text.decor = { commands, variant: spec.applied && text.role !== 'decoration' ? spec.variant : -1,
                x: placement.x, y: placement.y, width: dimensions.width, height: dimensions.height,
                enterX: placement.enterX, enterY: placement.enterY };
            delete text.segment;
            delete text.placement;
        }
    }
    return JSON.stringify(shots);
}
return { compile, geometry, transition: (...args) => require('sonnetTransitions').resolveSonnetTransitionEffectFrame(...args) };

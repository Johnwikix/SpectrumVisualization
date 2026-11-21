const cache = {};
let activeStrokes = null;
let activeNebbiaWidths = null;
function require(id) {
    if (cache[id]) return cache[id];
    const exports = cache[id] = {};
    modules[id](exports, require);
    return exports;
}

const bridgeKinds = ['mine', 'senrin', 'kiku', 'botan', 'ring', 'crossette', 'willow-ring', 'strobe'];

function fireworkEvents(shot, profile, region, width, height, unit, seed, index, glow, timing, timeline, bounds) {
    const { createRng } = require('hanabi/hanabiRandom');
    const { burstPreset } = require('hanabi/catalog');
    const rng = createRng(seed + ':' + shot.id + ':' + index + ':events');
    const bursts = [], launches = [];
    const start = shot.startTime, end = shot.endTime;
    const atScreen = (y,time) => y - timing.worldY - timeline.rise(time);
    const spark = (spec, at, x, y, size, key, launch) => {
        const shaped = ['ring', 'willow-ring', 'heart', 'star'].includes(spec.kind);
        const squash = shaped && spec.kind === 'ring' ? .4 + rng() * .5 : 1;
        const rotation = shaped ? (rng() - .5) * .6 : 0;
        bursts.push({ x, y, at, spec, scale: size, seed: seed + ':' + shot.id + ':' + key, squash, rotation });
        if (launch && spec.kind !== 'mine') launches.push({
            fromX: x + (rng() - .5) * width * .08, fromY: Math.max(atScreen(height * 1.03, at - .45), y + height * .2), toX: x, toY: y,
            at: at - .45, duration: .45, color: glow, seed: seed + ':' + shot.id + ':' + key + ':launch',
        });
    };
    if (shot.isBridge) {
        const from = start + .15, duration = Math.max(0, end - .3 - from);
        const count = Math.max(1, Math.min(10, Math.round(duration / .75)));
        const kinds = profile.burst ? [profile.burst.kind, ...bridgeKinds] : bridgeKinds;
        for (let k = 0; k < count; k++) {
            const at = from + (count === 1 ? 0 : k / (count - 1) * duration * .85) + rng() * .12;
            const kind = kinds[Math.floor(rng() * kinds.length)];
            const spec = burstPreset(kind, [k % 5, (k + 1) % 5, (k + 2) % 5]);
            if (kind === 'mine') spark(spec, at, width * (.2 + rng() * .6), atScreen(height * .94, at), .8 + rng() * .3, 'b' + k, false);
            else spark(spec, at, width * (.15 + rng() * .7), atScreen(height * (.14 + rng() * .34), at), .55 + rng() * .4, 'b' + k, true);
        }
    } else if (profile.burst) {
        const spec = profile.burst, radius = spec.radius * unit, cx = bounds.x + bounds.width/2, cy = bounds.y+bounds.height/2;
        const minY = height * .12, at = timing.enterAt;
        let points;
        if (spec.kind === 'mine') points = (profile.burstAt === 'sides' ? [.25, .75] : [.3 + rng() * .4]).map(x => [x * width, height * .94]);
        else if (profile.burstAt === 'above') points = [[cx + (rng() - .5) * region.w * width * .3, Math.max(minY, cy - region.h * height / 2 - radius * .45)]];
        else if (profile.burstAt === 'sides') {
            const offset = Math.min(width * .42, region.w * width / 2 + radius * .35);
            points = [[cx - offset, Math.max(minY, cy - radius * .3)], [cx + offset, Math.max(minY, cy - radius * .3)]];
        } else if (profile.burstAt === 'sky') points = [[width * (.2 + rng() * .6), atScreen(height * (.12 + rng() * .22), at + .6)]];
        else points = [[cx + (rng() - .5) * region.w * width * .15, cy - radius * .15]];
        points.forEach(([x, y], i) => spark(spec, at + i * .15, Math.min(width * .94, Math.max(width * .06, x)), y, 1, 'main' + i, profile.launch));
    }
    return { bursts, launches };
}

function packBurst(plan) {
    const stars = plan.stars.map(s => [
        s.x0, s.y0, s.vx, s.vy, s.drag, s.fall, s.start, s.life, s.size, s.color, s.endColor, s.shift, s.trailColor,
        s.trail, s.segments, s.fadeTail, s.strobe, s.strobeFrom, s.twinkle, s.glitter, s.spiral, s.spin, s.phase, s.depth, s.rank, s.tex,
    ].map(n => Math.round(n * 100000) / 100000));
    if (stars.some(star => !star.every(Number.isFinite))) throw new Error('Non-finite Hanabi star.');
    return { at: plan.at, end: plan.end, stars, flashes: plan.flashes };
}

function hanabiText(input, line, slice, profile, palette, timing, seed) {
    const { splitLyricGraphemes } = require('../../utils/lyrics/graphemeTiming');
    const { createRng } = require('hanabi/hanabiRandom');
    const { resolveHanabiGlyphColors, hanabiBackdropOf } = require('hanabi/color');
    const { mixColor } = require('hanabi/mg/mgMath');
    const { layoutText, wordScales } = require('hanabi/text/layout');
    const { enterOrder, scheduleEnter, scheduleExit } = require('hanabi/text/animations');
    const { blockLayoutParams } = require('hanabi/text/blockLayout');
    const text = line.fullText.slice(slice.startOffset, slice.endOffset);
    const chars = splitLyricGraphemes(text);
    const words = [];
    let cursor = 0;
    for (const s of line._segments ?? []) {
        const from = Math.max(slice.startOffset, s.startOffset), to = Math.min(slice.endOffset, s.endOffset);
        if (from >= to) continue;
        const part = line.fullText.slice(from, to);
        const count = splitLyricGraphemes(part).length;
        const space = !part.trim();
        words.push({ text: part, start: cursor, end: cursor + count, blank: space || !s.isWordLike, space, scale: 1 });
        cursor += count;
    }
    if (cursor !== chars.length)
        words.splice(0, words.length, ...chars.map((text, i) => ({ text, start:i, end:i+1, space:!text.trim(), blank:!/[\p{L}\p{N}]/u.test(text), scale:1 })));
    const spec = profile.text, r = profile.region;
    const scales = wordScales(words, spec.emphasis, seed);
    const glyphWord = new Array(chars.length).fill(0);
    words.forEach((word,i) => {
        word.scale = scales[i];
        for (let j = word.start; j < word.end; j++) glyphWord[j] = i;
    });
    const inputs = chars.map((char,i) => ({ char, advance: input.widths?.[char] ?? .6, blank: !char.trim(),
        upright: /[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Hangul}\u3000-\u303f\uff00-\uffef]/u.test(char), word: glyphWord[i] }));
    const unit = Math.min(input.width,input.height);
    const placement = layoutText(inputs, words, {
        layout: spec.layout, regionWidth: r.w*input.width, regionHeight:r.h*input.height,
        fontPx: spec.heroSize*unit*input.fontScale, tilt:spec.tilt, seed, ...blockLayoutParams(spec.block,spec.hardShadow),
    });
    const visible = placement.glyphs.map((g,i) => !g.blank ? i : -1).filter(i => i >= 0);
    const ranks = new Array(chars.length).fill(-1), exitRanks = [...ranks];
    const permutation = enterOrder(spec.enter, visible.length, createRng(seed+':text'));
    const shuffle = spec.exit==='scatter'||spec.exit==='dissolve'
        ? enterOrder('scramble',visible.length,createRng(seed+':exit-order')) : null;
    visible.forEach((index,i)=>{ranks[index]=permutation[i];exitRanks[index]=shuffle?shuffle[i]:i;});
    const enter = scheduleEnter(spec.enter, ranks, timing.enterAt, spec.stagger, timing.deadline);
    const exit = scheduleExit(exitRanks, timing.exitAt, timing.exitDuration);
    const first = palette.blocks[0] ?? palette.accent;
    const offset = splitLyricGraphemes(line.fullText.slice(0, slice.startOffset)).length;
    const onPaper = spec.block === 'none' || spec.block === 'outline' || spec.block === 'marker';
    const underlay = hanabiBackdropOf(palette);
    return { bounds: placement.bounds, glyphs: placement.glyphs.map((glyph,i) => {
        if (glyph.blank) return null;
        const slot = spec.block === 'band' ? glyph.row : ['tile','circle','stair','marker'].includes(spec.block) ? i : glyph.word;
        const block = palette.blocks[slot % palette.blocks.length] ?? first;
        const keyword = line.keywordColors?.[offset+i];
        const { ink, sung, flash, back } = resolveHanabiGlyphColors(palette,
            keyword ? parseInt(keyword.slice(1), 16) : onPaper ? palette.paper : palette.ink, onPaper ? underlay : block);
        const outline = ink === palette.paper || block === palette.paper ? first : palette.paper;
        const shadow = !onPaper ? mixColor(block, 0, .42) :
            spec.outline && outline === first ? (palette.blocks[1] ?? mixColor(first, 0, .45)) : first;
        return [glyph.x,glyph.y,glyph.size,glyph.rotation,glyph.boxW,glyph.boxH,glyph.word,glyph.row,enter.start[i],enter.duration,exit.start[i],exit.end[i],
            ink,sung,flash,back,outline,shadow,block];
    }) };
}

function hanabi(json) {
    const input = JSON.parse(json);
    const { compileHanabiProgram } = require('hanabi/hanabiProgram');
    const { profileOf } = require('hanabi/catalog');
    const { buildTimeline, planSpin } = require('hanabi/timeline');
    const { resolveHanabiPalette } = require('hanabi/color');
    const { planBurst } = require('hanabi/mg/burstPlan');
    const { resolveHanabiSceneFrames } = require('hanabi/hanabiSceneFrames');
    const { resolveHanabiEnterDuration } = require('hanabi/hanabiTransitions');
    const { planShotCollage, pickCollageKeyword } = require('hanabi/collage/collagePlan');
    const program = compileHanabiProgram(input.lines, input.seed, {}, { duration: input.duration });
    const theme = input.theme ?? {};
    let previous = null;
    return JSON.stringify({
        chorus: program.chorus,
        lyricEnd: program.lyricEndTime,
        shots: program.paragraphs.flatMap((paragraph, pi) => {
            const { createRng } = require('hanabi/hanabiRandom');
            const { hashVisualizerSeed } = require('visualizerSeedHash');
            const chapterSeed = input.seed + ':' + paragraph.id;
            const direction = hashVisualizerSeed(input.seed + ':spin-direction') % 2 === 0 ? 1 : -1;
            const spin = planSpin(paragraph.shots.map(shot => shot.endTime - shot.startTime),
                { amount: 1, direction, random: createRng(chapterSeed + ':spin') });
            previous = null;
            const timeline = buildTimeline(paragraph.shots.map(shot => ({
                startTime: shot.startTime,endTime:shot.endTime,firstSung:shot.isBridge?null:shot.startTime,
                lastSungEnd: shot.isBridge?null:shot.lyricEndTime,isBridge:shot.isBridge,camera:profileOf(shot.kind).camera,
            })), { unitStart:paragraph.startTime, unitEnd:paragraph.endTime, height:input.height, riseSpeed:1, motion:1,
                exitStart:paragraph.transitionOut?.startTime ?? null, spinAngles:spin.angles, spinDrift:spin.drift });
            return paragraph.shots.map((shot, si) => {
                const profile = profileOf(shot.kind);
                const region = profile.region;
                const start = shot.startTime, end = shot.endTime;
                const slice = shot.slices[0];
                const line = slice ? input.lines[slice.lineIndex] : null;
                const textSeed = input.seed + ':' + paragraph.id + ':' + shot.id + ':text';
                const timing = timeline.shots[si];
                const palette = resolveHanabiPalette(profile.palette, theme, 'mixed');
                const text = line ? hanabiText(input, line, slice, profile, palette, { ...timing, deadline: Math.max(line.startTime, shot.startTime) }, textSeed) : null;
                const bounds = text ? { ...text.bounds, x: text.bounds.x + region.cx * input.width, y: text.bounds.y + region.cy * input.height }
                    : { x: (region.cx-region.w/2)*input.width, y:(region.cy-region.h/2)*input.height, width:region.w*input.width,height:region.h*input.height };
                const unit = Math.min(input.width, input.height);
                const events = fireworkEvents(shot, profile, region, input.width, input.height, unit, chapterSeed, si, palette.glow, timing, timeline, bounds);
                const bursts = events.bursts.map(b => ({ ...b, kind: b.spec.kind, main: true, radius: b.spec.radius * unit * b.scale }));
                const launches = events.launches;
                const collage = planShotCollage({
                    seed: input.seed + ':' + paragraph.id + ':' + shot.id, family: profile.family, mood: profile.mood, isBridge: shot.isBridge,
                    block: profile.text.block, shotNumber: si, bounds,
                    screen: { x: 0, y: 0, width: input.width, height: input.height },
                    screenAt: time => ({ x:0, y:-timing.worldY-timeline.rise(time), width:input.width, height:input.height }), unit, palette,
                    enterAt: timing.enterAt, exitAt: timing.exitAt, exitDuration: timing.exitDuration, start, end,
                    keyword: pickCollageKeyword(line), motifs: [input.title, 'HANABI'].filter(Boolean), bursts, launches, amount: 1, previous,
                    opening: pi === 0 && si === 0 && shot.isBridge && input.title ? { title: input.title, artist: input.artist } : null,
                });
                previous = collage.recipe;
                const next = program.paragraphs[pi + 1];
                const transition = si === paragraph.shots.length - 1 ? paragraph.transitionOut : null;
                const track = transition && next ? (() => {
                    const { kind, startTime, endTime } = transition;
                    const enterEnd = endTime + resolveHanabiEnterDuration(kind, startTime, endTime);
                    const sample = (from, to, index, endExclusive) => Array.from({ length: 17 }, (_, i) => {
                        const time = from + (to - from) * i / 16 - (i === 16 && endExclusive ? 1e-8 : 0);
                        const layer = resolveHanabiSceneFrames(program, time, true).layers.find(layer => layer.index === index);
                        return layer ? { ...layer, rotation: layer.rotation * direction } : { alpha: 0, scale: 1, blur: 0, y: 0, rotation: 0 };
                    });
                    return { startTime, endTime, enterEnd,
                        lead: sample(startTime, endTime, pi, true),
                        leave: sample(endTime, enterEnd, pi, false),
                        enter: sample(endTime, enterEnd, pi + 1, false),
                    };
                })() : null;
                const fireworks = events.bursts.map(event => packBurst(planBurst(event, palette, { unit, amount: 1 })));
                const from = Math.min(timing.launchAt, timing.enterAt) - .25;
                const to = Math.max(timing.exitAt + timing.exitDuration + .3,
                    ...fireworks.map(burst=>burst.end+.05), ...launches.map(launch=>launch.at+launch.duration+.7),
                    ...collage.items.map(item=>item.exitAt == null ? item.at+item.duration+.55 : item.exitAt+item.exitDuration+.05));
                const count = Math.max(2,Math.ceil((to-from)*30)+1);
                const camera = Array.from({length:count},(_,i)=>{
                    const time=from+(to-from)*i/(count-1);
                    const offset=timeline.camera(time);
                    const angle=timeline.spin(time)+offset.rotation;
                    const rise=offset.scale*(timing.worldY+timeline.rise(time));
                    return [offset.dx-Math.sin(angle)*rise,offset.dy+Math.cos(angle)*rise,offset.scale,angle-timing.spin];
                });
                return { ...shot, paragraph:pi, collage: collage.items.map(item => ({ ...item, style: item.style ?? item.axis })),
                    ornaments: profile.ornaments, enterAt: timing.enterAt, exitAt: timing.exitAt, exitDuration: timing.exitDuration,
                    track, fireworks, launches, palette, text, camera, from, to };
            });
        }),
    });
}

function metaphor(json) {
    const input = JSON.parse(json);
    const { compileMetaphorProgram } = require('metaphor/metaphorProgram');
    const { resolveMetaphorPalette } = require('metaphor/metaphorPalette');
    const { buildMetaphorTokens, buildMetaphorTextTokens, buildMetaphorLayout } = require('metaphor/metaphorLayout');
    const { createVisualizerRng } = require('visualizerSeedHash');
    const { METAPHOR_EMPTY_STAGE, resolveMetaphorPose } = require('metaphor/metaphorCamera');
    const { planMetaphorFigures, planMetaphorBackdrop, layoutMetaphorNotes } = require('metaphor/metaphorFigures');
    const { planMetaphorShapes, joinMetaphorShapes, planMetaphorCollisions, METAPHOR_COLLISION_APPROACH, METAPHOR_COLLISION_LIFE } = require('metaphor/metaphorShapes');
    const program = compileMetaphorProgram(input.lines, {
        seed: input.seed, title: input.title, cutPace: 1, negative: true, transitions: true, intensity: 'normal',
    });
    const aspect = input.width / Math.max(input.height, 1);
    const measure = char => Object.hasOwn(input.widths, char) ? input.widths[char] : .6;
    const cues = program.cues.filter(cue => cue.start <= input.duration);
    const theme = input.theme ?? {};
    const palettes = [resolveMetaphorPalette(theme), resolveMetaphorPalette(theme, true)];
    return JSON.stringify({ palettes, cues: cues.map((cue, cueIndex) => {
        const rng = createVisualizerRng('metaphor:' + input.seed + ':' + cueIndex);
        const line = cue.lineIndex >= 0 ? input.lines[cue.lineIndex] : null;
        const tokens = line ? buildMetaphorTokens(line) : cue.text ? buildMetaphorTextTokens(cue.text, cue.start - 10) : [];
        let index = 0;
        const tints = tokens.map(token => {
            const end = index + token.chars.length;
            const tint = line?.keywordColors?.slice(index, end).find(color => color != null) ?? null;
            index = end;
            return tint;
        });
        const layout = tokens.some(token => !token.space)
            ? buildMetaphorLayout({ tokens, composition: cue.composition, measure, rng, tints })
            : { composition: 'block', glyphs: [], words: [], bounds: METAPHOR_EMPTY_STAGE };
        const interlude = cue.lineIndex < 0;
        const blobs = planMetaphorShapes(layout.bounds, rng, 1, interlude);
        let collisionTimes;
        if (interlude) {
            collisionTimes = [];
            const end = cue.loop > 0 ? cue.start + cue.loop : cue.end;
            for (let at = cue.start + METAPHOR_COLLISION_APPROACH;
                at < end - METAPHOR_COLLISION_LIFE && collisionTimes.length < 12; at += 5 + rng() * 4)
                collisionTimes.push(at);
        } else {
            const onsets = [...new Set(layout.words.map(word => word.t0))].sort((a,b)=>a-b);
            const first = Math.floor(rng() * Math.min(3, onsets.length));
            collisionTimes = onsets.filter((_, index) => index >= first && (index - first) % 3 === 0).slice(0, 3);
        }
        const collisions = planMetaphorCollisions(layout.bounds, rng, collisionTimes, 1, interlude);
        const backdrop = planMetaphorBackdrop(layout.bounds, rng, 1, interlude, { index: cue.lineIndex + 1, time: cue.singStart });
        const figures = planMetaphorFigures(layout.bounds, rng, 1, interlude, aspect);
        const shapes = joinMetaphorShapes(backdrop.shapes, blobs, figures.shapes);
        if (shapes.some(s => ![s.x, s.y, s.sx, s.sy, s.rot, s.born, s.param].every(Number.isFinite)))
            throw new Error('Non-finite Metaphor shape.');
        const notes = layoutMetaphorNotes([...backdrop.notes, ...figures.notes], measure);
        const shots = cue.shots.map(shot => {
            const time = new Set([shot.start, shot.end]);
            const addCut = at => {
                if (at > shot.start && at < shot.end) {
                    time.add(at - .00001);
                    time.add(at);
                }
            };
            for (let t = shot.start + .125; t < shot.end; t += .125) time.add(t);
            layout.words.forEach(word => addCut(word.t0));
            layout.glyphs.forEach(glyph => addCut(glyph.t0));
            for (let n = 1; n <= 3; n++) {
                addCut(shot.start + n * .16);
                addCut(shot.start + (shot.end - shot.start) * n / 4);
            }
            const poses = [...time].sort((a, b) => a - b).map(time => {
                const pose = resolveMetaphorPose({ shot, layout, time, aspect, motion: 1 });
                return [time, pose.x, pose.y, pose.distance, pose.roll];
            });
            if (poses.some(pose => !pose.every(Number.isFinite)))
                throw new Error('Non-finite Metaphor pose.');
            return { ...shot, poses };
        });
        return { ...cue, glyphs: layout.glyphs, shapes, collisions, notes, shots };
    }) });
}

function nebbia(json) {
    const input = JSON.parse(json);
    const { buildNebbiaLines, NEBBIA_DEFAULT_DOODLES, isIconChar } = require('nebbia/nebbiaChars');
    const { layoutNebbiaSong } = require('nebbia/nebbiaLayout');
    const { COMPOSITIONS } = require('nebbia/nebbiaCompositions');
    const unadorned = buildNebbiaLines(input.lines, input.seed, 0, []);
    const source = buildNebbiaLines(input.lines, input.seed, .3, NEBBIA_DEFAULT_DOODLES);
    const refog = 4, hold = 1.2;
    const fogAt = i => Math.min(source[i].next, source[i].end + 6) + hold;
    const linger = i => Math.max(fogAt(i), Math.min(fogAt(i) + 16, (source[i + 3]?.t ?? Infinity) + hold));
    activeStrokes = input.strokeCounts;
    activeNebbiaWidths = input.widths;
    try {
        const layout = layoutNebbiaSong(source, {
            W: input.width, H: input.height, fontScale: input.fontScale, jitter: .6,
            layouts: Object.keys(COMPOSITIONS), fogAt, visibleUntil: i => fogAt(i) + refog,
            lingerUntil: i => linger(i) + refog,
        });
        return JSON.stringify(layout.map((positions, index) => positions.map((p, at) => {
            const char = source[index].chars[at];
            const wide = isIconChar(char.ch) || /[\u2e80-\uffef]/u.test(char.ch);
            const extra = at >= unadorned[index].chars.length && isIconChar(char.ch);
            return { ...p, s: p.s * (wide ? 1 : 1.15), icon: extra ? char.ch.slice(5) : null, t: char.t };
        })));
    } finally {
        activeStrokes = null;
        activeNebbiaWidths = null;
    }
}

return { hanabi, metaphor, nebbia };

// Folia 581ae3c2, AGPL-3.0. Regenerate with Build/FoliaThemes/sync.mjs. Cold paths only.
var FoliaThemes = (function() {
const modules = { "src/utils/builtinTheme/generateBuiltinDualTheme.ts": function(exports, require) {
                                                    
const { adjustLightnessForContrast,
    clamp01,
    getContrastRatio,
    getHueDistance,
    hslToHex,
    normalizeHue, } = require("src/utils/themeColorMath.ts");
const { analyzeCoverPalette, } = require("src/utils/builtinTheme/coverPaletteAnalysis.ts");
const { pickBuiltinThemeDescriptions } = require("src/utils/builtinTheme/themeDescriptionTable.ts");
const { pickBuiltinThemeNames } = require("src/utils/builtinTheme/themeNameTable.ts");
const { ACCENT_MIN_CONTRAST,
    ACCENT_RANGES,
    BACKGROUND_TONES,
    MIN_BACKGROUND_SATURATION,
    PRIMARY_MIN_CONTRAST,
    PRIMARY_RANGES,
    SECONDARY_MIN_CONTRAST,
    SECONDARY_RANGES, } = require("src/utils/builtinTheme/themeColorRanges.ts");

// src/utils/builtinTheme/generateBuiltinDualTheme.ts
// Generates the light/dark theme pair used when no AI API key is configured. Colors are computed
// from the cover's own hues plus a harmony scheme instead of snapping onto a fixed preset, and
// every text color is pushed along its lightness axis until it clears the contrast floor, so it
// keeps its hue rather than collapsing into a hard-coded near-white / near-black.

const between = (random              , [min, max]       ) => min + random() * (max - min);

// Interpolates along the shorter arc so a base/accent pair never swings through the opposite hue.
const lerpHue = (from        , to        , amount        ) => {
    const delta = ((to - from + 540) % 360) - 180;
    return normalizeHue(from + delta * amount);
};

const buildBackgroundColor = (mode           , palette              , random              ) => {
    const tone = BACKGROUND_TONES[palette.tone];
    const lightnessRange = mode === 'dark' ? tone.darkLightness : tone.lightLightness;
    const saturationRange = mode === 'dark' ? tone.darkSaturation : tone.lightSaturation;
    // A washed-out cover should not force a washed-out ground, but it should still read calmer.
    const coverWeight = 0.55 + 0.45 * clamp01(palette.baseSaturation / 0.7);

    return hslToHex({
        h: palette.baseHue,
        s: Math.max(MIN_BACKGROUND_SATURATION, between(random, saturationRange) * coverWeight),
        l: between(random, lightnessRange),
    });
};

const buildAccentColor = (
    mode           ,
    palette              ,
    backgroundColor        ,
    primaryColor        ,
    random              ,
) => {
    const ranges = ACCENT_RANGES[mode];
    const saturation = between(random, ranges.saturation);
    const lightness = between(random, ranges.lightness);
    const accent = adjustLightnessForContrast(
        hslToHex({ h: palette.accentHue, s: saturation, l: lightness }),
        backgroundColor,
        ACCENT_MIN_CONTRAST,
    );

    // A monochrome/analogous accent can land on top of the primary text color; rotate it away.
    const collidesWithPrimary = getHueDistance(palette.accentHue, palette.baseHue) < 15
        && getContrastRatio(accent, primaryColor) < 1.3;

    if (!collidesWithPrimary) {
        return accent;
    }

    return adjustLightnessForContrast(
        hslToHex({ h: normalizeHue(palette.accentHue + 40), s: saturation, l: lightness }),
        backgroundColor,
        ACCENT_MIN_CONTRAST,
    );
};

const buildModeTheme = (
    mode           ,
    palette              ,
    random              ,
    name        ,
    description        ,
)        => {
    const backgroundColor = buildBackgroundColor(mode, palette, random);

    const primaryRanges = PRIMARY_RANGES[mode];
    const primaryColor = adjustLightnessForContrast(
        hslToHex({
            h: palette.baseHue,
            s: between(random, primaryRanges.saturation),
            l: between(random, primaryRanges.lightness),
        }),
        backgroundColor,
        PRIMARY_MIN_CONTRAST,
    );

    const accentColor = buildAccentColor(mode, palette, backgroundColor, primaryColor, random);

    const secondaryRanges = SECONDARY_RANGES[mode];
    const secondaryColor = adjustLightnessForContrast(
        hslToHex({
            h: lerpHue(palette.baseHue, palette.accentHue, 0.2 + random() * 0.4),
            s: between(random, secondaryRanges.saturation),
            l: between(random, secondaryRanges.lightness),
        }),
        backgroundColor,
        SECONDARY_MIN_CONTRAST,
    );

    return {
        name,
        description,
        backgroundColor,
        primaryColor,
        accentColor,
        secondaryColor,
        fontStyle: 'sans',
        animationIntensity: 'normal',
        wordColors: [],
        lyricsIcons: [],
        provider: 'Built-in',
    };
};

const generateBuiltinDualTheme = ({
    coverColors = [],
    random = Math.random,
}   
                           
                          
  = {})            => {
    const palette = analyzeCoverPalette(coverColors, random);
    const names = pickBuiltinThemeNames(palette, random);
    const descriptions = pickBuiltinThemeDescriptions(palette, random);

    return {
        light: buildModeTheme('light', palette, random, names.light, descriptions.light),
        dark: buildModeTheme('dark', palette, random, names.dark, descriptions.dark),
    };
};

exports.generateBuiltinDualTheme = generateBuiltinDualTheme;
},
"src/utils/themeColorMath.ts": function(exports, require) {
const { parseColorChannels } = require("src/components/visualizer/colorMix.ts");

// src/utils/themeColorMath.ts
// Hex-space color math for theme generation: RGB/HSL conversion, WCAG luminance and contrast,
// plus a lightness solver that keeps a generated color's hue while forcing it past a contrast
// floor (instead of swapping in a hard-coded fallback color).

                                                       
                                                       

const FALLBACK_THEME_COLOR = '#4f7cff';

const clamp01 = (value        ) => Math.max(0, Math.min(1, value));

const clampChannel = (value        ) => Math.max(0, Math.min(255, Math.round(value)));

const normalizeHue = (hue        ) => ((hue % 360) + 360) % 360;

const parseThemeColor = (value        )             => {
    const channels = typeof value === 'string' ? parseColorChannels(value) : null;
    if (!channels) {
        return null;
    }

    return {
        r: clampChannel(channels.r),
        g: clampChannel(channels.g),
        b: clampChannel(channels.b),
    };
};

const rgbToHex = ({ r, g, b }     ) => (
    `#${[r, g, b].map(channel => clampChannel(channel).toString(16).padStart(2, '0')).join('')}`
);

const rgbToHsl = ({ r, g, b }     )      => {
    const red = clampChannel(r) / 255;
    const green = clampChannel(g) / 255;
    const blue = clampChannel(b) / 255;
    const max = Math.max(red, green, blue);
    const min = Math.min(red, green, blue);
    const delta = max - min;
    const lightness = (max + min) / 2;

    if (delta === 0) {
        return { h: 0, s: 0, l: lightness };
    }

    const saturation = delta / (1 - Math.abs(2 * lightness - 1));

    let hue        ;
    if (max === red) {
        hue = ((green - blue) / delta) % 6;
    } else if (max === green) {
        hue = (blue - red) / delta + 2;
    } else {
        hue = (red - green) / delta + 4;
    }

    return { h: normalizeHue(hue * 60), s: clamp01(saturation), l: clamp01(lightness) };
};

const hslToRgb = ({ h, s, l }     )      => {
    const hue = normalizeHue(h);
    const saturation = clamp01(s);
    const lightness = clamp01(l);

    const chroma = (1 - Math.abs(2 * lightness - 1)) * saturation;
    const secondary = chroma * (1 - Math.abs(((hue / 60) % 2) - 1));
    const offset = lightness - chroma / 2;

    const sector = Math.floor(hue / 60) % 6;
    const [r, g, b] = sector === 0 ? [chroma, secondary, 0]
        : sector === 1 ? [secondary, chroma, 0]
            : sector === 2 ? [0, chroma, secondary]
                : sector === 3 ? [0, secondary, chroma]
                    : sector === 4 ? [secondary, 0, chroma]
                        : [chroma, 0, secondary];

    return {
        r: clampChannel((r + offset) * 255),
        g: clampChannel((g + offset) * 255),
        b: clampChannel((b + offset) * 255),
    };
};

const hexToHsl = (color        )             => {
    const parsed = parseThemeColor(color);
    return parsed ? rgbToHsl(parsed) : null;
};

const hslToHex = (hsl     ) => rgbToHex(hslToRgb(hsl));

// Linear RGB blend that stays in hex space; Theme color fields only accept hex.
const mixHexColors = (from        , to        , amount        ) => {
    const start = parseThemeColor(from);
    const end = parseThemeColor(to);

    if (!start || !end) {
        return rgbToHex(parseThemeColor(to) || parseThemeColor(from) || parseThemeColor(FALLBACK_THEME_COLOR) );
    }

    const ratio = clamp01(amount);
    return rgbToHex({
        r: start.r + (end.r - start.r) * ratio,
        g: start.g + (end.g - start.g) * ratio,
        b: start.b + (end.b - start.b) * ratio,
    });
};

const getRelativeLuminance = (color        ) => {
    const parsed = parseThemeColor(color);
    if (!parsed) return 0;

    const transform = (channel        ) => {
        const normalized = channel / 255;
        return normalized <= 0.03928
            ? normalized / 12.92
            : Math.pow((normalized + 0.055) / 1.055, 2.4);
    };

    return 0.2126 * transform(parsed.r) + 0.7152 * transform(parsed.g) + 0.0722 * transform(parsed.b);
};

const getContrastRatio = (foreground        , background        ) => {
    const luminanceA = getRelativeLuminance(foreground);
    const luminanceB = getRelativeLuminance(background);
    const [lighter, darker] = luminanceA > luminanceB
        ? [luminanceA, luminanceB]
        : [luminanceB, luminanceA];

    return (lighter + 0.05) / (darker + 0.05);
};

const getHueDistance = (a        , b        ) => {
    const distance = Math.abs(normalizeHue(a) - normalizeHue(b));
    return Math.min(distance, 360 - distance);
};

// Walks the color's lightness away from the background until the contrast floor is met, keeping
// hue and saturation intact. Returns the best-contrast candidate when the floor is unreachable.
const adjustLightnessForContrast = (
    color        ,
    background        ,
    minRatio        ,
    step = 0.02,
    maxSteps = 50,
) => {
    const hsl = hexToHsl(color);
    if (!hsl) {
        return color;
    }

    const goDarker = getRelativeLuminance(background) > 0.35;
    const direction = goDarker ? -1 : 1;

    let bestColor = hslToHex(hsl);
    let bestRatio = getContrastRatio(bestColor, background);

    for (let index = 0; index <= maxSteps; index += 1) {
        const lightness = clamp01(hsl.l + direction * step * index);
        const candidate = hslToHex({ ...hsl, l: lightness });
        const ratio = getContrastRatio(candidate, background);

        if (ratio > bestRatio) {
            bestRatio = ratio;
            bestColor = candidate;
        }

        if (ratio >= minRatio) {
            return candidate;
        }

        // Already clamped at an end of the lightness axis: no further step can help.
        if (index > 0 && (lightness === 0 || lightness === 1)) {
            break;
        }
    }

    return bestColor;
};

exports.clamp01 = clamp01;
exports.normalizeHue = normalizeHue;
exports.parseThemeColor = parseThemeColor;
exports.rgbToHex = rgbToHex;
exports.rgbToHsl = rgbToHsl;
exports.hslToRgb = hslToRgb;
exports.hexToHsl = hexToHsl;
exports.hslToHex = hslToHex;
exports.mixHexColors = mixHexColors;
exports.getRelativeLuminance = getRelativeLuminance;
exports.getContrastRatio = getContrastRatio;
exports.getHueDistance = getHueDistance;
exports.adjustLightnessForContrast = adjustLightnessForContrast;
},
"src/components/visualizer/colorMix.ts": function(exports, require) {
const { rememberBounded } = require("src/components/visualizer/textMeasureCache.ts");

// src/components/visualizer/colorMix.ts
// Shared color helpers for visualizer renderers.
const clamp = (value        , min        , max        ) => Math.max(min, Math.min(max, value));
const mix = (from        , to        , amount        ) => from + (to - from) * amount;
const FALLBACK_RGB = { r: 255, g: 255, b: 255 };

const isFiniteChannel = (value        ) => Number.isFinite(value);

const formatRgba = (channels                                     , alpha        ) => (
    `rgba(${Math.round(clamp(channels.r, 0, 255))}, ${Math.round(clamp(channels.g, 0, 255))}, ${Math.round(clamp(channels.b, 0, 255))}, ${alpha})`
);

                                                    
/**
 * A colour string read once: its channels, `fallback` for input that is meant to be a colour but
 * does not parse (empty, a broken hex or rgb()), or `passthrough` for anything else (named
 * colours, hsl(), ...) that colorWithAlpha hands back untouched.
 */
                                                         

const readColor = (normalizedColor        )              => {
    if (!normalizedColor) return 'fallback';

    if (normalizedColor.startsWith('#')) {
        const hex = normalizedColor.slice(1);
        const parse = (value        ) => Number.parseInt(value, 16);

        if (/^[0-9a-fA-F]{3}$/.test(hex)) {
            return {
                r: parse(hex[0] + hex[0]),
                g: parse(hex[1] + hex[1]),
                b: parse(hex[2] + hex[2]),
            };
        }

        if (/^[0-9a-fA-F]{6}$/.test(hex)) {
            return {
                r: parse(hex.slice(0, 2)),
                g: parse(hex.slice(2, 4)),
                b: parse(hex.slice(4, 6)),
            };
        }

        return 'fallback';
    }

    const rgbMatch = normalizedColor.match(/^rgba?\(([^)]+)\)$/);
    if (rgbMatch) {
        const [r, g, b] = rgbMatch[1].split(',').slice(0, 3).map(part => Number.parseFloat(part.trim()));
        if ([r, g, b].every(isFiniteChannel)) {
            return { r, g, b };
        }
        return 'fallback';
    }

    return 'passthrough';
};

// Renderers ask for the same few theme colours every frame, often once per glyph; the regexes
// and parseInt calls above are the expensive part, so each distinct string is read once.
const parsedColors = new Map                     ();
const PARSED_COLOR_LIMIT = 512;

const parseColor = (color        )                                              => {
    const normalized = typeof color === 'string' ? color.trim() : '';
    let parsed = parsedColors.get(normalized);
    if (parsed === undefined) {
        parsed = rememberBounded(parsedColors, normalized, readColor(normalized), PARSED_COLOR_LIMIT);
    }
    return { normalized, parsed };
};

const colorWithAlpha = (color        , alpha        ) => {
    const normalizedAlpha = clamp(alpha, 0, 1);
    const { normalized, parsed } = parseColor(color);
    if (parsed === 'passthrough') return normalized;
    return formatRgba(parsed === 'fallback' ? FALLBACK_RGB : parsed, normalizedAlpha);
};

/** The colour's channels, or null when it is not a hex or rgb() colour. Returns a fresh object. */
const parseColorChannels = (color        )                  => {
    const { parsed } = parseColor(color);
    return typeof parsed === 'string' ? null : { ...parsed };
};

const mixColors = (from        , to        , amount        , alpha = 1) => {
    const normalizedAmount = clamp(amount, 0, 1);
    const fromParsed = parseColor(from).parsed;
    const toParsed = parseColor(to).parsed;

    if (typeof fromParsed === 'string' || typeof toParsed === 'string') {
        return colorWithAlpha(normalizedAmount >= 0.5 ? to : from, alpha);
    }

    return `rgba(${Math.round(mix(fromParsed.r, toParsed.r, normalizedAmount))}, ${Math.round(mix(fromParsed.g, toParsed.g, normalizedAmount))}, ${Math.round(mix(fromParsed.b, toParsed.b, normalizedAmount))}, ${clamp(alpha, 0, 1)})`;
};

exports.colorWithAlpha = colorWithAlpha;
exports.parseColorChannels = parseColorChannels;
exports.mixColors = mixColors;
},
"src/components/visualizer/textMeasureCache.ts": function(exports, require) {
// Copyright (c) 2026 chthollyphile
// src/components/visualizer/textMeasureCache.ts
// 各模式文字测量共用的两件小事：按插入顺序淘汰的有界缓存，以及按整段前缀累计字素偏移的循环骨架。
// 测量本身（pretext 首行宽、pretext 自然宽加字距、canvas measureText）仍由各模式传入，结果与原实现逐位一致。

/**
 * Stores `value` under `key` in an insertion-ordered Map, first evicting the oldest entry when a new
 * key would push the cache past `limit`. Returns `value` so callers can `return rememberBounded(...)`.
 */
const rememberBounded =               (
    cache                   ,
    key      ,
    value        ,
    limit        ,
)         => {
    if (!cache.has(key) && cache.size >= limit) {
        const oldest = cache.keys().next();
        if (!oldest.done) cache.delete(oldest.value);
    }
    cache.set(key, value);
    return value;
};

/**
 * Cumulative grapheme offsets: `offsets[i]` is the advance of the first `i` graphemes, with
 * `offsets[0] = 0`. Each prefix is measured as a whole string (not as a sum of single glyphs) so
 * kerning and shaping across glyph boundaries stay exact; `measurePrefix` also receives the count and
 * the previous offset for modes that add tracking or keep the offsets monotonic.
 */
const measurePrefixOffsets = (
    graphemes                   ,
    measurePrefix                                                                   ,
)           => {
    const offsets = new Array        (graphemes.length + 1).fill(0);
    for (let count = 1; count <= graphemes.length; count += 1) {
        offsets[count] = measurePrefix(graphemes.slice(0, count).join(''), count, offsets[count - 1]);
    }
    return offsets;
};

exports.rememberBounded = rememberBounded;
exports.measurePrefixOffsets = measurePrefixOffsets;
},
"src/utils/builtinTheme/coverPaletteAnalysis.ts": function(exports, require) {
const { getHueDistance, hexToHsl, normalizeHue, } = require("src/utils/themeColorMath.ts");

// src/utils/builtinTheme/coverPaletteAnalysis.ts
// Reads the cover's extracted colors and decides the color skeleton of a built-in theme: which
// hue leads, which hue accents it, and how saturated/deep the backgrounds should read.

                                                                                                           
                                                    

                            
                    
                           
                          
                      
                          
                      
  

const MIN_SUPPORT_SATURATION = 0.15;
const MIN_SUPPORT_HUE_DISTANCE = 25;
const MAX_SUPPORT_HUE_DISTANCE = 160;
const MONOCHROME_SATURATION_CEILING = 0.18;

// Prefers colors that are both saturated and mid-lightness: those carry the cover's identity,
// while near-black / near-white pixels only describe its background.
const getCoverColorScore = ({ s, l }     ) => s * Math.max(0, 1 - Math.abs(l - 0.5) * 1.2);

const pickWeighted =     (random              , entries                                 )    => {
    const total = entries.reduce((sum, entry) => sum + entry.weight, 0);
    let cursor = random() * total;

    for (const entry of entries) {
        cursor -= entry.weight;
        if (cursor <= 0) {
            return entry.value;
        }
    }

    return entries[entries.length - 1].value;
};

const pickBaseColor = (parsedColors       , random              )      => {
    if (parsedColors.length === 0) {
        // No cover to read: pick a hue outright so a coverless song still varies run to run.
        return { h: random() * 360, s: 0.35 + random() * 0.3, l: 0.5 };
    }

    const scored = parsedColors.reduce((best, candidate) => (
        getCoverColorScore(candidate) > getCoverColorScore(best) ? candidate : best
    ), parsedColors[0]);

    return getCoverColorScore(scored) > 0.02 ? scored : parsedColors[0];
};

const pickSupportHue = (parsedColors       , base     ) => {
    const support = parsedColors.find(candidate => {
        if (candidate.s < MIN_SUPPORT_SATURATION) return false;
        const distance = getHueDistance(candidate.h, base.h);
        return distance >= MIN_SUPPORT_HUE_DISTANCE && distance <= MAX_SUPPORT_HUE_DISTANCE;
    });

    return support ? support.h : null;
};

const pickScheme = (
    baseSaturation        ,
    supportHue               ,
    random              ,
)                => {
    // A real second hue on the cover is the most "from this artwork" accent available.
    if (supportHue !== null && random() < 0.65) {
        return 'duotone';
    }

    return pickWeighted               (random, [
        { value: 'analogous', weight: 3 },
        { value: 'complementary', weight: 2 },
        { value: 'split', weight: 2 },
        { value: 'triadic', weight: 1.5 },
        { value: 'monochrome', weight: baseSaturation < MONOCHROME_SATURATION_CEILING ? 2.5 : 0 },
    ]);
};

const resolveAccentHue = (
    scheme               ,
    baseHue        ,
    supportHue               ,
    random              ,
) => {
    const sign = random() < 0.5 ? -1 : 1;

    switch (scheme) {
        case 'duotone':
            return supportHue ?? baseHue;
        case 'analogous':
            return baseHue + sign * (20 + random() * 15);
        case 'complementary':
            return baseHue + 150 + random() * 30;
        case 'split':
            return baseHue + (sign < 0 ? 150 : 210);
        case 'triadic':
            return baseHue + sign * 120;
        case 'monochrome':
        default:
            return baseHue;
    }
};

// Turns raw cover swatches into the hue/tone contract the built-in theme generator builds on.
const analyzeCoverPalette = (coverColors          , random              )               => {
    const parsedColors = coverColors
        .map(color => hexToHsl(color))
        .filter((color)               => color !== null);

    const base = pickBaseColor(parsedColors, random);
    const supportHue = pickSupportHue(parsedColors, base);
    const scheme = pickScheme(base.s, supportHue, random);
    const accentJitter = scheme === 'monochrome' ? 0 : (random() - 0.5) * 16;

    return {
        baseHue: normalizeHue(base.h),
        baseSaturation: base.s,
        baseLightness: base.l,
        accentHue: normalizeHue(resolveAccentHue(scheme, base.h, supportHue, random) + accentJitter),
        scheme,
        tone: pickWeighted             (random, [
            { value: 'ink', weight: 3 },
            { value: 'tinted', weight: 4 },
            { value: 'rich', weight: 2.5 },
        ]),
    };
};

exports.analyzeCoverPalette = analyzeCoverPalette;
},
"src/utils/builtinTheme/themeDescriptionTable.ts": function(exports, require) {
                                                           

// src/utils/builtinTheme/themeDescriptionTable.ts
// Atmosphere one-liners for built-in themes, bucketed by color family and light/dark. Style and
// length follow the AI theme contract's description rule (first-person, 15-30 Chinese chars) so a
// generated theme reads the same as an AI one inside Theme Park.

                                                                                   

const NEUTRAL_SATURATION_CEILING = 0.12;

const LIGHT_DESCRIPTIONS                                      = {
    warm: [
        '阳光落在窗台上，我忽然想起了那年的夏天。',
        '橘子汽水的味道，一整个下午都没有散去。',
        '我把心事晒在阳台，风一吹就全都跑掉了。',
        '热气腾腾的黄昏里，我们谁也没有先开口。',
        '像是握着一颗刚出炉的糖，烫手却舍不得放。',
    ],
    gold: [
        '麦田的风从耳边掠过，我又想起那条回家的路。',
        '午后的光把一切都镀成蜜色，时间慢得像糖。',
        '我把耳机塞进耳朵，整个世界忽然温柔起来。',
        '阳光穿过树叶的缝隙，落成一地细碎的金子。',
        '骑车经过那条巷子时，风里全是稻草的味道。',
    ],
    green: [
        '雨后的青草味钻进鼻腔，我忽然想去很远的地方。',
        '推开窗，整个春天都涌了进来，我却说不出话。',
        '阳光落在叶脉上，我数着它们发呆了一下午。',
        '山间的风很轻，把我心里的褶皱也抚平了。',
        '我躺在草地上，听见这个世界安静地生长。',
    ],
    blue: [
        '海风吹过晾晒的白衬衫，那年夏天还没有结束。',
        '推开窗是一整片蓝，我忽然不想去任何地方了。',
        '天空蓝得不像话，我把心事都寄给了云。',
        '清晨的水汽还没散，整座城市都是浅蓝色的。',
        '我沿着河堤走了很久，风一直安静地陪着我。',
    ],
    purple: [
        '薰衣草的味道很淡，像一段快要忘掉的心事。',
        '黄昏把云染成淡紫，我站在原地看了很久。',
        '梦醒之后天光微紫，我还留在昨夜的情节里。',
        '紫丁香开了满树，我却只想起一个人的名字。',
        '我把那封信折成四折，藏进了春天的口袋里。',
    ],
    neutral: [
        '灰白的天空下，我把耳机音量调到了最大。',
        '雾还没散，整座城市都像一张过曝的旧照片。',
        '我在空荡的房间里坐着，阳光淡得几乎没有。',
        '所有颜色都退去了，只剩下我和这段旋律。',
        '冬天的早晨很安静，我听见自己呼吸的声音。',
    ],
};

const DARK_DESCRIPTIONS                                      = {
    warm: [
        '余温还留在指尖，夜色已经漫过了整条街。',
        '炉火熄了，我仍坐在原地想那句没说完的话。',
        '深夜的红灯一闪一闪，像谁没能说出口的爱。',
        '我在暗处点了一支烟，把心事烧成了灰烬。',
        '热烈过后的沉默里，我听见自己的心跳。',
    ],
    gold: [
        '路灯把影子拉得很长，我一个人慢慢走回去。',
        '夜里的灯火像散落的碎金，我数着它们入睡。',
        '我在黑暗里回忆那个金色的下午，久久不愿醒。',
        '秋夜的风有点凉，可我还是不想早点回家。',
        '屋里只剩一盏暖灯，照着我还没写完的信。',
    ],
    green: [
        '深林的夜没有星星，只有风穿过树梢的声音。',
        '我在暗绿色的夜里走着，像走进了一场旧梦。',
        '夜露沾湿了裤脚，我还舍不得离开这片安静。',
        '整座山都睡着了，只有我的心事还醒着。',
        '苔藓在黑暗里悄悄蔓延，像我没说出口的思念。',
    ],
    blue: [
        '深夜的海把所有声音都吞掉了，只剩下呼吸。',
        '我在蓝色的黑暗里漂着，谁也找不到我。',
        '窗外下着雨，我把整个夜晚都听成了潮声。',
        '星星沉在海底，我在岸上等一句晚安。',
        '午夜的车窗外一片深蓝，我不想到站。',
    ],
    purple: [
        '暮色一寸寸沉下去，我的心事也跟着沉了。',
        '紫色的夜里我做了一个梦，梦里全是旧时光。',
        '霓虹把夜色染成暧昧的紫，我不敢多看一眼。',
        '我在深夜反复播放这首歌，像反复回到了过去。',
        '月光穿过窗帘的缝，把我的孤独照得很轻。',
    ],
    neutral: [
        '黑白的夜里，我把所有心事都调成了静音。',
        '墨色漫过窗台，我在这片安静里慢慢沉下去。',
        '我关掉了所有的灯，只留这首歌陪着我。',
        '深夜的房间没有颜色，只有旋律在轻轻发亮。',
        '世界安静得像一场默片，而我是唯一的观众。',
    ],
};

const resolveDescriptionFamily = ({ baseHue, baseSaturation }              )                    => {
    if (baseSaturation < NEUTRAL_SATURATION_CEILING) return 'neutral';
    if (baseHue < 45 || baseHue >= 330) return 'warm';
    if (baseHue < 105) return 'gold';
    if (baseHue < 165) return 'green';
    if (baseHue < 255) return 'blue';
    return 'purple';
};

const pickFrom = (items                   , random              ) => (
    items[Math.min(items.length - 1, Math.floor(random() * items.length))]
);

// Both sides draw from the same color family so the pair reads as one theme seen day and night.
const pickBuiltinThemeDescriptions = (
    palette              ,
    random              ,
)                                   => {
    const family = resolveDescriptionFamily(palette);

    return {
        light: pickFrom(LIGHT_DESCRIPTIONS[family], random),
        dark: pickFrom(DARK_DESCRIPTIONS[family], random),
    };
};

exports.pickBuiltinThemeDescriptions = pickBuiltinThemeDescriptions;
},
"src/utils/builtinTheme/themeNameTable.ts": function(exports, require) {
                                                           

// src/utils/builtinTheme/themeNameTable.ts
// The built-in theme naming table. Names mirror the AI theme contract's paired form
// (e.g. 忧郁破晓 / 忧郁子夜): light and dark share the Chinese prefix and differ only in the suffix,
// then both carry the English Built-in marker so a generated theme never reads as an AI one.

const BUILTIN_NAME_SUFFIX = ' Built-in';

const NEUTRAL_SATURATION_CEILING = 0.12;

const NEUTRAL_PREFIXES = ['月白', '银灰', '素墨', '雾白', '灰烬'];

const HUE_PREFIX_BUCKETS                                            = [
    { maxHue: 20, prefixes: ['绯色', '朱砂', '珊瑚', '赤霞', '石榴'] },
    { maxHue: 45, prefixes: ['琥珀', '暮光', '橘光', '麦浪', '落日'] },
    { maxHue: 70, prefixes: ['鎏金', '稻香', '芒草', '秋黄', '沙丘'] },
    { maxHue: 105, prefixes: ['新柳', '青苔', '竹露', '草木', '春芜'] },
    { maxHue: 165, prefixes: ['苔痕', '松风', '薄荷', '翡翠', '深林'] },
    { maxHue: 205, prefixes: ['潮汐', '雾蓝', '海雾', '天青', '汀洲'] },
    { maxHue: 255, prefixes: ['深蓝', '靛青', '群青', '远洋', '蓝调'] },
    { maxHue: 300, prefixes: ['紫烟', '丁香', '暮霭', '紫棠', '薰衣'] },
    { maxHue: 360, prefixes: ['玫影', '桃夭', '藕荷', '蔷薇', '胭脂'] },
];

const LIGHT_SUFFIXES = ['破晓', '晨光', '微光', '初雪', '白昼', '浮云', '日砂'];
const DARK_SUFFIXES = ['子夜', '夜色', '深潭', '暗涌', '沉夜', '夜航', '星屑'];

const pickFrom =     (items              , random              )    => (
    items[Math.min(items.length - 1, Math.floor(random() * items.length))]
);

const resolveHuePrefixes = (hue        ) => (
    HUE_PREFIX_BUCKETS.find(bucket => hue < bucket.maxHue)?.prefixes
    ?? HUE_PREFIX_BUCKETS[HUE_PREFIX_BUCKETS.length - 1].prefixes
);

// Light and dark share the hue-matched prefix; only the day/night suffix differs.
const pickBuiltinThemeNames = (
    palette              ,
    random              ,
)                                   => {
    const prefixes = palette.baseSaturation < NEUTRAL_SATURATION_CEILING
        ? NEUTRAL_PREFIXES
        : resolveHuePrefixes(palette.baseHue);
    const prefix = pickFrom(prefixes, random);

    return {
        light: `${prefix}${pickFrom(LIGHT_SUFFIXES, random)}${BUILTIN_NAME_SUFFIX}`,
        dark: `${prefix}${pickFrom(DARK_SUFFIXES, random)}${BUILTIN_NAME_SUFFIX}`,
    };
};

exports.BUILTIN_NAME_SUFFIX = BUILTIN_NAME_SUFFIX;
exports.pickBuiltinThemeNames = pickBuiltinThemeNames;
},
"src/utils/builtinTheme/themeColorRanges.ts": function(exports, require) {
                                                          

// src/utils/builtinTheme/themeColorRanges.ts
// The numeric design space of a built-in theme: how deep/tinted each background tone reads and
// which saturation/lightness band each role starts from before the contrast solver runs.

                                         

                                     

                              
                         
                          
                          
                           
  

const PRIMARY_MIN_CONTRAST = 9;
const ACCENT_MIN_CONTRAST = 3.2;
const SECONDARY_MIN_CONTRAST = 4.5;
const MIN_BACKGROUND_SATURATION = 0.04;

// Background character: how far the theme leans from near-neutral ink to a fully tinted ground.
const BACKGROUND_TONES                                      = {
    ink: {
        darkLightness: [0.055, 0.085],
        darkSaturation: [0.10, 0.22],
        lightLightness: [0.940, 0.965],
        lightSaturation: [0.05, 0.12],
    },
    tinted: {
        darkLightness: [0.075, 0.110],
        darkSaturation: [0.20, 0.36],
        lightLightness: [0.920, 0.950],
        lightSaturation: [0.10, 0.20],
    },
    rich: {
        darkLightness: [0.095, 0.140],
        darkSaturation: [0.32, 0.50],
        lightLightness: [0.900, 0.935],
        lightSaturation: [0.18, 0.30],
    },
};

// Kept off the pure #ffffff / #000000 poles and tinted toward the background hue, the way the AI
// theme contract asks for (soft cream / pale mint on dark, deep navy / plum on light).
const PRIMARY_RANGES                                                              = {
    light: { saturation: [0.15, 0.40], lightness: [0.10, 0.20] },
    dark: { saturation: [0.10, 0.30], lightness: [0.90, 0.96] },
};

const ACCENT_RANGES                                                              = {
    light: { saturation: [0.55, 0.88], lightness: [0.38, 0.55] },
    dark: { saturation: [0.55, 0.92], lightness: [0.55, 0.72] },
};

const SECONDARY_RANGES                                                              = {
    light: { saturation: [0.15, 0.45], lightness: [0.32, 0.46] },
    dark: { saturation: [0.15, 0.45], lightness: [0.62, 0.76] },
};


exports.PRIMARY_MIN_CONTRAST = PRIMARY_MIN_CONTRAST;
exports.ACCENT_MIN_CONTRAST = ACCENT_MIN_CONTRAST;
exports.SECONDARY_MIN_CONTRAST = SECONDARY_MIN_CONTRAST;
exports.MIN_BACKGROUND_SATURATION = MIN_BACKGROUND_SATURATION;
exports.BACKGROUND_TONES = BACKGROUND_TONES;
exports.PRIMARY_RANGES = PRIMARY_RANGES;
exports.ACCENT_RANGES = ACCENT_RANGES;
exports.SECONDARY_RANGES = SECONDARY_RANGES;
},
"src/utils/colorExtractor.ts": function(exports, require) {
const { extractRepresentativeColorsFromPixels } = require("src/utils/colorPalette.ts");

// src/utils/colorExtractor.ts

               
              
              
              
 

/**
 * 把图片缩到 50×50 读出像素（crossOrigin 匿名请求）。图片加载失败、没有 CORS 头、画布被污染（读像素抛
 * SecurityError）时都返回 null。silent：失败时不打 warn（墙上一批头像取色时用，失败只是回退）。
 */
const loadImagePixels = (imageUrl        , { silent = false }                       = {})                                    => (
    new Promise(resolve => {
        const img = new Image();
        img.crossOrigin = "Anonymous";

        img.onload = () => {
            const canvas = document.createElement('canvas');
            const ctx = canvas.getContext('2d');
            if (!ctx) {
                resolve(null);
                return;
            }

            // Resize for performance
            const width = 50;
            const height = 50;
            canvas.width = width;
            canvas.height = height;

            try {
                ctx.drawImage(img, 0, 0, width, height);
                resolve(ctx.getImageData(0, 0, width, height).data);
            } catch (error) {
                // 画布被跨源图片污染时 getImageData 抛错；不吞掉的话这个 Promise 永远不 resolve。
                if (!silent) console.warn("Failed to read image pixels for color extraction", error);
                resolve(null);
            }
        };

        img.onerror = (e) => {
            if (!silent) console.warn("Failed to load image for color extraction", e);
            resolve(null);
        };
        img.src = imageUrl;
    })
);

const extractVibrantColorsFromPixels = (imageData                   , count        )           => {
    const colors        = [];

    // Improved sampling: step = 2 instead of 5 for better coverage
    const step = 2;
    for (let i = 0; i < imageData.length; i += 4 * step) {
        const r = imageData[i];
        const g = imageData[i + 1];
        const b = imageData[i + 2];
        const a = imageData[i + 3];

        if (a < 128) continue; // Skip transparent

        // Calculate saturation and brightness
        const max = Math.max(r, g, b);
        const min = Math.min(r, g, b);
        const l = (max + min) / 2;
        const saturation = max === min ? 0 : (max - min) / (255 - Math.abs(max + min - 255));

        // Prefer colors with some saturation (avoid pure grays)
        // and exclude very dark or very bright colors
        if (saturation > 0.2 && l > 30 && l < 220) {
            colors.push({ r, g, b });
        } else if (colors.length < 100) {
            // Still collect some low-saturation colors as fallback
            colors.push({ r, g, b });
        }
    }

    // Sort by saturation (vibrant colors first)
    colors.sort((a, b) => {
        const satA = (Math.max(a.r, a.g, a.b) - Math.min(a.r, a.g, a.b)) / 255;
        const satB = (Math.max(b.r, b.g, b.b) - Math.min(b.r, b.g, b.b)) / 255;
        return satB - satA;
    });

    // Extract distinct colors with lower distance threshold
    const distinctColors        = [];
    const minDistance = 20; // Reduced from 30 for more variety

    for (const c of colors) {
        if (distinctColors.length >= count) break;

        const isDistinct = distinctColors.every(dc => {
            const d = Math.sqrt(
                Math.pow(c.r - dc.r, 2)
                + Math.pow(c.g - dc.g, 2)
                + Math.pow(c.b - dc.b, 2)
            );
            return d > minDistance;
        });

        if (isDistinct || distinctColors.length === 0) {
            distinctColors.push(c);
        }
    }

    return distinctColors.map(c => {
        const hexR = c.r.toString(16).padStart(2, '0');
        const hexG = c.g.toString(16).padStart(2, '0');
        const hexB = c.b.toString(16).padStart(2, '0');
        return `#${hexR}${hexG}${hexB}`;
    });
};

const extractColors = async (imageUrl        , count         = 5)                    => {
    const pixels = await loadImagePixels(imageUrl);
    return pixels ? extractVibrantColorsFromPixels(pixels, count) : [];
};

const extractRepresentativeColors = async (
    imageUrl        ,
    count         = 5,
)                    => {
    const pixels = await loadImagePixels(imageUrl);
    return pixels ? extractRepresentativeColorsFromPixels(pixels, count) : [];
};

exports.loadImagePixels = loadImagePixels;
exports.extractColors = extractColors;
exports.extractRepresentativeColors = extractRepresentativeColors;
exports.extractVibrantColorsFromPixels = extractVibrantColorsFromPixels;
},
"src/utils/colorPalette.ts": function(exports, require) {
// src/utils/colorPalette.ts
// Builds a population-weighted palette for consumers that need representative cover colors.

                         
              
              
              
                   
 

                       
                            
                   
 

const QUANTIZATION_SHIFT = 4;
const MIN_ALPHA = 128;

const getChannelRange = (colors                 , channel                 ) => {
    let min = 255;
    let max = 0;
    for (const color of colors) {
        min = Math.min(min, color[channel]);
        max = Math.max(max, color[channel]);
    }
    return max - min;
};

const splitBucket = (bucket             )                                    => {
    if (bucket.colors.length < 2) return null;

    const ranges = {
        r: getChannelRange(bucket.colors, 'r'),
        g: getChannelRange(bucket.colors, 'g'),
        b: getChannelRange(bucket.colors, 'b'),
    };
    const channel = (Object.keys(ranges)                              )
        .reduce((widest, candidate) => ranges[candidate] > ranges[widest] ? candidate : widest, 'r');
    const sorted = [...bucket.colors].sort((a, b) => a[channel] - b[channel]);
    const midpoint = bucket.weight / 2;
    let accumulatedWeight = 0;
    let splitIndex = 1;

    for (let index = 0; index < sorted.length - 1; index += 1) {
        accumulatedWeight += sorted[index].weight;
        splitIndex = index + 1;
        if (accumulatedWeight >= midpoint) break;
    }

    const left = sorted.slice(0, splitIndex);
    const right = sorted.slice(splitIndex);
    return [
        { colors: left, weight: left.reduce((sum, color) => sum + color.weight, 0) },
        { colors: right, weight: right.reduce((sum, color) => sum + color.weight, 0) },
    ];
};

const averageBucket = (bucket             )                => {
    const totals = bucket.colors.reduce((result, color) => ({
        r: result.r + color.r * color.weight,
        g: result.g + color.g * color.weight,
        b: result.b + color.b * color.weight,
    }), { r: 0, g: 0, b: 0 });
    return {
        r: Math.round(totals.r / bucket.weight),
        g: Math.round(totals.g / bucket.weight),
        b: Math.round(totals.b / bucket.weight),
        weight: bucket.weight,
    };
};

const toHex = ({ r, g, b }               ) => (
    `#${r.toString(16).padStart(2, '0')}${g.toString(16).padStart(2, '0')}${b.toString(16).padStart(2, '0')}`
);

// Uses weighted median-cut so large cover regions matter more than small, highly saturated details.
const extractRepresentativeColorsFromPixels = (
    pixels                   ,
    count         = 5,
)           => {
    if (count <= 0) return [];

    const histogram = new Map                       ();
    for (let index = 0; index < pixels.length; index += 4) {
        if (pixels[index + 3] < MIN_ALPHA) continue;
        const r = pixels[index];
        const g = pixels[index + 1];
        const b = pixels[index + 2];
        const key = ((r >> QUANTIZATION_SHIFT) << 8)
            | ((g >> QUANTIZATION_SHIFT) << 4)
            | (b >> QUANTIZATION_SHIFT);
        const existing = histogram.get(key);
        if (existing) {
            existing.r += r;
            existing.g += g;
            existing.b += b;
            existing.weight += 1;
        } else {
            histogram.set(key, { r, g, b, weight: 1 });
        }
    }

    const colors = [...histogram.values()].map(color => ({
        r: Math.round(color.r / color.weight),
        g: Math.round(color.g / color.weight),
        b: Math.round(color.b / color.weight),
        weight: color.weight,
    }));
    if (colors.length === 0) return [];

    const buckets                = [{
        colors,
        weight: colors.reduce((sum, color) => sum + color.weight, 0),
    }];
    while (buckets.length < Math.min(count, colors.length)) {
        let bucketIndex = -1;
        let bestScore = -1;
        for (let index = 0; index < buckets.length; index += 1) {
            const bucket = buckets[index];
            if (bucket.colors.length < 2) continue;
            const widestRange = Math.max(
                getChannelRange(bucket.colors, 'r'),
                getChannelRange(bucket.colors, 'g'),
                getChannelRange(bucket.colors, 'b'),
            );
            const score = widestRange * Math.sqrt(bucket.weight);
            if (score > bestScore) {
                bestScore = score;
                bucketIndex = index;
            }
        }
        if (bucketIndex < 0) break;
        const split = splitBucket(buckets[bucketIndex]);
        if (!split) break;
        buckets.splice(bucketIndex, 1, ...split);
    }

    return buckets
        .map(averageBucket)
        .sort((a, b) => b.weight - a.weight)
        .map(toHex);
};

exports.extractRepresentativeColorsFromPixels = extractRepresentativeColorsFromPixels;
} };
const loaded = {};
function require(id) {
    if (!loaded[id]) {
        const exports = loaded[id] = {};
        modules[id](exports, require);
    }
    return loaded[id];
}
return {
    generate(json) {
        const input = JSON.parse(json);
        let state = (input.seed >>> 0) || 1;
        const random = () => {
            state ^= state << 13;
            state ^= state >>> 17;
            state ^= state << 5;
            return (state >>> 0) / 4294967296;
        };
        const colors = require('src/utils/colorExtractor.ts').extractVibrantColorsFromPixels(input.pixels, 5);
        const pair = require('src/utils/builtinTheme/generateBuiltinDualTheme.ts').generateBuiltinDualTheme({ coverColors: colors, random });
        return JSON.stringify(pair);
    }
};

})();

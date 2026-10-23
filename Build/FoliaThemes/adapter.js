const modules = { /* MODULES */ };
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
    },
    match(json) {
        const input = JSON.parse(json);
        const build = require('src/components/visualizer/wordColoring.ts').buildWordColorRanges;
        return JSON.stringify(input.texts.map(text => build(text, input.colors)));
    }
};

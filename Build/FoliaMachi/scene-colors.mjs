// Palette for the exported sets. Emission levels use the luminance of the
// matching source light so that stronger chroma doesn't dim the whole pool.
export const sceneColors = {
    konbini: {
        signSaturation: 1.42,
        paint: { brand0: 0xf74470, brand2: 0x08c3b3, trim: 0x3b7e91, frame: 0x1f4350,
            vendingBody: 0xdd3c62, bike: 0x29adbf, umbrella: 0xf29331 },
        glow: { neonRim: [0xff387d, 3.05], fridgeGlow: [0x56ddff, 1.18], snackCase: [0xffbc65, 1.65] },
        lights: {},
    },
    station: {
        signSaturation: 1.46,
        paint: { brand0: 0xf65a2b, brand2: 0x00aabb, canopyColumn: 0xafdbe0, pumpBody: 0xc5e3df,
            motelBack: 0x442c64, marker: 0xffb523, truckCab: 0x247eba },
        glow: { motelNeon: [0xff497b, 2.5], pumpScreen: [0x61ffee, 1.35] },
        lights: { 'Lights/motel': [0xff557e, 1.30] },
    },
    kowloon: {
        signSaturation: 1.55,
        paint: { wallA: 0x535374, wallB: 0x786350, wallC: 0x376674, wallD: 0x806971,
            wallE: 0x725884, barWall: 0x482458, shutter: 0x448582, shutterDark: 0x345b60,
            stool: 0xc33770, stallGreen: 0x148f75, tarpRed: 0xd83053, stoolBlue: 0x326fd0, crateBlue: 0x247aba },
        glow: { barBacklight: [0xf562b4, 1.14], barStrip: [0x36dfff, 2.5], stripPink: [0xff399b, 2.2],
            stripCyan: [0x40e1ff, 2.2], pharmacy: [0x91ffd1, .82], cafe: [0xffbf73, .90] },
        lights: { 'Lights/bar': [0xff78a4, 1.10], 'Lights/neonPink': [0xff3da1, 1.56],
            'Lights/neonCyan': [0x40dffe, 1.46], 'Lights/holo': [0x4fdafb, 1.10] },
    },
};

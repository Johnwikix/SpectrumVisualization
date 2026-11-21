// Palette for the exported sets. Emission levels use the luminance of the
// matching source light so that stronger chroma doesn't dim the whole pool.
export const sceneColors = {
    konbini: {
        signSaturation: 1.42,
        paint: { brand0: 0xf55270, brand2: 0x1bbdc9, vendingBody: 0xdd3c62, bike: 0x37a1bd },
        glow: { neonRim: [0xff417a, 2.8], fridgeGlow: [0x56ddff, 1.18] },
        lights: {},
    },
    station: {
        signSaturation: 1.46,
        paint: { brand0: 0xf36045, brand2: 0x20b5c4, motelBack: 0x174652, marker: 0xffad34, truckCab: 0x287fbb },
        glow: { motelNeon: [0xff4675, 2.4] },
        lights: { 'Lights/motel': [0xff517d, 1.30] },
    },
    kowloon: {
        signSaturation: 1.55,
        paint: { wallC: 0x47606c, wallE: 0x6d5778, barWall: 0x3e2547, stallGreen: 0x197e76,
            tarpRed: 0xc9355a, stoolBlue: 0x326fd0, crateBlue: 0x247aba },
        glow: { barStrip: [0x35dbff, 2.5], stripPink: [0xff399b, 2.2], stripCyan: [0x3addea, 2.2],
            barWindow: [0xff729e, 1.05] },
        lights: { 'Lights/bar': [0xff78a4, 1.10], 'Lights/neonPink': [0xff3da1, 1.56],
            'Lights/neonCyan': [0x40dffe, 1.46], 'Lights/holo': [0x4fdafb, 1.10] },
    },
};

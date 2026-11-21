// Periodic relief in world metres. Each texel stores reflectance variation,
// two tangent slopes and roughness variation, all filtered in linear space.
const size = 256;
const lane = 390;
const srgb = x => Math.round(255 * (x <= .0031308 ? 12.92 * x : 1.055 * x ** (1 / 2.4) - .055));
const clamp = x => Math.min(1, Math.max(0, x));

function hash(x, y, seed) {
    let h = Math.imul(x + 3127, 0x85ebca6b) ^ Math.imul(y - 942, 0xc2b2ae35) ^ seed;
    h = Math.imul(h ^ h >>> 16, 0x7feb352d);
    return ((h ^ h >>> 15) >>> 0) / 4294967296;
}

function noise(u, v, count, seed) {
    const x = u * count, y = v * count;
    const i = Math.floor(x), j = Math.floor(y);
    const at = (a, b) => hash((a % count + count) % count, (b % count + count) % count, seed);
    let s = x-i, t = y-j;
    s *= s * (3-2*s);
    t *= t * (3-2*t);
    return (1-t) * ((1-s)*at(i,j) + s*at(i+1,j)) + t * ((1-s)*at(i,j+1) + s*at(i+1,j+1));
}

const edge = x => Math.abs(x - Math.round(x));

function surface(tile, u, v) {
    const fine = noise(u,v,107,6342), middle = noise(u,v,23,317), broad = noise(u,v,4,845);
    if (tile === 0) {
        // Light aggregate stands proud of the dark bituminous binder.
        const chips = clamp((fine + .32*middle - .69)*3.5);
        return [.32 + .43*chips + .15*broad, .55*chips + .07*middle, .24 + .43*chips];
    }
    if (tile === 1 || tile === 2) {
        const rows = tile === 1 ? 4 : 14, cols = tile === 1 ? 4 : 9;
        const offset = tile === 2 ? (Math.floor(v*rows) % 2) * .5 : 0;
        const joint = clamp(1 - Math.min(edge(u*cols+offset)*size/cols,
                                          edge(v*rows)*size/rows) / (tile === 1 ? 1.05 : .92));
        const field = hash(Math.floor((u*cols+offset) % cols), Math.floor(v*rows), 127);
        return [.53 + .13*field + .07*middle - .22*joint, .26*(1-joint) + fine*.07,
            .40 + .26*joint + .13*fine];
    }
    if (tile === 3) {
        const pits = clamp((.28 - fine)*4);
        return [.48 + .21*broad + .06*middle - .12*pits, .13*middle - .15*pits, .4 + .24*fine];
    }
    const seam = clamp(1 - edge(u*6)*size/6/1.7);
    return [.56 + .10*middle + .07*seam, .42*seam + .04*fine, .39 + .14*broad + .15*seam];
}

// The last 258 atlas rows hold five independent tiles with a one-texel wrap
// border at every mip. The 128..16 levels fit vertically beside the 256 level.
export const surfaceRows = 258;
export function bakeSurfaces(image) {
    const row = image.height - surfaceRows;
    for (let tile = 0; tile < 5; tile++) {
        const samples = Array.from({length:size*size}, (_,i) => surface(tile,(i%size+.5)/size,(Math.floor(i/size)+.5)/size));
        const height = (x,y) => samples[(y & 255)*size + (x & 255)][1];
        let values = new Float32Array(size*size*4);
        for (let y=0; y<size; y++) for (let x=0; x<size; x++) {
            const i = y*size+x;
            values.set([samples[i][0], .5+.4*(height(x+1,y)-height(x-1,y)),
                .5+.4*(height(x,y+1)-height(x,y-1)), samples[i][2]], i*4);
        }
        let nextY = 0;
        // A 16-texel terminal mip retains the broad stains at distant footprints.
        for (let n=size; n>=16; n>>=1) {
            const left = tile*lane + (n === size ? 0 : size+2);
            const top = row + nextY;
            for (let y=-1; y<=n; y++) for (let x=-1; x<=n; x++) {
                const pixel = 4*((y & (n-1))*n + (x & (n-1)));
                const out = 4*((top+y+1)*image.width + left+x+1);
                for (let c=0; c<3; c++) image.data[out+c] = srgb(clamp(values[pixel+c]));
                image.data[out+3] = Math.round(255*clamp(values[pixel+3]));
            }
            if (n !== size) nextY += n+2;
            if (n === 16) break;
            const half = n/2, mip = new Float32Array(half*half*4);
            for (let y=0; y<half; y++) for (let x=0; x<half; x++) {
                for (let c=0; c<4; c++) {
                    const i = 4*(2*y*n+2*x)+c;
                    mip[4*(y*half+x)+c] = .25*(values[i]+values[i+4]+values[i+4*n]+values[i+4*n+4]);
                }
            }
            values = mip;
        }
    }
}

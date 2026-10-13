// Image helpers for the asset scripts: they decode and encode images with the canvas of a page in
// headless Chrome (tools/shared/browser.mjs starts it), so no image library is needed.
import { readFileSync } from 'node:fs';
import { extname } from 'node:path';
import { fail } from '../shared/browser.mjs';

const MIME_TYPES = { '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.png': 'image/png', '.webp': 'image/webp', '.svg': 'image/svg+xml' };

/** Reads an image file into a data: URL so the page can decode it without file:// access. */
export function fileToDataUrl(filePath) {
    const mimeType = MIME_TYPES[extname(filePath).toLowerCase()];
    if (!mimeType) {
        fail(`unsupported image type: ${filePath}`);
    }

    return `data:${mimeType};base64,${readFileSync(filePath).toString('base64')}`;
}

// Duotone ramp: the darkest tones become brand charcoal, the brightest brand lime.
const DUOTONE_DARK = [0x0e, 0x11, 0x16];
const DUOTONE_LIGHT = [0xc6, 0xf4, 0x32];
// Levels are stretched between these brightness percentiles so dark and bright photos match.
const DUOTONE_LOW_PERCENTILE = 0.02;
const DUOTONE_HIGH_PERCENTILE = 0.98;
// Above 1 keeps the midtones darker, so lime stays an accent on the athlete and the kit.
const DUOTONE_GAMMA = 1.35;

/**
 * Turns an image into a charcoal-to-lime duotone in the page and returns it as a PNG data: URL.
 * Each photo's brightness range is stretched first, so a dark gym and a bright one look alike.
 */
export async function toDuotone(page, sourceDataUrl) {
    const settings = {
        dark: DUOTONE_DARK,
        light: DUOTONE_LIGHT,
        lowPercentile: DUOTONE_LOW_PERCENTILE,
        highPercentile: DUOTONE_HIGH_PERCENTILE,
        gamma: DUOTONE_GAMMA,
    };

    return page.evaluate(async ({ source, ramp }) => {
        const image = new Image();
        image.src = source;
        await image.decode();

        const canvas = document.createElement('canvas');
        canvas.width = image.naturalWidth;
        canvas.height = image.naturalHeight;
        const context = canvas.getContext('2d');
        context.drawImage(image, 0, 0);
        const pixels = context.getImageData(0, 0, canvas.width, canvas.height);
        const data = pixels.data;

        // Brightness (0..255) of every pixel, plus a histogram to find the percentiles.
        const pixelCount = data.length / 4;
        const brightness = new Float32Array(pixelCount);
        const histogram = new Array(256).fill(0);
        for (let index = 0; index < pixelCount; index++) {
            const offset = index * 4;
            const value = 0.2126 * data[offset] + 0.7152 * data[offset + 1] + 0.0722 * data[offset + 2];
            brightness[index] = value;
            histogram[Math.round(value)]++;
        }

        const findPercentile = fraction => {
            let seen = 0;
            for (let level = 0; level < 256; level++) {
                seen += histogram[level];
                if (seen >= fraction * pixelCount) return level;
            }
            return 255;
        };
        const low = findPercentile(ramp.lowPercentile);
        const high = Math.max(low + 1, findPercentile(ramp.highPercentile));

        for (let index = 0; index < pixelCount; index++) {
            const stretched = Math.min(1, Math.max(0, (brightness[index] - low) / (high - low)));
            const position = Math.pow(stretched, ramp.gamma);
            const offset = index * 4;
            for (let channel = 0; channel < 3; channel++) {
                data[offset + channel] = ramp.dark[channel] + (ramp.light[channel] - ramp.dark[channel]) * position;
            }
        }

        context.putImageData(pixels, 0, 0);
        return canvas.toDataURL('image/png');
    }, { source: sourceDataUrl, ramp: settings });
}

/**
 * Decodes an image in the page, scales it down to at most maxWidth (never up) and returns the
 * WebP bytes. quality is 0..1, as for canvas.toDataURL.
 */
export async function encodeWebp(page, sourceDataUrl, maxWidth, quality) {
    const webpDataUrl = await page.evaluate(async ({ source, widthLimit, webpQuality }) => {
        const image = new Image();
        image.src = source;
        await image.decode();

        const scale = Math.min(1, widthLimit / image.naturalWidth);
        const canvas = document.createElement('canvas');
        canvas.width = Math.round(image.naturalWidth * scale);
        canvas.height = Math.round(image.naturalHeight * scale);

        const context = canvas.getContext('2d');
        context.imageSmoothingQuality = 'high';
        context.drawImage(image, 0, 0, canvas.width, canvas.height);
        return canvas.toDataURL('image/webp', webpQuality);
    }, { source: sourceDataUrl, widthLimit: maxWidth, webpQuality: quality });

    if (!webpDataUrl.startsWith('data:image/webp')) {
        fail('this browser cannot encode WebP (canvas returned a different format)');
    }

    return Buffer.from(webpDataUrl.split(',')[1], 'base64');
}

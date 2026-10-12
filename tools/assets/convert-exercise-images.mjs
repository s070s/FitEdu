// Converts photos to WebP with headless Chrome (see browser.mjs).
//
// Exercise mode: converts the JPG pairs of every exercise listed in the repo's exercises.json
// to <target>/<slug>/0.webp and 1.webp, max 480 px wide, quality 0.8.
//   node tools/assets/convert-exercise-images.mjs <exercises.json> <download>/exercises <target folder>
//
// Single mode: converts one image (used for the Home hero).
//   node tools/assets/convert-exercise-images.mjs --single <input> <output.webp> <maxWidth> [quality]
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, dirname, join, resolve } from 'node:path';
import { encodeWebp, fail, fileToDataUrl, launchBrowser } from './browser.mjs';

const EXERCISE_MAX_WIDTH = 480;
const EXERCISE_QUALITY = 0.8;
const SINGLE_DEFAULT_QUALITY = 0.78;

/** Same rule as SlugHelper.FromExerciseId: lower-case, runs of non [a-z0-9] become "-", trim "-". */
function toSlug(exerciseId) {
    return exerciseId.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
}

function readExercises(exercisesJsonPath) {
    if (!existsSync(exercisesJsonPath)) {
        fail(`exercises.json not found: ${exercisesJsonPath}`);
    }

    const exercises = JSON.parse(readFileSync(exercisesJsonPath, 'utf8'));
    if (!Array.isArray(exercises) || exercises.length === 0) {
        fail(`no exercises in ${exercisesJsonPath}`);
    }

    return exercises;
}

async function convertExercises(exercisesJsonPath, sourceFolder, targetFolder) {
    if (!existsSync(sourceFolder)) {
        fail(`source folder not found: ${sourceFolder}`);
    }

    const exercises = readExercises(exercisesJsonPath);
    const browser = await launchBrowser();
    const page = await browser.newPage();
    let convertedCount = 0;
    let skippedCount = 0;

    for (const exercise of exercises) {
        const slugFolder = join(targetFolder, toSlug(exercise.id));
        mkdirSync(slugFolder, { recursive: true });

        // "images" holds paths like "Pushups/0.jpg", relative to the download's exercises folder.
        for (const relativeImagePath of exercise.images) {
            const outputPath = join(slugFolder, basename(relativeImagePath).replace(/\.jpe?g$/i, '.webp'));
            if (existsSync(outputPath)) {
                skippedCount++;
                continue;
            }

            const sourcePath = join(sourceFolder, relativeImagePath);
            if (!existsSync(sourcePath)) {
                fail(`missing source image: ${sourcePath}`);
            }

            const webpBytes = await encodeWebp(page, fileToDataUrl(sourcePath), EXERCISE_MAX_WIDTH, EXERCISE_QUALITY);
            writeFileSync(outputPath, webpBytes);
            convertedCount++;
        }
    }

    await browser.close();
    console.log(`exercises: ${exercises.length}, converted ${convertedCount} images, skipped ${skippedCount} existing`);
}

async function convertSingle(inputPath, outputPath, maxWidth, quality) {
    if (!existsSync(inputPath)) {
        fail(`input image not found: ${inputPath}`);
    }

    if (!Number.isFinite(maxWidth) || maxWidth <= 0) {
        fail('maxWidth must be a positive number');
    }

    if (!Number.isFinite(quality) || quality <= 0 || quality > 1) {
        fail('quality must be between 0 and 1');
    }

    const browser = await launchBrowser();
    const page = await browser.newPage();
    const webpBytes = await encodeWebp(page, fileToDataUrl(inputPath), maxWidth, quality);
    await browser.close();

    mkdirSync(dirname(outputPath), { recursive: true });
    writeFileSync(outputPath, webpBytes);
    console.log(`wrote ${outputPath} (${Math.round(webpBytes.length / 1024)} KB)`);
}

const commandLineArguments = process.argv.slice(2);
const isSingleMode = commandLineArguments[0] === '--single';

if (isSingleMode) {
    const [, inputPath, outputPath, maxWidthText, qualityText] = commandLineArguments;
    if (!inputPath || !outputPath || !maxWidthText) {
        fail('usage: --single <input> <output.webp> <maxWidth> [quality]');
    }

    const quality = qualityText === undefined ? SINGLE_DEFAULT_QUALITY : Number(qualityText);
    await convertSingle(resolve(inputPath), resolve(outputPath), Number(maxWidthText), quality);
} else {
    const [exercisesJsonPath, sourceFolder, targetFolder] = commandLineArguments;
    if (!exercisesJsonPath || !sourceFolder || !targetFolder) {
        fail('usage: <exercises.json> <download>/exercises <target folder>   (or --single ...)');
    }

    await convertExercises(resolve(exercisesJsonPath), resolve(sourceFolder), resolve(targetFolder));
}

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

/** One { sourcePath, outputPath } job per photo of the exercise; stops on a row without an id or images. */
function planExerciseImages(exercise, sourceFolder, targetFolder) {
    const hasImagePaths = Array.isArray(exercise?.images)
        && exercise.images.length > 0
        && exercise.images.every(imagePath => typeof imagePath === 'string');
    if (typeof exercise?.id !== 'string' || !hasImagePaths) {
        fail(`exercise row without an id or an "images" array of paths: ${JSON.stringify(exercise)?.slice(0, 120)}`);
    }

    const slugFolder = join(targetFolder, toSlug(exercise.id));
    // "images" holds paths like "Pushups/0.jpg", relative to the download's exercises folder.
    return exercise.images.map(relativeImagePath => ({
        sourcePath: join(sourceFolder, relativeImagePath),
        outputPath: join(slugFolder, basename(relativeImagePath).replace(/\.jpe?g$/i, '.webp'))
    }));
}

async function convertExercises(exercisesJsonPath, sourceFolder, targetFolder) {
    if (!existsSync(sourceFolder)) {
        fail(`source folder not found: ${sourceFolder}`);
    }

    // Every row and every needed source photo is checked before a folder is made or Chrome starts,
    // so a bad row stops the run without leaving half-made output behind.
    const exercises = readExercises(exercisesJsonPath);
    const jobs = exercises.flatMap(exercise => planExerciseImages(exercise, sourceFolder, targetFolder));
    const pendingJobs = jobs.filter(job => !existsSync(job.outputPath));
    const jobWithoutSource = pendingJobs.find(job => !existsSync(job.sourcePath));
    if (jobWithoutSource) {
        fail(`missing source image: ${jobWithoutSource.sourcePath}`);
    }

    if (pendingJobs.length > 0) {
        await convertPendingImages(pendingJobs);
    }

    const skippedCount = jobs.length - pendingJobs.length;
    console.log(`exercises: ${exercises.length}, converted ${pendingJobs.length} images, skipped ${skippedCount} existing`);
}

async function convertPendingImages(pendingJobs) {
    const browser = await launchBrowser();
    try {
        const page = await browser.newPage();
        for (const job of pendingJobs) {
            mkdirSync(dirname(job.outputPath), { recursive: true });
            const webpBytes = await encodeWebp(page, fileToDataUrl(job.sourcePath), EXERCISE_MAX_WIDTH, EXERCISE_QUALITY);
            writeFileSync(job.outputPath, webpBytes);
        }
    } finally {
        await browser.close();
    }
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

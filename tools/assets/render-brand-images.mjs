// Renders the brand images with headless Chrome (see ../shared/browser.mjs):
//   favicon.svg          -> favicon.png (64 px), icon-192.png (192 px)
//   og-template.html     -> images/og-home.jpg (1200x630)
//   blog-cover-template  -> images/blog/<article slug>.webp (1200x675)
//
//   node tools/assets/render-brand-images.mjs [--photos <download>/exercises] [icons] [og] [blog]
//
// No job name renders all three. Photos are turned into a charcoal/lime duotone (images.mjs)
// before they go into a template. The OG collage uses the converted exercise photos
// (convert-exercise-images.mjs). Blog covers are full-width, so they use the larger original
// JPGs from the Free Exercise DB download, passed with --photos.
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { fail, launchBrowser } from '../shared/browser.mjs';
import { encodeWebp, fileToDataUrl, toDuotone } from './images.mjs';

const TOOLS_FOLDER = dirname(fileURLToPath(import.meta.url));
const WEB_ROOT = resolve(TOOLS_FOLDER, '../../src/FitEdu.Web/wwwroot');

const ICON_SIZES = [
    { size: 64, fileName: 'favicon.png' },
    { size: 192, fileName: 'icon-192.png' },
];

const OG_SIZE = { width: 1200, height: 630 };
const OG_JPEG_QUALITY = 85;

const BLOG_COVER_SIZE = { width: 1200, height: 675 };
const BLOG_COVER_QUALITY = 0.82;
// exerciseId is the Free Exercise DB id (the download's folder name; its slug is in the comment),
// frame the start (0.jpg) or end (1.jpg) photo, focus the CSS object-position that keeps the
// athlete's head inside the wider 16:9 crop.
const BLOG_COVERS = [
    { articleSlug: 'progressive-overload-explained', exerciseId: 'Barbell_Squat', frame: '0.jpg', focus: 'center 20%' }, // barbell-squat
    { articleSlug: 'how-much-protein-do-you-need', exerciseId: 'Dumbbell_Bench_Press', frame: '0.jpg', focus: 'center' }, // dumbbell-bench-press
    { articleSlug: 'sleep-is-your-best-recovery-tool', exerciseId: 'Plank', frame: '0.jpg', focus: 'center' }, // plank
    { articleSlug: 'five-minute-warm-up-routine', exerciseId: 'Star_Jump', frame: '1.jpg', focus: 'center top' }, // star-jump, mid-jump
];

const ALL_JOBS = ['icons', 'og', 'blog'];
const PHOTOS_OPTION = '--photos';

function requireFile(filePath) {
    if (!existsSync(filePath)) {
        fail(`missing input: ${filePath}`);
    }
}

function writeOutput(relativePath, bytes) {
    const outputPath = join(WEB_ROOT, relativePath);
    mkdirSync(dirname(outputPath), { recursive: true });
    writeFileSync(outputPath, bytes);
    console.log(`wrote ${relativePath} (${Math.round(bytes.length / 1024)} KB)`);
}

/** Waits until every <img> on the page has decoded, so screenshots never catch a blank tile. */
async function waitForImages(page) {
    await page.waitForFunction(() => [...document.images].every(image => image.complete && image.naturalWidth > 0));
}

async function renderIcons(browser) {
    const svgPath = join(WEB_ROOT, 'favicon.svg');
    requireFile(svgPath);
    const svgDataUrl = fileToDataUrl(svgPath);

    for (const icon of ICON_SIZES) {
        const page = await browser.newPage({ viewport: { width: icon.size, height: icon.size } });
        await page.setContent(`<html><body style="margin:0;background:transparent">
            <img src="${svgDataUrl}" width="${icon.size}" height="${icon.size}" style="display:block"></body></html>`);
        await waitForImages(page);
        writeOutput(icon.fileName, await page.screenshot({ omitBackground: true }));
        await page.close();
    }
}

async function renderOgImage(browser) {
    const templatePath = join(TOOLS_FOLDER, 'og-template.html');
    requireFile(templatePath);

    const toolPage = await browser.newPage();
    const page = await browser.newPage({ viewport: OG_SIZE });
    await page.goto(pathToFileURL(templatePath).href);

    // Each collage tile names its exercise in data-exercise; fill it with that photo's duotone.
    const exerciseSlugs = await page.$$eval('img[data-exercise]', images => images.map(image => image.dataset.exercise));
    for (const exerciseSlug of exerciseSlugs) {
        const photoPath = join(WEB_ROOT, 'images', 'exercises', exerciseSlug, '0.webp');
        requireFile(photoPath);
        const duotoneDataUrl = await toDuotone(toolPage, fileToDataUrl(photoPath));
        await page.$eval(`img[data-exercise="${exerciseSlug}"]`, (image, source) => { image.src = source; }, duotoneDataUrl);
    }

    await waitForImages(page);
    writeOutput('images/og-home.jpg', await page.screenshot({ type: 'jpeg', quality: OG_JPEG_QUALITY }));
    await page.close();
    await toolPage.close();
}

async function renderBlogCovers(browser, photosFolder) {
    const templateUrl = pathToFileURL(join(TOOLS_FOLDER, 'blog-cover-template.html')).href;
    const toolPage = await browser.newPage();

    for (const cover of BLOG_COVERS) {
        const photoPath = join(photosFolder, cover.exerciseId, cover.frame);
        requireFile(photoPath);
        const duotoneDataUrl = await toDuotone(toolPage, fileToDataUrl(photoPath));

        const page = await browser.newPage({ viewport: BLOG_COVER_SIZE });
        await page.goto(templateUrl);
        await page.$eval('#photo', (image, { source, focus }) => {
            image.style.objectPosition = focus;
            image.src = source;
        }, { source: duotoneDataUrl, focus: cover.focus });
        await waitForImages(page);
        const pngBytes = await page.screenshot();
        await page.close();

        // Screenshots are PNG or JPEG only, so the browser's canvas re-encodes the PNG as WebP.
        const pngDataUrl = `data:image/png;base64,${pngBytes.toString('base64')}`;
        const webpBytes = await encodeWebp(toolPage, pngDataUrl, BLOG_COVER_SIZE.width, BLOG_COVER_QUALITY);
        writeOutput(`images/blog/${cover.articleSlug}.webp`, webpBytes);
    }

    await toolPage.close();
}

/** Splits the command line into the --photos folder (optional) and the job names. */
function parseArguments(commandLineArguments) {
    const photosIndex = commandLineArguments.indexOf(PHOTOS_OPTION);
    if (photosIndex === -1) {
        return { photosFolder: undefined, jobNames: commandLineArguments };
    }

    const photosValue = commandLineArguments[photosIndex + 1];
    if (!photosValue) {
        fail(`${PHOTOS_OPTION} needs a folder`);
    }

    const jobNames = commandLineArguments.filter((_, index) => index !== photosIndex && index !== photosIndex + 1);
    return { photosFolder: resolve(photosValue), jobNames };
}

const RENDERERS = { icons: renderIcons, og: renderOgImage, blog: renderBlogCovers };

const { photosFolder, jobNames } = parseArguments(process.argv.slice(2));
const jobs = jobNames.length === 0 ? ALL_JOBS : jobNames;
const unknownJobs = jobs.filter(job => !ALL_JOBS.includes(job));
if (unknownJobs.length > 0) {
    fail(`unknown job(s): ${unknownJobs.join(', ')}. Use any of: ${ALL_JOBS.join(', ')}`);
}

if (jobs.includes('blog') && !photosFolder) {
    fail(`blog covers need ${PHOTOS_OPTION} <download>/exercises (the original Free Exercise DB photos)`);
}

const browser = await launchBrowser();
for (const job of jobs) {
    await RENDERERS[job](browser, photosFolder);
}
await browser.close();

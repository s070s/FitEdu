// Starts headless Chrome through playwright-core for the scripts under tools/ (no browser download,
// nothing installed).
//
// playwright-core is not a dependency of this repo. Set PLAYWRIGHT_CORE to the folder of an existing
// copy of the package, and CHROME_PATH to a Chrome or Chromium executable when it is not in the
// default place (see tools/assets/README.md).
import { createRequire } from 'node:module';
import { existsSync } from 'node:fs';
import { join } from 'node:path';

const DEFAULT_CHROME_PATH = 'C:/Program Files/Google/Chrome/Application/chrome.exe';

const chromePath = process.env.CHROME_PATH || DEFAULT_CHROME_PATH;

/** Stops the script with a readable message instead of a stack trace. */
export function fail(message) {
    console.error(`error: ${message}`);
    process.exit(1);
}

function loadChromium() {
    const playwrightCoreFolder = process.env.PLAYWRIGHT_CORE;
    if (!playwrightCoreFolder) {
        fail('PLAYWRIGHT_CORE is not set. Set it to the folder of an existing playwright-core package.');
    }

    const entryFile = join(playwrightCoreFolder, 'index.js');
    if (!existsSync(entryFile)) {
        fail(`playwright-core not found at "${playwrightCoreFolder}". Set PLAYWRIGHT_CORE to the playwright-core package folder.`);
    }

    const require = createRequire(import.meta.url);
    return require(entryFile).chromium;
}

/** Starts headless Chrome. Call browser.close() when done. */
export async function launchBrowser() {
    if (!existsSync(chromePath)) {
        fail(`Chrome not found at "${chromePath}". Set CHROME_PATH to a Chrome or Chromium executable.`);
    }

    const chromium = loadChromium();
    // File access lets HTML templates opened from disk load the images next to them.
    return chromium.launch({ executablePath: chromePath, headless: true, args: ['--allow-file-access-from-files'] });
}

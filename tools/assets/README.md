# Asset scripts

These scripts make every image the app ships under `src/FitEdu.Web/wwwroot`. They use headless
Chrome for decoding, resizing, the duotone effect and WebP encoding, so no image library is needed.
What each file is, where it came from and its licence is recorded in
[`docs/assets/ASSET-MANIFEST.md`](../../docs/assets/ASSET-MANIFEST.md); `AssetManifestTests` fails
when a shipped image has no manifest row.

| File | What it does |
|---|---|
| `images.mjs` | Image helpers: reads images as `data:` URLs, makes the charcoal/lime duotone, encodes WebP. |
| `convert-exercise-images.mjs` | Converts the Free Exercise DB photo pairs, and single photos such as the Home hero. |
| `render-brand-images.mjs` | Renders the favicon PNGs, the social preview image and the blog covers. |
| `og-template.html`, `blog-cover-template.html`, `brand-templates.css` | The layouts the render script screenshots. |

## Requirements

- Node.js (tested with 24).
- **playwright-core**, used as a library. It is not a dependency of this repo and nothing is
  installed: set `PLAYWRIGHT_CORE` (required, no default) to the folder of an existing
  `playwright-core` package (tested with 1.61.1), for example one left in the npm cache by `npx`.
- **Chrome or Chromium**: set `CHROME_PATH` to the executable. Default:
  `C:/Program Files/Google/Chrome/Application/chrome.exe`.

Chrome is started by [`tools/shared/browser.mjs`](../shared/browser.mjs), which the responsive check
uses too. When either path is missing the scripts stop with a message naming the variable to set.

## Sources

Downloads stay outside the repo. Licences were checked on 2026-10-12; see the manifest for the
quoted licence text.

| Source | Download | Licence |
|---|---|---|
| Free Exercise DB photos (`exercises/<id>/0.jpg`, `1.jpg`) | <https://github.com/yuhonas/free-exercise-db/archive/refs/heads/main.zip> | The Unlicense (public domain) |
| Home hero, "Weight training gear (23673556034).jpg" by StockyPics | <https://commons.wikimedia.org/wiki/File:Weight_training_gear_(23673556034).jpg> | CC0 1.0 |
| Logo, icons, social preview and blog-cover layouts | this folder and `wwwroot/favicon.svg` | MIT (own work, repo licence) |

## Re-running

Run from the repository root (Git Bash). `<download>` is the extracted `free-exercise-db-main`
folder. `<staging>` is any folder outside the repo where you keep the other downloads; step 2
expects the hero photo from Wikimedia Commons saved in its `wikimedia` subfolder.

```bash
# 1. Exercise photos -> wwwroot/images/exercises/<slug>/0.webp, 1.webp (480 px wide, quality 0.8).
#    Only the exercises in the repo's exercises.json are converted (873; 3 upstream exercises have no photos).
node tools/assets/convert-exercise-images.mjs src/FitEdu.Shared/JSON/Exercises/exercises.json \
  "<download>/exercises" src/FitEdu.Web/wwwroot/images/exercises

# 2. Home hero -> wwwroot/images/hero-home.webp (1920 px wide, quality 0.78, about 275 KB).
node tools/assets/convert-exercise-images.mjs --single \
  "<staging>/wikimedia/Weight_training_gear_(23673556034).jpg" src/FitEdu.Web/wwwroot/images/hero-home.webp 1920 0.78

# 3. favicon.png (64), icon-192.png, images/og-home.jpg (1200x630) and images/blog/<slug>.webp (1200x675).
#    The OG collage uses the photos from step 1; the blog covers use the larger originals.
node tools/assets/render-brand-images.mjs --photos "<download>/exercises"

# Render only some outputs: any of icons, og, blog (blog needs --photos).
node tools/assets/render-brand-images.mjs icons og
```

The exercise converter skips files that already exist, so delete
`wwwroot/images/exercises` first to convert everything again. The other outputs are always
overwritten.

Exercise folders use the slug rule of `SlugHelper.FromExerciseId`: lower-case the id, replace every
run of characters other than `a-z0-9` with `-`, trim `-` (`3_4_Sit-Up` becomes `3-4-sit-up`). Check
a run with:

```bash
find src/FitEdu.Web/wwwroot/images/exercises -name '*.webp' | wc -l   # 1746
ls src/FitEdu.Web/wwwroot/images/exercises | LC_ALL=C sort | diff - <(tr -d '\r' < docs/assets/exercise-slugs.txt)
```

To change the art, edit `wwwroot/favicon.svg` or the templates, re-run step 3 and look at every
output before committing. A new image file also needs a row in `docs/assets/asset-manifest.json`.

# Asset manifest

Every image the app ships (`src/FitEdu.Web/wwwroot/images/**`, `favicon.*`, `icon-*.png`) is
public domain, CC0 or our own work. [`asset-manifest.json`](asset-manifest.json) is the
machine-readable record: one row per asset family with a path glob relative to `wwwroot`.
`tests/FitEdu.Web.Tests/AssetManifestTests.cs` fails when a shipped image matches no row, when a
row matches no file, or when a row's licence is not one of `Unlicense`, `CC0-1.0`,
`Public domain`, `MIT (own work)`. The scripts that produce the files are in
[`tools/assets/`](../../tools/assets/README.md).

## Shipped assets

All sources were downloaded and their licences checked on **2026-10-12**.

| Files (under `wwwroot`) | Source | Licence | Where the licence is stated | Notes |
|---|---|---|---|---|
| `images/exercises/<slug>/0.webp`, `1.webp` (873 exercises, 1746 files, about 28 MB) | Free Exercise DB, <https://github.com/yuhonas/free-exercise-db>, ZIP of branch `main` | The Unlicense | <https://github.com/yuhonas/free-exercise-db/blob/main/LICENSE.md>; GitHub API licence `spdx_id` "Unlicense" | Start and end photo per exercise, 480 px wide WebP. The data and photos come from <https://github.com/wrkout/exercises.json> (also the Unlicense). |
| `images/hero-home.webp` | "Weight training gear (23673556034).jpg" by StockyPics, <https://commons.wikimedia.org/wiki/File:Weight_training_gear_(23673556034).jpg> (Flickr original: <https://www.flickr.com/photos/stockypics/23673556034/>) | CC0 1.0 | File page, Licensing section | Home hero, 1920x1440 WebP from the 4896x3672 original. |
| `images/blog/*.webp` | Free Exercise DB photos (as above) | The Unlicense | as above | Four 1200x675 duotone covers: barbell-squat, dumbbell-bench-press, plank, star-jump. |
| `images/og-home.jpg` | Own layout, `tools/assets/og-template.html` | MIT (own work) | [`LICENSE`](../../LICENSE) | 1200x630 social preview. The six collage frames are Free Exercise DB photos (Unlicense). |
| `favicon.svg`, `favicon.png` (64 px), `icon-192.png` | Own artwork, `wwwroot/favicon.svg` | MIT (own work) | [`LICENSE`](../../LICENSE) | Dumbbell mark in the brand palette; the PNGs are rendered from the SVG. |

## Licence text as seen on 2026-10-12

**Free Exercise DB** (`LICENSE.md`, the Unlicense), opening and dedication:

> This is free and unencumbered software released into the public domain.
>
> Anyone is free to copy, modify, publish, use, compile, sell, or distribute this software, either
> in source code form or as a compiled binary, for any purpose, commercial or non-commercial, and
> by any means.
>
> In jurisdictions that recognize copyright laws, the author or authors of this software dedicate
> any and all copyright interest in the software to the public domain.

**Hero photo** (Wikimedia Commons file page): licence template `{{cc-zero}}`, shown as
"Creative Commons Zero, Public Domain Dedication". The Flickr licence review passed:
FlickreviewR 2, review date 2020-12-15, reviewed licence `cc-zero`. Attribution is not required;
we credit the author here anyway.

## Excluded

| What | Why |
|---|---|
| Pexels, Unsplash, Pixabay photos | Their own licences, not CC0 or public domain. |
| wger exercise images | CC BY-SA (attribution and share-alike). |
| Everkinetic exercise illustrations | CC BY-SA. |
| Any Wikimedia Commons file whose page shows CC BY, CC BY-SA or GFDL | Attribution or share-alike terms. Only CC0 and public-domain files qualify. |
| exrx.net text and media | Copyrighted. Used as a factual reference only; nothing is copied. |
| Free Exercise DB exercises without photos: `Kettlebell_Halo`, `Kettlebell_Halo_With_Overhead_Extension`, `Kettlebell_Overhead_Triceps_Extension` | No photos to ship, so they are left out of the library and the image set (873 of 876 kept). |
| The previous `images/background.png` (classroom photo), `og-home.jpg`, `favicon.png`, `icon-192.png` | Provenance unknown. Deleted or replaced by the files above. |

Some exercise photos show small maker logos on clothing or gym equipment. They are incidental
parts of public-domain photos and are not used as branding.

## Adding an asset

1. Use only public-domain, CC0 or own-work files. Check the licence on the source page on the
   download day.
2. Add a row to `asset-manifest.json` (glob, name, source URL, licence, where it is stated, date,
   type) and a line to the table above.
3. Run `dotnet test tests/FitEdu.Web.Tests --filter "Category=Assets"`.

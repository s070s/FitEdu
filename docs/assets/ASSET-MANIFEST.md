# Asset manifest

Every image the app ships (`src/FitEdu.Web/wwwroot/images/**`, `favicon.*`, `icon-*.png`) is
public domain, CC0 or our own work. [`asset-manifest.json`](asset-manifest.json) is the
machine-readable record: one row per asset family with a path glob relative to `wwwroot`.
`tests/FitEdu.Web.Tests/AssetManifestTests.cs` fails when a shipped image matches no row, when a
row matches no file, or when a row's licence is not one of `Unlicense`, `CC0-1.0`,
`Public domain`, `MIT (own work)`; that list is kept in the test, so it also fails when the manifest's
`allowedLicences` differs from it. The scripts that produce the files are in
[`tools/assets/`](../../tools/assets/README.md). Third-party code libraries are listed separately
under [Third-party code (not media)](#third-party-code-not-media) and the seed data under
[Seed data (not media)](#seed-data-not-media); neither is part of the image guard.

## Shipped assets

All sources were downloaded and their licences checked on **2026-10-12**.

| Files (under `wwwroot`) | Source | Licence | Where the licence is stated | Notes |
|---|---|---|---|---|
| `images/exercises/<slug>/0.webp`, `1.webp` (873 exercises, 1746 files, about 28 MB) | Free Exercise DB, <https://github.com/yuhonas/free-exercise-db>, ZIP of branch `main` | The Unlicense | <https://github.com/yuhonas/free-exercise-db/blob/main/LICENSE.md>; GitHub API licence `spdx_id` "Unlicense" | Start and end photo per exercise, 480 px wide WebP. The data and photos come from <https://github.com/wrkout/exercises.json> (also the Unlicense). The licence covers the repository as a whole; per-image authorship is not published, so the credit is "free-exercise-db contributors". |
| `images/hero-home.webp` | "Weight training gear (23673556034).jpg" by StockyPics, <https://commons.wikimedia.org/wiki/File:Weight_training_gear_(23673556034).jpg> (Flickr original: <https://www.flickr.com/photos/stockypics/23673556034/>) | CC0 1.0 | File page, Licensing section | Home hero, 1920x1440 WebP from the 4896x3672 original. |
| `images/blog/*.webp` | Free Exercise DB photos (as above), credit "free-exercise-db contributors" | The Unlicense | as above | Four 1200x675 duotone covers: barbell-squat, dumbbell-bench-press, plank, star-jump. Rendered from the original JPGs of the download (frame `0.jpg`; star-jump uses `1.jpg`). |
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

The licence file sits at the repository root and covers the repository as a whole: the exercise
data and every photo in it. There is no per-image licence and no per-image author list, so the
photos are credited to "free-exercise-db contributors".

**Hero photo** (Wikimedia Commons file page): licence template `{{cc-zero}}`, shown as
"Creative Commons Zero, Public Domain Dedication". The Flickr licence review passed:
FlickreviewR 2, review date 2020-12-15, reviewed licence `cc-zero`. Attribution is not required;
we credit the author here anyway.

## Seed data (not media)

Text the app seeds into the database. It is not an image, so it is not a row in
`asset-manifest.json`.

| Files (under `src/FitEdu.Shared/JSON`) | Source | Licence | Where the licence is stated |
|---|---|---|---|
| `Exercises/exercises.json` (873 exercises: names, muscles, equipment, English steps) | Free Exercise DB `dist/exercises.json`, <https://github.com/yuhonas/free-exercise-db>, checked on 2026-10-12 | The Unlicense | <https://github.com/yuhonas/free-exercise-db/blob/main/LICENSE.md>; details in [`Exercises/README.md`](../../src/FitEdu.Shared/JSON/Exercises/README.md) |
| `Exercises/exercises.el.json`, `Exercises/el/*.el.json` (Greek names and steps) | Our own translation of the rows above | MIT (own work) | [`LICENSE`](../../LICENSE) |
| `Courses`, `Assessments`, `Paths`, `Showcase`, `Students` | Our own writing | MIT (own work) | [`LICENSE`](../../LICENSE) |

## Third-party code (not media)

These are code libraries, not media assets, so they are not rows in `asset-manifest.json`. Each
keeps its own licence.

| Library | Version | How it is loaded | Licence | Where the licence is stated |
|---|---|---|---|---|
| Bootstrap (CSS) | 5.3.3 | Vendored in `wwwroot/lib/bootstrap` | MIT | Header of `bootstrap.min.css`; <https://github.com/twbs/bootstrap/blob/main/LICENSE> |
| Bootstrap Icons | 1.11.3 | jsDelivr CDN, linked from `App.razor` | MIT | <https://github.com/twbs/icons/blob/main/LICENSE> |
| Quill (rich-text editor) | 2.0.3 | jsDelivr CDN, linked from `App.razor` | BSD-3-Clause | <https://github.com/slab/quill/blob/main/LICENSE> |
| Font Awesome Free | 6.5.1 | cdnjs CDN, linked from `App.razor` | Icons CC BY 4.0, fonts SIL OFL 1.1, code MIT | <https://fontawesome.com/license/free> (attribution is kept in the file headers) |

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

A new front-end code library goes in the [Third-party code](#third-party-code-not-media) table
instead, with its version, how it is loaded and its licence.

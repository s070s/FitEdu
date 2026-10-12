# Exercise library seed data

`exercises.json` is a trimmed copy of the **Free Exercise DB**. Do not hand-edit it: regenerate it
with the commands below so it always matches the upstream data.

## Source and licence

| | |
|---|---|
| Dataset | Free Exercise DB, `dist/exercises.json` on branch `main` — <https://github.com/yuhonas/free-exercise-db> (its data comes from <https://github.com/wrkout/exercises.json>) |
| Download | <https://github.com/yuhonas/free-exercise-db/archive/refs/heads/main.zip> |
| Licence | The Unlicense (public domain dedication) — <https://github.com/yuhonas/free-exercise-db/blob/main/LICENSE.md>. No attribution is required; we credit the project anyway. |
| Downloaded and licence checked | 2026-10-12 |

The exercise text (names, instructions) is public domain and may be reused word for word. The
matching photos (`exercises/<id>/0.jpg`, `1.jpg` in the same download) are not stored here; the app
expects them as `src/FitEdu.Web/wwwroot/images/exercises/<slug>/0.webp` and `1.webp`.

## How it was trimmed

The download has 876 exercises. Three have no photos and are left out everywhere
(`Kettlebell_Halo`, `Kettlebell_Halo_With_Overhead_Extension`,
`Kettlebell_Overhead_Triceps_Extension`), leaving **873**. Every other field is kept unchanged,
including the original `id` (the seeder turns it into the slug) and the `images` array.

Run from Git Bash in the repository root, with `<download>` pointing at the extracted
`free-exercise-db-main` folder:

```bash
jq '[.[] | select((.images|length)>=2) | {id, name, force, level, mechanic, equipment, primaryMuscles, secondaryMuscles, instructions, category, images}]' \
  "<download>/dist/exercises.json" > src/FitEdu.Shared/JSON/Exercises/exercises.json

jq -r '.[].id | ascii_downcase | gsub("[^a-z0-9]+"; "-") | ltrimstr("-") | rtrimstr("-")' \
  src/FitEdu.Shared/JSON/Exercises/exercises.json | tr -d '\r' | LC_ALL=C sort > docs/assets/exercise-slugs.txt
```

The second command rebuilds `docs/assets/exercise-slugs.txt` (one slug per line, sorted), the list
course authors pick slugs from.

## Slugs

A slug is the `id` lower-cased, with every run of characters other than `a-z` and `0-9` replaced by
one `-`, and leading or trailing `-` removed (`3_4_Sit-Up` → `3-4-sit-up`). The rule lives in
`SlugHelper.FromExerciseId` (`src/FitEdu.Application/Common/SlugHelper.cs`).

## Greek translations

Greek text is kept out of `exercises.json` so the upstream copy stays untouched. The seeder reads:

1. `exercises.el.json` in this folder, if it exists, then
2. every `el/*.el.json` file, in ordinal file-name order.

`exercises.el.json` holds one row for each of the 140 exercises the seed courses use (a course
exercise without a Greek name fails `SeedContentIntegrityTests`). `el/` is empty and takes extra
fragment files when more exercises are translated.

Each file is a JSON array of rows:

```json
[
  { "slug": "pushups", "nameEl": "Κάμψεις", "instructionsEl": ["Βήμα ένα.", "Βήμα δύο."] }
]
```

Names use sentence case, as Greek does: only the first word of the name and of any brackets
starts with a capital (`Πιέσεις ώμων με αλτήρες (Dumbbell shoulder press)`), proper nouns such as
Arnold aside; `SeedContentIntegrityTests` checks it.

The first row for a slug wins; later rows for the same slug are skipped with a warning. A row whose
slug is not in `exercises.json` is skipped with a warning. A missing Greek name or instruction list
falls back to English in the app.

## Seeding

`ExerciseSeeder` loads this folder in Development when `SeedData:Enabled` is true, after the blog
articles and before the courses. The folder path is `SeedData:JsonPaths:Exercises` in
`appsettings.Development.json`. It seeds only when the `Exercises` table is empty, so a change here
needs an empty table or a fresh database.

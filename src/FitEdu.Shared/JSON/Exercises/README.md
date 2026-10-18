# Exercise library seed data

`exercises.json` is a trimmed and edited copy of the **Free Exercise DB**. Only the `instructions`
text was edited (see [Edits to the instructions](#edits-to-the-instructions)); every other field
matches the upstream data.

## Source and licence

| | |
|---|---|
| Dataset | Free Exercise DB, `dist/exercises.json` on branch `main` — <https://github.com/yuhonas/free-exercise-db> (its data comes from <https://github.com/wrkout/exercises.json>) |
| Download | <https://github.com/yuhonas/free-exercise-db/archive/refs/heads/main.zip> |
| Licence | The Unlicense (public domain dedication) — <https://github.com/yuhonas/free-exercise-db/blob/main/LICENSE.md>. No attribution is required; we credit the project anyway. |
| Downloaded and licence checked | 2026-10-12 |

The exercise text (names, instructions) is public domain and may be reused and edited freely; the
licence is unchanged by our edits. The matching photos (`exercises/<id>/0.jpg`, `1.jpg` in the same download) are not stored here; the app
expects them as `src/FitEdu.Web/wwwroot/images/exercises/<slug>/0.webp` and `1.webp`.

## How it was trimmed

The download has 876 exercises. Three have no photos and are left out everywhere
(`Kettlebell_Halo`, `Kettlebell_Halo_With_Overhead_Extension`,
`Kettlebell_Overhead_Triceps_Extension`), leaving **873**. Every other field is kept unchanged,
including the original `id` (the seeder turns it into the slug) and the `images` array.

Re-running the commands below rebuilds the upstream copy and discards the instruction edits
described in the next section, so only do it to start over from upstream, and redo those edits
afterwards. From Git Bash in the repository root, with `<download>` pointing at the extracted
`free-exercise-db-main` folder:

```bash
jq '[.[] | select((.images|length)>=2) | {id, name, force, level, mechanic, equipment, primaryMuscles, secondaryMuscles, instructions, category, images}]' \
  "<download>/dist/exercises.json" > src/FitEdu.Shared/JSON/Exercises/exercises.json

jq -r '.[].id | ascii_downcase | gsub("[^a-z0-9]+"; "-") | ltrimstr("-") | rtrimstr("-")' \
  src/FitEdu.Shared/JSON/Exercises/exercises.json | tr -d '\r' | LC_ALL=C sort > docs/assets/exercise-slugs.txt
```

The second command rebuilds `docs/assets/exercise-slugs.txt` (one slug per line, sorted), the list
course authors pick slugs from.

## Edits to the instructions

The English `instructions` are an edited copy of the upstream text (edited 2026-10-18):

- Typos and grammar fixed (for example "asborbing" is now "absorbing"; "Lay on the bench" is now
  "Lie on the bench"), and stray spaces and punctuation tidied.
- British spelling (centre, tyre, programme, stabilise, pedalling).
- Metric units: inches, feet, yards, pounds and miles per hour were converted to centimetres,
  metres, kilograms and km/h, rounded to gym-friendly values (6 inches is 15 cm, 25 lb is 10 kg).

Only strings inside `instructions` changed. The `id` (and so the slug), `name`, `images` and every
other field match upstream, and every exercise keeps its number of steps, so the Greek step counts
in `exercises.el.json` still match.

## Slugs

A slug is the `id` lower-cased, with every run of characters other than `a-z` and `0-9` replaced by
one `-`, and leading or trailing `-` removed (`3_4_Sit-Up` → `3-4-sit-up`). The rule lives in
`SlugHelper.FromExerciseId` (`src/FitEdu.Application/Common/SlugHelper.cs`).

## Greek translations

Greek text is kept out of `exercises.json` so the upstream copy stays untouched. The seeder reads:

1. `exercises.el.json` in this folder, if it exists, then
2. every `el/*.el.json` file, in ordinal file-name order.

`exercises.el.json` holds one row for every one of the 873 exercises, sorted by slug, with as
many Greek steps as English ones; `SeedContentIntegrityTests` fails when an exercise has no Greek
name or a different step count. `el/` is empty and takes extra fragment files, for example a
batch of new translations before it is merged into `exercises.el.json`.

Each file is a JSON array of rows:

```json
[
  { "slug": "pushups", "nameEl": "Κάμψεις", "instructionsEl": ["Βήμα ένα.", "Βήμα δύο."] }
]
```

Names use sentence case, as Greek does: only the first word of the name and of any brackets
starts with a capital (`Πιέσεις ώμων με αλτήρες (Dumbbell shoulder press)`), proper nouns such as
Arnold or Smith and letter names such as EZ, SMR, T-bar or V-up aside; `SeedContentIntegrityTests`
checks it. Commands address the reader as εσύ and use the imperative (`Επίστρεψε`, `Κράτα`).

The first row for a slug wins; later rows for the same slug are skipped with a warning. A row whose
slug is not in `exercises.json` is skipped with a warning, and so is a row whose name or joined
instructions are longer than the `NameEl` (150) or `InstructionsEl` (4000) column, because SQL Server
would refuse the whole save. A missing Greek name or instruction list falls back to English in the
app. A Greek file that cannot be read, is not valid JSON or holds no array is skipped with a logged
error or warning; the other files still apply.

## Seeding

`ExerciseSeeder` loads this folder in Development when `SeedData:Enabled` is true, after the blog
articles and before the courses. The folder path is `SeedData:JsonPaths:Exercises` in
`appsettings.Development.json`. It seeds only when the `Exercises` table is empty, so a change here
needs an empty table or a fresh database. An `exercises.json` row without an id, name, level or
category, or whose slug repeats an earlier row's, is skipped with a warning; the other rows still seed.

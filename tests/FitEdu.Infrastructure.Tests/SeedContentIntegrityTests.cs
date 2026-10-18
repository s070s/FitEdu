using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

/// <summary>
/// Content gates for the real seed files under src/FitEdu.Shared/JSON and the resx files:
/// counts, Greek text, exercise references and photos, and no career-era vocabulary.
/// Each fact lists every problem it finds, so one run shows all the fixes a content file needs.
/// </summary>
[Trait("Category", "SeedContent")]
public partial class SeedContentIntegrityTests
{
    private const int ExpectedBlogArticleCount = 4;
    private const string RatingQuestionType = "rating";
    private const string AssessmentSchemaVersion = "1.0";
    private const string EnglishResxPattern = "*.resx";
    private const string GreekResxSuffix = ".el.resx";
    private const string AllFilesPattern = "*";

    private static readonly string ExerciseImagesFolder = Path.Combine(RepoPaths.WebRoot, "images", "exercises");

    // The slugs the exercise image folders were made from (tools/assets/README.md).
    private static readonly string ExerciseSlugListPath = Path.Combine(RepoPaths.RepoRoot, "docs", "assets", "exercise-slugs.txt");

    // Every exercise photo pair is a start and an end frame.
    private static readonly string[] ExerciseImageFileNames = ["0.webp", "1.webp"];

    // The seven course categories and their Greek names (they become the Category rows).
    private static readonly Dictionary<string, string> CourseCategoryGreekNames = new(StringComparer.Ordinal)
    {
        ["Strength"] = "Δύναμη",
        ["Cardio"] = "Κάρντιο",
        ["Mobility"] = "Κινητικότητα",
        ["Core"] = "Κορμός",
        ["Nutrition"] = "Διατροφή",
        ["Recovery"] = "Αποκατάσταση",
        ["Performance"] = "Απόδοση"
    };

    // The question types the student form renders ("multi-select" is an older spelling it still reads).
    private static readonly HashSet<string> KnownQuestionTypes = new(StringComparer.Ordinal)
    {
        RatingQuestionType, "choice", "multiselect", "multi-select", "text"
    };

    // The blog tag vocabulary (tags stay English keywords in both languages).
    private static readonly HashSet<string> BlogTags = new(StringComparer.Ordinal)
    {
        "Strength", "Nutrition", "Recovery", "Mobility", "Beginners"
    };

    // Greek values that show a person's name, where every word is capitalised.
    private static readonly HashSet<string> GreekPersonNameKeys = new(StringComparer.Ordinal)
    {
        "AdminRes.TestimonialNamePlaceholder"
    };

    private static readonly string SourceRoot = Path.Combine(RepoPaths.RepoRoot, "src");

    // Where the text gates (career vocabulary, Greek command forms) look: the seed JSON, every
    // resx file, the seed data written in C# (blog articles, plans, the seeders) and the other
    // member-facing text written in C# (the assistant's system prompt, the certificate PDF, the emails, the Swagger request examples).
    private static readonly TextScanRoot[] TextScanRoots =
    [
        new(RepoPaths.SeedJsonRoot, AllFilesPattern),
        new(RepoPaths.ResourcesRoot, EnglishResxPattern),
        new(Path.Combine(SourceRoot, "FitEdu.Application", "DTOs", "Blog"), "BlogSeedData.cs"),
        new(Path.Combine(SourceRoot, "FitEdu.Infrastructure", "Seeding"), "*.cs"),
        new(Path.Combine(SourceRoot, "FitEdu.Web", "Startup"), "DatabaseSeedingExtensions.cs"),
        new(Path.Combine(SourceRoot, "FitEdu.Application", "ApiServices"), "AssistantService.cs"),
        new(Path.Combine(SourceRoot, "FitEdu.Infrastructure", "ApiServices"), "CertificateService.cs"),
        new(Path.Combine(SourceRoot, "FitEdu.Infrastructure", "ApiServices"), "EmailTemplates.cs"),
        new(Path.Combine(SourceRoot, "FitEdu.Web", "OpenApi"), "OpenApiExtensions.cs")
    ];

    // ---- Courses -----------------------------------------------------------------------------

    [Fact]
    public void Courses_AreExactlyNineteen_WithBilingualHeaderAndFitnessFields()
    {
        var courseFiles = SeedJsonFiles.LoadCourses();

        var problems = courseFiles.SelectMany(FindCourseHeaderProblems).ToList();

        courseFiles.Count.ShouldBe(SeedJsonFiles.ExpectedCourseCount);
        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Courses_CategoryIsOneOfTheSeven_WithItsExactGreekName()
    {
        var problems = new List<string>();

        foreach (var (fileName, course) in SeedJsonFiles.LoadCourses())
        {
            var category = course.Category ?? string.Empty;
            if (!CourseCategoryGreekNames.TryGetValue(category, out var greekName))
                problems.Add($"{fileName}: unknown category '{category}'");
            else if (course.CategoryEl != greekName)
                problems.Add($"{fileName}: categoryEl '{course.CategoryEl}' should be '{greekName}'");
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Courses_EquipmentUsesOnlyKnownCodes()
    {
        var problems = new List<string>();

        foreach (var (fileName, course) in SeedJsonFiles.LoadCourses())
        {
            // No trimming: the catalogue filter matches ",code," exactly, so " bands" would never match.
            var codes = (course.Equipment ?? string.Empty).Split(Course.EquipmentSeparator);
            problems.AddRange(codes
                .Where(code => !Course.EquipmentCodes.Contains(code))
                .Select(code => $"{fileName}: unknown equipment code '{code}'"));
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Courses_EveryModuleAndLessonHasGreekText_AndEveryLessonHasContentOrExercises()
    {
        var problems = new List<string>();

        foreach (var (fileName, course) in SeedJsonFiles.LoadCourses())
        {
            foreach (var module in course.Modules)
                problems.AddRange(FindModuleProblems($"{fileName} / {module.Title}", module));
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Courses_PrescriptionRepsUseLanguageNeutralNotation()
    {
        var problems = FindPrescriptions()
            .Where(prescription => !IsLanguageNeutralReps(prescription.Exercise.Reps))
            .Select(prescription => $"{prescription.Location} / {prescription.Exercise.Slug}: reps '{prescription.Exercise.Reps}'")
            .ToList();

        problems.ShouldBeEmpty("reps show unchanged in both languages: keep numbers and unit symbols, move words to notes/notesEl");
    }

    [Fact]
    public void Courses_EquipmentAndPrescriptionsFitTheirColumns()
    {
        // SQLite ignores HasMaxLength, so a too-long value would only fail on SQL Server.
        var equipmentMaxLength = ColumnMaxLength<Course>(nameof(Course.Equipment));
        var repsMaxLength = ColumnMaxLength<LessonExercise>(nameof(LessonExercise.Reps));
        var notesMaxLength = ColumnMaxLength<LessonExercise>(nameof(LessonExercise.NotesEn));
        var notesElMaxLength = ColumnMaxLength<LessonExercise>(nameof(LessonExercise.NotesEl));

        var problems = SeedJsonFiles.LoadCourses()
            .Where(file => file.Content.Equipment?.Length > equipmentMaxLength)
            .Select(file => $"{file.Name}: equipment is {file.Content.Equipment!.Length} characters, limit {equipmentMaxLength}")
            .ToList();
        foreach (var prescription in FindPrescriptions())
        {
            var location = $"{prescription.Location} / {prescription.Exercise.Slug}";
            problems.AddRange(FindTooLong(location, "reps", prescription.Exercise.Reps, repsMaxLength));
            problems.AddRange(FindTooLong(location, "notes", prescription.Exercise.Notes, notesMaxLength));
            problems.AddRange(FindTooLong(location, "notesEl", prescription.Exercise.NotesEl, notesElMaxLength));
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Courses_EveryExerciseSlugExistsInExerciseLibrary()
    {
        var librarySlugs = SeedJsonFiles.LoadExerciseLibrary()
            .Select(exercise => SlugHelper.FromExerciseId(exercise.Id))
            .ToHashSet(StringComparer.Ordinal);

        var problems = FindReferencedExercises()
            .Where(reference => !librarySlugs.Contains(reference.Slug))
            .Select(reference => $"{reference.Location}: unknown exercise slug '{reference.Slug}'")
            .ToList();

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Courses_ReferencedExercisesHaveBothImagesOnDisk()
    {
        var problems = new List<string>();

        foreach (var slug in FindReferencedExercises().Select(reference => reference.Slug).Distinct())
        {
            problems.AddRange(ExerciseImageFileNames
                .Select(imageFileName => Path.Combine(ExerciseImagesFolder, slug, imageFileName))
                .Where(imagePath => !File.Exists(imagePath))
                .Select(imagePath => $"missing image {Path.GetRelativePath(RepoPaths.WebRoot, imagePath)}"));
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Courses_ReferencedExercisesHaveAGreekOverlayRow()
    {
        var translatedSlugs = SeedJsonFiles.LoadGreekOverlay()
            .Where(row => !string.IsNullOrWhiteSpace(row.NameEl))
            .Select(row => row.Slug)
            .ToHashSet(StringComparer.Ordinal);

        var untranslatedSlugs = FindReferencedExercises()
            .Select(reference => reference.Slug)
            .Where(slug => !translatedSlugs.Contains(slug))
            .Distinct()
            .ToList();

        untranslatedSlugs.ShouldBeEmpty("exercises.el.json or el/*.el.json needs a nameEl for these slugs");
    }

    // ---- Exercises ---------------------------------------------------------------------------

    private static List<string> LibrarySlugs() =>
        SeedJsonFiles.LoadExerciseLibrary().Select(exercise => SlugHelper.FromExerciseId(exercise.Id)).ToList();

    [Fact]
    public void ExerciseLibrary_EveryExerciseHasBothImagesOnDisk()
    {
        var problems = LibrarySlugs()
            .SelectMany(slug => ExerciseImageFileNames.Select(imageFileName => Path.Combine(ExerciseImagesFolder, slug, imageFileName)))
            .Where(imagePath => !File.Exists(imagePath))
            .Select(imagePath => $"missing image {Path.GetRelativePath(RepoPaths.WebRoot, imagePath)}")
            .ToList();

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void ExerciseLibrary_SlugsMatchTheRecordedSlugList()
    {
        // A change to exercises.json or to the slug rule must update the list and the image folders too.
        var recordedSlugs = File.ReadAllLines(ExerciseSlugListPath).Where(line => line.Length > 0).ToList();

        var librarySlugs = LibrarySlugs().Order(StringComparer.Ordinal).ToList();

        librarySlugs.ShouldBe(recordedSlugs);
    }

    [Fact]
    public void ExerciseLibrary_EveryExerciseHasAGreekName_AndAsManyGreekStepsAsEnglishOnes()
    {
        var greekRowsBySlug = SeedJsonFiles.LoadGreekOverlay().ToDictionary(row => row.Slug, StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var exercise in SeedJsonFiles.LoadExerciseLibrary())
        {
            var slug = SlugHelper.FromExerciseId(exercise.Id);
            if (!greekRowsBySlug.TryGetValue(slug, out var greekRow))
            {
                problems.Add($"{slug}: no Greek row");
                continue;
            }

            problems.AddRange(FindGreekRowProblems(slug, exercise, greekRow));
        }

        problems.ShouldBeEmpty("exercises.el.json needs a Greek name and one Greek step per English step");
    }

    [Fact]
    public void GreekOverlay_NamesAndInstructionsFitTheirColumns()
    {
        // The seeder keeps a row English when its Greek text is too long, so check the files themselves.
        var nameMaxLength = ColumnMaxLength<Exercise>(nameof(Exercise.NameEl));
        var instructionsMaxLength = ColumnMaxLength<Exercise>(nameof(Exercise.InstructionsEl));
        var problems = new List<string>();

        foreach (var row in SeedJsonFiles.LoadGreekOverlay())
        {
            // Stored the way ExerciseSeeder joins the steps.
            var instructionsEl = string.Join(Exercise.InstructionSeparator, row.InstructionsEl);
            problems.AddRange(FindTooLong(row.Slug, "nameEl", row.NameEl, nameMaxLength));
            problems.AddRange(FindTooLong(row.Slug, "instructionsEl", instructionsEl, instructionsMaxLength));
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void GreekOverlay_ExerciseNamesUseSentenceCase()
    {
        var problems = SeedJsonFiles.LoadGreekOverlay()
            .Where(row => row.NameEl is not null)
            .Where(row => CapitalisedInnerWordRegex().IsMatch(row.NameEl!))
            .Select(row => $"{row.Slug}: '{row.NameEl}'")
            .ToList();

        problems.ShouldBeEmpty("Greek names capitalise only their first word");
    }

    // ---- Assessments -------------------------------------------------------------------------

    [Fact]
    public void Assessments_AreExactlyNineteen_AndBilingual()
    {
        var assessmentFiles = SeedJsonFiles.LoadAssessments();

        var problems = assessmentFiles.SelectMany(FindAssessmentProblems).ToList();

        assessmentFiles.Count.ShouldBe(SeedJsonFiles.ExpectedAssessmentCount);
        problems.ShouldBeEmpty();
    }

    // ---- Blog --------------------------------------------------------------------------------

    [Fact]
    public void BlogSeed_CoverImagesAreSiteRootPathsThatExistOnDisk()
    {
        var problems = new List<string>();

        foreach (var article in BlogSeedData.SeedArticles)
        {
            // Every seeded article has a cover, so the checks below can never pass on an empty list.
            var coverUrl = article.CoverImageUrl;
            if (string.IsNullOrWhiteSpace(coverUrl))
            {
                problems.Add($"{article.Slug}: has no cover");
                continue;
            }

            // A relative path renders broken on nested pages, so covers must start at the site root.
            if (!coverUrl.StartsWith('/'))
            {
                problems.Add($"{article.Slug}: cover '{coverUrl}' is not a site-root path");
                continue;
            }

            var coverPath = Path.Combine(RepoPaths.WebRoot, coverUrl.TrimStart('/'));
            if (!File.Exists(coverPath))
                problems.Add($"{article.Slug}: cover '{coverUrl}' is missing under wwwroot");
        }

        BlogSeedData.SeedArticles.ShouldNotBeEmpty();
        problems.ShouldBeEmpty();
    }

    [Fact]
    public void BlogSeed_FourBilingualFitnessArticles_WithUniqueSlugsCoversAndKnownTags()
    {
        var articles = BlogSeedData.SeedArticles;

        var problems = articles.SelectMany(FindBlogArticleProblems).ToList();

        articles.Count.ShouldBe(ExpectedBlogArticleCount);
        articles.Select(article => article.Slug).Distinct(StringComparer.Ordinal).Count().ShouldBe(ExpectedBlogArticleCount);
        problems.ShouldBeEmpty();
    }

    // ---- Greek titles ------------------------------------------------------------------------

    [Fact]
    public void SeedTitles_GreekUseSentenceCase()
    {
        var titles = FindGreekCourseTitles().Concat(FindGreekAssessmentTitles()).Concat(FindOtherGreekTitles()).ToList();

        var titleCaseTitles = titles
            .Where(title => CapitalisedInnerTitleWordRegex().IsMatch(title.Text))
            .Select(title => $"{title.Location}: '{title.Text}'")
            .ToList();

        titles.ShouldNotBeEmpty();
        titleCaseTitles.ShouldBeEmpty("Greek titles use sentence case (Βασικές ασκήσεις με βάρος σώματος, not Βασικές Ασκήσεις με Βάρος Σώματος)");
    }

    // ---- Vocabulary --------------------------------------------------------------------------

    [Fact]
    public void SeedFiles_ContainNoCareerVocabulary()
    {
        FindTextMatches(CareerVocabularyRegex()).ShouldBeEmpty();
    }

    [Fact]
    public void SeedFiles_GreekInstructionsUseImperativeNotPastTense()
    {
        FindTextMatches(GreekPastTenseCommandRegex()).ShouldBeEmpty("use the imperative (e.g. Επίλεξε, not Επέλεξε)");
    }

    // ---- Resources ---------------------------------------------------------------------------

    public static TheoryData<string> EnglishResxFiles()
    {
        var englishResxFiles = Directory.GetFiles(RepoPaths.ResourcesRoot, EnglishResxPattern, SearchOption.AllDirectories)
            .Where(filePath => !filePath.EndsWith(GreekResxSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(filePath => Path.GetRelativePath(RepoPaths.ResourcesRoot, filePath))
            .Order(StringComparer.Ordinal);

        return new TheoryData<string>(englishResxFiles);
    }

    [Theory]
    [MemberData(nameof(EnglishResxFiles))]
    public void Resx_EnglishAndGreekKeysMatchEachOtherAndTheDesignerClass(string englishResxRelativePath)
    {
        var englishResxPath = Path.Combine(RepoPaths.ResourcesRoot, englishResxRelativePath);
        var greekResxPath = Path.ChangeExtension(englishResxPath, null) + GreekResxSuffix;
        var englishValues = LoadResxValues(englishResxPath);
        var greekValues = LoadResxValues(greekResxPath);
        var designerKeys = LoadDesignerKeys(Path.GetFileNameWithoutExtension(englishResxPath));

        englishValues.Keys.Except(greekValues.Keys).ShouldBeEmpty("keys missing from the .el.resx");
        greekValues.Keys.Except(englishValues.Keys).ShouldBeEmpty("keys only in the .el.resx");
        englishValues.Keys.Except(designerKeys).ShouldBeEmpty("keys missing from the Designer.cs class");
        designerKeys.Except(englishValues.Keys).ShouldBeEmpty("Designer.cs properties with no resx key");
        greekValues.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key)
            .ShouldBeEmpty("keys with an empty Greek value");
    }

    [Theory]
    [MemberData(nameof(EnglishResxFiles))]
    public void Resx_GreekValuesAreNotCopiesOfTheEnglishText(string englishResxRelativePath)
    {
        var englishResxPath = Path.Combine(RepoPaths.ResourcesRoot, englishResxRelativePath);
        var greekResxPath = Path.ChangeExtension(englishResxPath, null) + GreekResxSuffix;
        var resourceClassName = Path.GetFileNameWithoutExtension(englishResxPath);
        var englishValues = LoadResxValues(englishResxPath);
        var greekValues = LoadResxValues(greekResxPath);

        var copiedKeys = greekValues
            .Where(pair => englishValues.TryGetValue(pair.Key, out var englishValue) && englishValue == pair.Value)
            .Where(pair => HasTranslatableText(pair.Value))
            .Select(pair => $"{resourceClassName}.{pair.Key}")
            .ToList();

        copiedKeys.ShouldBeEmpty("Greek values that repeat the English text");
    }

    [Theory]
    [MemberData(nameof(EnglishResxFiles))]
    public void Resx_ValuesCarryNoEmojiOrArrowGlyphs(string englishResxRelativePath)
    {
        var englishResxPath = Path.Combine(RepoPaths.ResourcesRoot, englishResxRelativePath);
        var greekResxPath = Path.ChangeExtension(englishResxPath, null) + GreekResxSuffix;
        var resourceClassName = Path.GetFileNameWithoutExtension(englishResxPath);
        var allValues = LoadResxValues(englishResxPath).Concat(LoadResxValues(greekResxPath));

        var valuesWithGlyphs = allValues
            .Where(pair => IconGlyphRegex().IsMatch(pair.Value))
            .Select(pair => $"{resourceClassName}.{pair.Key}: {pair.Value}")
            .ToList();

        valuesWithGlyphs.ShouldBeEmpty("icons belong in the markup as Bootstrap Icons (bi-*), not in the text");
    }

    [Fact]
    public void GreekResx_AddressesTheMemberInformally()
    {
        var greekValues = LoadAllGreekResxValues();

        var formalValues = greekValues
            .Where(value => GreekFormalAddressRegex().IsMatch(value.Text))
            .Select(value => $"{value.Location}: {value.Text}")
            .ToList();

        greekValues.ShouldNotBeEmpty();
        formalValues.ShouldBeEmpty("the Greek UI says εσύ (e.g. Δοκίμασε, not Δοκιμάστε; σου, not σας)");
    }

    [Fact]
    public void GreekResx_UsesSentenceCase()
    {
        var greekValues = LoadAllGreekResxValues();

        var titleCaseValues = greekValues
            .Where(value => !GreekPersonNameKeys.Contains(value.Location))
            .Where(value => GreekTitleCaseRegex().IsMatch(value.Text))
            .Select(value => $"{value.Location}: {value.Text}")
            .ToList();

        greekValues.ShouldNotBeEmpty();
        titleCaseValues.ShouldBeEmpty("Greek capitalises only the first word (Διαχείριση χρηστών, not Διαχείριση Χρηστών)");
    }

    [Fact]
    public void GreekResx_BoldWordsMidSentenceStayLowercase()
    {
        var greekValues = LoadAllGreekResxValues();

        var capitalisedBoldValues = greekValues
            .Where(value => GreekCapitalisedBoldMidSentenceRegex().IsMatch(value.Text))
            .Select(value => $"{value.Location}: {value.Text}")
            .ToList();

        greekValues.ShouldNotBeEmpty();
        capitalisedBoldValues.ShouldBeEmpty("bold does not make a Greek word a heading (<strong>ενότητες</strong>, not <strong>Ενότητες</strong>)");
    }

    [Fact]
    public void CourseRes_HasALabelForEveryLevelGoalAndEquipmentCode()
    {
        var expectedKeys = Enum.GetNames<CourseLevel>().Select(name => $"Level_{name}")
            .Concat(Enum.GetNames<FitnessGoal>().Select(name => $"Goal_{name}"))
            .Concat(Course.EquipmentCodes.Select(code => $"Equipment_{code}"));

        var missingKeys = expectedKeys
            .Where(key => CourseRes.ResourceManager.GetString(key, CultureInfo.InvariantCulture) is null)
            .ToList();

        missingKeys.ShouldBeEmpty("course chips and filters show these values through CourseRes");
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static IEnumerable<string> FindCourseHeaderProblems(SeedFile<CourseSeedDto> file)
    {
        var course = file.Content;
        (string Field, string? Value)[] requiredTexts =
        [
            ("title", course.Title), ("titleEl", course.TitleEl),
            ("description", course.Description), ("descriptionEl", course.DescriptionEl),
            ("category", course.Category), ("categoryEl", course.CategoryEl)
        ];

        foreach (var (field, _) in requiredTexts.Where(text => string.IsNullOrWhiteSpace(text.Value)))
            yield return $"{file.Name}: {field} is empty";

        // The seeder silently falls back to a default for an unknown name, so check the names here.
        if (!IsEnumName<CourseLevel>(course.Level))
            yield return $"{file.Name}: unknown level '{course.Level}'";
        if (!IsEnumName<FitnessGoal>(course.Goal))
            yield return $"{file.Name}: unknown goal '{course.Goal}'";
        if (!IsEnumName<SubscriptionTier>(course.RequiredTier))
            yield return $"{file.Name}: unknown requiredTier '{course.RequiredTier}'";
        if (course.EstimatedWeeks is not > 0 || course.SessionsPerWeek is not > 0)
            yield return $"{file.Name}: estimatedWeeks and sessionsPerWeek must be positive";
    }

    private static IEnumerable<string> FindBlogArticleProblems(SaveBlogArticleRequest article)
    {
        (string Field, string? Value)[] greekTexts =
            [("TitleEl", article.TitleEl), ("SummaryEl", article.SummaryEl), ("ContentEl", article.ContentEl)];

        foreach (var (field, _) in greekTexts.Where(text => string.IsNullOrWhiteSpace(text.Value)))
            yield return $"{article.Slug}: {field} is empty";
        if (string.IsNullOrWhiteSpace(article.CoverImageUrl))
            yield return $"{article.Slug}: no cover image";
        foreach (var tag in (article.Tags ?? []).Where(tag => !BlogTags.Contains(tag)))
            yield return $"{article.Slug}: unknown tag '{tag}'";

        string[] allTexts = [article.TitleEn, article.SummaryEn, article.ContentEn, .. greekTexts.Select(text => text.Value ?? string.Empty)];
        foreach (var match in allTexts.SelectMany(text => CareerVocabularyRegex().Matches(text)))
            yield return $"{article.Slug}: career-era word '{match.Value}'";
        foreach (var match in greekTexts.SelectMany(text => GreekPastTenseCommandRegex().Matches(text.Value ?? string.Empty)))
            yield return $"{article.Slug}: past tense '{match.Value}' used as a command";
    }

    /// <summary>
    /// The Greek titles of every course, module and lesson, plus the heading that opens each
    /// lesson's Greek content (it repeats the lesson title).
    /// </summary>
    private static List<GreekTitle> FindGreekCourseTitles()
    {
        var titles = new List<GreekTitle>();

        foreach (var (fileName, course) in SeedJsonFiles.LoadCourses())
        {
            titles.Add(new GreekTitle(fileName, course.TitleEl ?? string.Empty));
            titles.AddRange(course.Modules.Select(module => new GreekTitle($"{fileName} / {module.Title}", module.TitleEl ?? string.Empty)));

            var lessons = course.Modules.SelectMany(module => module.Lessons).ToList();
            titles.AddRange(lessons.Select(lesson => new GreekTitle($"{fileName} / {lesson.Title}", lesson.TitleEl ?? string.Empty)));
            titles.AddRange(lessons
                .Select(lesson => new GreekTitle($"{fileName} / {lesson.Title} heading", ReadMarkdownTitle(lesson.ContentEl)))
                .Where(title => title.Text.Length > 0));
        }

        return titles;
    }

    /// <summary>The Greek assessment titles: the row, its schema and each schema section.</summary>
    private static List<GreekTitle> FindGreekAssessmentTitles()
    {
        var titles = new List<GreekTitle>();

        foreach (var (fileName, assessment) in SeedJsonFiles.LoadAssessments())
        {
            titles.Add(new GreekTitle(fileName, assessment.TitleEl ?? string.Empty));

            using var schema = JsonDocument.Parse(assessment.SchemaJson);
            titles.Add(new GreekTitle($"{fileName} schema", ReadText(schema.RootElement, "titleEl") ?? string.Empty));
            titles.AddRange(ReadArray(schema.RootElement, "sections")
                .Select(section => new GreekTitle($"{fileName} / {ReadText(section, "id")}", ReadText(section, "titleEl") ?? string.Empty)));
        }

        return titles;
    }

    /// <summary>The Greek titles of the learning paths, live classes, testimonials and blog articles.</summary>
    private static List<GreekTitle> FindOtherGreekTitles()
    {
        var pathTitles = SeedJsonFiles.LoadPaths().Select(path => new GreekTitle($"path {path.Title}", path.TitleEl ?? string.Empty));
        var liveClassTitles = SeedJsonFiles.LoadLiveClasses().Select(liveClass => new GreekTitle($"live class {liveClass.Title}", liveClass.TitleEl ?? string.Empty));
        var testimonialTitles = SeedJsonFiles.LoadTestimonials().Select(testimonial => new GreekTitle($"testimonial {testimonial.FullName}", testimonial.RoleOrTitleEl ?? string.Empty));
        var blogTitles = BlogSeedData.SeedArticles.Select(article => new GreekTitle($"blog {article.Slug}", article.TitleEl ?? string.Empty));

        return pathTitles.Concat(liveClassTitles).Concat(testimonialTitles).Concat(blogTitles).ToList();
    }

    /// <summary>The text of a "# " heading on the first line of the content, or "" when it has none.</summary>
    private static string ReadMarkdownTitle(string? content)
    {
        const string headingMarker = "# ";
        var firstLine = (content ?? string.Empty).Split('\n')[0].TrimEnd('\r');

        return firstLine.StartsWith(headingMarker, StringComparison.Ordinal) ? firstLine[headingMarker.Length..] : string.Empty;
    }

    private static bool IsEnumName<TEnum>(string? value) where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>().Contains(value, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> FindModuleProblems(string location, ModuleSeedDto module)
    {
        if (string.IsNullOrWhiteSpace(module.TitleEl))
            yield return $"{location}: module titleEl is empty";
        if (HasTextWithoutGreek(module.Description, module.DescriptionEl))
            yield return $"{location}: module descriptionEl is empty";
        if (module.Lessons.Count == 0)
            yield return $"{location}: module has no lessons";

        foreach (var lesson in module.Lessons)
        {
            foreach (var problem in FindLessonProblems($"{location} / {lesson.Title}", lesson))
                yield return problem;
        }
    }

    private static IEnumerable<string> FindLessonProblems(string location, LessonSeedDto lesson)
    {
        if (string.IsNullOrWhiteSpace(lesson.TitleEl))
            yield return $"{location}: titleEl is empty";
        if (HasTextWithoutGreek(lesson.Content, lesson.ContentEl))
            yield return $"{location}: contentEl is empty";
        if (string.IsNullOrWhiteSpace(lesson.Content) && lesson.Exercises.Count == 0)
            yield return $"{location}: lesson has neither content nor exercises";
        if (lesson.DurationMinutes is not > 0)
            yield return $"{location}: durationMinutes must be positive";

        foreach (var exercise in lesson.Exercises.Where(exercise => HasTextWithoutGreek(exercise.Notes, exercise.NotesEl)))
            yield return $"{location} / {exercise.Slug}: notesEl is empty";
    }

    /// <summary>
    /// Reps have no Greek column, so they may hold only numbers, symbols and the unit symbols
    /// both languages read the same (s, min, m, rpm, RPE, AMRAP), and no decimal numbers
    /// (1.5 is written 1,5 in Greek).
    /// </summary>
    private static bool IsLanguageNeutralReps(string? reps)
    {
        if (string.IsNullOrEmpty(reps))
            return true;

        var withoutUnits = RepsUnitSymbolRegex().Replace(reps, string.Empty);
        return !withoutUnits.Any(char.IsLetter) && !DecimalNumberRegex().IsMatch(reps);
    }

    private static IEnumerable<string> FindGreekRowProblems(string slug, ExerciseSeedDto exercise, ExerciseGreekSeedDto greekRow)
    {
        // Common gym moves keep their English name in Greek too («Ab roller»), so only require a name.
        if (string.IsNullOrWhiteSpace(greekRow.NameEl))
            yield return $"{slug}: nameEl is empty";

        if (greekRow.InstructionsEl.Count != exercise.Instructions.Count)
            yield return $"{slug}: {greekRow.InstructionsEl.Count} Greek steps for {exercise.Instructions.Count} English ones";
    }

    private static bool HasTextWithoutGreek(string? english, string? greek) =>
        !string.IsNullOrWhiteSpace(english) && string.IsNullOrWhiteSpace(greek);

    /// <summary>Every lesson prescription in the course files, located as "file / lesson".</summary>
    private static List<Prescription> FindPrescriptions()
    {
        var prescriptions = new List<Prescription>();

        foreach (var (fileName, course) in SeedJsonFiles.LoadCourses())
        {
            var lessons = course.Modules.SelectMany(module => module.Lessons);
            prescriptions.AddRange(lessons.SelectMany(lesson =>
                lesson.Exercises.Select(exercise => new Prescription($"{fileName} / {lesson.Title}", exercise))));
        }

        return prescriptions;
    }

    /// <summary>Every exercise a course points at: its cover photo and each lesson prescription.</summary>
    private static List<ExerciseReference> FindReferencedExercises()
    {
        var coverReferences = SeedJsonFiles.LoadCourses()
            .Where(file => !string.IsNullOrWhiteSpace(file.Content.CoverExerciseSlug))
            .Select(file => new ExerciseReference($"{file.Name} cover", file.Content.CoverExerciseSlug!));
        var prescriptionReferences = FindPrescriptions()
            .Select(prescription => new ExerciseReference(prescription.Location, prescription.Exercise.Slug));

        return coverReferences.Concat(prescriptionReferences).ToList();
    }

    private static IEnumerable<string> FindAssessmentProblems(SeedFile<AssessmentSeedDto> file)
    {
        var assessment = file.Content;
        (string Field, string? Value)[] requiredTexts =
        [
            ("Title", assessment.Title), ("TitleEl", assessment.TitleEl),
            ("Description", assessment.Description), ("DescriptionEl", assessment.DescriptionEl),
            ("Category", assessment.Category), ("CategoryEl", assessment.CategoryEl)
        ];

        foreach (var (field, _) in requiredTexts.Where(text => string.IsNullOrWhiteSpace(text.Value)))
            yield return $"{file.Name}: {field} is empty";
        if (assessment.Key != file.Name)
            yield return $"{file.Name}: Key '{assessment.Key}' does not match the file name";

        // SchemaJson is itself a JSON document stored as a string.
        using var schema = JsonDocument.Parse(assessment.SchemaJson);
        foreach (var problem in FindSchemaProblems(file.Name, assessment.Key, schema.RootElement))
            yield return problem;
    }

    private static IEnumerable<string> FindSchemaProblems(string location, string key, JsonElement schema)
    {
        if (ReadText(schema, "id") != key)
            yield return $"{location}: schema id does not equal the Key";
        if (ReadText(schema, "version") != AssessmentSchemaVersion)
            yield return $"{location}: schema version should be '{AssessmentSchemaVersion}'";
        if (string.IsNullOrWhiteSpace(ReadText(schema, "titleEl")))
            yield return $"{location}: schema titleEl is empty";

        foreach (var problem in ReadArray(schema, "sections").SelectMany(section => FindSectionProblems(location, section)))
            yield return problem;
    }

    private static IEnumerable<string> FindSectionProblems(string location, JsonElement section)
    {
        var sectionLocation = $"{location} / {ReadText(section, "id")}";
        if (string.IsNullOrWhiteSpace(ReadText(section, "titleEl")))
            yield return $"{sectionLocation}: section titleEl is empty";

        var questionProblems = ReadArray(section, "questions")
            .SelectMany(question => FindQuestionProblems($"{sectionLocation} / {ReadText(question, "id")}", question));
        foreach (var problem in questionProblems)
            yield return problem;
    }

    private static IEnumerable<string> FindQuestionProblems(string location, JsonElement question)
    {
        if (string.IsNullOrWhiteSpace(ReadText(question, "labelEl")))
            yield return $"{location}: labelEl is empty";

        var questionType = ReadText(question, "type") ?? string.Empty;
        if (!KnownQuestionTypes.Contains(questionType))
            yield return $"{location}: unknown question type '{questionType}'";

        var isRating = questionType == RatingQuestionType;
        var hasRatingLabels = !string.IsNullOrWhiteSpace(ReadText(question, "minLabelEl"))
            && !string.IsNullOrWhiteSpace(ReadText(question, "maxLabelEl"));
        if (isRating && !hasRatingLabels)
            yield return $"{location}: rating question needs minLabelEl and maxLabelEl";

        // Greek options are shown in place of the English ones, so the two lists must line up.
        var optionCount = ReadArray(question, "options").Count;
        if (optionCount > 0 && ReadArray(question, "optionsEl").Count != optionCount)
            yield return $"{location}: optionsEl must have one entry per option";
    }

    /// <summary>The column limit the EF model sets (HasMaxLength), the same source the seeders read.</summary>
    private static int ColumnMaxLength<TEntity>(string propertyName)
    {
        using var db = DbContextFactory.CreateInMemory();
        var maxLength = db.Model.FindEntityType(typeof(TEntity))?.FindProperty(propertyName)?.GetMaxLength();
        return maxLength ?? throw new InvalidOperationException($"{typeof(TEntity).Name}.{propertyName} has no max length");
    }

    private static IEnumerable<string> FindTooLong(string location, string field, string? value, int maxLength)
    {
        if (value?.Length > maxLength)
            yield return $"{location}: {field} is {value.Length} characters, limit {maxLength}";
    }

    private static string? ReadText(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static List<JsonElement> ReadArray(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToList()
            : [];

    /// <summary>Every match of the pattern in the scanned files, as "file: 'match'" (once per file).</summary>
    private static List<string> FindTextMatches(Regex pattern)
    {
        var matches = new List<string>();

        foreach (var filePath in TextScanRoots.SelectMany(FindFilesToScan))
        {
            var text = File.ReadAllText(filePath);
            matches.AddRange(pattern.Matches(text)
                .Select(match => $"{Path.GetRelativePath(RepoPaths.RepoRoot, filePath)}: '{match.Value}'")
                .Distinct());
        }

        return matches;
    }

    private static IEnumerable<string> FindFilesToScan(TextScanRoot root) =>
        Directory.GetFiles(root.Folder, root.SearchPattern, SearchOption.AllDirectories);

    private static Dictionary<string, string> LoadResxValues(string resxPath)
    {
        File.Exists(resxPath).ShouldBeTrue($"{resxPath} is missing");

        return XDocument.Load(resxPath).Root!
            .Elements("data")
            .ToDictionary(
                data => (string)data.Attribute("name")!,
                data => (string?)data.Element("value") ?? string.Empty,
                StringComparer.Ordinal);
    }

    /// <summary>Every value of every .el.resx (Messages/ included), located as "ResourceName.Key".</summary>
    private static List<ResxValue> LoadAllGreekResxValues()
    {
        var greekValues = new List<ResxValue>();

        foreach (var greekResxPath in Directory.GetFiles(RepoPaths.ResourcesRoot, "*" + GreekResxSuffix, SearchOption.AllDirectories))
        {
            var resourceName = Path.GetFileName(greekResxPath)[..^GreekResxSuffix.Length];
            greekValues.AddRange(LoadResxValues(greekResxPath)
                .Select(pair => new ResxValue($"{resourceName}.{pair.Key}", pair.Value)));
        }

        return greekValues;
    }

    /// <summary>
    /// True when the value has words to translate once the terms Greek keeps in English
    /// (the brand, "Email", "PDF", ...) are removed; numbers and symbols need no translation.
    /// </summary>
    private static bool HasTranslatableText(string value) =>
        TermsKeptInEnglishRegex().Replace(value, string.Empty).Any(char.IsLetter);

    /// <summary>The public string properties of the hand-edited Designer class with this name.</summary>
    private static HashSet<string> LoadDesignerKeys(string resourceClassName)
    {
        var resourceAssembly = typeof(GlobalRes).Assembly;
        var designerClass = resourceAssembly.GetTypes().SingleOrDefault(type => type.Name == resourceClassName);
        designerClass.ShouldNotBeNull($"no Designer class named {resourceClassName} in {resourceAssembly.GetName().Name}");

        return designerClass
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    // Career-era words: career, job(s), interview, résumé/resume, CV, salary, LinkedIn, employer,
    // psychosocial, counsel(ling), and the Greek career, profession, career guidance, interview,
    // CV, employer and psychosocial stems. "Resume a course" is written "Continue" instead.
    [GeneratedRegex(@"career|\bjobs?\b|interview|\br[eé]sum[eé]|\bCV\b|salary|linkedin|employer|psychosocial|counsel"
        + @"|καριέρ|επάγγελμα|επαγγελματικ\w*\s+προσανατολισμ|συνέντευξ|βιογραφικ|εργοδότ|ψυχοκοινωνικ",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CareerVocabularyRegex();

    // Icon-like characters: arrows, technical symbols (stopwatch, hourglass), miscellaneous symbols,
    // dingbats (check marks, crosses), stars, and every emoji outside the Basic Multilingual Plane
    // (a surrogate pair). Punctuation such as the middle dot, ellipsis and «» quotes is allowed.
    [GeneratedRegex(@"[←-⇿⌀-⏿☀-➿⬀-⯿]|[\uD83C-\uD83E][\uDC00-\uDFFF]")]
    private static partial Regex IconGlyphRegex();

    // Third-person past forms that read as "he/she selected" when used as a command.
    // The imperatives are Επίλεξε, Επίστρεψε, Κατάγραψε, Διάκοψε, Κατάκτησε, Επανάλαβε,
    // Απόφυγε, Παράμεινε, Πρόσθεσε, Μετάφερε, Σύνδεσε, Επίτρεψε, Εισπνεύσε, Εκπνεύσε.
    [GeneratedRegex(@"\b(?:επέλεξε|επέστρεψε|κατέγραψε|διέκοψε|κατέκτησε|επανέλαβε|απέφυγε|παρέμεινε|προσέθεσε|μετέφερε|συνέδεσε|επέτρεψε|εισέπνευσε|εξέπνευσε)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GreekPastTenseCommandRegex();

    // The polite plural (εσείς) a UI string tends to use: commands whose informal forms are
    // Δοκίμασε, Κάνε, Επίλεξε, Πάτησε, Γράψε, Ρώτα, Έλεγξε, Αναβάθμισε, Επικοινώνησε, Επιβεβαίωσε,
    // Δες and Επανάφερε, plus είστε, θέλετε, έχετε and σας (εσύ: είσαι, θέλεις, έχεις, σου).
    // "We" forms such as επεξεργαζόμαστε do not match.
    [GeneratedRegex(@"\b(?:δοκιμάστε|κάντε|επιλέξτε|πατήστε|γράψτε|ρωτήστε|ελέγξτε|αναβαθμίστε|εισαγάγετε"
        + @"|επικοινωνήστε|επιβεβαιώστε|δείτε|επαναφέρετε|είστε|θέλετε|έχετε|σας)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GreekFormalAddressRegex();

    // Two capitalised Greek words in a row, the title case of "Διαχείριση Χρηστών", also when a
    // short connector ("Πίσω στο Πρόγραμμα"), "&" or "/" ("Χρεώσεις & Συναλλαγές") sits between
    // them, or after a short abbreviation such as "Ημ. Γέννησης".
    [GeneratedRegex(@"\b[ΆΈ-ΏΑ-Ω][ά-ώ]+(?:(?: [α-ωά-ώ]{1,3})? | ?[&/] ?)[ΆΈ-ΏΑ-Ω][ά-ώ]+|\b[ΆΈ-ΏΑ-Ω][ά-ώ]{1,3}\. [ΆΈ-ΏΑ-Ω][ά-ώ]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex GreekTitleCaseRegex();

    // A bold Greek word that starts with a capital right after a lowercase word or a comma, so in
    // the middle of a sentence: "να προσθέσεις <strong>Ενότητες</strong>".
    [GeneratedRegex(@"[α-ωά-ώϊϋΐΰ,] <strong>[ΆΈ-ΏΑ-Ω]", RegexOptions.CultureInvariant)]
    private static partial Regex GreekCapitalisedBoldMidSentenceRegex();

    // A capital (Latin or Greek) starting a word or word part after a space, "/" or "-", so not
    // the first word of the name or of its brackets. Proper nouns (Arnold, Turkish, Smith, ...) and
    // the letter or acronym names of equipment and moves (E-Z bar, SMR, T-bar, V-up) keep theirs.
    [GeneratedRegex(@"(?<=[ /-])(?!(?:Arnold|Turkish|Romanian|Atlas|Bradford|Rocky|Conan|Gironda|Janda|Russian|Scott|Smith|Zottman|Bosu|EZ|E-Z|SMR|T|V|Z)\b)[A-ZΆΈ-ΏΑ-Ϋ]",
        RegexOptions.CultureInvariant)]
    private static partial Regex CapitalisedInnerWordRegex();

    // A capital (Greek or Latin) followed by a lowercase letter, starting a word after a space or
    // a word part after "-", so a word after the first: "Βασικές Ασκήσεις". A new part of the title
    // keeps its capital after ": ", "(" or "«" ("Προπόνηση Α: Ήπια ροή", "(Side plank)"); all-caps
    // acronyms and letter labels (HIIT, RPE, Προπόνηση Α) never match. The allow-list holds the
    // names that keep their capitals: the Push Pull Legs programme and its Push and Pull days,
    // the HIIT Express class and the Turkish get-up (but not the push-up or pull-up moves).
    [GeneratedRegex(@"(?<=(?<!:) |-)(?!(?:Push|Pull|Legs|Express|Turkish)\b(?!-))[A-ZΆΈ-ΏΑ-Ϋ](?=\p{Ll})",
        RegexOptions.CultureInvariant)]
    private static partial Regex CapitalisedInnerTitleWordRegex();

    // The brand, the Plus and Pro plan names and the terms the Greek UI keeps in English.
    [GeneratedRegex(@"\b(?:FitEdu|Plus|Pro|Email|PDF|Slug|ID|Cookies|Kettlebell)\b", RegexOptions.CultureInvariant)]
    private static partial Regex TermsKeptInEnglishRegex();

    [GeneratedRegex(@"\b(?:s|min|m|rpm|RPE|AMRAP)\b", RegexOptions.CultureInvariant)]
    private static partial Regex RepsUnitSymbolRegex();

    [GeneratedRegex(@"\d[.,]\d", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalNumberRegex();

    private sealed record ResxValue(string Location, string Text);

    private sealed record GreekTitle(string Location, string Text);

    private sealed record ExerciseReference(string Location, string Slug);

    private sealed record Prescription(string Location, LessonExerciseSeedDto Exercise);

    private sealed record TextScanRoot(string Folder, string SearchPattern);
}

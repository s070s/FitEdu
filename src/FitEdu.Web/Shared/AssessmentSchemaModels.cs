namespace FitEdu.Web.Shared;

/// <summary>The question types the admin editor offers, and which extra fields each type has.</summary>
public static class AssessmentQuestionTypes
{
    public const string Text = "text";
    public const string Rating = "rating";
    public const string Choice = "choice";
    public const string MultiSelect = "multiselect";

    /// <summary>Read by the student form only; the editor does not offer it.</summary>
    public const string Date = "date";

    /// <summary>
    /// Types that list options. "single-select" and "multi-select" are other spellings the student form reads.
    /// </summary>
    public static bool HasOptions(string type) =>
        type is Choice or MultiSelect or "single-select" or "multi-select";

    /// <summary>Types shown as a number scale; the student form treats "likert" like "rating".</summary>
    public static bool IsRatingScale(string type) => type is Rating or "likert";

    /// <summary>One answer from a list; seeds write "choice", older schemas "single-select".</summary>
    public static bool IsSingleChoice(string type) => type is Choice or "single-select";

    /// <summary>Any number of answers from a list; seeds write "multiselect", older schemas "multi-select".</summary>
    public static bool IsMultiSelect(string type) => type is MultiSelect or "multi-select";
}

/// <summary>An assessment schema as the admin editor holds it: a list of sections of questions.</summary>
public sealed class EditableAssessmentSchema
{
    public const string DefaultVersion = "1.0";

    public string Version { get; set; } = DefaultVersion;

    public List<EditableSection> Sections { get; } = [];

    /// <summary>A section id ("s1", "s2", ...) that no section uses yet.</summary>
    public string NextSectionId() =>
        NextFreeId("s", Sections.Select(section => section.Id));

    /// <summary>A question id ("q1", "q2", ...) unused in every section, because answers are stored by question id.</summary>
    public string NextQuestionId() =>
        NextFreeId("q", Sections.SelectMany(section => section.Questions).Select(question => question.Id));

    private static string NextFreeId(string prefix, IEnumerable<string> usedIds)
    {
        var used = usedIds.ToHashSet(StringComparer.Ordinal);
        var number = used.Count + 1;
        while (used.Contains($"{prefix}{number}"))
            number++;

        return $"{prefix}{number}";
    }
}

/// <summary>One section of an assessment: a title in English and Greek and its questions.</summary>
public sealed class EditableSection
{
    /// <summary>Stable key for rendering; not saved.</summary>
    public string TempId { get; } = Guid.NewGuid().ToString("N");

    public string Id { get; set; } = string.Empty;

    public string TitleEn { get; set; } = string.Empty;

    public string? TitleEl { get; set; }

    public List<EditableQuestion> Questions { get; } = [];
}

/// <summary>One question; the rating and option fields are saved only for the types that use them.</summary>
public sealed class EditableQuestion
{
    public const int DefaultMin = 1;
    public const int DefaultMax = 5;

    /// <summary>Stable key for rendering and for remembering which cards are open; not saved.</summary>
    public string TempId { get; } = Guid.NewGuid().ToString("N");

    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = AssessmentQuestionTypes.Text;

    public string LabelEn { get; set; } = string.Empty;

    public string? LabelEl { get; set; }

    public bool Required { get; set; }

    public int Min { get; set; } = DefaultMin;

    public int Max { get; set; } = DefaultMax;

    public string? MinLabelEn { get; set; }

    public string? MaxLabelEn { get; set; }

    public string? MinLabelEl { get; set; }

    public string? MaxLabelEl { get; set; }

    /// <summary>The English options, one per line.</summary>
    public string OptionsTextEn { get; set; } = string.Empty;

    /// <summary>The Greek options, one per line.</summary>
    public string OptionsTextEl { get; set; } = string.Empty;

    /// <summary>"true" or "false", for the Required drop-down, which binds to text.</summary>
    public string RequiredText
    {
        get => Required ? "true" : "false";
        set => Required = value == "true";
    }

    public bool HasOptions => AssessmentQuestionTypes.HasOptions(Type);

    public bool IsRatingScale => AssessmentQuestionTypes.IsRatingScale(Type);

    public List<string> OptionsEn => SplitLines(OptionsTextEn);

    public List<string> OptionsEl => SplitLines(OptionsTextEl);

    private static List<string> SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

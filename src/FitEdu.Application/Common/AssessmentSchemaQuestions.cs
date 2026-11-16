using System.Text.Json;

namespace FitEdu.Application.Common;

/// <summary>One question of an assessment schema, read in both languages.</summary>
/// <param name="Id">The question's id in the schema.</param>
/// <param name="Type">The question's type as written in the schema (for example text, rating or choice).</param>
/// <param name="LabelEn">The English label.</param>
/// <param name="LabelEl">The Greek label, or null when the schema has none.</param>
/// <param name="Required">True when the question must be answered.</param>
/// <param name="Min">The rating scale's lowest value as written in the schema, or null when it has none.</param>
/// <param name="Max">The rating scale's highest value as written in the schema, or null when it has none.</param>
/// <param name="OptionsEn">The English options of a choice question; empty for other types.</param>
/// <param name="OptionsEl">The Greek options of a choice question; empty when the schema has none.</param>
public sealed record AssessmentQuestion(
    string Id,
    string Type,
    string LabelEn,
    string? LabelEl,
    bool Required,
    int? Min,
    int? Max,
    IReadOnlyList<string> OptionsEn,
    IReadOnlyList<string> OptionsEl)
{
    /// <summary>The label in the reader's language: Greek when asked for and present, else English, else the id.</summary>
    public string LabelFor(bool isEl)
    {
        var label = isEl && !string.IsNullOrWhiteSpace(LabelEl) ? LabelEl : LabelEn;
        return string.IsNullOrWhiteSpace(label) ? Id : label;
    }
}

/// <summary>
/// Reads the questions of a schema, flat {"questions":[...]} or sectioned {"sections":[{"questions":[...]}]}.
/// The single place that knows both layouts and both key styles ("label"/"options" in seeds,
/// "labelEn"/"optionsEn" in older editor saves); the student service, the submission check, the schema
/// validator, the history labels and the data export all read through it.
/// </summary>
public static class AssessmentSchemaQuestions
{
    /// <summary>The lowest rating value of a scale that gives none (the editor's default).</summary>
    public const int DefaultMin = 1;

    /// <summary>The highest rating value of a scale that gives none (the editor's default).</summary>
    public const int DefaultMax = 5;

    /// <summary>
    /// Collects question elements from either a flat {"questions":[...]} or sectioned
    /// {"sections":[{"questions":[...]}]} schema.
    /// </summary>
    public static List<JsonElement> CollectQuestions(JsonElement schemaRoot)
    {
        var questions = new List<JsonElement>();

        if (schemaRoot.TryGetProperty("questions", out var flatQ) && flatQ.ValueKind == JsonValueKind.Array)
        {
            foreach (var q in flatQ.EnumerateArray())
                questions.Add(q);
        }
        else if (schemaRoot.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
        {
            foreach (var section in sections.EnumerateArray())
            {
                if (section.TryGetProperty("questions", out var sectionQ) && sectionQ.ValueKind == JsonValueKind.Array)
                {
                    foreach (var q in sectionQ.EnumerateArray())
                        questions.Add(q);
                }
            }
        }

        return questions;
    }

    /// <summary>The questions of a schema in order; empty for a schema that is not valid JSON or not an object.</summary>
    public static IReadOnlyList<AssessmentQuestion> Read(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            return [];

        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return [];

            return CollectQuestions(document.RootElement).Select(ReadQuestion).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The "version" text of a schema, or null when it has none or is not valid JSON.</summary>
    public static string? ReadVersion(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            return document.RootElement.ValueKind == JsonValueKind.Object ? ReadString(document.RootElement, "version") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AssessmentQuestion ReadQuestion(JsonElement question)
    {
        if (question.ValueKind != JsonValueKind.Object)
            return new AssessmentQuestion(string.Empty, "text", string.Empty, null, false, null, null, [], []);

        return new AssessmentQuestion(
            Id: ReadString(question, "id") ?? string.Empty,
            Type: ReadString(question, "type") ?? "text",
            LabelEn: ReadString(question, "labelEn") ?? ReadString(question, "label") ?? string.Empty,
            LabelEl: ReadString(question, "labelEl"),
            Required: question.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.True,
            Min: ReadInt(question, "min"),
            Max: ReadInt(question, "max"),
            OptionsEn: ReadStrings(question, question.TryGetProperty("optionsEn", out _) ? "optionsEn" : "options"),
            OptionsEl: ReadStrings(question, "optionsEl"));
    }

    private static string? ReadString(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? ReadInt(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static List<string> ReadStrings(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        return items.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty)
            .ToList();
    }
}

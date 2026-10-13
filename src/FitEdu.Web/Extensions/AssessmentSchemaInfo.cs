using System.Text.Json;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Small facts about an assessment schema for the catalogue cards: how many questions it has
/// and roughly how long it takes.
/// </summary>
public static class AssessmentSchemaInfo
{
    /// <summary>About half a minute per question, rounded up.</summary>
    private const double MinutesPerQuestion = 0.5;

    /// <summary>
    /// Counts the questions in a flat {"questions":[...]} schema, or the questions of every section
    /// in a {"sections":[{"questions":[...]}]} schema. Blank or invalid JSON counts as 0.
    /// </summary>
    public static int CountQuestions(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            return 0;

        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            return CountQuestions(document.RootElement);
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>Estimated minutes to answer <paramref name="questionCount"/> questions; at least 1.</summary>
    public static int EstimatedMinutes(int questionCount) =>
        Math.Max(1, (int)Math.Ceiling(questionCount * MinutesPerQuestion));

    private static int CountQuestions(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return 0;

        if (root.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array)
            return questions.GetArrayLength();

        if (!root.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array)
            return 0;

        var total = 0;
        foreach (var section in sections.EnumerateArray())
        {
            if (section.TryGetProperty("questions", out var sectionQuestions) && sectionQuestions.ValueKind == JsonValueKind.Array)
                total += sectionQuestions.GetArrayLength();
        }
        return total;
    }
}

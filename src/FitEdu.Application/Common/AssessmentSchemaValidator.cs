using System.Text.Json;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Application.Common;

public enum AssessmentSchemaProblemKind
{
    /// <summary>Not a JSON object with a "questions" or "sections" list.</summary>
    NotASchema,
    BlankQuestionId,
    DuplicateQuestionId,
    ChoiceWithoutOptions,
    /// <summary>The Greek option list is given but has another length than the English one.</summary>
    OptionsDiffer,
    /// <summary>A rating scale outside 0 &lt;= min &lt; max &lt;= 10.</summary>
    RatingScaleInvalid
}

/// <summary>One thing wrong with a schema; <see cref="QuestionId"/> names the question it is about.</summary>
public sealed record AssessmentSchemaProblem(AssessmentSchemaProblemKind Kind, string? QuestionId = null)
{
    /// <summary>The problem in the current UI language, for the admin who saved the schema.</summary>
    public string Message => Kind switch
    {
        AssessmentSchemaProblemKind.NotASchema => ErrorMessagesRes.AssessmentSchemaShapeInvalid,
        AssessmentSchemaProblemKind.BlankQuestionId => ErrorMessagesRes.AssessmentSchemaQuestionIdBlank,
        AssessmentSchemaProblemKind.DuplicateQuestionId => ResourceFormat.Fill(ErrorMessagesRes.AssessmentSchemaQuestionIdDuplicateFormat, QuestionId),
        AssessmentSchemaProblemKind.ChoiceWithoutOptions => ResourceFormat.Fill(ErrorMessagesRes.AssessmentSchemaChoiceNeedsOptionsFormat, QuestionId),
        AssessmentSchemaProblemKind.OptionsDiffer => ResourceFormat.Fill(ErrorMessagesRes.AssessmentSchemaOptionsDifferFormat, QuestionId),
        _ => ResourceFormat.Fill(ErrorMessagesRes.AssessmentSchemaRatingScaleFormat, QuestionId)
    };
}

/// <summary>
/// The one structure check of an assessment schema, used by the admin save and the seeder: a schema a
/// student could not answer (no ids, repeated ids, a choice without options, a rating scale of a million
/// radio buttons) is refused before it is stored.
/// </summary>
public static class AssessmentSchemaValidator
{
    /// <summary>The highest rating value a scale may have; the student form draws one button per value.</summary>
    public const int MaxRatingValue = 10;

    /// <summary>Every problem of the schema; empty when it is fine.</summary>
    public static IReadOnlyList<AssessmentSchemaProblem> Validate(string? schemaJson)
    {
        if (!HasQuestionList(schemaJson))
            return [new AssessmentSchemaProblem(AssessmentSchemaProblemKind.NotASchema)];

        var problems = new List<AssessmentSchemaProblem>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var question in AssessmentSchemaQuestions.Read(schemaJson))
        {
            if (string.IsNullOrWhiteSpace(question.Id))
                problems.Add(new AssessmentSchemaProblem(AssessmentSchemaProblemKind.BlankQuestionId));
            else if (!seenIds.Add(question.Id))
                problems.Add(new AssessmentSchemaProblem(AssessmentSchemaProblemKind.DuplicateQuestionId, question.Id));

            problems.AddRange(FindQuestionProblems(question));
        }

        return problems;
    }

    private static IEnumerable<AssessmentSchemaProblem> FindQuestionProblems(AssessmentQuestion question)
    {
        if (AssessmentQuestionKinds.HasOptions(question.Type))
        {
            if (question.OptionsEn.Count == 0)
                yield return new AssessmentSchemaProblem(AssessmentSchemaProblemKind.ChoiceWithoutOptions, question.Id);
            else if (question.OptionsEl.Count > 0 && question.OptionsEl.Count != question.OptionsEn.Count)
                yield return new AssessmentSchemaProblem(AssessmentSchemaProblemKind.OptionsDiffer, question.Id);
        }

        if (AssessmentQuestionKinds.IsRatingScale(question.Type) && !HasValidScale(question))
            yield return new AssessmentSchemaProblem(AssessmentSchemaProblemKind.RatingScaleInvalid, question.Id);
    }

    private static bool HasValidScale(AssessmentQuestion question)
    {
        var min = question.Min ?? AssessmentSchemaQuestions.DefaultMin;
        var max = question.Max ?? AssessmentSchemaQuestions.DefaultMax;
        return min >= 0 && min < max && max <= MaxRatingValue;
    }

    private static bool HasQuestionList(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            return false;

        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                ((root.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array) ||
                 (root.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

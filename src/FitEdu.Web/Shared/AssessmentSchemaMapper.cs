using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace FitEdu.Web.Shared;

/// <summary>
/// Turns an assessment's SchemaJson into the admin editor's model and back, so opening and saving
/// an assessment keeps its sections, ids, question types, Greek text, options and rating labels.
/// It reads both key styles, "label"/"options" (seed files) and "labelEn"/"optionsEn" (older editor
/// saves), and always writes "label"/"options", the seed style.
/// </summary>
public static class AssessmentSchemaMapper
{
    private const string LegacySectionId = "s1";

    // Keeps Greek letters readable in the stored JSON; HTML-sensitive characters are still escaped.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>
    /// Reads a schema. A flat {"questions": [...]} schema becomes one section;
    /// empty or invalid JSON gives a schema with no sections.
    /// </summary>
    public static EditableAssessmentSchema Parse(string? schemaJson)
    {
        var schema = new EditableAssessmentSchema();
        var root = ParseObject(schemaJson);
        if (root is null)
            return schema;

        schema.Version = ReadString(root, "version") ?? EditableAssessmentSchema.DefaultVersion;

        if (root["sections"] is JsonArray sections)
        {
            foreach (var sectionNode in sections.OfType<JsonObject>())
                schema.Sections.Add(ParseSection(sectionNode));
        }
        else if (root["questions"] is JsonArray flatQuestions)
        {
            var section = new EditableSection { Id = LegacySectionId };
            AddQuestions(section, flatQuestions);
            schema.Sections.Add(section);
        }

        return schema;
    }

    /// <summary>
    /// Writes the schema. The root id and titles come from the assessment's key and titles,
    /// so the schema always matches the assessment it belongs to.
    /// </summary>
    public static string Serialize(EditableAssessmentSchema schema, string key, string titleEn, string? titleEl)
    {
        var root = new JsonObject
        {
            ["id"] = key,
            ["title"] = titleEn
        };
        AddIfPresent(root, "titleEl", titleEl);
        root["version"] = schema.Version;
        root["sections"] = ToJsonArray(schema.Sections.Select(SerializeSection));

        return root.ToJsonString(WriteOptions);
    }

    private static JsonObject? ParseObject(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            return null;

        try
        {
            return JsonNode.Parse(schemaJson) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static EditableSection ParseSection(JsonObject sectionNode)
    {
        var section = new EditableSection
        {
            Id = ReadString(sectionNode, "id") ?? string.Empty,
            TitleEn = ReadString(sectionNode, "title") ?? string.Empty,
            TitleEl = ReadString(sectionNode, "titleEl")
        };

        if (sectionNode["questions"] is JsonArray questions)
            AddQuestions(section, questions);

        return section;
    }

    private static void AddQuestions(EditableSection section, JsonArray questions)
    {
        foreach (var questionNode in questions.OfType<JsonObject>())
            section.Questions.Add(ParseQuestion(questionNode));
    }

    private static EditableQuestion ParseQuestion(JsonObject questionNode)
    {
        var optionsEn = ReadStringList(questionNode, "optionsEn") ?? ReadStringList(questionNode, "options");

        return new EditableQuestion
        {
            Id = ReadString(questionNode, "id") ?? string.Empty,
            // The type is kept as written (e.g. "multiselect" or "multi-select"), never renamed.
            Type = ReadString(questionNode, "type") ?? AssessmentQuestionTypes.Text,
            LabelEn = ReadString(questionNode, "labelEn") ?? ReadString(questionNode, "label") ?? string.Empty,
            LabelEl = ReadString(questionNode, "labelEl"),
            Required = ReadBool(questionNode, "required"),
            Min = ReadInt(questionNode, "min") ?? EditableQuestion.DefaultMin,
            Max = ReadInt(questionNode, "max") ?? EditableQuestion.DefaultMax,
            MinLabelEn = ReadString(questionNode, "minLabel"),
            MaxLabelEn = ReadString(questionNode, "maxLabel"),
            MinLabelEl = ReadString(questionNode, "minLabelEl"),
            MaxLabelEl = ReadString(questionNode, "maxLabelEl"),
            OptionsTextEn = JoinLines(optionsEn),
            OptionsTextEl = JoinLines(ReadStringList(questionNode, "optionsEl"))
        };
    }

    private static JsonObject SerializeSection(EditableSection section)
    {
        var sectionNode = new JsonObject
        {
            ["id"] = section.Id,
            ["title"] = section.TitleEn
        };
        AddIfPresent(sectionNode, "titleEl", section.TitleEl);
        sectionNode["questions"] = ToJsonArray(section.Questions.Select(SerializeQuestion));

        return sectionNode;
    }

    // Keys are written in the seed files' order: id, type, labels, type-specific fields, required.
    private static JsonObject SerializeQuestion(EditableQuestion question)
    {
        var questionNode = new JsonObject
        {
            ["id"] = question.Id,
            ["type"] = question.Type,
            ["label"] = question.LabelEn
        };
        AddIfPresent(questionNode, "labelEl", question.LabelEl);

        if (question.HasOptions)
            AddOptions(questionNode, question);
        else if (question.IsRatingScale)
            AddRatingScale(questionNode, question);

        questionNode["required"] = question.Required;
        return questionNode;
    }

    private static void AddOptions(JsonObject questionNode, EditableQuestion question)
    {
        questionNode["options"] = ToJsonArray(question.OptionsEn);

        var optionsEl = question.OptionsEl;
        if (optionsEl.Count > 0)
            questionNode["optionsEl"] = ToJsonArray(optionsEl);
    }

    private static void AddRatingScale(JsonObject questionNode, EditableQuestion question)
    {
        questionNode["min"] = question.Min;
        questionNode["max"] = question.Max;
        AddIfPresent(questionNode, "minLabel", question.MinLabelEn);
        AddIfPresent(questionNode, "maxLabel", question.MaxLabelEn);
        AddIfPresent(questionNode, "minLabelEl", question.MinLabelEl);
        AddIfPresent(questionNode, "maxLabelEl", question.MaxLabelEl);
    }

    /// <summary>Blank optional text is left out, so the student form falls back to English.</summary>
    private static void AddIfPresent(JsonObject node, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            node[name] = value;
    }

    // The casts pick JsonArray's plain JsonNode constructor instead of its reflection-based generic one.
    private static JsonArray ToJsonArray(IEnumerable<JsonObject> nodes) =>
        new(nodes.Select(node => (JsonNode?)node).ToArray());

    private static JsonArray ToJsonArray(IEnumerable<string> items) =>
        new(items.Select(item => (JsonNode?)item).ToArray());

    private static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static string? ReadString(JsonObject node, string name) => AsString(node[name]);

    private static int? ReadInt(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue(out int number) ? number : null;

    private static bool ReadBool(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue(out bool flag) && flag;

    private static List<string>? ReadStringList(JsonObject node, string name)
    {
        if (node[name] is not JsonArray items)
            return null;

        return items.Select(item => AsString(item) ?? string.Empty).ToList();
    }

    private static string JoinLines(List<string>? items) =>
        items is null ? string.Empty : string.Join("\n", items);
}

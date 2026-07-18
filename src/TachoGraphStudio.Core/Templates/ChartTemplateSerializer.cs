using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TachoGraphStudio.Core.Templates;

// GIMP 版 JSON フォーマットと互換のシリアライズ・検証。
// キーは snake_case、enum 値は小文字(旧フォーマットの "left" / "top" 等)
public static partial class ChartTemplateSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        WriteIndented = true,
    };

    public static ChartTemplate Deserialize(string json)
    {
        ChartTemplate? template;
        try
        {
            template = JsonSerializer.Deserialize<ChartTemplate>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new TemplateFormatException("テンプレート JSON を解析できません。", exception);
        }

        if (template is null)
        {
            throw new TemplateFormatException("テンプレート JSON がオブジェクトではありません。");
        }

        Validate(template);
        return template;
    }

    public static string Serialize(ChartTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        Validate(template);
        return JsonSerializer.Serialize(template, Options);
    }

    // JSON の明示的な null は init 既定値を上書きするため、非 null 宣言のプロパティにも
    // null が入り得る。実装例外を漏らさず TemplateFormatException の契約を維持する
    private static void Validate(ChartTemplate template)
    {
        if (template.Name is null || template.Version is null || template.Description is null)
        {
            throw new TemplateFormatException("name / version / description に null は指定できません。");
        }

        if (template.Fields is null)
        {
            throw new TemplateFormatException("fields に null は指定できません。");
        }

        if (template.ReferenceWidth < 1 || template.ReferenceHeight < 1)
        {
            throw new TemplateFormatException(
                $"reference_width / reference_height は 1 以上で指定してください: {template.ReferenceWidth}x{template.ReferenceHeight}");
        }

        foreach ((string name, TextFieldDefinition field) in template.Fields)
        {
            if (field is null)
            {
                throw new TemplateFormatException($"フィールド '{name}' に null は指定できません。");
            }

            if (field.Position is null)
            {
                throw new TemplateFormatException($"フィールド '{name}' の position に null は指定できません。");
            }

            if (field.Font is null)
            {
                throw new TemplateFormatException($"フィールド '{name}' の font に null は指定できません。");
            }

            if (field.Font.Family is null)
            {
                throw new TemplateFormatException($"フィールド '{name}' の family に null は指定できません。");
            }

            if (field.Position.XRatio is < 0.0 or > 1.0 || field.Position.YRatio is < 0.0 or > 1.0)
            {
                throw new TemplateFormatException(
                    $"フィールド '{name}' の position は 0〜1 の比率で指定してください: ({field.Position.XRatio}, {field.Position.YRatio})");
            }

            if (field.Font.SizeRatio is <= 0.0 or > 1.0)
            {
                throw new TemplateFormatException(
                    $"フィールド '{name}' の size_ratio は 0 より大きく 1 以下で指定してください: {field.Font.SizeRatio}");
            }

            if (field.Font.Color is null || !HexColorRegex().IsMatch(field.Font.Color))
            {
                throw new TemplateFormatException(
                    $"フィールド '{name}' の color は #RRGGBB 形式で指定してください: {field.Font.Color ?? "null"}");
            }
        }
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();
}

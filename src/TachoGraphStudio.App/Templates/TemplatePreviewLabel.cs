using TachoGraphStudio.App.Stage;
using TachoGraphStudio.Core.Templates;

namespace TachoGraphStudio.App.Templates;

public static class TemplatePreviewLabel
{
    public static string Resolve(string fieldName, DiscMetadata? metadata)
    {
        string? value = metadata is null
            ? null
            : ChartTextValueResolver.Resolve(fieldName, metadata.ToTextValues());

        return string.IsNullOrWhiteSpace(value) ? fieldName : value;
    }
}

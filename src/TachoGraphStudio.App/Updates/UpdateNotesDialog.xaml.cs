using System.Text.RegularExpressions;

using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

using TachoGraphStudio.Core.Updates;

namespace TachoGraphStudio.App.Updates;

public sealed partial class UpdateNotesDialog : ContentDialog
{
    public UpdateNotesDialog(
        IReadOnlyList<ChangelogSection> sections,
        Uri releasePageUri)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(releasePageUri);

        InitializeComponent();
        ReleaseLinkButton.NavigateUri = releasePageUri;
        ChangelogMarkdownRenderer.Render(NotesTextBlock, sections);
    }
}

internal static partial class ChangelogMarkdownRenderer
{
    [GeneratedRegex(
        @"(?<strong>\*\*(?<strongText>.+?)\*\*)|(?<code>`(?<codeText>[^`]+)`)|(?<link>\[(?<linkText>[^\]]+)\]\((?<linkUri>https?://[^)\s]+)\))",
        RegexOptions.CultureInvariant)]
    private static partial Regex InlineMarkupRegex();

    [GeneratedRegex(@"^\s*-\s+(?<text>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BulletRegex();

    public static void Render(
        RichTextBlock target,
        IReadOnlyList<ChangelogSection> sections)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sections);

        target.Blocks.Clear();

        foreach (ChangelogSection section in sections)
        {
            Paragraph heading = new();
            heading.Inlines.Add(new Run
            {
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Text = $"v{FormatVersion(section.Version)}",
            });
            if (!string.IsNullOrWhiteSpace(section.Date))
            {
                heading.Inlines.Add(new Run
                {
                    FontSize = 14,
                    Text = $" - {section.Date}",
                });
            }

            target.Blocks.Add(heading);
            RenderMarkdown(target, section.Markdown);
            target.Blocks.Add(new Paragraph());
        }
    }

    private static void RenderMarkdown(RichTextBlock target, string markdown)
    {
        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                target.Blocks.Add(new Paragraph());
                continue;
            }

            Match bullet = BulletRegex().Match(line);
            if (bullet.Success)
            {
                Paragraph paragraph = new();
                paragraph.Inlines.Add(new Run { Text = "• " });
                AppendInlineMarkup(paragraph, bullet.Groups["text"].Value);
                target.Blocks.Add(paragraph);
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                Paragraph paragraph = new();
                paragraph.Inlines.Add(new Run
                {
                    FontWeight = FontWeights.SemiBold,
                    Text = line[4..],
                });
                target.Blocks.Add(paragraph);
                continue;
            }

            Paragraph normalParagraph = new();
            AppendInlineMarkup(normalParagraph, line.Trim());
            target.Blocks.Add(normalParagraph);
        }
    }

    private static void AppendInlineMarkup(Paragraph paragraph, string text)
    {
        MatchCollection matches = InlineMarkupRegex().Matches(text);
        int position = 0;

        foreach (Match match in matches)
        {
            if (match.Index > position)
            {
                paragraph.Inlines.Add(new Run { Text = text[position..match.Index] });
            }

            if (match.Groups["strong"].Success)
            {
                Bold bold = new();
                bold.Inlines.Add(new Run { Text = match.Groups["strongText"].Value });
                paragraph.Inlines.Add(bold);
            }
            else if (match.Groups["code"].Success)
            {
                paragraph.Inlines.Add(new Run
                {
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    Text = match.Groups["codeText"].Value,
                });
            }
            else if (match.Groups["link"].Success
                && Uri.TryCreate(
                    match.Groups["linkUri"].Value,
                    UriKind.Absolute,
                    out Uri? uri))
            {
                Hyperlink hyperlink = new() { NavigateUri = uri };
                hyperlink.Inlines.Add(new Run { Text = match.Groups["linkText"].Value });
                paragraph.Inlines.Add(hyperlink);
            }
            else
            {
                paragraph.Inlines.Add(new Run { Text = match.Value });
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            paragraph.Inlines.Add(new Run { Text = text[position..] });
        }
    }

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{version.Build}";
}

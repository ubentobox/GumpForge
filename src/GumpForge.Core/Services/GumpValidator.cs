using GumpForge.Core.Models;

namespace GumpForge.Core.Services;

/// <summary>
/// Severity level for validation problems.
/// </summary>
public enum ProblemSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// A single validation problem detected in the gump document.
/// </summary>
public record GumpProblem(
    ProblemSeverity Severity,
    string Code,
    string Message,
    string ElementName,
    int Page
);

/// <summary>
/// Validates a GumpDocument and produces a list of problems (warnings, errors, info).
/// </summary>
public static class GumpValidator
{
    public static List<GumpProblem> Validate(GumpDocument doc)
    {
        var problems = new List<GumpProblem>();

        if (doc.CanvasWidth <= 0 || doc.CanvasHeight <= 0)
            problems.Add(new(ProblemSeverity.Error, "DOC001", "Canvas has zero or negative dimensions.", "Document", 0));

        if (doc.Pages.Count == 0)
            problems.Add(new(ProblemSeverity.Warning, "DOC002", "Document has no pages.", "Document", 0));

        foreach (var page in doc.Pages)
        {
            foreach (var element in page.Elements)
            {
                ValidateElement(problems, element, page.PageNumber, doc);
            }
        }

        // Check for duplicate button IDs
        var buttonIds = doc.GetAllElements().OfType<GumpButton>().GroupBy(b => b.ButtonId).Where(g => g.Count() > 1);
        foreach (var dupe in buttonIds)
        {
            var names = string.Join(", ", dupe.Select(b => b.Name));
            problems.Add(new(ProblemSeverity.Warning, "BTN001",
                $"Duplicate ButtonID {dupe.Key} used by: {names}", names, 0));
        }

        // Check for duplicate switch IDs
        var switchIds = doc.GetAllElements().OfType<GumpCheck>().GroupBy(c => c.SwitchId).Where(g => g.Count() > 1);
        foreach (var dupe in switchIds)
        {
            var names = string.Join(", ", dupe.Select(c => c.Name));
            problems.Add(new(ProblemSeverity.Warning, "CHK001",
                $"Duplicate SwitchID {dupe.Key} used by: {names}", names, 0));
        }

        // Check for duplicate entry IDs
        var entryIds = doc.GetAllElements().OfType<GumpTextEntry>().GroupBy(e => e.EntryId).Where(g => g.Count() > 1);
        foreach (var dupe in entryIds)
        {
            var names = string.Join(", ", dupe.Select(e => e.Name));
            problems.Add(new(ProblemSeverity.Warning, "TXT001",
                $"Duplicate EntryID {dupe.Key} used by: {names}", names, 0));
        }

        return problems;
    }

    private static void ValidateElement(List<GumpProblem> problems, GumpElement element, int page, GumpDocument doc)
    {
        // Check for out-of-bounds elements
        if (element.X < 0 || element.Y < 0)
            problems.Add(new(ProblemSeverity.Info, "POS001",
                $"Element is at negative position ({element.X},{element.Y}).", element.Name, page));

        if (element.X + element.Width > doc.CanvasWidth || element.Y + element.Height > doc.CanvasHeight)
            problems.Add(new(ProblemSeverity.Info, "POS002",
                $"Element extends beyond canvas bounds.", element.Name, page));

        // Zero-dimension elements
        if (element.Width <= 0 && element is not GumpLabel and not GumpTooltip)
            problems.Add(new(ProblemSeverity.Warning, "DIM001",
                $"Element has zero or negative width ({element.Width}).", element.Name, page));

        if (element.Height <= 0 && element is not GumpLabel and not GumpTooltip)
            problems.Add(new(ProblemSeverity.Warning, "DIM002",
                $"Element has zero or negative height ({element.Height}).", element.Name, page));

        // Type-specific checks
        switch (element)
        {
            case GumpButton btn:
                if (btn.NormalId == 0)
                    problems.Add(new(ProblemSeverity.Warning, "BTN002",
                        "Button has NormalID = 0 (no art).", element.Name, page));
                break;

            case GumpLabel label:
                if (string.IsNullOrWhiteSpace(label.Text))
                    problems.Add(new(ProblemSeverity.Info, "LBL001",
                        "Label has empty text.", element.Name, page));
                break;

            case GumpHtml html:
                if (string.IsNullOrWhiteSpace(html.Text))
                    problems.Add(new(ProblemSeverity.Info, "HTM001",
                        "HTML element has empty text.", element.Name, page));
                break;

            case GumpImage img:
                if (img.GumpId == 0)
                    problems.Add(new(ProblemSeverity.Warning, "IMG001",
                        "Image has GumpID = 0.", element.Name, page));
                break;

            case GumpBackground bg:
                if (bg.GumpId == 0)
                    problems.Add(new(ProblemSeverity.Warning, "BG001",
                        "Background has GumpID = 0.", element.Name, page));
                break;

            case GumpGroup group:
                foreach (var child in group.Children)
                    ValidateElement(problems, child, page, doc);
                break;
        }
    }
}

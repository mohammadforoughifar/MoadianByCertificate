using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Inventory.Shared.Security;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services.Office.Outgoing;

/// <summary>Maps the editor's sanitized HTML structure to QuestPDF blocks and inline text spans.</summary>
internal static class RichLetterPdfRenderer
{
    private sealed class Node
    {
        public string Tag { get; init; } = "#root";
        public string Text { get; init; } = string.Empty;
        public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Node> Children { get; } = new();
    }

    private static readonly Regex Tokens = new(@"<[^>]*>|[^<]+", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TagHeader = new(@"^<\s*(?<close>/?)\s*(?<name>[a-zA-Z][a-zA-Z0-9]*)\b(?<attrs>.*?)\s*/?\s*>$", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Attribute = new(@"(?<name>[^\s=/>]+)(?:\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<bare>[^\s>]+)))?", RegexOptions.Compiled);
    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase) { "br", "hr", "img" };
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
        { "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "ul", "ol", "table", "hr" };

    internal static bool AddTo(ColumnDescriptor column, string? html, float defaultFontSize, float lineHeight,
        Action<ColumnDescriptor>? keepWithTrailingText = null)
    {
        var root = Parse(RichLetterHtml.Sanitize(html));
        if (keepWithTrailingText != null)
        {
            var lastIndex = root.Children.FindLastIndex(node => node.Tag != "#text" || !string.IsNullOrWhiteSpace(node.Text));
            if (lastIndex >= 0)
            {
                var trailing = root.Children[lastIndex];
                var isParagraph = trailing.Tag is "p" or "#text" or "strong" or "b" or "em" or "i" or "u" or "span" ||
                    trailing.Tag == "div" && !trailing.Children.Any(child => BlockTags.Contains(child.Tag));
                if (isParagraph && TextLength(trailing) <= 360)
                {
                    RenderChildren(column, root.Children.Take(lastIndex).ToList(), defaultFontSize, lineHeight);
                    column.Item().ShowEntire().Column(group =>
                    {
                        RenderChildren(group, new[] { trailing }, defaultFontSize, lineHeight);
                        keepWithTrailingText(group);
                    });
                    return true;
                }

                if (trailing.Tag is "ol" or "ul")
                {
                    var items = trailing.Children.Where(child => child.Tag == "li").ToList();
                    if (items.Count >= 2 && TextLength(items[^1]) + TextLength(items[^2]) <= 360)
                    {
                        var ordered = trailing.Tag == "ol";
                        var prefixItems = trailing.Children.Where(child => child.Tag == "li").Take(items.Count - 2).ToList();
                        var preceding = root.Children.Take(lastIndex).ToList();
                        if (prefixItems.Count > 0) preceding.Add(CloneWithChildren(trailing, prefixItems));
                        RenderChildren(column, preceding, defaultFontSize, lineHeight);
                        column.Item().ShowEntire().Column(group =>
                        {
                            RenderListItem(group, items[^2], ordered, items.Count - 1, defaultFontSize, lineHeight);
                            RenderListItem(group, items[^1], ordered, items.Count, defaultFontSize, lineHeight);
                            keepWithTrailingText(group);
                        });
                        return true;
                    }
                }
            }
        }

        RenderChildren(column, root.Children, defaultFontSize, lineHeight);
        return false;
    }

    private static int TextLength(Node node) => node.Tag == "#text"
        ? node.Text.Length
        : node.Children.Sum(TextLength);

    private static Node CloneWithChildren(Node source, IEnumerable<Node> children)
    {
        var clone = new Node { Tag = source.Tag, Text = source.Text };
        foreach (var attribute in source.Attributes) clone.Attributes[attribute.Key] = attribute.Value;
        clone.Children.AddRange(children);
        return clone;
    }

    private static Node Parse(string html)
    {
        var root = new Node();
        var stack = new List<Node> { root };
        foreach (Match token in Tokens.Matches(html))
        {
            var value = token.Value;
            if (!value.StartsWith('<'))
            {
                stack[^1].Children.Add(new Node { Tag = "#text", Text = WebUtility.HtmlDecode(value) });
                continue;
            }

            var header = TagHeader.Match(value);
            if (!header.Success) continue;
            var name = header.Groups["name"].Value.ToLowerInvariant();
            if (header.Groups["close"].Value == "/")
            {
                var closeIndex = stack.FindLastIndex(x => x.Tag.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (closeIndex > 0) stack.RemoveRange(closeIndex, stack.Count - closeIndex);
                continue;
            }

            var node = new Node { Tag = name };
            foreach (Match attribute in Attribute.Matches(header.Groups["attrs"].Value))
            {
                var key = attribute.Groups["name"].Value.ToLowerInvariant();
                if (key is not ("style" or "colspan" or "rowspan" or "href" or "src" or "alt" or "width" or "height" or "dir"))
                    continue;
                var attrValue = attribute.Groups["dq"].Success ? attribute.Groups["dq"].Value
                    : attribute.Groups["sq"].Success ? attribute.Groups["sq"].Value
                    : attribute.Groups["bare"].Value;
                node.Attributes[key] = WebUtility.HtmlDecode(attrValue);
            }
            stack[^1].Children.Add(node);
            if (!VoidTags.Contains(name)) stack.Add(node);
        }
        return root;
    }

    private static void RenderChildren(ColumnDescriptor column, IReadOnlyList<Node> nodes, float fontSize, float lineHeight)
    {
        var inline = new List<Node>();
        void FlushInline()
        {
            if (inline.Count == 0) return;
            AddTextBlock(column, inline, fontSize, lineHeight, bold: false, extraTop: 0);
            inline.Clear();
        }

        foreach (var node in nodes)
        {
            if (node.Tag == "#text")
            {
                if (!string.IsNullOrWhiteSpace(node.Text)) inline.Add(node);
                else if (inline.Count > 0) inline.Add(new Node { Tag = "#text", Text = " " });
                continue;
            }
            if (BlockTags.Contains(node.Tag))
            {
                FlushInline();
                switch (node.Tag)
                {
                    case "ul":
                    case "ol":
                        RenderList(column, node, fontSize, lineHeight);
                        break;
                    case "table":
                        RenderTable(column, node, fontSize, lineHeight);
                        break;
                    case "hr":
                        column.Item().PaddingVertical(4).LineHorizontal(0.6f).LineColor("#94a3b8");
                        break;
                    case "blockquote":
                        column.Item().PaddingRight(10).Text(text =>
                        {
                            text.DefaultTextStyle(style => style
                                .FontSize(fontSize)
                                .LineHeight(lineHeight)
                                .DirectionFromRightToLeft());
                            text.AlignRight();
                            AppendInline(text, node, new Formatting(), fontSize);
                        });
                        break;
                    case "div":
                        if (node.Children.Any(child => child.Tag != "#text" && BlockTags.Contains(child.Tag)))
                            RenderChildren(column, node.Children, fontSize, lineHeight);
                        else
                            AddTextBlock(column, node.Children, fontSize, lineHeight, bold: false, extraTop: 0,
                                node.Attributes.GetValueOrDefault("style"));
                        break;
                    default:
                        var heading = node.Tag.Length == 2 && node.Tag[0] == 'h';
                        var headingSize = heading ? Math.Min(fontSize + 2, 20) : fontSize;
                        AddTextBlock(column, node.Children, headingSize, lineHeight, heading, ExtraTop(node.Tag, fontSize),
                            node.Attributes.GetValueOrDefault("style"));
                        break;
                }
            }
            else
            {
                inline.Add(node);
            }
        }
        FlushInline();
    }

    private static float ExtraTop(string tag, float fontSize) => tag.StartsWith('h') ? Math.Max(2, fontSize * .25f) : 0;

    private static void AddTextBlock(ColumnDescriptor column, IReadOnlyList<Node> nodes, float fontSize,
        float lineHeight, bool bold, float extraTop, string? style = null)
    {
        var item = column.Item();
        if (extraTop > 0) item = item.PaddingTop(extraTop);
        var blockStyle = ParseStyle(style);
        if (blockStyle.TryGetValue("font-size", out var size) && TryFontSize(size, out var styledSize))
            fontSize = Math.Clamp(styledSize, 6, 30);
        item.Text(text =>
        {
            text.DefaultTextStyle(style => style
                .FontSize(fontSize)
                .LineHeight(lineHeight)
                .DirectionFromRightToLeft());
            switch (blockStyle.GetValueOrDefault("text-align"))
            {
                case "center": text.AlignCenter(); break;
                case "left": text.AlignLeft(); break;
                case "justify": text.Justify(); break;
                default: text.AlignRight(); break;
            }
            foreach (var node in nodes) AppendInline(text, node, new Formatting { Bold = bold }, fontSize);
        });
    }

    private static void RenderList(ColumnDescriptor column, Node list, float fontSize, float lineHeight)
    {
        var ordered = list.Tag == "ol";
        var index = 0;
        foreach (var item in list.Children.Where(x => x.Tag == "li"))
        {
            index++;
            RenderListItem(column, item, ordered, index, fontSize, lineHeight);
        }
    }

    private static void RenderListItem(ColumnDescriptor column, Node item, bool ordered, int index,
        float fontSize, float lineHeight)
    {
        var nested = item.Children.Where(x => x.Tag is "ol" or "ul").ToList();
        var marker = ordered ? PersianDigits(index.ToString(CultureInfo.InvariantCulture)) + ". " : "• ";
        column.Item().Text(text =>
        {
            text.DefaultTextStyle(style => style
                .FontSize(fontSize)
                .LineHeight(lineHeight)
                .DirectionFromRightToLeft());
            text.AlignRight();
            text.Span(marker).Bold();
            foreach (var child in item.Children.Where(x => x.Tag is not ("ol" or "ul" or "table")))
                AppendInline(text, child, new Formatting(), fontSize);
        });
        foreach (var nestedList in nested) RenderList(column, nestedList, fontSize, lineHeight);
    }

    private static void RenderTable(ColumnDescriptor column, Node tableNode, float fontSize, float lineHeight)
    {
        var rows = Descendants(tableNode).Where(x => x.Tag == "tr").ToList();
        if (rows.Count == 0) return;
        var columnCount = Math.Max(1, rows.Max(row => row.Children.Count(cell => cell.Tag is "td" or "th")));
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                for (var index = 0; index < columnCount; index++) columns.RelativeColumn();
            });
            foreach (var row in rows)
            {
                foreach (var cell in row.Children.Where(x => x.Tag is "td" or "th"))
                {
                    table.Cell().Border(0.5f).BorderColor("#94a3b8").Padding(4)
                        .Text(text =>
                        {
                            text.DefaultTextStyle(style => style
                                .FontSize(fontSize)
                                .LineHeight(lineHeight)
                                .DirectionFromRightToLeft());
                            text.AlignRight();
                            AppendInline(text, cell, new Formatting { Bold = cell.Tag == "th" }, fontSize);
                        });
                }
            }
        });
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private sealed class Formatting
    {
        public bool Bold { get; init; }
        public bool Italic { get; init; }
        public bool Underline { get; init; }
        public bool Strike { get; init; }
    }

    private static void AppendInline(TextDescriptor text, Node node, Formatting parent, float fontSize)
    {
        if (node.Tag == "#text")
        {
            if (node.Text.Length == 0) return;
            var span = text.Span(node.Text.Replace("\u00a0", " ").Replace("\u200b", string.Empty));
            if (parent.Bold) span.Bold();
            if (parent.Italic) span.Italic();
            if (parent.Underline) span.Underline();
            if (parent.Strike) span.Strikethrough();
            var style = ParseStyle(node.Attributes.GetValueOrDefault("style"));
            if (style.TryGetValue("font-size", out var size) && TryFontSize(size, out var styledSize)) span.FontSize(Math.Clamp(styledSize, 6, 30));
            return;
        }
        if (node.Tag is "br")
        {
            text.Span("\n");
            return;
        }
        if (node.Tag is "img")
        {
            var alt = node.Attributes.GetValueOrDefault("alt");
            if (!string.IsNullOrWhiteSpace(alt)) text.Span($"[{alt}]");
            return;
        }

        var styles = ParseStyle(node.Attributes.GetValueOrDefault("style"));
        var formatting = new Formatting
        {
            Bold = parent.Bold || node.Tag is "strong" or "b" || styles.GetValueOrDefault("font-weight") is "bold" or "700" or "800" or "900",
            Italic = parent.Italic || node.Tag is "em" or "i" || styles.GetValueOrDefault("font-style") == "italic",
            Underline = parent.Underline || node.Tag == "u" || styles.GetValueOrDefault("text-decoration")?.Contains("underline", StringComparison.OrdinalIgnoreCase) == true,
            Strike = parent.Strike || node.Tag is "s" or "del" || styles.GetValueOrDefault("text-decoration")?.Contains("line-through", StringComparison.OrdinalIgnoreCase) == true
        };
        foreach (var child in node.Children) AppendInline(text, child, formatting, fontSize);
    }

    private static Dictionary<string, string> ParseStyle(string? source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in (source ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = declaration.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2) result[parts[0].ToLowerInvariant()] = parts[1];
        }
        return result;
    }

    private static bool TryFontSize(string value, out float points)
    {
        points = 0;
        var match = Regex.Match(value, @"^(?<n>\d+(?:\.\d+)?)(?<unit>px|pt|em|rem)?$", RegexOptions.IgnoreCase);
        if (!match.Success || !float.TryParse(match.Groups["n"].Value, CultureInfo.InvariantCulture, out var amount)) return false;
        points = match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "px" => amount * .75f,
            "em" or "rem" => amount * 12,
            _ => amount
        };
        return true;
    }

    private static string PersianDigits(string value) => new(value.Select(c => c is >= '0' and <= '9' ? "۰۱۲۳۴۵۶۷۸۹"[c - '0'] : c).ToArray());
}

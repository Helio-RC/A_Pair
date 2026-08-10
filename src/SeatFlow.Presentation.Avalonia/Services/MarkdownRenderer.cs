using System.Collections.Generic;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SeatFlow.Presentation.Avalonia.Services;

public enum MdBlockKind { Title, Heading, SubHeading, ListItem, Paragraph, Code, Empty }

/// <param name="Text">渲染文本（内联样式已展平为纯文本）</param>
/// <param name="Kind">块类型</param>
public record MdBlock (string Text, MdBlockKind Kind)
{
    public bool IsTitle      => Kind == MdBlockKind.Title;
    public bool IsHeading    => Kind == MdBlockKind.Heading;
    public bool IsSubHeading => Kind == MdBlockKind.SubHeading;
    public bool IsListItem   => Kind == MdBlockKind.ListItem;
    public bool IsParagraph  => Kind == MdBlockKind.Paragraph;
    public bool IsCode       => Kind == MdBlockKind.Code;
    public bool IsEmpty      => Kind == MdBlockKind.Empty;
}

public static class MarkdownRenderer
{
    public static List<MdBlock> Render (string markdown)
    {
        var document = Markdown.Parse(markdown);
        var blocks = new List<MdBlock>();

        foreach (var block in document)
            RenderBlock(block, blocks);

        return blocks;
    }

    private static void RenderBlock (Block block, List<MdBlock> blocks)
    {
        switch (block)
        {
            case HeadingBlock h:
                blocks.Add(new MdBlock(FlattenInlines(h.Inline), h.Level switch
                {
                    1 => MdBlockKind.Title,
                    2 => MdBlockKind.Heading,
                    _ => MdBlockKind.SubHeading,
                }));
                break;

            case ParagraphBlock p:
                blocks.Add(new MdBlock(FlattenInlines(p.Inline), MdBlockKind.Paragraph));
                break;

            case CodeBlock c:
                blocks.Add(new MdBlock(string.Join("\n", c.Lines), MdBlockKind.Code));
                break;

            case ListBlock l:
                foreach (var item in l)
                {
                    if (item is ListItemBlock li)
                    foreach (var sub in li)
                    {
                        if (sub is ParagraphBlock lp)
                            blocks.Add(new MdBlock(FlattenInlines(lp.Inline), MdBlockKind.ListItem));
                    }
                }
                break;

            case ThematicBreakBlock:
                blocks.Add(new MdBlock("", MdBlockKind.Empty));
                break;
        }
    }

    /// <summary>展平内联元素为纯文本</summary>
    private static string FlattenInlines (ContainerInline? container)
    {
        if (container is null) return "";

        var sb = new System.Text.StringBuilder();
        foreach (var inline in container)
            AppendInline(inline, sb);
        return sb.ToString();
    }

    private static void AppendInline (Inline inline, System.Text.StringBuilder sb)
    {
        switch (inline)
        {
            case LiteralInline lit:
                sb.Append(lit.Content.ToString());
                break;
            case CodeInline code:
                sb.Append(code.Content);
                break;
            case LineBreakInline:
                sb.Append(' ');
                break;
            case LinkInline link:
                sb.Append(link.Label ?? link.Url ?? "");
                break;
            case EmphasisInline em:
                foreach (var child in em)
                    AppendInline(child, sb);
                break;
            case ContainerInline ci:
                foreach (var child in ci)
                    AppendInline(child, sb);
                break;
        }
    }
}

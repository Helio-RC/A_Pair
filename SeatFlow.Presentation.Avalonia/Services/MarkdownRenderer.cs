using System;
using System.Collections.Generic;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SeatFlow.Presentation.Avalonia.Services;

public static class MarkdownRenderer
{
    private static readonly Thickness ListItemMargin  = new(16, 1, 0, 1);
    private static readonly Thickness Header1Margin   = new(0, 16, 0, 4);
    private static readonly Thickness Header2Margin   = new(0, 12, 0, 2);
    private static readonly Thickness Header3Margin   = new(0, 8, 0, 2);
    private static readonly Thickness ParagraphMargin = new(0, 3, 0, 3);
    private const string MediumBrushKey = "SystemControlForegroundBaseMediumBrush";

    public static List<Control> Render (string markdown)
    {
        var document = Markdown.Parse(markdown);
        var controls = new List<Control>();

        foreach (var block in document)
            RenderBlock(block, controls);

        return controls;
    }

    private static void RenderBlock (Block block, List<Control> controls)
    {
        switch (block)
        {
            case HeadingBlock h:
                controls.Add(RenderHeading(h));
                break;
            case ParagraphBlock p:
                controls.Add(RenderParagraph(p));
                break;
            case ListBlock l:
                controls.Add(RenderList(l));
                break;
            case CodeBlock c:
                controls.Add(RenderCode(c));
                break;
            case ThematicBreakBlock:
                controls.Add(new Separator { Margin = new Thickness(0, 8) });
                break;
        }
    }

    // ═══ Headings ═══

    private static Control RenderHeading (HeadingBlock block)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
        RenderInlines(block.Inline, tb.Inlines);

        (tb.FontSize, tb.FontWeight, tb.Margin) = block.Level switch
        {
            1 => (20, FontWeight.Bold, Header1Margin),
            2 => (16, FontWeight.SemiBold, Header2Margin),
            _ => (14, FontWeight.SemiBold, Header3Margin),
        };

        return tb;
    }

    // ═══ Paragraph ═══

    private static Control RenderParagraph (ParagraphBlock block)
    {
        if (block.Inline is null)
            return new Border { Height = 6 };

        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Margin = ParagraphMargin,
            Foreground = TryGetBrush(MediumBrushKey),
        };
        RenderInlines(block.Inline, tb.Inlines);
        return tb;
    }

    // ═══ List ═══

    private static Control RenderList (ListBlock block)
    {
        var panel = new StackPanel { Spacing = 0 };

        foreach (var item in block)
        {
            if (item is not ListItemBlock li) continue;

            var itemPanel = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Horizontal
            };

            itemPanel.Children.Add(new TextBlock
            {
                Text = "·",
                FontSize = 13,
                Margin = new Thickness(16, 1, 6, 0),
                Foreground = TryGetBrush(MediumBrushKey),
            });

            var contentStack = new StackPanel();
            foreach (var subBlock in li)
            {
                if (subBlock is ParagraphBlock p)
                {
                    var tb = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13,
                        Margin = ListItemMargin,
                        Foreground = TryGetBrush(MediumBrushKey),
                    };
                    if (p.Inline is not null)
                        RenderInlines(p.Inline, tb.Inlines);
                    contentStack.Children.Add(tb);
                }
            }
            itemPanel.Children.Add(contentStack);
            panel.Children.Add(itemPanel);
        }

        return panel;
    }

    // ═══ Code ═══

    private static Control RenderCode (CodeBlock block)
    {
        var border = new Border
        {
            CornerRadius = new global::Avalonia.CornerRadius(4),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 4),
            Background = TryGetBrush("SystemControlBackgroundChromeMediumLowBrush"),
        };
        border.Child = new TextBlock
        {
            Text = string.Join("\n", block.Lines),
            FontFamily = new FontFamily("Cascadia Code, JetBrains Mono, Consolas, monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        return border;
    }

    // ═══ Inline rendering ═══

    private static void RenderInlines (ContainerInline? container, InlineCollection target)
    {
        if (container is null) return;

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    target.Add(new Run(lit.Content.ToString() ?? ""));
                    break;

                case CodeInline code:
                    target.Add(new Run(code.Content)
                    {
                        FontFamily = new FontFamily("Cascadia Code, JetBrains Mono, Consolas, monospace"),
                        FontSize = 12,
                    });
                    break;

                case EmphasisInline em when em.DelimiterCount == 1:
                    RenderEmphasisChildren(em, target, FontStyle.Italic, FontWeight.Normal);
                    break;

                case EmphasisInline em when em.DelimiterCount >= 2:
                    RenderEmphasisChildren(em, target, FontStyle.Normal, FontWeight.Bold);
                    break;

                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;

                case LinkInline link:
                    target.Add(new Run(link.Label ?? link.Url ?? "")
                    {
                        Foreground = TryGetBrush("SystemAccentColor"),
                    });
                    break;
            }
        }
    }

    private static void RenderEmphasisChildren (ContainerInline container, InlineCollection target,
        FontStyle style, FontWeight weight)
    {
        foreach (var child in container)
        {
            if (child is LiteralInline lit)
                target.Add(new Run(lit.Content.ToString() ?? "") { FontStyle = style, FontWeight = weight });
        }
    }

    private static IBrush? TryGetBrush (string key)
    {
        return global::Avalonia.Application.Current?.Resources.TryGetResource(key, null, out var resource) == true
            ? resource as IBrush
            : null;
    }
}

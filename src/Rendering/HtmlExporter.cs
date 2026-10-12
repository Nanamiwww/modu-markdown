using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CodexMarkdownReader.Markdown;

namespace CodexMarkdownReader.Rendering
{
    public sealed class MathImage
    {
        public string DataUri;
        public double Width;    // CSS pixels
        public double Height;
        /// <summary>Distance from the formula's baseline to the bottom of the image (aligns inline formulas with the text).</summary>
        public double Depth;
        /// <summary>The equation tag of a displayed formula, e.g. "(1)"; null if none.</summary>
        public string Tag;
    }

    /// <summary>
    /// Exports a parsed document as a single self-contained HTML file: styles are inline,
    /// local images are embedded as data URIs and formulas are embedded as rendered PNGs.
    /// Raw HTML from the Markdown source is escaped, exactly like the preview.
    /// </summary>
    public sealed class HtmlExporter
    {
        private readonly string baseDirectory;
        private readonly Func<string, bool, MathImage> mathImage;
        private StringBuilder html;
        private int embeddedImages, remoteImages, missingImages;

        /// <summary>Local images written into the file as data URIs.</summary>
        public int EmbeddedImageCount { get { return embeddedImages; } }
        /// <summary>Remote images are kept as plain links (never loaded automatically), like the preview.</summary>
        public int RemoteImageCount { get { return remoteImages; } }
        /// <summary>Images that could not be found or embedded.</summary>
        public int MissingImageCount { get { return missingImages; } }

        /// <param name="mathImage">Returns a rendered image for (latex, isBlock), or null to fall back to the source text.</param>
        public HtmlExporter(string baseDirectory, Func<string, bool, MathImage> mathImage)
        {
            this.baseDirectory = baseDirectory;
            this.mathImage = mathImage;
        }

        public string Export(MarkdownDocument document, string title)
        {
            html = new StringBuilder();
            embeddedImages = 0; remoteImages = 0; missingImages = 0;
            html.Append("<!DOCTYPE html>\n<html lang=\"zh-CN\">\n<head>\n<meta charset=\"utf-8\">\n");
            html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            html.Append("<meta name=\"generator\" content=\"墨读 Markdown\">\n");
            html.Append("<title>").Append(TextUtil.HtmlEncode(String.IsNullOrWhiteSpace(title) ? "Markdown 文档" : title)).Append("</title>\n");
            html.Append("<style>\n").Append(Css).Append("\n</style>\n</head>\n<body>\n<main class=\"markdown-body\">\n");
            using (MathContext.Use(MathContext.ForDocument(document)))
            {
                WriteBlocks(document.Blocks, 0);
                if (document.UsedFootnotes.Count > 0)
                {
                    html.Append("<section class=\"footnotes\">\n<hr>\n<ol>\n");
                    foreach (FootnoteDefinition footnote in document.UsedFootnotes)
                    {
                        string number = footnote.Number.ToString(CultureInfo.InvariantCulture);
                        html.Append("<li id=\"fn-").Append(number).Append("\">\n");
                        WriteBlocks(footnote.Blocks, 1);
                        html.Append("<a href=\"#fnref-").Append(number).Append("\" class=\"footnote-back\" aria-label=\"返回正文\">↩</a>\n</li>\n");
                    }
                    html.Append("</ol>\n</section>\n");
                }
            }
            html.Append("</main>\n</body>\n</html>\n");
            return html.ToString();
        }

        private void WriteBlocks(List<Block> blocks, int depth)
        {
            foreach (Block block in blocks) WriteBlock(block, depth);
        }

        private void WriteBlock(Block block, int depth)
        {
            switch (block.Kind)
            {
                case BlockKind.Paragraph:
                    html.Append("<p>");
                    WriteInlines(block.Inlines);
                    html.Append("</p>\n");
                    break;
                case BlockKind.Heading:
                    {
                        string tag = "h" + Math.Max(1, Math.Min(6, block.Level)).ToString(CultureInfo.InvariantCulture);
                        html.Append('<').Append(tag);
                        if (!String.IsNullOrEmpty(block.Id)) html.Append(" id=\"").Append(TextUtil.HtmlEncode(block.Id)).Append('"');
                        html.Append('>');
                        WriteInlines(block.Inlines);
                        html.Append("</").Append(tag).Append(">\n");
                        break;
                    }
                case BlockKind.CodeBlock:
                    {
                        string language = (block.Info ?? "").Trim();
                        int space = language.IndexOfAny(new char[] { ' ', '\t', '{' });
                        if (space > 0) language = language.Substring(0, space);
                        html.Append("<pre><code");
                        if (language.Length > 0) html.Append(" class=\"language-").Append(TextUtil.HtmlEncode(language)).Append('"');
                        html.Append('>').Append(TextUtil.HtmlEncode(block.RawText)).Append("</code></pre>\n");
                        break;
                    }
                case BlockKind.MathBlock:
                    WriteMathBlock(block.RawText);
                    break;
                case BlockKind.ThematicBreak:
                    html.Append("<hr>\n");
                    break;
                case BlockKind.Quote:
                    if (block.AlertKind != null)
                    {
                        string kind = block.AlertKind.ToLowerInvariant();
                        string label = block.AlertKind == "NOTE" ? "说明" : block.AlertKind == "TIP" ? "提示" : block.AlertKind == "IMPORTANT" ? "重要" : block.AlertKind == "WARNING" ? "警告" : "注意";
                        html.Append("<div class=\"alert alert-").Append(kind).Append("\"><p class=\"alert-title\">").Append(label).Append("</p>\n");
                        WriteBlocks(block.Children, depth);
                        html.Append("</div>\n");
                    }
                    else
                    {
                        html.Append("<blockquote>\n");
                        WriteBlocks(block.Children, depth);
                        html.Append("</blockquote>\n");
                    }
                    break;
                case BlockKind.List:
                    {
                        bool anyTask = false;
                        foreach (Block item in block.Children) if (item.IsTask) { anyTask = true; break; }
                        string tag = block.Ordered ? "ol" : "ul";
                        html.Append('<').Append(tag);
                        if (block.Ordered && block.Start != 1) html.Append(" start=\"").Append(block.Start.ToString(CultureInfo.InvariantCulture)).Append('"');
                        if (anyTask) html.Append(" class=\"task-list\"");
                        html.Append(">\n");
                        foreach (Block item in block.Children)
                        {
                            html.Append("<li>");
                            if (item.IsTask) html.Append("<input type=\"checkbox\" disabled").Append(item.TaskChecked ? " checked" : "").Append("> ");
                            if (block.Tight && item.Children.Count > 0 && item.Children[0].Kind == BlockKind.Paragraph)
                            {
                                WriteInlines(item.Children[0].Inlines);
                                if (item.Children.Count > 1) html.Append('\n');
                                for (int c = 1; c < item.Children.Count; c++)
                                {
                                    if (item.Children[c].Kind == BlockKind.Paragraph) { WriteInlines(item.Children[c].Inlines); html.Append('\n'); }
                                    else WriteBlock(item.Children[c], depth + 1);
                                }
                            }
                            else WriteBlocks(item.Children, depth + 1);
                            html.Append("</li>\n");
                        }
                        html.Append("</").Append(tag).Append(">\n");
                        break;
                    }
                case BlockKind.Table:
                    {
                        html.Append("<div class=\"table-wrap\"><table>\n<thead><tr>");
                        for (int c = 0; c < block.Aligns.Count; c++)
                        {
                            html.Append("<th").Append(AlignAttribute(block.Aligns[c])).Append('>');
                            if (c < block.HeaderCells.Count) WriteInlines(block.HeaderCells[c]);
                            html.Append("</th>");
                        }
                        html.Append("</tr></thead>\n<tbody>\n");
                        foreach (List<List<Inline>> row in block.Rows)
                        {
                            html.Append("<tr>");
                            for (int c = 0; c < block.Aligns.Count; c++)
                            {
                                html.Append("<td").Append(AlignAttribute(block.Aligns[c])).Append('>');
                                if (c < row.Count) WriteInlines(row[c]);
                                html.Append("</td>");
                            }
                            html.Append("</tr>\n");
                        }
                        html.Append("</tbody>\n</table></div>\n");
                        break;
                    }
            }
        }

        private static string AlignAttribute(TableAlign align)
        {
            switch (align)
            {
                case TableAlign.Center: return " style=\"text-align:center\"";
                case TableAlign.Right: return " style=\"text-align:right\"";
                case TableAlign.Left: return " style=\"text-align:left\"";
            }
            return "";
        }

        private void WriteInlines(List<Inline> inlines)
        {
            if (inlines == null) return;
            foreach (Inline inline in inlines) WriteInline(inline);
        }

        private void WriteWrapped(string tag, Inline inline)
        {
            html.Append('<').Append(tag).Append('>');
            WriteInlines(inline.Children);
            html.Append("</").Append(tag).Append('>');
        }

        private void WriteInline(Inline inline)
        {
            switch (inline.Kind)
            {
                case InlineKind.Text: html.Append(TextUtil.HtmlEncode(inline.Text)); break;
                case InlineKind.SoftBreak: html.Append('\n'); break;
                case InlineKind.HardBreak: html.Append("<br>\n"); break;
                case InlineKind.Code: html.Append("<code>").Append(TextUtil.HtmlEncode(inline.Text)).Append("</code>"); break;
                case InlineKind.Emphasis: WriteWrapped("em", inline); break;
                case InlineKind.Strong: WriteWrapped("strong", inline); break;
                case InlineKind.Strikethrough: WriteWrapped("del", inline); break;
                case InlineKind.Subscript: WriteWrapped("sub", inline); break;
                case InlineKind.Superscript: WriteWrapped("sup", inline); break;
                case InlineKind.Keyboard: WriteWrapped("kbd", inline); break;
                case InlineKind.Mark: WriteWrapped("mark", inline); break;
                case InlineKind.Math: WriteMath(inline.Text, false); break;
                case InlineKind.FootnoteRef:
                    {
                        string number = inline.FootnoteNumber.ToString(CultureInfo.InvariantCulture);
                        html.Append("<sup class=\"footnote-ref\"><a id=\"fnref-").Append(number).Append("\" href=\"#fn-").Append(number).Append("\">[").Append(number).Append("]</a></sup>");
                        break;
                    }
                case InlineKind.Link:
                    {
                        string href = ExportHref(inline.Href);
                        if (href == null) { WriteInlines(inline.Children); break; }
                        html.Append("<a href=\"").Append(TextUtil.HtmlEncode(href)).Append('"');
                        if (!String.IsNullOrEmpty(inline.Title)) html.Append(" title=\"").Append(TextUtil.HtmlEncode(inline.Title)).Append('"');
                        html.Append('>');
                        WriteInlines(inline.Children);
                        html.Append("</a>");
                        break;
                    }
                case InlineKind.Image:
                    WriteImage(inline);
                    break;
            }
        }

        private string ExportHref(string raw)
        {
            LinkTarget target = LinkPolicy.Resolve(raw, baseDirectory);
            switch (target.Kind)
            {
                case LinkKind.Web:
                case LinkKind.Mail: return target.Uri.AbsoluteUri;
                case LinkKind.Anchor: return "#" + Uri.EscapeDataString(target.Fragment ?? "");
                case LinkKind.MarkdownFile:
                case LinkKind.LocalFile:
                case LinkKind.LocalDirectory:
                    return target.Uri == null ? null : target.Uri.AbsoluteUri + (String.IsNullOrEmpty(target.Fragment) ? "" : "#" + Uri.EscapeDataString(target.Fragment));
            }
            return null;
        }

        private void WriteImage(Inline inline)
        {
            string alt = TextUtil.HtmlEncode(inline.Text ?? "");
            LinkTarget target = LinkPolicy.Resolve(inline.Href, baseDirectory);
            if (target.Kind == LinkKind.Web)
            {
                // Same rule as the preview: remote images are never fetched automatically. Keep a plain link instead.
                remoteImages++;
                html.Append("<span class=\"missing-image\">[远程图片（未内嵌）：<a href=\"").Append(TextUtil.HtmlEncode(target.Uri.AbsoluteUri))
                    .Append("\">").Append(alt.Length > 0 ? alt : "图片").Append("</a>]</span>");
                return;
            }
            string source = target.Kind == LinkKind.LocalFile ? EmbedFile(target.Path) : null;
            if (source == null)
            {
                missingImages++;
                html.Append("<span class=\"missing-image\">[图片：").Append(alt).Append("]</span>");
                return;
            }
            embeddedImages++;
            html.Append("<img src=\"").Append(TextUtil.HtmlEncode(source)).Append("\" alt=\"").Append(alt).Append('"');
            if (!String.IsNullOrEmpty(inline.Title)) html.Append(" title=\"").Append(TextUtil.HtmlEncode(inline.Title)).Append('"');
            html.Append(" loading=\"lazy\">");
        }

        private static string EmbedFile(string path)
        {
            if (String.IsNullOrEmpty(path) || LinkPolicy.IsDevicePath(path)) return null;
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists || info.Length > ImageCache.MaxFileBytes) return null;
                string mime;
                switch (info.Extension.ToLowerInvariant())
                {
                    case ".png": mime = "image/png"; break;
                    case ".jpg":
                    case ".jpeg": mime = "image/jpeg"; break;
                    case ".gif": mime = "image/gif"; break;
                    case ".bmp": mime = "image/bmp"; break;
                    case ".webp": mime = "image/webp"; break;
                    case ".svg": mime = "image/svg+xml"; break;
                    case ".ico": mime = "image/x-icon"; break;
                    case ".tif":
                    case ".tiff": mime = "image/tiff"; break;
                    case ".avif": mime = "image/avif"; break;
                    default: return null;
                }
                return "data:" + mime + ";base64," + Convert.ToBase64String(File.ReadAllBytes(info.FullName));
            }
            catch { return null; }
        }

        private MathImage RenderMath(string source, bool block)
        {
            if (mathImage == null) return null;
            try { return mathImage(source, block); }
            catch { return null; }
        }

        private void WriteMathBlock(string source)
        {
            MathImage image = RenderMath(source, true);
            bool tagged = image != null && !String.IsNullOrEmpty(image.DataUri) && !String.IsNullOrEmpty(image.Tag);
            html.Append(tagged ? "<div class=\"math-block math-tagged\">" : "<div class=\"math-block\">");
            WriteMathImage(source, true, image);
            if (tagged) html.Append("<span class=\"math-tag\">").Append(TextUtil.HtmlEncode(image.Tag)).Append("</span>");
            html.Append("</div>\n");
        }

        private void WriteMath(string source, bool block)
        {
            WriteMathImage(source, block, RenderMath(source, block));
        }

        private void WriteMathImage(string source, bool block, MathImage image)
        {
            string alt = TextUtil.HtmlEncode(source ?? "");
            if (image == null || String.IsNullOrEmpty(image.DataUri))
            {
                html.Append(block ? "<code class=\"math-source\">$$ " : "<code class=\"math-source\">$").Append(alt).Append(block ? " $$</code>" : "$</code>");
                return;
            }
            html.Append("<img class=\"").Append(block ? "math math-display" : "math math-inline").Append("\" src=\"").Append(image.DataUri)
                .Append("\" width=\"").Append(Math.Ceiling(image.Width).ToString(CultureInfo.InvariantCulture))
                .Append("\" height=\"").Append(Math.Ceiling(image.Height).ToString(CultureInfo.InvariantCulture)).Append('"');
            if (!block) html.Append(" style=\"vertical-align:-").Append(Math.Round(image.Depth, 1).ToString(CultureInfo.InvariantCulture)).Append("px\"");
            html.Append(" alt=\"").Append(alt).Append("\" title=\"").Append(alt).Append("\">");
        }

        private const string Css = @":root { color-scheme: light dark; --text:#1f2328; --muted:#5e6772; --border:#e3e6ea; --code:#f6f7f9; --inline:#eff1f3; --accent:#16866a; --link:#0a7a5f; --quote:#f6f8f9; --bar:#cfd6dd; --bg:#ffffff; }
@media (prefers-color-scheme: dark) { :root { --text:#e6e8eb; --muted:#9ba4ae; --border:#2a3037; --code:#161a1f; --inline:#23282f; --accent:#3aae8c; --link:#6acbae; --quote:#14191b; --bar:#3a424b; --bg:#0f1215; } img.math { filter: invert(1) hue-rotate(180deg); } }
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--text); font: 16px/1.75 ""Segoe UI"", ""Microsoft YaHei"", ""PingFang SC"", ""Noto Sans CJK SC"", sans-serif; }
.markdown-body { max-width: 860px; margin: 0 auto; padding: 40px 28px 80px; }
h1, h2, h3, h4, h5, h6 { line-height: 1.35; margin: 1.6em 0 0.6em; font-weight: 600; }
h1 { font-size: 1.8em; } h2 { font-size: 1.45em; } h3 { font-size: 1.22em; } h4 { font-size: 1.08em; } h5 { font-size: 1em; } h6 { font-size: .95em; color: var(--muted); }
h1, h2 { padding-bottom: .3em; border-bottom: 1px solid var(--border); }
.markdown-body > :first-child { margin-top: 0; }
p, ul, ol, blockquote, pre, table, .alert { margin: 0 0 1em; }
a { color: var(--link); text-decoration: none; } a:hover { text-decoration: underline; }
code, kbd, pre { font-family: ""Cascadia Mono"", Consolas, ""Microsoft YaHei"", monospace; }
code { background: var(--inline); padding: .1em .35em; border-radius: 4px; font-size: .9em; }
pre { background: var(--code); border: 1px solid var(--border); border-radius: 8px; padding: 14px 16px; overflow: auto; line-height: 1.55; }
pre code { background: none; padding: 0; font-size: .88em; }
kbd { border: 1px solid var(--border); border-bottom-width: 2px; border-radius: 4px; padding: 0 .35em; font-size: .85em; }
mark { background: #fff0a0; }
blockquote { margin-left: 0; padding: .6em 1em; border-left: 3px solid var(--bar); background: var(--quote); color: var(--muted); }
blockquote > :last-child, .alert > :last-child { margin-bottom: 0; }
.alert { padding: .6em 1em; border-left: 3px solid; background: var(--quote); }
.alert-title { font-weight: 600; margin-bottom: .3em; }
.alert-note { border-color: #0969da; } .alert-note .alert-title { color: #0969da; }
.alert-tip { border-color: #1a7f37; } .alert-tip .alert-title { color: #1a7f37; }
.alert-important { border-color: #8250df; } .alert-important .alert-title { color: #8250df; }
.alert-warning { border-color: #9a6700; } .alert-warning .alert-title { color: #9a6700; }
.alert-caution { border-color: #cf222e; } .alert-caution .alert-title { color: #cf222e; }
ul, ol { padding-left: 1.8em; } li + li { margin-top: .2em; } li > p { margin: .3em 0; }
ul.task-list { list-style: none; padding-left: .4em; } ul.task-list input { margin-right: .5em; }
.table-wrap { overflow-x: auto; margin-bottom: 1em; } table { border-collapse: collapse; margin: 0; }
th, td { border: 1px solid var(--border); padding: 7px 12px; } th { background: var(--code); font-weight: 600; }
tbody tr:nth-child(even) { background: color-mix(in srgb, var(--code) 50%, transparent); }
hr { border: 0; border-top: 1px solid var(--border); margin: 1.8em 0; }
img { max-width: 100%; height: auto; }
img.math { max-width: none; }
.math-block { text-align: center; overflow-x: auto; margin: 0 0 1em; }
.math-tagged { position: relative; padding: 0 4.5em; } .math-tag { position: absolute; right: 0; top: 50%; transform: translateY(-50%); }
code.math-source { white-space: pre-wrap; }
.footnotes { color: var(--muted); font-size: .92em; } .footnote-ref a { font-size: .8em; }
.missing-image { color: var(--muted); font-style: italic; }
@media print { body { background: #fff; color: #000; } .markdown-body { max-width: none; padding: 0; } pre { white-space: pre-wrap; } }";
    }
}

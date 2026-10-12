using System;
using System.Collections.Generic;
using System.Text;

namespace CodexMarkdownReader.Markdown
{
    public enum BlockKind
    {
        Paragraph,
        Heading,
        CodeBlock,
        MathBlock,
        List,
        ListItem,
        Quote,
        Table,
        ThematicBreak
    }

    public enum TableAlign { None, Left, Center, Right }

    /// <summary>A parsed block. Container blocks keep their children in <see cref="Children"/>.</summary>
    public sealed class Block
    {
        public BlockKind Kind;
        public int SourceLine;          // zero-based line in the original text
        public int SourceEndLine;       // zero-based, inclusive

        // Leaf content (paragraph, heading, table cells are parsed separately).
        public List<Inline> Inlines;
        public string RawText;          // code, math source, heading raw text

        // Heading
        public int Level;
        public string Id;

        // Code block
        public string Info;

        // List
        public bool Ordered;
        public int Start = 1;
        public bool Tight = true;
        public char BulletChar;

        // List item
        public bool IsTask;
        public bool TaskChecked;

        // Quote alert: NOTE, TIP, IMPORTANT, WARNING, CAUTION or null
        public string AlertKind;

        // Table
        public List<TableAlign> Aligns;
        public List<List<Inline>> HeaderCells;
        public List<List<List<Inline>>> Rows;
        internal List<string> RawHeader;
        internal List<List<string>> RawRows;

        public List<Block> Children = new List<Block>();

        public Block(BlockKind kind, int line)
        {
            Kind = kind;
            SourceLine = line;
            SourceEndLine = line;
        }
    }

    public enum InlineKind
    {
        Text,
        Code,
        Emphasis,
        Strong,
        Strikethrough,
        Link,
        Image,
        Math,
        SoftBreak,
        HardBreak,
        FootnoteRef,
        Subscript,
        Superscript,
        Keyboard,
        Mark
    }

    public sealed class Inline
    {
        public InlineKind Kind;
        public string Text;             // text, code, math source, image alt
        public string Href;             // link / image destination (raw, unresolved)
        public string Title;
        public int FootnoteNumber;      // footnote reference display number
        public string FootnoteId;
        public List<Inline> Children;

        // Levels of nested containers below and including this node (0 for text), -1 until measured.
        // Set while parsing so that the depth limit for emphasis can be checked cheaply.
        internal int CachedHeight = -1;

        // Delimiter bookkeeping used only while parsing emphasis.
        internal char DelimiterChar;
        internal int DelimiterCount;
        internal int DelimiterOriginal;
        internal bool CanOpen;
        internal bool CanClose;
        internal bool IsDelimiter;

        public Inline(InlineKind kind)
        {
            Kind = kind;
        }

        public static Inline MakeText(string value)
        {
            Inline node = new Inline(InlineKind.Text);
            node.Text = value;
            return node;
        }

        public static string PlainText(IEnumerable<Inline> inlines)
        {
            StringBuilder builder = new StringBuilder();
            AppendPlain(builder, inlines);
            return builder.ToString();
        }

        private static void AppendPlain(StringBuilder builder, IEnumerable<Inline> inlines)
        {
            if (inlines == null) return;
            foreach (Inline inline in inlines)
            {
                switch (inline.Kind)
                {
                    case InlineKind.Text:
                    case InlineKind.Code:
                    case InlineKind.Math:
                        builder.Append(inline.Text);
                        break;
                    case InlineKind.Image:
                        builder.Append(inline.Text);
                        break;
                    case InlineKind.SoftBreak:
                    case InlineKind.HardBreak:
                        builder.Append(' ');
                        break;
                    case InlineKind.FootnoteRef:
                        break;
                    default:
                        AppendPlain(builder, inline.Children);
                        break;
                }
            }
        }
    }

    public sealed class FootnoteDefinition
    {
        public string Id;
        public string Label;
        public int Number;
        public List<Block> Blocks = new List<Block>();
    }

    public sealed class LinkReference
    {
        public string Destination;
        public string Title;
    }

    public sealed class HeadingInfo
    {
        public int Level;
        public string Text;
        public string Id;
        public int SourceLine;
        public Block Block;
    }

    public sealed class MarkdownDocument
    {
        public List<Block> Blocks = new List<Block>();
        public Dictionary<string, LinkReference> References = new Dictionary<string, LinkReference>(StringComparer.Ordinal);
        public Dictionary<string, FootnoteDefinition> Footnotes = new Dictionary<string, FootnoteDefinition>(StringComparer.Ordinal);
        public List<FootnoteDefinition> UsedFootnotes = new List<FootnoteDefinition>();
        public List<HeadingInfo> Headings = new List<HeadingInfo>();

        public string FirstHeadingText()
        {
            foreach (HeadingInfo heading in Headings)
                if (heading.Level == 1 && !String.IsNullOrWhiteSpace(heading.Text)) return heading.Text;
            return Headings.Count > 0 ? Headings[0].Text : null;
        }
    }
}

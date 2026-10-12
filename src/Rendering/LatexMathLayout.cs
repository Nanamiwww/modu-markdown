using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using CodexMarkdownReader.Markdown;
using CodexMarkdownReader.UI;
using MdBlock = CodexMarkdownReader.Markdown.Block;
using MdInline = CodexMarkdownReader.Markdown.Inline;

namespace CodexMarkdownReader.Rendering
{
    internal enum MathLevel { Display = 0, Text = 1, Script = 2, ScriptScript = 3 }

    /// <summary>TeX's current style (display, text, script, scriptscript), font alphabet, color and size.</summary>
    internal sealed class MathStyle
    {
        public MathLevel Level;
        public bool Cramped;
        public double BaseSize;
        public double Scale = 1;
        public MathVariant Variant = MathVariant.Default;
        public Brush Ink;

        public MathStyle(MathLevel level, double baseSize, Brush ink)
        {
            Level = level;
            BaseSize = baseSize;
            Ink = ink;
        }

        public MathStyle Copy() { return (MathStyle)MemberwiseClone(); }

        public double Size
        {
            get
            {
                double factor = Level == MathLevel.Script ? 0.71 : Level == MathLevel.ScriptScript ? 0.56 : 1.0;
                return Math.Max(5, Math.Min(400, BaseSize * Scale * factor));
            }
        }

        public bool IsDisplay { get { return Level == MathLevel.Display; } }
        public bool IsScript { get { return Level >= MathLevel.Script; } }

        public MathStyle WithLevel(MathLevel level, bool cramped)
        {
            MathStyle style = Copy();
            style.Level = level;
            style.Cramped = cramped;
            return style;
        }

        public MathStyle WithVariant(MathVariant variant)
        {
            MathStyle style = Copy();
            style.Variant = variant;
            return style;
        }

        public MathStyle WithInk(Brush ink)
        {
            MathStyle style = Copy();
            style.Ink = ink;
            return style;
        }

        public MathStyle AsCramped() { return WithLevel(Level, true); }
        public MathStyle ForSuperscript() { return WithLevel(Level <= MathLevel.Text ? MathLevel.Script : MathLevel.ScriptScript, Cramped); }
        public MathStyle ForSubscript() { return WithLevel(Level <= MathLevel.Text ? MathLevel.Script : MathLevel.ScriptScript, true); }

        public MathStyle ForNumerator()
        {
            MathLevel level = Level == MathLevel.Display ? MathLevel.Text : Level == MathLevel.Text ? MathLevel.Script : MathLevel.ScriptScript;
            return WithLevel(level, Cramped);
        }

        public MathStyle ForDenominator()
        {
            MathStyle style = ForNumerator();
            style.Cramped = true;
            return style;
        }
    }

    /// <summary>A command defined in a formula with \newcommand, \def or \DeclareMathOperator.</summary>
    internal sealed class MathMacro
    {
        public readonly int Arity;
        public readonly string OptionalDefault;
        public readonly string Body;
        /// <summary>
        /// \let\b\sqrt: the built-in command this name stands for. It is run directly, so that redefining \sqrt later
        /// does not change \b (TeX's \let copies the meaning at that moment).
        /// </summary>
        public readonly string Primitive;

        public MathMacro(int arity, string optionalDefault, string body)
        {
            Arity = arity;
            OptionalDefault = optionalDefault;
            Body = body ?? "";
        }

        private MathMacro(string primitive)
        {
            Body = "";
            Primitive = primitive;
        }

        public static MathMacro ForPrimitive(string name)
        {
            return new MathMacro(name);
        }
    }

    /// <summary>
    /// What the formulas of one document share: commands defined so far (in document order, as in MathJax) and the
    /// \tag of every \label, so that \eqref can refer to any formula.
    /// </summary>
    internal sealed class MathContext
    {
        public readonly Dictionary<string, MathMacro> Macros = new Dictionary<string, MathMacro>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal);

        [ThreadStatic]
        private static MathContext current;

        /// <summary>The context of the document being rendered on this thread, or null.</summary>
        public static MathContext Current { get { return current; } }

        public static IDisposable Use(MathContext context)
        {
            MathContext previous = current;
            current = context;
            return new Restore(previous);
        }

        private sealed class Restore : IDisposable
        {
            private readonly MathContext previous;
            private bool done;
            public Restore(MathContext previous) { this.previous = previous; }
            public void Dispose()
            {
                if (done) return;
                done = true;
                current = previous;
            }
        }

        /// <summary>
        /// A context for one document. When the document refers to formulas (\eqref, \ref), the tags of all its labels
        /// are collected up front, so that a reference may come before its formula.
        /// </summary>
        public static MathContext ForDocument(MarkdownDocument document)
        {
            MathContext context = new MathContext();
            if (document == null) return context;
            List<KeyValuePair<string, bool>> formulas = new List<KeyValuePair<string, bool>>();
            CollectFormulas(document.Blocks, formulas);
            foreach (FootnoteDefinition footnote in document.UsedFootnotes) CollectFormulas(footnote.Blocks, formulas);
            foreach (KeyValuePair<string, bool> formula in formulas)
            {
                if (formula.Key.IndexOf("\\eqref", StringComparison.Ordinal) < 0 && formula.Key.IndexOf("\\ref", StringComparison.Ordinal) < 0) continue;
                context.CollectLabels(formulas);
                break;
            }
            return context;
        }

        /// <summary>
        /// Finds the tag of every \label by typesetting the formulas once, in document order, exactly as they will be
        /// shown: comments are skipped and labels written by macros are found. Commands defined on the way are kept in
        /// a separate context, so that when the formulas are shown each command still exists only after its definition.
        /// </summary>
        private void CollectLabels(List<KeyValuePair<string, bool>> formulas)
        {
            MathContext scratch = new MathContext();
            foreach (KeyValuePair<string, bool> formula in formulas)
            {
                // A formula without commands defines nothing and labels nothing.
                if (formula.Key.IndexOf('\\') < 0) continue;
                LatexMathLayout layout = new LatexMathLayout(formula.Key, Brushes.Black, formula.Value, scratch);
                try
                {
                    layout.Build(16);
                }
                catch (Exception)
                {
                    // A formula that cannot be typeset shows no tag either.
                    continue;
                }
                foreach (KeyValuePair<string, string> label in layout.Labels)
                    if (!Labels.ContainsKey(label.Key)) Labels[label.Key] = label.Value;
            }
        }

        /// <summary>The formulas of the blocks in document order, with whether each is displayed (true) or inline.</summary>
        private static void CollectFormulas(List<MdBlock> blocks, List<KeyValuePair<string, bool>> formulas)
        {
            if (blocks == null) return;
            foreach (MdBlock block in blocks)
            {
                if (block.Kind == BlockKind.MathBlock && block.RawText != null) formulas.Add(new KeyValuePair<string, bool>(block.RawText, true));
                CollectInlines(block.Inlines, formulas);
                if (block.HeaderCells != null) foreach (List<MdInline> cell in block.HeaderCells) CollectInlines(cell, formulas);
                if (block.Rows != null) foreach (List<List<MdInline>> row in block.Rows) foreach (List<MdInline> cell in row) CollectInlines(cell, formulas);
                CollectFormulas(block.Children, formulas);
            }
        }

        private static void CollectInlines(List<MdInline> inlines, List<KeyValuePair<string, bool>> formulas)
        {
            if (inlines == null) return;
            foreach (MdInline inline in inlines)
            {
                if (inline.Kind == InlineKind.Math && inline.Text != null) formulas.Add(new KeyValuePair<string, bool>(inline.Text, false));
                CollectInlines(inline.Children, formulas);
            }
        }
    }

    /// <summary>
    /// The formula engine: a recursive-descent reader of LaTeX math that builds TeX-style boxes (see MathBoxes.cs) —
    /// atom classes and their spacing, the four styles, math alphabets, fractions, radicals, stretchy delimiters,
    /// matrices and alignment environments, equation tags, user macros, and chemistry/unit notation (\ce, \pu, siunitx).
    /// Anything it cannot typeset throws, and the caller shows the formula's source instead.
    /// </summary>
    internal sealed partial class LatexMathLayout
    {
        // The reader is recursive. Without limits a few kilobytes of nested braces overflow the stack, which .NET
        // cannot catch and which ends the whole process. Real formulas stay far below these.
        public const int MaxSourceLength = 4000;
        private const int MaxDepth = 64;
        private const int MaxAtoms = 8000;
        private const int MaxExpandedLength = 40000;
        private const int MaxExpansions = 2000;

        // Where a row stops (besides the end of the formula and a closing brace).
        private const int StopCell = 1;        // & \\ \end
        private const int StopRight = 2;       // \right \middle
        private const int StopBracket = 4;     // ] of an optional argument
        private const int StopDollar = 8;      // $ closing math inside \text
        private const int StopInfix = 16;      // \over \atop \choose
        private const int NoInfix = 32;        // the row was already checked for \over
        private const int TopLevel = 64;       // a stray } is shown instead of ending the formula

        /// <summary>Column alignment of aligned/align: right, left, right, left ...</summary>
        private const string AlignedColumns = "rlrlrlrlrlrlrlrlrlrlrlrlrlrlrlrl";

        internal static readonly Typeface MathFace = new Typeface(ReaderTypography.Math, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly FontFamily TextFamily = new FontFamily("Cambria, Cambria Math, Microsoft YaHei, Microsoft YaHei UI, SimSun");
        private static readonly Typeface TextFace = new Typeface(TextFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface TextBoldFace = new Typeface(TextFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Typeface TextItalicFace = new Typeface(TextFamily, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface SansFace = new Typeface(ReaderTypography.Body, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface MonoFace = new Typeface(ReaderTypography.Code, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private string source;
        private int position;
        private readonly Brush foreground;
        private readonly bool display;
        private readonly MathContext context;
        private int depth, atomCount, expansions;
        private string equationTag;
        private bool equationTagStarred;
        private string pendingTag;
        private bool pendingTagStarred;
        /// <summary>\label keys of the line being read, until its tag is known.</summary>
        private List<string> pendingLabels = new List<string>();
        private readonly List<KeyValuePair<string, string>> labels = new List<KeyValuePair<string, string>>();
        private AtomClass lastRowClass = AtomClass.Ord;

        public LatexMathLayout(string source, Brush foreground) : this(source, foreground, false, null) { }

        public LatexMathLayout(string source, Brush foreground, bool display) : this(source, foreground, display, null) { }

        public LatexMathLayout(string source, Brush foreground, bool display, MathContext context)
        {
            this.source = source ?? "";
            this.foreground = foreground ?? Brushes.Black;
            this.display = display;
            this.context = context ?? MathContext.Current ?? new MathContext();
        }

        /// <summary>After <see cref="Build"/>: each \label with the tag of its own line, or of the whole formula.</summary>
        public IList<KeyValuePair<string, string>> Labels { get { return labels; } }

        /// <summary>The tag of a displayed formula ("(1)", or the text of \tag*), shown at the right edge; null if none.</summary>
        public string EquationTag
        {
            get
            {
                if (equationTag == null) return null;
                return equationTagStarred ? equationTag : "(" + equationTag + ")";
            }
        }

        /// <summary>
        /// Typesets the formula. Throws <see cref="InvalidOperationException"/> when it is too long or nested too deeply
        /// and <see cref="NotSupportedException"/> for commands and environments it does not know; callers then show
        /// the source instead.
        /// </summary>
        public FrameworkElement Build(double fontSize)
        {
            if (source.Length > MaxSourceLength) throw new InvalidOperationException("公式过长");
            position = 0;
            depth = 0;
            atomCount = 0;
            expansions = 0;
            equationTag = null;
            pendingTag = null;
            pendingLabels = new List<string>();
            labels.Clear();
            MathStyle style = new MathStyle(display ? MathLevel.Display : MathLevel.Text, Math.Max(4, fontSize), foreground);
            MathBox body = ParseFormula(style);
            if (equationTag != null && !display)
            {
                // Inline formulas keep the tag right after the formula.
                MathRow row = new MathRow();
                row.Add(body);
                row.Add(new MathSpace(style.Size, style.Size));
                row.Add(TextRun(EquationTag, TextFace, style));
                equationTag = null;
                return row;
            }
            return body;
        }

        private void Enter()
        {
            if (++depth > MaxDepth) throw new InvalidOperationException("公式嵌套过深");
            if (++atomCount > MaxAtoms) throw new InvalidOperationException("公式过于复杂");
        }

        private void Exit()
        {
            depth--;
        }

        // ------------------------------------------------------------------ atoms and rows

        private sealed class Atom
        {
            public AtomClass Class;
            public MathBox Box;
            public MathStyle Style;
            public bool IsSpace;
            public bool Limits;
            public bool ForceLimits;
            /// <summary>An upright letter that may join the letter before it (\mathrm{mol} is one run).</summary>
            public bool Mergeable;

            public Atom(AtomClass cls, MathBox box, MathStyle style)
            {
                Class = cls;
                Box = box;
                Style = style;
            }
        }

        // TeX's spacing between atom classes (TeXbook chapter 18): 0 none, 1 thin, 2 medium, 3 thick;
        // negative values only in display and text style.
        private static readonly int[,] Spacing =
        {
            //          Ord Op Bin Rel Open Close Punct Inner
            /* Ord   */ { 0,  1, -2, -3,  0,  0,  0, -1 },
            /* Op    */ { 1,  1,  0, -3,  0,  0,  0, -1 },
            /* Bin   */ { -2, -2, 0,  0, -2,  0,  0, -2 },
            /* Rel   */ { -3, -3, 0,  0, -3,  0,  0, -3 },
            /* Open  */ { 0,  0,  0,  0,  0,  0,  0,  0 },
            /* Close */ { 0,  1, -2, -3,  0,  0,  0, -1 },
            /* Punct */ { -1, -1, 0, -1, -1, -1, -1, -1 },
            /* Inner */ { -1, 1, -2, -3, -1,  0, -1, -1 }
        };

        private MathBox BuildRow(List<Atom> atoms)
        {
            // A binary operator with nothing to its left, or next to another operator, relation, opening or punctuation,
            // is an ordinary symbol (unary minus); so is one before a relation, closing or punctuation (TeX rules 5 and 6).
            Atom previous = null;
            foreach (Atom atom in atoms)
            {
                if (atom.IsSpace) continue;
                if (atom.Class == AtomClass.Bin)
                {
                    if (previous == null || previous.Class == AtomClass.Bin || previous.Class == AtomClass.Op || previous.Class == AtomClass.Rel ||
                        previous.Class == AtomClass.Open || previous.Class == AtomClass.Punct)
                        atom.Class = AtomClass.Ord;
                }
                else if (atom.Class == AtomClass.Rel || atom.Class == AtomClass.Close || atom.Class == AtomClass.Punct)
                {
                    if (previous != null && previous.Class == AtomClass.Bin) previous.Class = AtomClass.Ord;
                }
                previous = atom;
            }
            if (previous != null && previous.Class == AtomClass.Bin) previous.Class = AtomClass.Ord;

            MathRow row = new MathRow();
            Atom left = null;
            int count = 0;
            foreach (Atom atom in atoms)
            {
                if (atom.IsSpace)
                {
                    row.Add(atom.Box);
                    continue;
                }
                if (left != null)
                {
                    int kind = Spacing[(int)left.Class, (int)atom.Class];
                    if (kind < 0) kind = atom.Style.IsScript ? 0 : -kind;
                    if (kind > 0)
                    {
                        double size = atom.Style.Size;
                        row.Add(new MathSpace((kind + 2) * size / 18, size));
                    }
                }
                row.Add(atom.Box);
                left = atom;
                count++;
            }
            lastRowClass = count == 1 && left != null ? left.Class : AtomClass.Ord;
            return row;
        }

        private MathBox ParseFormula(MathStyle style)
        {
            TableData table = ParseCells(style, null, true, false, TopLevel);
            MathBox result;
            if (table.Rows.Count == 1 && table.Rows[0].Count == 1)
            {
                if (table.Tags[0] != null)
                {
                    equationTag = table.Tags[0];
                    equationTagStarred = table.TagStars[0];
                }
                result = table.Rows[0][0];
            }
            else
            {
                // \\ and & at the top level of a displayed formula: lines (aligned at & as in the aligned environment).
                pendingTag = null;
                result = table.HasAmpersand
                    ? BuildTable("aligned", table, style, AlignedColumns, null, 1.0, 0.3, 0, 0.84, 0.36, true, false)
                    : BuildTable("gathered", table, style, "c", null, 1.0, 0.3, 0, 0.84, 0.36, false, false);
                if (pendingTag != null)
                {
                    equationTag = pendingTag;
                    equationTagStarred = pendingTagStarred;
                    pendingTag = null;
                }
            }
            // Labels of lines without a tag of their own refer to the formula's tag, if it has one.
            if (equationTag != null) foreach (string key in pendingLabels) RecordLabel(key, equationTag);
            pendingLabels = new List<string>();
            return result;
        }

        private void RecordLabel(string key, string tag)
        {
            string name = (key ?? "").Trim();
            if (name.Length > 0 && tag != null) labels.Add(new KeyValuePair<string, string>(name, tag));
        }

        private MathBox ParseRow(MathStyle style, int stops, bool leadingEmptyOrd)
        {
            Enter();
            try
            {
                if ((stops & NoInfix) == 0 && HasInfixFraction(stops)) return ParseInfixFraction(style, stops);
                List<Atom> atoms = new List<Atom>();
                // In the right-hand cells of align, "&= b" spaces the relation as if something stood before it.
                if (leadingEmptyOrd) atoms.Add(new Atom(AtomClass.Ord, new MathRow(), style));
                MathStyle current = style;
                while (true)
                {
                    SkipIgnorable();
                    if (position >= source.Length) break;
                    char c = source[position];
                    if (c == '}')
                    {
                        if ((stops & TopLevel) == 0) break;
                        position++;
                        atoms.Add(SymbolAtom("}", AtomClass.Close, current));
                        continue;
                    }
                    if (c == ']' && (stops & StopBracket) != 0) break;
                    if (c == '$' && (stops & StopDollar) != 0) break;
                    if (c == '&')
                    {
                        if ((stops & StopCell) != 0) break;
                        throw new NotSupportedException("& 只能用在矩阵和对齐环境中");
                    }
                    if (c == '\\')
                    {
                        string name = PeekCommand();
                        if (name == "\\" || name == "cr" || name == "newline")
                        {
                            if ((stops & StopCell) != 0) break;
                            // A line break where the formula cannot break: a space.
                            SkipCommand(name);
                            ReadBracketText();
                            atoms.Add(SpaceAtom(current.Size, current));
                            continue;
                        }
                        if (name == "end")
                        {
                            if ((stops & StopCell) != 0) break;
                            throw new NotSupportedException("多余的 \\end");
                        }
                        if (name == "right" || name == "middle")
                        {
                            if ((stops & StopRight) != 0) break;
                            throw new NotSupportedException("\\" + name + " 前面缺少 \\left");
                        }
                        if (IsInfix(name))
                        {
                            if ((stops & StopInfix) != 0) break;
                            throw new NotSupportedException("同一组里只能有一个 \\" + name);
                        }
                        if (TrySwitch(name, ref current)) continue;
                    }
                    if (c == '^' || c == '_' || c == '\'')
                    {
                        atoms.Add(AttachScripts(new Atom(AtomClass.Ord, new MathRow(), current), current));
                        continue;
                    }
                    Atom atom = ParseAtom(current, false);
                    if (atom == null) continue;
                    if (atom.Mergeable && atoms.Count > 0)
                    {
                        Atom before = atoms[atoms.Count - 1];
                        if (before.Mergeable && CanMerge(before, atom))
                        {
                            atoms.RemoveAt(atoms.Count - 1);
                            atom = Merge(before, atom);
                        }
                    }
                    atoms.Add(AttachScripts(atom, current));
                }
                return BuildRow(atoms);
            }
            finally
            {
                Exit();
            }
        }

        private static bool CanMerge(Atom first, Atom second)
        {
            MathGlyph a = first.Box as MathGlyph, b = second.Box as MathGlyph;
            return a != null && b != null && a.Variant == b.Variant && a.Em == b.Em && ReferenceEquals(a.Ink, b.Ink) && Equals(a.Face, b.Face);
        }

        private static Atom Merge(Atom first, Atom second)
        {
            MathGlyph a = (MathGlyph)first.Box, b = (MathGlyph)second.Box;
            MathGlyph joined = new MathGlyph(a.Text + b.Text, (a.Original ?? a.Text) + (b.Original ?? b.Text), a.Variant, a.Face, a.Em, a.Ink, false);
            Atom atom = new Atom(first.Class, joined, first.Style);
            atom.Mergeable = true;
            return atom;
        }

        // ------------------------------------------------------------------ scripts

        private Atom AttachScripts(Atom nucleus, MathStyle style)
        {
            MathBox superscript = null, subscript = null;
            int primes = 0;
            int limits = 0;   // 1 \limits, -1 \nolimits
            while (true)
            {
                SkipIgnorable();
                if (position >= source.Length) break;
                char c = source[position];
                if (c == '\'' && superscript == null)
                {
                    position++;
                    primes++;
                    continue;
                }
                if (c == '^')
                {
                    if (superscript != null) break;   // a second superscript starts a new, empty nucleus
                    position++;
                    superscript = ParseScriptArgument(style.ForSuperscript());
                    continue;
                }
                if (c == '_')
                {
                    if (subscript != null) break;
                    position++;
                    subscript = ParseScriptArgument(style.ForSubscript());
                    continue;
                }
                if (c == '\\')
                {
                    string name = PeekCommand();
                    if (name == "limits" || name == "nolimits" || name == "displaylimits")
                    {
                        SkipCommand(name);
                        limits = name == "limits" ? 1 : name == "nolimits" ? -1 : 0;
                        continue;
                    }
                }
                break;
            }
            if (primes > 0)
            {
                MathRow withPrime = new MathRow();
                withPrime.Add(nucleus.Box);
                withPrime.Add(Glyph(PrimeText(primes), null, MathVariant.Roman, MathFace, style, false));
                Atom primed = new Atom(nucleus.Class, withPrime, nucleus.Style);
                primed.Limits = nucleus.Limits;
                nucleus = primed;
            }
            if (superscript == null && subscript == null) return nucleus;
            bool stack = limits > 0 || (limits == 0 && (nucleus.ForceLimits || (nucleus.Limits && style.IsDisplay)));
            MathBox box = stack
                ? (MathBox)new MathUnderOver(nucleus.Box, superscript, subscript, style.Size, "limits")
                : new MathScripts(nucleus.Box, superscript, subscript, style.Size, style.IsDisplay, style.Cramped);
            return new Atom(nucleus.Class, box, nucleus.Style);
        }

        private static string PrimeText(int count)
        {
            if (count == 1) return "′";
            if (count == 2) return "″";
            if (count == 3) return "‴";
            if (count == 4) return "⁗";
            return new string('′', count);
        }

        /// <summary>One token, as TeX takes it: x^23 is x² followed by 3, \frac12 is ½.</summary>
        private MathBox ParseScriptArgument(MathStyle style)
        {
            Atom atom = ParseSingle(style);
            return atom == null ? new MathRow() : atom.Box;
        }

        private MathBox ParseArgument(MathStyle style)
        {
            ExpandMacroArgument();
            Atom atom = ParseSingle(style);
            return atom == null ? new MathRow() : atom.Box;
        }

        /// <summary>
        /// An argument that is a macro, as in \frac\half2: LaTeX hands the macro to \frac unexpanded, so its whole
        /// expansion is one argument. It is expanded here and put in braces. Superscripts are different: TeX expands
        /// first and takes the first token, so x^\half is left to <see cref="ParseScriptArgument"/>.
        /// </summary>
        private void ExpandMacroArgument()
        {
            SkipIgnorable();
            string name = PeekCommand();
            if (String.IsNullOrEmpty(name)) return;
            MathMacro macro;
            if (!context.Macros.TryGetValue(name, out macro) && !BuiltinMacros.TryGetValue(name, out macro)) return;
            if (macro.Primitive != null) return;
            int start = position;
            SkipCommand(name);
            Splice(start, "{" + ExpandMacro(macro) + "}");
        }

        /// <summary>The next token as an atom; commands that produce nothing (\label ...) are skipped.</summary>
        private Atom ParseSingle(MathStyle style)
        {
            while (true)
            {
                SkipIgnorable();
                if (position >= source.Length) return null;
                char c = source[position];
                if (c == '}' || c == '&' || c == '$' || c == ']') return null;
                if (c == '^' || c == '_') throw new NotSupportedException("上下标的写法有误");
                int before = position, expanded = expansions;
                Atom atom = ParseAtom(style, true);
                if (atom != null) return atom;
                if (position == before && expansions == expanded) throw new NotSupportedException("无法识别的写法");
            }
        }

        // ------------------------------------------------------------------ single atoms

        private Atom ParseAtom(MathStyle style, bool single)
        {
            Enter();
            try { return ParseAtomCore(style, single); }
            finally { Exit(); }
        }

        private Atom ParseAtomCore(MathStyle style, bool single)
        {
            SkipIgnorable();
            if (position >= source.Length) return null;
            char c = source[position];
            if (c == '{')
            {
                position++;
                MathBox group = ParseRow(style, 0, false);
                Expect('}');
                return new Atom(AtomClass.Ord, group, style);
            }
            if (c == '\\') return ParseCommand(style, single);
            if (IsLetter(c))
            {
                position++;
                return LetterAtom(c.ToString(), style);
            }
            if (c >= '0' && c <= '9')
            {
                int start = position++;
                if (!single)
                {
                    while (position < source.Length)
                    {
                        char d = source[position];
                        if (d >= '0' && d <= '9') { position++; continue; }
                        if ((d == '.' || d == ',') && position + 1 < source.Length && source[position + 1] >= '0' && source[position + 1] <= '9') { position++; continue; }
                        break;
                    }
                }
                return NumberAtom(source.Substring(start, position - start), style);
            }
            position++;
            switch (c)
            {
                case '+': return SymbolAtom("+", AtomClass.Bin, style);
                case '-': return SymbolAtom("−", AtomClass.Bin, style);
                case '*': return SymbolAtom("∗", AtomClass.Bin, style);
                case '=': return SymbolAtom("=", AtomClass.Rel, style);
                case '<': return SymbolAtom("<", AtomClass.Rel, style);
                case '>': return SymbolAtom(">", AtomClass.Rel, style);
                case ':': return SymbolAtom(":", AtomClass.Rel, style);
                case ',': return SymbolAtom(",", AtomClass.Punct, style);
                case ';': return SymbolAtom(";", AtomClass.Punct, style);
                case '!': return SymbolAtom("!", AtomClass.Close, style);
                case '?': return SymbolAtom("?", AtomClass.Close, style);
                case '(': return SymbolAtom("(", AtomClass.Open, style);
                case '[': return SymbolAtom("[", AtomClass.Open, style);
                case ')': return SymbolAtom(")", AtomClass.Close, style);
                case ']': return SymbolAtom("]", AtomClass.Close, style);
                case '|': return SymbolAtom("|", AtomClass.Ord, style);
                case '/': return SymbolAtom("/", AtomClass.Ord, style);
                case '.': return SymbolAtom(".", AtomClass.Ord, style);
                case '\'': return SymbolAtom("′", AtomClass.Ord, style);
                case '~': return SpaceAtom(style.Size / 3, style);
                case '$': throw new NotSupportedException("公式中有多余的 $");
                case '&': throw new NotSupportedException("& 只能用在矩阵和对齐环境中");
                case '^':
                case '_': throw new NotSupportedException("上下标的写法有误");
            }
            string text = c.ToString();
            if (Char.IsHighSurrogate(c) && position < source.Length && Char.IsLowSurrogate(source[position]))
            {
                text += source[position];
                position++;
            }
            return UnicodeAtom(text, style);
        }

        /// <summary>Characters typed directly: Greek letters behave like \alpha, operators get their class, CJK is text.</summary>
        private Atom UnicodeAtom(string text, MathStyle style)
        {
            AtomClass cls;
            if (LatexSymbols.CharacterClasses.TryGetValue(text, out cls))
            {
                if (text == "·") text = "⋅";
                return SymbolAtom(text, cls, style);
            }
            bool slanted;
            string mapped = LatexSymbols.MapCharacter(text, style.Variant, out slanted);
            if (mapped != text || IsGreek(text))
                return new Atom(AtomClass.Ord, Glyph(mapped, text, style.Variant, MathFace, style, slanted), style);
            if (text.Length == 1 && !Char.IsLetter(text[0])) return SymbolAtom(text, AtomClass.Ord, style);
            // Chinese and other letters: upright text, joined with neighbours.
            Atom atom = new Atom(AtomClass.Ord, Glyph(text, text, MathVariant.Roman, TextFace, style, false), style);
            atom.Mergeable = true;
            return atom;
        }

        private static bool IsGreek(string text)
        {
            return text.Length == 1 && ((text[0] >= '\u0391' && text[0] <= '\u03C9') || text[0] == '\u03D1' || text[0] == '\u03D5' || text[0] == '\u03D6' ||
                text[0] == '\u03F0' || text[0] == '\u03F1' || text[0] == '\u03F5');
        }

        private Atom LetterAtom(string letter, MathStyle style)
        {
            bool slanted;
            string mapped = LatexSymbols.MapCharacter(letter, style.Variant, out slanted);
            Atom atom = new Atom(AtomClass.Ord, Glyph(mapped, letter, style.Variant, MathFace, style, slanted), style);
            atom.Mergeable = !slanted && style.Variant != MathVariant.Default;
            return atom;
        }

        private Atom NumberAtom(string digits, MathStyle style)
        {
            StringBuilder mapped = new StringBuilder();
            foreach (char d in digits) mapped.Append(d >= '0' && d <= '9' ? LatexSymbols.MapCharacter(d.ToString(), style.Variant) : d.ToString());
            return new Atom(AtomClass.Ord, Glyph(mapped.ToString(), digits, style.Variant, MathFace, style, false), style);
        }

        private Atom SymbolAtom(string text, AtomClass cls, MathStyle style)
        {
            return new Atom(cls, Glyph(text, null, MathVariant.Roman, MathFace, style, false), style);
        }

        private Atom SpaceAtom(double width, MathStyle style)
        {
            Atom atom = new Atom(AtomClass.Ord, new MathSpace(width, style.Size), style);
            atom.IsSpace = true;
            return atom;
        }

        private MathGlyph Glyph(string text, string original, MathVariant variant, Typeface face, MathStyle style, bool slanted)
        {
            return new MathGlyph(text, original, variant, face, style.Size, style.Ink, slanted);
        }

        private MathGlyph TextRun(string text, Typeface face, MathStyle style)
        {
            return new MathGlyph(text, text, MathVariant.Roman, face, style.Size, style.Ink, face.Style == FontStyles.Italic);
        }

        // ------------------------------------------------------------------ infix fractions (\over, \atop, \choose)

        private static bool IsInfix(string name)
        {
            return name == "over" || name == "atop" || name == "choose" || name == "brace" || name == "brack" || name == "above";
        }

        /// <summary>Whether the rest of the current group contains \over (or \atop ...) at its own level.</summary>
        private bool HasInfixFraction(int stops)
        {
            int level = 0;
            for (int i = position; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '%')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    continue;
                }
                if (c == '{') { level++; continue; }
                if (c == '}')
                {
                    if (level == 0) return false;
                    level--;
                    continue;
                }
                if (level == 0)
                {
                    if (c == '&' && (stops & StopCell) != 0) return false;
                    if (c == ']' && (stops & StopBracket) != 0) return false;
                    if (c == '$' && (stops & StopDollar) != 0) return false;
                }
                if (c != '\\' || i + 1 >= source.Length) continue;
                char next = source[i + 1];
                if (!IsLetter(next))
                {
                    if (next == '\\' && level == 0 && (stops & StopCell) != 0) return false;
                    i++;
                    continue;
                }
                int end = i + 1;
                while (end < source.Length && IsLetter(source[end])) end++;
                string name = source.Substring(i + 1, end - i - 1);
                i = end - 1;
                if (name == "begin" || name == "left") { level++; continue; }
                if (name == "end" || name == "right")
                {
                    if (level == 0) return false;
                    level--;
                    continue;
                }
                if (level == 0 && IsInfix(name)) return true;
            }
            return false;
        }

        private MathBox ParseInfixFraction(MathStyle style, int stops)
        {
            MathBox numerator = ParseRow(style.ForNumerator(), stops | StopInfix | NoInfix, false);
            string name = PeekCommand();
            if (name == null || !IsInfix(name)) return numerator;
            SkipCommand(name);
            double thickness = MathMetrics.Rule(style.Size);
            if (name == "above")
            {
                double width;
                if (TryReadDimension(style.Size, out width)) thickness = Math.Max(0, width);
            }
            else if (name != "over") thickness = 0;
            MathBox denominator = ParseRow(style.ForDenominator(), (stops & ~StopInfix) | NoInfix, false);
            MathBox fraction = new MathFraction(numerator, denominator, style.Size, style.IsDisplay, thickness, style.Ink);
            if (name == "choose") fraction = Fence(fraction, "(", ")", style);
            else if (name == "brace") fraction = Fence(fraction, "{", "}", style);
            else if (name == "brack") fraction = Fence(fraction, "[", "]", style);
            MathRow row = new MathRow();
            row.Add(fraction);
            lastRowClass = AtomClass.Inner;
            return row;
        }

        private MathBox Fence(MathBox content, string left, string right, MathStyle style)
        {
            return new MathDelimited(left, new MathBox[] { content }, new string[0], right, style.Size, style.Ink, MathFace);
        }

        // ------------------------------------------------------------------ tables

        private sealed class TableData
        {
            public readonly List<List<MathBox>> Rows = new List<List<MathBox>>();
            public readonly List<string> Tags = new List<string>();
            public readonly List<bool> TagStars = new List<bool>();
            /// <summary>Number of \hline before each row; the last entry is after the last row.</summary>
            public readonly List<int> Lines = new List<int>();
            public bool HasAmpersand;
        }

        /// <summary>
        /// Reads cells separated by &amp; and rows separated by \\ until \end{environment} (or the end of the formula
        /// when environment is null, or a closing brace when braceEnds).
        /// </summary>
        private TableData ParseCells(MathStyle cellStyle, string environment, bool alignedPairs, bool braceEnds, int stops)
        {
            // A \tag of the enclosing line, read before this table, must survive the table's own lines.
            string outerTag = pendingTag;
            bool outerStarred = pendingTagStarred;
            // Labels of the enclosing line, and labels of lines here without a tag (they belong to the enclosing line).
            List<string> outerLabels = pendingLabels;
            List<string> looseLabels = new List<string>();
            List<string> rowLabels = new List<string>();
            TableData table = new TableData();
            List<MathBox> row = new List<MathBox>();
            string rowTag = null;
            bool rowStar = false;
            int pendingLines = 0;
            while (true)
            {
                if (row.Count == 0) pendingLines += SkipRules();
                pendingTag = null;
                pendingLabels = new List<string>();
                MathBox cell = ParseRow(cellStyle, StopCell | stops, alignedPairs && row.Count % 2 == 1);
                row.Add(cell);
                if (pendingTag != null)
                {
                    rowTag = pendingTag;
                    rowStar = pendingTagStarred;
                    pendingTag = null;
                }
                rowLabels.AddRange(pendingLabels);
                SkipIgnorable();
                bool endRow = false, endTable = false;
                if (position >= source.Length) endTable = true;
                else
                {
                    char c = source[position];
                    if (c == '&')
                    {
                        position++;
                        table.HasAmpersand = true;
                        continue;
                    }
                    if (c == '}')
                    {
                        if (!braceEnds) throw new NotSupportedException("花括号不配对");
                        endTable = true;
                    }
                    else if (c == '\\')
                    {
                        string name = PeekCommand();
                        if (name == "\\" || name == "cr" || name == "newline")
                        {
                            SkipCommand(name);
                            ReadBracketText();
                            endRow = true;
                        }
                        else if (name == "end")
                        {
                            if (environment == null) throw new NotSupportedException("多余的 \\end");
                            SkipCommand(name);
                            string closing = ReadTexGroupText().Trim();
                            if (closing != environment) throw new NotSupportedException("\\begin{" + environment + "} 与 \\end{" + closing + "} 不配对");
                            endTable = true;
                        }
                        else throw new NotSupportedException("无法识别的写法 \\" + name);
                    }
                    else throw new NotSupportedException("无法识别的写法 " + c);
                }
                if (!endRow && !endTable) continue;
                bool emptyRow = row.Count == 1 && row[0].IsEmpty && rowTag == null;
                if (!(endTable && emptyRow && table.Rows.Count > 0))
                {
                    // A final \\ before \end adds no row.
                    table.Rows.Add(row);
                    table.Tags.Add(rowTag);
                    table.TagStars.Add(rowStar);
                    table.Lines.Add(pendingLines);
                    pendingLines = 0;
                }
                if (rowTag != null) foreach (string key in rowLabels) RecordLabel(key, rowTag);
                else looseLabels.AddRange(rowLabels);
                rowLabels = new List<string>();
                row = new List<MathBox>();
                rowTag = null;
                rowStar = false;
                if (endTable)
                {
                    table.Lines.Add(pendingLines);
                    break;
                }
            }
            if (table.Rows.Count == 0)
            {
                table.Rows.Add(new List<MathBox>(new MathBox[] { new MathRow() }));
                table.Tags.Add(null);
                table.TagStars.Add(false);
                table.Lines.Insert(0, 0);
            }
            pendingTag = outerTag;
            pendingTagStarred = outerStarred;
            outerLabels.AddRange(looseLabels);
            pendingLabels = outerLabels;
            return table;
        }

        private int SkipRules()
        {
            int count = 0;
            while (true)
            {
                SkipIgnorable();
                string name = PeekCommand();
                if (name != "hline" && name != "hdashline") return count;
                SkipCommand(name);
                count++;
            }
        }

        private MathBox BuildTable(string kind, TableData table, MathStyle style, string align, bool[] columnLines, double columnGap, double rowGap,
            double outerPad, double strutAscent, double strutDescent, bool pairs, bool multline)
        {
            List<MathBox> tags = new List<MathBox>();
            int tagged = 0;
            for (int i = 0; i < table.Rows.Count; i++)
            {
                string tag = i < table.Tags.Count ? table.Tags[i] : null;
                if (tag != null) tagged++;
                tags.Add(null);
            }
            if (tagged == 1 && table.Rows.Count == 1)
            {
                // One line with a tag: the tag belongs to the whole formula (shown at the right edge).
                if (pendingTag == null)
                {
                    pendingTag = table.Tags[0];
                    pendingTagStarred = table.TagStars[0];
                }
            }
            else if (tagged > 0)
            {
                MathStyle tagStyle = style.WithLevel(MathLevel.Text, false);
                for (int i = 0; i < table.Rows.Count; i++)
                    if (table.Tags[i] != null) tags[i] = TextRun(table.TagStars[i] ? table.Tags[i] : "(" + table.Tags[i] + ")", TextFace, tagStyle);
            }
            bool tagColumn = tagged > 0 && !(tagged == 1 && table.Rows.Count == 1);
            return new MathTable(kind, table.Rows, tagColumn ? tags : null, table.Lines, align, columnLines, style.Size, columnGap, rowGap, outerPad,
                strutAscent, strutDescent, pairs, multline, style.Ink);
        }

        // ------------------------------------------------------------------ reading the source

        private static bool IsLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        /// <summary>Skips white space and % comments (math mode ignores spaces).</summary>
        private void SkipIgnorable()
        {
            while (position < source.Length)
            {
                char c = source[position];
                if (c == '%')
                {
                    while (position < source.Length && source[position] != '\n') position++;
                    continue;
                }
                if (Char.IsWhiteSpace(c))
                {
                    position++;
                    continue;
                }
                break;
            }
        }

        /// <summary>The name of the command at the current position, without consuming it; null if there is none.</summary>
        private string PeekCommand()
        {
            if (position >= source.Length || source[position] != '\\') return null;
            int p = position + 1;
            if (p >= source.Length) return "";
            if (!IsLetter(source[p])) return source[p].ToString();
            int start = p;
            while (p < source.Length && IsLetter(source[p])) p++;
            return source.Substring(start, p - start);
        }

        private void SkipCommand(string name)
        {
            position = Math.Min(source.Length, position + 1 + (name ?? "").Length);
        }

        private void Expect(char c)
        {
            SkipIgnorable();
            if (position < source.Length && source[position] == c) position++;
        }

        private bool TryConsume(char c)
        {
            SkipIgnorable();
            if (position < source.Length && source[position] == c)
            {
                position++;
                return true;
            }
            return false;
        }

        private bool PeekChar(char c)
        {
            SkipIgnorable();
            return position < source.Length && source[position] == c;
        }

        /// <summary>
        /// The raw text of a {group} (inner braces and escapes kept) or of the next token. Used for arguments in which % is
        /// text (\ce, \pu, the units of \si, \SI and the like, URLs) and for single tokens; every other argument, numbers
        /// included, is read with <see cref="ReadTexGroupText"/>, which skips comments.
        /// </summary>
        private string ReadGroupText()
        {
            SkipIgnorable();
            if (position >= source.Length) return "";
            char c = source[position];
            if (c == '{')
            {
                int start = position + 1, level = 0;
                while (position < source.Length)
                {
                    char ch = source[position];
                    if (ch == '\\')
                    {
                        position += 2;
                        continue;
                    }
                    if (ch == '{') level++;
                    else if (ch == '}')
                    {
                        level--;
                        if (level == 0)
                        {
                            position++;
                            return source.Substring(start, position - 1 - start);
                        }
                    }
                    position++;
                }
                position = source.Length;
                return start <= source.Length ? source.Substring(start) : "";
            }
            if (c == '\\')
            {
                string name = PeekCommand();
                SkipCommand(name);
                return "\\" + name;
            }
            position++;
            if (Char.IsHighSurrogate(c) && position < source.Length && Char.IsLowSurrogate(source[position]))
            {
                position++;
                return source.Substring(position - 2, 2);
            }
            return c.ToString();
        }

        /// <summary>
        /// Like <see cref="ReadGroupText"/>, but % starts a comment, as when TeX reads an argument: the comment, its line
        /// break and the spaces after it are dropped, and a } inside it does not close the group. Used for every argument
        /// TeX reads as tokens: definitions and macro arguments, which are read again as TeX, and names, labels, colors,
        /// lengths and the numbers of \num, \SI and the like. \ce, \pu and units keep % as a percent sign
        /// (\ce{98% H2SO4}, \SI{50}{%}), and URLs keep it as written.
        /// </summary>
        private string ReadTexGroupText()
        {
            SkipIgnorable();
            if (position >= source.Length || source[position] != '{') return ReadGroupText();
            position++;
            StringBuilder text = new StringBuilder();
            int level = 1;
            while (position < source.Length)
            {
                char ch = source[position];
                if (ch == '\\')
                {
                    // An escaped character, \% \{ \} included, is kept with its backslash.
                    text.Append(source, position, Math.Min(2, source.Length - position));
                    position += 2;
                    continue;
                }
                if (ch == '%')
                {
                    SkipComment(text);
                    continue;
                }
                if (ch == '{') level++;
                else if (ch == '}' && --level == 0)
                {
                    position++;
                    return text.ToString();
                }
                text.Append(ch);
                position++;
            }
            position = source.Length;
            return text.ToString();
        }

        /// <summary>Where reading continues after the comment starting at <paramref name="percent"/>: past its line break and the spaces after it.</summary>
        private int CommentEnd(int percent)
        {
            int p = percent;
            while (p < source.Length && source[p] != '\n') p++;
            if (p < source.Length) p++;
            while (p < source.Length && (source[p] == ' ' || source[p] == '\t')) p++;
            return p;
        }

        /// <summary>
        /// Skips the comment at the current position while text is being collected. TeX has already ended a command name
        /// at the %, so when letters follow on the next line (\alpha% ⏎ x) a space keeps them apart: the text becomes
        /// \alpha x, the command \alpha and the letter x, never \alphax. TeX ignores a space after a command name, so the
        /// space changes nothing else; plain letters still join (a% ⏎ b is ab).
        /// </summary>
        private void SkipComment(StringBuilder text)
        {
            position = CommentEnd(position);
            if (position < source.Length && IsLetter(source[position]) && EndsWithControlWord(text)) text.Append(' ');
        }

        /// <summary>
        /// The text of an optional [argument] (brackets inside braces do not end it), or null if there is none. As when TeX
        /// reads it, % starts a comment: the comment, its line break and the spaces after it are dropped, and a ] or a brace
        /// inside it neither ends the argument nor counts (\newcommand{\f}[1][a%]⏎+b]{#1} has the default a+b).
        /// </summary>
        private string ReadBracketText()
        {
            SkipIgnorable();
            if (position >= source.Length || source[position] != '[') return null;
            position++;
            StringBuilder text = new StringBuilder();
            int level = 0;
            while (position < source.Length)
            {
                char ch = source[position];
                if (ch == '\\')
                {
                    // An escaped character, \% \] \{ included, is kept with its backslash.
                    text.Append(source, position, Math.Min(2, source.Length - position));
                    position += 2;
                    continue;
                }
                if (ch == '%')
                {
                    SkipComment(text);
                    continue;
                }
                if (ch == '{') level++;
                else if (ch == '}') level--;
                else if (ch == ']' && level <= 0)
                {
                    position++;
                    return text.ToString();
                }
                text.Append(ch);
                position++;
            }
            position = source.Length;
            return text.ToString();
        }

        /// <summary>Replaces source[start, position) with the expansion and continues reading at its start.</summary>
        private void Splice(int start, string expansion)
        {
            if (++expansions > MaxExpansions) throw new InvalidOperationException("公式中的命令展开次数过多");
            string next = source.Substring(0, start) + expansion + source.Substring(Math.Min(position, source.Length));
            if (next.Length > MaxExpandedLength) throw new InvalidOperationException("公式展开后过长");
            source = next;
            position = start;
        }

        // ------------------------------------------------------------------ dimensions and colors

        private bool TryReadDimension(double em, out double pixels)
        {
            SkipIgnorable();
            int start = position;
            int p = position;
            if (p < source.Length && (source[p] == '-' || source[p] == '+')) p++;
            while (p < source.Length && (Char.IsDigit(source[p]) || source[p] == '.')) p++;
            while (p < source.Length && source[p] == ' ') p++;
            int unitStart = p;
            while (p < source.Length && IsLetter(source[p]) && p - unitStart < 2) p++;
            if (TryParseDimension(source.Substring(start, p - start), em, out pixels))
            {
                position = p;
                return true;
            }
            pixels = 0;
            return false;
        }

        /// <summary>Converts "1.5em", "3pt", "-2mu", "0.5ex" ... to pixels at the given font size.</summary>
        internal static bool TryParseDimension(string text, double em, out double pixels)
        {
            pixels = 0;
            Match match = Regex.Match(text ?? "", @"^\s*([+-]?(?:\d+\.?\d*|\.\d+))\s*(em|ex|pt|pc|in|cm|mm|bp|dd|sp|mu|px)\s*$", RegexOptions.CultureInvariant);
            if (!match.Success) return false;
            double value;
            if (!Double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            double inEm;
            switch (match.Groups[2].Value)
            {
                case "em": inEm = 1; break;
                case "ex": inEm = MathMetrics.XHeight; break;
                case "pt": inEm = 0.1; break;
                case "pc": inEm = 1.2; break;
                case "in": inEm = 7.227; break;
                case "cm": inEm = 2.845; break;
                case "mm": inEm = 0.2845; break;
                case "bp": inEm = 0.1004; break;
                case "dd": inEm = 0.107; break;
                case "sp": inEm = 0.1 / 65536; break;
                case "mu": inEm = 1.0 / 18; break;
                default: pixels = Math.Max(-2000, Math.Min(2000, value)); return true;   // px
            }
            pixels = Math.Max(-2000, Math.Min(2000, value * inEm * em));
            return true;
        }

        private static readonly Dictionary<string, Color> NamedColors = CreateColors();

        private static Dictionary<string, Color> CreateColors()
        {
            Dictionary<string, Color> map = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            string[] pairs =
            {
                "black 000000", "white FFFFFF", "red D32F2F", "green 2E7D32", "blue 1565C0", "cyan 00838F", "magenta C2185B", "yellow F9A825",
                "orange EF6C00", "purple 6A1B9A", "violet 7B1FA2", "brown 6D4C41", "pink D81B60", "lime 9E9D24", "olive 827717", "teal 00796B",
                "gray 757575", "grey 757575", "darkgray 424242", "lightgray BDBDBD", "darkgreen 1B5E20", "darkblue 0D47A1", "darkred B71C1C",
                "navy 1A237E", "maroon 880E4F", "gold C79100", "skyblue 0288D1", "indigo 283593", "royalblue 1E5BC6", "forestgreen 2E7D32",
                "crimson C62828", "goldenrod B8860B", "salmon E57373", "orchid AB47BC", "coral FF7043"
            };
            foreach (string pair in pairs)
            {
                string[] parts = pair.Split(' ');
                map[parts[0]] = ParseHex(parts[1]);
            }
            return map;
        }

        private static Color ParseHex(string hex)
        {
            int value = Int32.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }

        /// <summary>A color from a name, #RRGGBB, or an xcolor model ([rgb]{1,0,0}, [RGB]{255,0,0}, [HTML]{FF0000}, [gray]{0.5}).</summary>
        private Brush ReadColor(string model, string spec)
        {
            Color color;
            string value = (spec ?? "").Trim();
            if (!TryParseColor(model, value, out color)) throw new NotSupportedException("无法识别的颜色 " + value);
            // Black on a dark background (or white on a light one) would be invisible: use the normal ink instead.
            SolidColorBrush ink = foreground as SolidColorBrush;
            if (ink != null)
            {
                double background = Luminance(ink.Color) > 0.5 ? 0 : 1;
                if (Math.Abs(Luminance(color) - background) < 0.12) return foreground;
            }
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static double Luminance(Color c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        private static bool TryParseColor(string model, string value, out Color color)
        {
            color = Colors.Black;
            string m = (model ?? "").Trim();
            try
            {
                if (m.Length == 0)
                {
                    if (NamedColors.TryGetValue(value, out color)) return true;
                    string hex = value.StartsWith("#", StringComparison.Ordinal) ? value.Substring(1) : value;
                    if (Regex.IsMatch(hex, "^[0-9A-Fa-f]{6}$")) { color = ParseHex(hex); return true; }
                    if (Regex.IsMatch(hex, "^[0-9A-Fa-f]{3}$"))
                    {
                        color = ParseHex(new string(new char[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] }));
                        return true;
                    }
                    return false;
                }
                string[] parts = value.Split(',');
                if (m == "HTML" || m == "html")
                {
                    if (!Regex.IsMatch(value, "^[0-9A-Fa-f]{6}$")) return false;
                    color = ParseHex(value);
                    return true;
                }
                if (m == "rgb" && parts.Length == 3)
                {
                    color = Color.FromRgb(Unit(parts[0]), Unit(parts[1]), Unit(parts[2]));
                    return true;
                }
                if (m == "RGB" && parts.Length == 3)
                {
                    color = Color.FromRgb(Byte(parts[0]), Byte(parts[1]), Byte(parts[2]));
                    return true;
                }
                if (m == "gray" && parts.Length == 1)
                {
                    byte g = Unit(parts[0]);
                    color = Color.FromRgb(g, g, g);
                    return true;
                }
            }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
            return false;
        }

        private static byte Unit(string text)
        {
            double value = Double.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            return (byte)Math.Round(Math.Max(0, Math.Min(1, value)) * 255);
        }

        private static byte Byte(string text)
        {
            double value = Double.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            return (byte)Math.Round(Math.Max(0, Math.Min(255, value)));
        }

        // ------------------------------------------------------------------ self-test helpers

        /// <summary>Self-test: the structure of a typeset formula as text (spaces as ‹mu› when requested).</summary>
        internal static string Describe(FrameworkElement element, bool spaces)
        {
            MathBox box = element as MathBox;
            if (box == null) return element == null ? "" : "?" + element.GetType().Name;
            box.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            return box.Describe(spaces);
        }

        /// <summary>Self-test: every glyph of a formula as text:alphabet (x:Default, d:Roman ...).</summary>
        internal static List<string> Glyphs(FrameworkElement element)
        {
            List<string> result = new List<string>();
            CollectGlyphs(element, result);
            return result;
        }

        private static void CollectGlyphs(DependencyObject element, List<string> result)
        {
            MathGlyph glyph = element as MathGlyph;
            if (glyph != null)
            {
                result.Add((glyph.Original ?? glyph.Text) + ":" + glyph.Variant.ToString() + (glyph.Face.Weight == FontWeights.Bold ? ":bold" : ""));
                return;
            }
            System.Windows.Controls.Panel panel = element as System.Windows.Controls.Panel;
            if (panel == null) return;
            foreach (UIElement child in panel.Children) CollectGlyphs(child, result);
        }

        /// <summary>Self-test: the first box of the given type in a formula (depth first).</summary>
        internal static T Find<T>(DependencyObject element) where T : MathBox
        {
            T match = element as T;
            if (match != null) return match;
            System.Windows.Controls.Panel panel = element as System.Windows.Controls.Panel;
            if (panel == null) return null;
            foreach (UIElement child in panel.Children)
            {
                T found = Find<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}

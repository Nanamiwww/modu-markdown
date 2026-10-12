using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace CodexMarkdownReader.Rendering
{
    internal sealed partial class LatexMathLayout
    {
        private static readonly Dictionary<string, string> Accents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "hat", "hat" }, { "widehat", "widehat" }, { "check", "check" }, { "widecheck", "widecheck" }, { "tilde", "tilde" },
            { "widetilde", "widetilde" }, { "acute", "acute" }, { "grave", "grave" }, { "dot", "dot" }, { "ddot", "ddot" }, { "dddot", "dddot" },
            { "ddddot", "ddddot" }, { "breve", "breve" }, { "bar", "bar" }, { "vec", "vec" }, { "Vec", "vec" }, { "mathring", "mathring" },
            { "overline", "overline" }, { "underline", "underline" }, { "underbar", "underline" }, { "overrightarrow", "overrightarrow" },
            { "overleftarrow", "overleftarrow" }, { "overleftrightarrow", "overleftrightarrow" }, { "underrightarrow", "underrightarrow" },
            { "underleftarrow", "underleftarrow" }, { "underleftrightarrow", "underleftrightarrow" }, { "utilde", "utilde" },
            { "overbrace", "overbrace" }, { "underbrace", "underbrace" }
        };

        private static readonly Dictionary<string, MathVariant> Variants = new Dictionary<string, MathVariant>(StringComparer.Ordinal)
        {
            { "mathrm", MathVariant.Roman }, { "mathup", MathVariant.Roman }, { "mathnormal", MathVariant.Default }, { "mathit", MathVariant.Italic },
            { "mathbf", MathVariant.Bold }, { "bold", MathVariant.Bold }, { "mathbfit", MathVariant.BoldItalic }, { "boldsymbol", MathVariant.BoldItalic },
            { "bm", MathVariant.BoldItalic }, { "pmb", MathVariant.BoldItalic }, { "mathsf", MathVariant.Sans }, { "mathsfit", MathVariant.SansItalic },
            { "mathbfsf", MathVariant.SansBold }, { "mathsfbf", MathVariant.SansBold }, { "mathtt", MathVariant.Mono }, { "mathcal", MathVariant.Script },
            { "mathscr", MathVariant.Script }, { "mathbfcal", MathVariant.BoldScript }, { "mathbfscr", MathVariant.BoldScript },
            { "mathfrak", MathVariant.Fraktur }, { "mathbffrak", MathVariant.BoldFraktur }, { "mathbb", MathVariant.Blackboard }, { "Bbb", MathVariant.Blackboard }
        };

        private static readonly Dictionary<string, string> Arrows = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "xrightarrow", "right" }, { "xleftarrow", "left" }, { "xleftrightarrow", "leftright" }, { "xRightarrow", "Right" },
            { "xLeftarrow", "Left" }, { "xLeftrightarrow", "LeftRight" }, { "xrightleftharpoons", "eq" }, { "xleftrightharpoons", "eq" },
            { "xlongequal", "equal" }, { "xmapsto", "mapsto" }, { "xhookrightarrow", "right" }, { "xhookleftarrow", "left" },
            { "xtwoheadrightarrow", "right" }, { "xtwoheadleftarrow", "left" }, { "xrightharpoonup", "right" }, { "xrightharpoondown", "right" },
            { "xleftharpoonup", "left" }, { "xleftharpoondown", "left" }, { "xrightleftarrows", "arrows" }, { "xtofrom", "arrows" },
            { "xrightequilibrium", "eqright" }, { "xleftequilibrium", "eqleft" }
        };

        private static readonly Dictionary<string, string> Negations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "=", "≠" }, { "in", "∉" }, { "<", "≮" }, { ">", "≯" }, { "le", "≰" }, { "leq", "≰" }, { "ge", "≱" }, { "geq", "≱" },
            { "equiv", "≢" }, { "sim", "≁" }, { "approx", "≉" }, { "simeq", "≄" }, { "cong", "≇" }, { "subset", "⊄" }, { "supset", "⊅" },
            { "subseteq", "⊈" }, { "supseteq", "⊉" }, { "mid", "∤" }, { "parallel", "∦" }, { "ni", "∌" }, { "prec", "⊀" }, { "succ", "⊁" },
            { "preceq", "⋠" }, { "succeq", "⋡" }, { "vdash", "⊬" }, { "models", "⊭" }, { "leftarrow", "↚" }, { "rightarrow", "↛" }, { "to", "↛" },
            { "Leftarrow", "⇍" }, { "Rightarrow", "⇏" }, { "leftrightarrow", "↮" }, { "Leftrightarrow", "⇎" }, { "sqsubseteq", "⋢" },
            { "sqsupseteq", "⋣" }, { "asymp", "≭" }, { "exists", "∄" }
        };

        private static readonly Dictionary<string, double> SizeCommands = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            { "tiny", 0.5 }, { "scriptsize", 0.7 }, { "footnotesize", 0.8 }, { "small", 0.9 }, { "normalsize", 1.0 }, { "large", 1.2 },
            { "Large", 1.44 }, { "LARGE", 1.728 }, { "huge", 2.074 }, { "Huge", 2.488 }
        };

        private static readonly Dictionary<string, MathMacro> BuiltinMacros = CreateBuiltins();

        private static Dictionary<string, MathMacro> CreateBuiltins()
        {
            Dictionary<string, MathMacro> map = new Dictionary<string, MathMacro>(StringComparer.Ordinal);
            map["iff"] = new MathMacro(0, null, "\\;\\Longleftrightarrow\\;");
            map["implies"] = new MathMacro(0, null, "\\;\\Longrightarrow\\;");
            map["impliedby"] = new MathMacro(0, null, "\\;\\Longleftarrow\\;");
            map["pmod"] = new MathMacro(1, null, "\\quad(\\operatorname{mod}\\,#1)");
            map["pod"] = new MathMacro(1, null, "\\quad(#1)");
            map["mod"] = new MathMacro(1, null, "\\quad\\operatorname{mod}\\,\\,#1");
            map["bmod"] = new MathMacro(0, null, "\\mathbin{\\operatorname{mod}}");
            map["lt"] = new MathMacro(0, null, "<");
            map["gt"] = new MathMacro(0, null, ">");
            map["And"] = new MathMacro(0, null, "\\mathbin{\\&}");
            map["abs"] = new MathMacro(1, null, "\\left|#1\\right|");
            map["norm"] = new MathMacro(1, null, "\\left\\|#1\\right\\|");
            map["bra"] = new MathMacro(1, null, "\\left\\langle #1\\right|");
            map["ket"] = new MathMacro(1, null, "\\left|#1\\right\\rangle");
            map["braket"] = new MathMacro(1, null, "\\left\\langle #1\\right\\rangle");
            map["expval"] = new MathMacro(1, null, "\\left\\langle #1\\right\\rangle");
            map["mel"] = new MathMacro(3, null, "\\left\\langle #1\\middle|#2\\middle|#3\\right\\rangle");
            map["vb"] = new MathMacro(1, null, "\\mathbf{#1}");
            map["va"] = new MathMacro(1, null, "\\vec{#1}");
            map["vu"] = new MathMacro(1, null, "\\hat{\\mathbf{#1}}");
            map["grad"] = new MathMacro(0, null, "\\nabla");
            map["curl"] = new MathMacro(0, null, "\\nabla\\times");
            map["laplacian"] = new MathMacro(0, null, "\\nabla^{2}");
            map["order"] = new MathMacro(1, null, "\\mathcal{O}\\left(#1\\right)");
            map["dd"] = new MathMacro(0, null, "\\mathrm{d}");
            map["TeX"] = new MathMacro(0, null, "\\text{TeX}");
            map["LaTeX"] = new MathMacro(0, null, "\\text{LaTeX}");
            map["textdegree"] = new MathMacro(0, null, "{°}");
            map["degreeCelsius"] = new MathMacro(0, null, "{°\\mathrm{C}}");
            // gensymb and friends, common in lab notes outside \si: 25\celsius, 5\micro m, 1.5\AA.
            map["degree"] = new MathMacro(0, null, "{°}");
            map["celsius"] = new MathMacro(0, null, "{°\\mathrm{C}}");
            map["ohm"] = new MathMacro(0, null, "{\\Omega}");
            map["micro"] = new MathMacro(0, null, "{\\mathrm{μ}}");
            map["AA"] = new MathMacro(0, null, "{\\mathrm{Å}}");
            map["angstrom"] = new MathMacro(0, null, "{\\mathrm{Å}}");
            map["perthousand"] = new MathMacro(0, null, "{‰}");
            map["permil"] = new MathMacro(0, null, "{‰}");
            map["operatornamewithlimits"] = new MathMacro(1, null, "\\operatorname*{#1}");
            return map;
        }

        // ------------------------------------------------------------------ commands

        private Atom ParseCommand(MathStyle style, bool single)
        {
            int start = position;
            string name = PeekCommand();
            if (String.IsNullOrEmpty(name))
            {
                position = Math.Min(source.Length, position + 1);
                return SymbolAtom("\\", AtomClass.Ord, style);
            }
            SkipCommand(name);
            return Command(name, start, style, single);
        }

        /// <summary>One command (already read). Returns null when it produced nothing or was replaced by its expansion.</summary>
        private Atom Command(string name, int start, MathStyle style, bool single)
        {
            MathMacro macro;
            if (context.Macros.TryGetValue(name, out macro) || BuiltinMacros.TryGetValue(name, out macro))
            {
                if (macro.Primitive != null) return PrimitiveCommand(macro.Primitive, start, style, single);
                Splice(start, ExpandMacro(macro));
                return null;
            }
            return PrimitiveCommand(name, start, style, single);
        }

        /// <summary>A built-in command, whatever macros of the same name say.</summary>
        private Atom PrimitiveCommand(string name, int start, MathStyle style, bool single)
        {
            double em = style.Size;
            switch (name)
            {
                case ",":
                case "thinspace": return SpaceAtom(3 * em / 18, style);
                case ":":
                case ">":
                case "medspace": return SpaceAtom(4 * em / 18, style);
                case ";":
                case "thickspace": return SpaceAtom(5 * em / 18, style);
                case "!":
                case "negthinspace": return SpaceAtom(-3 * em / 18, style);
                case "negmedspace": return SpaceAtom(-4 * em / 18, style);
                case "negthickspace": return SpaceAtom(-5 * em / 18, style);
                case " ":
                case "space":
                case "nobreakspace": return SpaceAtom(em / 3, style);
                case "quad": return SpaceAtom(em, style);
                case "qquad": return SpaceAtom(2 * em, style);
                case "enspace":
                case "enskip": return SpaceAtom(em / 2, style);
                case "hspace":
                case "mspace":
                case "kern":
                case "mkern":
                case "hskip":
                case "mskip": return ExplicitSpace(name, style);
                case "{": return SymbolAtom("{", AtomClass.Open, style);
                case "}": return SymbolAtom("}", AtomClass.Close, style);
                case "|": return SymbolAtom("‖", AtomClass.Ord, style);
                case "%":
                case "&":
                case "#":
                case "$":
                case "_": return SymbolAtom(name, AtomClass.Ord, style);
                case "\\":
                case "cr":
                case "newline": return SpaceAtom(em, style);
                case "frac":
                case "dfrac":
                case "tfrac":
                case "cfrac": return Fraction(name, style);
                case "binom":
                case "dbinom":
                case "tbinom": return Binomial(name, style);
                case "genfrac": return GeneralFraction(style);
                case "sqrt": return Radical(style);
                case "left": return LeftRight(style);
                case "begin": return Environment(style);
                case "substack": return Substack(style);
                case "text":
                case "textrm":
                case "textnormal":
                case "textup":
                case "textmd":
                case "textsc":
                case "textbf":
                case "textit":
                case "textsl":
                case "emph":
                case "textsf":
                case "texttt":
                case "mbox":
                case "hbox": return TextCommand(name, style);
                case "operatorname": return OperatorName(style);
                case "mathop":
                    {
                        Atom op = new Atom(AtomClass.Op, ParseArgument(style), style);
                        op.Limits = true;
                        return op;
                    }
                case "mathbin": return new Atom(AtomClass.Bin, ParseArgument(style), style);
                case "mathrel": return new Atom(AtomClass.Rel, ParseArgument(style), style);
                case "mathord": return new Atom(AtomClass.Ord, ParseArgument(style), style);
                case "mathopen": return new Atom(AtomClass.Open, ParseArgument(style), style);
                case "mathclose": return new Atom(AtomClass.Close, ParseArgument(style), style);
                case "mathpunct": return new Atom(AtomClass.Punct, ParseArgument(style), style);
                case "mathinner": return new Atom(AtomClass.Inner, ParseArgument(style), style);
                case "overset":
                case "underset":
                case "stackrel": return Stacked(name, style);
                case "boxed": return new Atom(AtomClass.Ord, new MathEnclosure(ParseArgument(style), "box", em, style.Ink, null, 0), style);
                case "fbox":
                case "framebox":
                    ReadBracketText();
                    ReadBracketText();
                    return new Atom(AtomClass.Ord, new MathEnclosure(TextArgument(style, TextFace), "box", em, style.Ink, null, 0), style);
                case "colorbox":
                    {
                        string model = ReadBracketText();
                        Brush fill = FillColor(model, ReadTexGroupText());
                        return new Atom(AtomClass.Ord, new MathEnclosure(TextArgument(style, TextFace), "colorbox", em, style.Ink, Lighten(fill), 0), style);
                    }
                case "fcolorbox":
                    {
                        string model = ReadBracketText();
                        Brush frame = ReadColor(model, ReadTexGroupText());
                        Brush fill = FillColor(model, ReadTexGroupText());
                        return new Atom(AtomClass.Ord, new MathEnclosure(TextArgument(style, TextFace), "fcolorbox", em, frame, Lighten(fill), 0), style);
                    }
                case "bbox":
                    {
                        string options = ReadBracketText();
                        Brush fill = null;
                        if (options != null)
                        {
                            Color color;
                            string first = options.Split(',')[0].Trim();
                            if (TryParseColor(null, first, out color)) fill = Lighten(FillColor(null, first));
                        }
                        MathBox content = ParseArgument(style);
                        return new Atom(AtomClass.Ord, new MathEnclosure(content, fill != null ? "colorbox" : "box", em, style.Ink, fill, 0), style);
                    }
                case "cancel":
                case "bcancel":
                case "xcancel": return new Atom(AtomClass.Ord, new MathEnclosure(ParseArgument(style), name, em, style.Ink, null, 0), style);
                case "cancelto":
                    {
                        MathBox value = ParseArgument(style.ForSuperscript());
                        MathBox crossed = new MathEnclosure(ParseArgument(style), "cancelto", em, style.Ink, null, 0);
                        return new Atom(AtomClass.Ord, new MathScripts(crossed, value, null, em, style.IsDisplay, style.Cramped), style);
                    }
                case "sout": return new Atom(AtomClass.Ord, new MathEnclosure(TextArgument(style, TextFace), "sout", em, style.Ink, null, 0), style);
                case "phantom":
                case "hphantom":
                case "vphantom": return new Atom(AtomClass.Ord, new MathEnclosure(ParseArgument(style), name, em, style.Ink, null, 0), style);
                case "smash":
                    {
                        string which = ReadBracketText();
                        string kind = which == "t" ? "smasht" : which == "b" ? "smashb" : "smash";
                        return new Atom(AtomClass.Ord, new MathEnclosure(ParseArgument(style), kind, em, style.Ink, null, 0), style);
                    }
                case "llap":
                case "mathllap": return new Atom(AtomClass.Ord, new MathEnclosure(ParseArgument(style), "llap", em, style.Ink, null, 0), style);
                case "rlap":
                case "mathrlap": return new Atom(AtomClass.Ord, new MathEnclosure(ParseArgument(style), "rlap", em, style.Ink, null, 0), style);
                case "clap":
                case "mathclap": return new Atom(AtomClass.Ord, new MathEnclosure(ParseArgument(style), "clap", em, style.Ink, null, 0), style);
                case "raisebox":
                    {
                        string amount = ReadTexGroupText();
                        double raise;
                        if (!TryParseDimension(amount, em, out raise)) throw new NotSupportedException("无法识别的长度 " + amount);
                        return new Atom(AtomClass.Ord, new MathEnclosure(TextArgument(style, TextFace), "raise", em, style.Ink, null, raise), style);
                    }
                case "mathstrut": return new Atom(AtomClass.Ord, new MathEnclosure(Glyph("(", null, MathVariant.Roman, MathFace, style, false), "vphantom", em, style.Ink, null, 0), style);
                case "strut": return new Atom(AtomClass.Ord, new MathRule(0, 0.84 * em, 0.36 * em, null), style);
                case "rule": return RuleCommand(style);
                case "textcolor":
                    {
                        string model = ReadBracketText();
                        Brush ink = ReadColor(model, ReadTexGroupText());
                        return new Atom(AtomClass.Ord, ParseArgument(style.WithInk(ink)), style);
                    }
                case "href":
                case "class":
                case "cssId":
                case "style":
                    ReadGroupText();
                    return new Atom(AtomClass.Ord, ParseArgument(style), style);
                case "url": return new Atom(AtomClass.Ord, TextRun(ReadGroupText(), MonoFace, style), style);
                case "not": return Not(style);
                case "unicode": return UnicodeCommand(style);
                case "tag":
                    {
                        bool starred = TryConsume('*');
                        pendingTag = CleanTag(ReadTexGroupText());
                        pendingTagStarred = starred;
                        return null;
                    }
                case "label":
                    pendingLabels.Add(ReadTexGroupText());
                    return null;
                case "require":
                    ReadGroupText();
                    return null;
                case "eqref":
                case "ref":
                    {
                        string key = ReadTexGroupText().Trim();
                        string tag;
                        string text = context.Labels.TryGetValue(key, out tag) ? tag : "??";
                        return new Atom(AtomClass.Ord, TextRun(name == "eqref" ? "(" + text + ")" : text, TextFace, style), style);
                    }
                case "newcommand":
                case "renewcommand":
                case "providecommand":
                case "DeclareRobustCommand":
                    Define(name);
                    return null;
                case "def":
                    DefineDef();
                    return null;
                case "let":
                    DefineLet();
                    return null;
                case "DeclareMathOperator":
                    DeclareOperator();
                    return null;
                case "dv":
                case "pdv":
                    Derivative(name, start);
                    return null;
                case "ce":
                    {
                        string formula = ReadGroupText();
                        Splice(start, "{" + ScienceNotation.Chemistry(formula) + "}");
                        return null;
                    }
                case "pu":
                    {
                        string quantity = ReadGroupText();
                        Splice(start, "{" + ScienceNotation.PhysicalUnit(quantity) + "}");
                        return null;
                    }
                // siunitx: numbers are read as TeX reads them, so % starts a comment there (\num{123% ⏎ } is 123);
                // units keep % as a percent sign, like \ce and \pu (\SI{50}{%}).
                case "num":
                case "tablenum":
                    {
                        ReadBracketText();
                        string number = ReadTexGroupText();
                        Splice(start, "{" + ScienceNotation.Number(number) + "}");
                        return null;
                    }
                case "si":
                case "unit":
                    {
                        ReadBracketText();
                        string units = ReadGroupText();
                        Splice(start, "{" + ScienceNotation.Units(units) + "}");
                        return null;
                    }
                case "SI":
                case "qty":
                    {
                        ReadBracketText();
                        string number = ReadTexGroupText();
                        string before = ReadBracketText();
                        string units = ReadGroupText();
                        Splice(start, "{" + ScienceNotation.Quantity(number, units, before) + "}");
                        return null;
                    }
                case "ang":
                    {
                        ReadBracketText();
                        string angle = ReadTexGroupText();
                        Splice(start, "{" + ScienceNotation.Angle(angle) + "}");
                        return null;
                    }
                case "numrange":
                    {
                        ReadBracketText();
                        string from = ReadTexGroupText();
                        string to = ReadTexGroupText();
                        Splice(start, "{" + ScienceNotation.NumberRange(from, to) + "}");
                        return null;
                    }
                case "SIrange":
                case "qtyrange":
                    {
                        ReadBracketText();
                        string from = ReadTexGroupText();
                        string to = ReadTexGroupText();
                        string units = ReadGroupText();
                        Splice(start, "{" + ScienceNotation.QuantityRange(from, to, units) + "}");
                        return null;
                    }
                case "numlist":
                    {
                        ReadBracketText();
                        string list = ReadTexGroupText();
                        Splice(start, "{" + ScienceNotation.NumberList(list, null) + "}");
                        return null;
                    }
                case "SIlist":
                case "qtylist":
                    {
                        ReadBracketText();
                        string list = ReadTexGroupText();
                        string units = ReadGroupText();
                        Splice(start, "{" + ScienceNotation.NumberList(list, units) + "}");
                        return null;
                    }
                case "relax":
                case "nobreak":
                case "allowbreak":
                case "protect":
                case "nonumber":
                case "notag":
                case "limits":
                case "nolimits":
                case "displaylimits":
                case "hline":
                case "hdashline":
                    return null;
                case "displaybreak":
                    ReadBracketText();
                    return null;
            }

            string accent;
            if (Accents.TryGetValue(name, out accent)) return AccentCommand(accent, style);
            MathVariant variant;
            if (Variants.TryGetValue(name, out variant)) return new Atom(AtomClass.Ord, ParseArgument(style.WithVariant(variant)), style);
            string arrow;
            if (Arrows.TryGetValue(name, out arrow)) return ArrowCommand(arrow, style);
            double factor;
            AtomClass bigClass;
            if (IsBig(name, out factor, out bigClass)) return BigDelimiter(factor, bigClass, style);
            MathSymbol symbol;
            if (LatexSymbols.Commands.TryGetValue(name, out symbol)) return SymbolCommand(symbol, style);
            string function;
            if (LatexSymbols.Functions.TryGetValue(name, out function)) return Operator(function, LatexSymbols.LimitFunctions.Contains(name), style);
            MathStyle switched = style;
            if (TrySwitchName(name, ref switched)) return null;
            throw new NotSupportedException("暂不支持的命令 \\" + name);
        }

        /// <summary>Style, font, size and color switches: they change the rest of the current group.</summary>
        private bool TrySwitch(string name, ref MathStyle current)
        {
            if (!IsSwitch(name)) return false;
            SkipCommand(name);
            TrySwitchName(name, ref current);
            return true;
        }

        private static bool IsSwitch(string name)
        {
            switch (name)
            {
                case "displaystyle":
                case "textstyle":
                case "scriptstyle":
                case "scriptscriptstyle":
                case "rm":
                case "it":
                case "bf":
                case "sf":
                case "tt":
                case "cal":
                case "mit":
                case "frak":
                case "color":
                    return true;
            }
            return SizeCommands.ContainsKey(name);
        }

        /// <summary>Applies a switch whose name has already been read.</summary>
        private bool TrySwitchName(string name, ref MathStyle current)
        {
            switch (name)
            {
                case "displaystyle": current = current.WithLevel(MathLevel.Display, false); return true;
                case "textstyle": current = current.WithLevel(MathLevel.Text, false); return true;
                case "scriptstyle": current = current.WithLevel(MathLevel.Script, false); return true;
                case "scriptscriptstyle": current = current.WithLevel(MathLevel.ScriptScript, false); return true;
                case "rm": current = current.WithVariant(MathVariant.Roman); return true;
                case "it": current = current.WithVariant(MathVariant.Italic); return true;
                case "bf": current = current.WithVariant(MathVariant.Bold); return true;
                case "sf": current = current.WithVariant(MathVariant.Sans); return true;
                case "tt": current = current.WithVariant(MathVariant.Mono); return true;
                case "cal": current = current.WithVariant(MathVariant.Script); return true;
                case "frak": current = current.WithVariant(MathVariant.Fraktur); return true;
                case "mit": current = current.WithVariant(MathVariant.Default); return true;
                case "color":
                    {
                        string model = ReadBracketText();
                        current = current.WithInk(ReadColor(model, ReadTexGroupText()));
                        return true;
                    }
            }
            double scale;
            if (SizeCommands.TryGetValue(name, out scale))
            {
                current = current.Copy();
                current.Scale = scale;
                return true;
            }
            return false;
        }

        private static string CleanTag(string text)
        {
            return (text ?? "").Replace("$", "").Trim();
        }

        /// <summary>
        /// Bright versions of the named colors for backgrounds: the text versions are darkened so that colored letters stay
        /// readable, but \colorbox{yellow} should still look yellow.
        /// </summary>
        private static readonly Dictionary<string, Color> HighlightColors = CreateHighlightColors();

        private static Dictionary<string, Color> CreateHighlightColors()
        {
            Dictionary<string, Color> map = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            string[] pairs =
            {
                "yellow FFEB3B", "lime CDDC39", "gold FFC107", "orange FF9800", "cyan 26C6DA", "green 66BB6A", "blue 42A5F5", "red EF5350",
                "pink F06292", "magenta EC407A", "purple AB47BC", "violet AB47BC", "teal 26A69A", "gray BDBDBD", "grey BDBDBD",
                "lightgray E0E0E0", "skyblue 4FC3F7", "orchid CE93D8", "salmon FF8A80", "coral FF8A65"
            };
            foreach (string pair in pairs)
            {
                string[] parts = pair.Split(' ');
                map[parts[0]] = ParseHex(parts[1]);
            }
            return map;
        }

        private Brush FillColor(string model, string spec)
        {
            string value = (spec ?? "").Trim();
            Color color;
            if (String.IsNullOrEmpty((model ?? "").Trim()) && HighlightColors.TryGetValue(value, out color))
            {
                SolidColorBrush brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            return ReadColor(model, value);
        }

        private static Brush Lighten(Brush brush)
        {
            SolidColorBrush solid = brush as SolidColorBrush;
            if (solid == null) return brush;
            // Background colors are softened so the formula stays readable in both themes.
            Color c = solid.Color;
            SolidColorBrush soft = new SolidColorBrush(Color.FromArgb(0x55, c.R, c.G, c.B));
            soft.Freeze();
            return soft;
        }

        // ------------------------------------------------------------------ macros

        private string ExpandMacro(MathMacro macro)
        {
            string[] arguments = new string[Math.Max(0, Math.Min(9, macro.Arity))];
            for (int i = 0; i < arguments.Length; i++)
            {
                if (i == 0 && macro.OptionalDefault != null)
                {
                    string optional = ReadBracketText();
                    arguments[i] = optional ?? macro.OptionalDefault;
                }
                else arguments[i] = ReadTexGroupText();
            }
            StringBuilder body = new StringBuilder();
            string text = macro.Body;
            bool afterArgument = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length && !IsLetter(text[i + 1]))
                {
                    // \# \\ \{ ...: an escaped character, never a parameter (\text{\#1} keeps its #1).
                    body.Append(c).Append(text[i + 1]);
                    afterArgument = false;
                    i++;
                    continue;
                }
                if (c == '#' && i + 1 < text.Length)
                {
                    char next = text[i + 1];
                    if (next >= '1' && next <= '9')
                    {
                        int index = next - '1';
                        if (index < arguments.Length)
                        {
                            string argument = arguments[index];
                            // TeX substitutes tokens: \alpha followed by x stays two tokens, so the text gets a space
                            // wherever a command name would otherwise run into a letter.
                            if (argument.Length > 0 && IsLetter(argument[0]) && EndsWithControlWord(body)) body.Append(' ');
                            body.Append(argument);
                        }
                        afterArgument = true;
                        i++;
                        continue;
                    }
                    if (next == '#')
                    {
                        body.Append('#');
                        afterArgument = false;
                        i++;
                        continue;
                    }
                }
                if (afterArgument && IsLetter(c) && EndsWithControlWord(body)) body.Append(' ');
                afterArgument = false;
                body.Append(c);
            }
            // \R x must not become \Rx.
            if (body.Length > 0 && IsLetter(body[body.Length - 1]) && position < source.Length && IsLetter(source[position])) body.Append(' ');
            return body.ToString();
        }

        /// <summary>True when the text ends with a command name such as \alpha (and not with a line break \\ before letters).</summary>
        private static bool EndsWithControlWord(StringBuilder text)
        {
            int end = text.Length, k = end;
            while (k > 0 && IsLetter(text[k - 1])) k--;
            if (k == end || k == 0 || text[k - 1] != '\\') return false;
            int slashes = 0;
            while (k - 1 - slashes >= 0 && text[k - 1 - slashes] == '\\') slashes++;
            return slashes % 2 == 1;
        }

        private void Define(string command)
        {
            TryConsume('*');
            string target = ReadTexGroupText().Trim();
            if (target.Length < 2 || target[0] != '\\') throw new NotSupportedException("\\" + command + " 的写法有误");
            string name = target.Substring(1);
            int arity = 0;
            string optional = null;
            string count = ReadBracketText();
            if (count != null)
            {
                if (!Int32.TryParse(count.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out arity) || arity < 0 || arity > 9)
                    throw new NotSupportedException("\\" + command + " 的参数个数有误");
                optional = ReadBracketText();
            }
            string body = ReadTexGroupText();
            if (command == "providecommand" && (context.Macros.ContainsKey(name) || BuiltinMacros.ContainsKey(name) || LatexSymbols.Commands.ContainsKey(name))) return;
            context.Macros[name] = new MathMacro(arity, optional, body);
        }

        private void DefineDef()
        {
            SkipIgnorable();
            string name = PeekCommand();
            if (String.IsNullOrEmpty(name)) throw new NotSupportedException("\\def 的写法有误");
            SkipCommand(name);
            int arity = 0;
            while (true)
            {
                SkipIgnorable();
                if (position + 1 < source.Length && source[position] == '#' && source[position + 1] >= '1' && source[position + 1] <= '9')
                {
                    arity = Math.Max(arity, source[position + 1] - '0');
                    position += 2;
                    continue;
                }
                break;
            }
            string body = ReadTexGroupText();
            context.Macros[name] = new MathMacro(arity, null, body);
        }

        /// <summary>
        /// \let\b\a copies what \a means now: a macro's definition, or the built-in command itself. Redefining \a later
        /// leaves \b unchanged, so the common \let\oldsqrt\sqrt \renewcommand{\sqrt}... works and does not loop.
        /// </summary>
        private void DefineLet()
        {
            SkipIgnorable();
            string name = PeekCommand();
            if (String.IsNullOrEmpty(name)) throw new NotSupportedException("\\let 的写法有误");
            SkipCommand(name);
            TryConsume('=');
            string target = ReadTexGroupText();
            MathMacro meaning;
            if (target.Length > 1 && target[0] == '\\')
            {
                string targetName = target.Substring(1);
                MathMacro existing;
                // Definitions are replaced, never changed in place, so sharing the object keeps the old meaning.
                if (context.Macros.TryGetValue(targetName, out existing) || BuiltinMacros.TryGetValue(targetName, out existing)) meaning = existing;
                else meaning = MathMacro.ForPrimitive(targetName);
            }
            else meaning = new MathMacro(0, null, target);
            context.Macros[name] = meaning;
        }

        private void DeclareOperator()
        {
            bool limits = TryConsume('*');
            string target = ReadTexGroupText().Trim();
            string text = ReadTexGroupText();
            if (target.Length < 2 || target[0] != '\\') throw new NotSupportedException("\\DeclareMathOperator 的写法有误");
            context.Macros[target.Substring(1)] = new MathMacro(0, null, (limits ? "\\operatorname*{" : "\\operatorname{") + text + "}");
        }

        /// <summary>\dv and \pdv of the physics package: \dv{x}, \dv{f}{x}, \dv[n]{f}{x}, \pdv{f}{x}{y}.</summary>
        private void Derivative(string name, int start)
        {
            string d = name == "pdv" ? "\\partial " : "\\mathrm{d}";
            string order = ReadBracketText();
            string first = ReadTexGroupText();
            string second = PeekChar('{') ? ReadTexGroupText() : null;
            string third = second != null && name == "pdv" && PeekChar('{') ? ReadTexGroupText() : null;
            string power = String.IsNullOrEmpty(order) ? "" : "^{" + order + "}";
            string tex;
            if (second == null) tex = "\\frac{" + d + power + "}{" + d + "{" + first + "}" + power + "}";
            else if (third != null) tex = "\\frac{" + d + "^{2}{" + first + "}}{" + d + "{" + second + "}\\," + d + "{" + third + "}}";
            else tex = "\\frac{" + d + power + "{" + first + "}}{" + d + "{" + second + "}" + power + "}";
            Splice(start, "{" + tex + "}");
        }

        // ------------------------------------------------------------------ structures

        private Atom Fraction(string name, MathStyle style)
        {
            MathStyle outer = style;
            if (name == "dfrac" || name == "cfrac") outer = style.WithLevel(MathLevel.Display, style.Cramped);
            else if (name == "tfrac") outer = style.WithLevel(MathLevel.Text, style.Cramped);
            if (name == "cfrac") ReadBracketText();
            MathStyle numeratorStyle = name == "cfrac" ? outer.WithLevel(MathLevel.Display, false) : outer.ForNumerator();
            MathStyle denominatorStyle = name == "cfrac" ? outer.WithLevel(MathLevel.Display, true) : outer.ForDenominator();
            MathBox numerator = ParseArgument(numeratorStyle);
            MathBox denominator = ParseArgument(denominatorStyle);
            MathFraction fraction = new MathFraction(numerator, denominator, outer.Size, outer.IsDisplay, MathMetrics.Rule(outer.Size), outer.Ink);
            return new Atom(AtomClass.Inner, fraction, style);
        }

        private Atom Binomial(string name, MathStyle style)
        {
            MathStyle outer = name == "dbinom" ? style.WithLevel(MathLevel.Display, style.Cramped)
                : name == "tbinom" ? style.WithLevel(MathLevel.Text, style.Cramped) : style;
            MathBox top = ParseArgument(outer.ForNumerator());
            MathBox bottom = ParseArgument(outer.ForDenominator());
            MathBox stack = new MathFraction(top, bottom, outer.Size, outer.IsDisplay, 0, outer.Ink);
            return new Atom(AtomClass.Inner, Fence(stack, "(", ")", outer), style);
        }

        /// <summary>\genfrac{left}{right}{thickness}{style}{numerator}{denominator}.</summary>
        private Atom GeneralFraction(MathStyle style)
        {
            string left = DelimiterFromText(ReadTexGroupText());
            string right = DelimiterFromText(ReadTexGroupText());
            string thickness = ReadTexGroupText().Trim();
            string level = ReadTexGroupText().Trim();
            MathStyle outer = style;
            if (level == "0") outer = style.WithLevel(MathLevel.Display, style.Cramped);
            else if (level == "1") outer = style.WithLevel(MathLevel.Text, style.Cramped);
            else if (level == "2") outer = style.WithLevel(MathLevel.Script, style.Cramped);
            else if (level == "3") outer = style.WithLevel(MathLevel.ScriptScript, style.Cramped);
            double rule = MathMetrics.Rule(outer.Size);
            if (thickness.Length > 0 && !TryParseDimension(thickness, outer.Size, out rule)) throw new NotSupportedException("无法识别的长度 " + thickness);
            MathBox numerator = ParseArgument(outer.ForNumerator());
            MathBox denominator = ParseArgument(outer.ForDenominator());
            MathBox fraction = new MathFraction(numerator, denominator, outer.Size, outer.IsDisplay, Math.Max(0, rule), outer.Ink);
            if (left != "." || right != ".") fraction = Fence(fraction, left, right, outer);
            return new Atom(AtomClass.Inner, fraction, style);
        }

        private static string DelimiterFromText(string text)
        {
            string key = (text ?? "").Trim();
            if (key.Length == 0) return ".";
            string mapped;
            if (LatexSymbols.Delimiters.TryGetValue(key, out mapped)) return mapped;
            if (key.StartsWith("\\", StringComparison.Ordinal) && LatexSymbols.Delimiters.TryGetValue(key.Substring(1), out mapped)) return mapped;
            throw new NotSupportedException(key + " 不能用作伸缩括号");
        }

        private Atom Radical(MathStyle style)
        {
            MathBox index = null;
            if (PeekChar('['))
            {
                position++;
                index = ParseRow(style.WithLevel(MathLevel.ScriptScript, false), StopBracket, false);
                Expect(']');
            }
            MathBox body = ParseArgument(style.AsCramped());
            return new Atom(AtomClass.Ord, new MathRadical(body, index, style.Size, style.IsDisplay, style.Ink), style);
        }

        private Atom LeftRight(MathStyle style)
        {
            string left = ReadDelimiter();
            List<MathBox> segments = new List<MathBox>();
            List<string> middles = new List<string>();
            string right = ".";
            while (true)
            {
                segments.Add(ParseRow(style, StopRight, false));
                SkipIgnorable();
                string name = PeekCommand();
                if (name == "middle")
                {
                    SkipCommand(name);
                    middles.Add(ReadDelimiter());
                    continue;
                }
                if (name == "right")
                {
                    SkipCommand(name);
                    right = ReadDelimiter();
                }
                // Otherwise \right is missing: close at the end of the group.
                break;
            }
            return new Atom(AtomClass.Inner, new MathDelimited(left, segments, middles, right, style.Size, style.Ink, MathFace), style);
        }

        private string ReadDelimiter()
        {
            SkipIgnorable();
            if (position >= source.Length) return ".";
            string found;
            if (source[position] == '\\')
            {
                string name = PeekCommand();
                SkipCommand(name);
                if (LatexSymbols.Delimiters.TryGetValue("\\" + name, out found)) return found;
                if (LatexSymbols.Delimiters.TryGetValue(name, out found)) return found;
                throw new NotSupportedException("\\" + name + " 不能用作伸缩括号");
            }
            char c = source[position++];
            string text = c.ToString();
            if (Char.IsHighSurrogate(c) && position < source.Length)
            {
                text += source[position];
                position++;
            }
            if (LatexSymbols.Delimiters.TryGetValue(text, out found)) return found;
            throw new NotSupportedException(text + " 不能用作伸缩括号");
        }

        private static bool IsBig(string name, out double factor, out AtomClass cls)
        {
            factor = 0;
            cls = AtomClass.Ord;
            string[] cores = { "Bigg", "bigg", "Big", "big" };
            double[] sizes = { 3.0, 2.4, 1.8, 1.2 };
            for (int i = 0; i < cores.Length; i++)
            {
                if (!name.StartsWith(cores[i], StringComparison.Ordinal)) continue;
                string suffix = name.Substring(cores[i].Length);
                if (suffix.Length == 0) cls = AtomClass.Ord;
                else if (suffix == "l") cls = AtomClass.Open;
                else if (suffix == "r") cls = AtomClass.Close;
                else if (suffix == "m") cls = AtomClass.Rel;
                else return false;
                factor = sizes[i];
                return true;
            }
            return false;
        }

        private Atom BigDelimiter(double factor, AtomClass cls, MathStyle style)
        {
            MathDelimiter delimiter = new MathDelimiter(ReadDelimiter(), style.Size, style.Ink, MathFace);
            delimiter.TargetHeight = factor * style.Size;
            // Cambria Math's ( is already about as tall as TeX's \big(: take the font's larger versions anyway.
            delimiter.Enlarged = true;
            return new Atom(cls, delimiter, style);
        }

        private Atom AccentCommand(string kind, MathStyle style)
        {
            MathBox nucleus = ParseArgument(style.AsCramped());
            Atom atom = new Atom(AtomClass.Ord, new MathAccent(nucleus, kind, style.Size, style.Ink), style);
            if (kind == "overbrace" || kind == "underbrace")
            {
                // Labels go above \overbrace and below \underbrace: \overbrace{a+b}^{n}.
                atom.Class = AtomClass.Op;
                atom.ForceLimits = true;
            }
            return atom;
        }

        private Atom Stacked(string name, MathStyle style)
        {
            MathBox label = ParseArgument(name == "underset" ? style.ForSubscript() : style.ForSuperscript());
            ExpandMacroArgument();
            bool group = PeekChar('{');
            Atom nucleus = ParseSingle(style);
            MathBox body = nucleus == null ? new MathRow() : nucleus.Box;
            AtomClass cls = name == "stackrel" ? AtomClass.Rel : nucleus == null ? AtomClass.Ord : group ? lastRowClass : nucleus.Class;
            MathBox box = name == "underset" ? new MathUnderOver(body, null, label, style.Size, "underset") : new MathUnderOver(body, label, null, style.Size, "overset");
            return new Atom(cls, box, style);
        }

        private Atom ArrowCommand(string kind, MathStyle style)
        {
            MathBox below = null;
            if (PeekChar('['))
            {
                position++;
                below = ParseRow(style.ForSubscript(), StopBracket, false);
                Expect(']');
            }
            MathBox above = ParseArgument(style.ForSuperscript());
            return new Atom(AtomClass.Rel, new MathArrow(kind, above, below, style.Size, style.Ink), style);
        }

        private Atom Not(MathStyle style)
        {
            SkipIgnorable();
            string command = PeekCommand();
            string key = command ?? (position < source.Length ? source[position].ToString() : "");
            string negated;
            if (Negations.TryGetValue(key, out negated))
            {
                if (command != null) SkipCommand(command);
                else position++;
                return SymbolAtom(negated, key == "exists" ? AtomClass.Ord : AtomClass.Rel, style);
            }
            Atom atom = ParseSingle(style);
            if (atom == null) return SymbolAtom("/", AtomClass.Ord, style);
            return new Atom(atom.Class, new MathEnclosure(atom.Box, "not", style.Size, style.Ink, null, 0), style);
        }

        private Atom UnicodeCommand(MathStyle style)
        {
            string text = ReadTexGroupText().Trim();
            int code;
            int marker = text.IndexOfAny(new char[] { 'x', 'X' });
            bool parsed = marker >= 0 && marker <= 1
                ? Int32.TryParse(text.Substring(marker + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)
                : Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
            if (!parsed || code <= 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF)) throw new NotSupportedException("无法识别的字符编码 " + text);
            return UnicodeAtom(Char.ConvertFromUtf32(code), style);
        }

        private Atom ExplicitSpace(string name, MathStyle style)
        {
            double width;
            if (name == "hspace" || name == "mspace")
            {
                TryConsume('*');
                string text = ReadTexGroupText();
                if (!TryParseDimension(text, style.Size, out width)) throw new NotSupportedException("无法识别的长度 " + text);
                return SpaceAtom(width, style);
            }
            if (PeekChar('{'))
            {
                string text = ReadTexGroupText();
                if (!TryParseDimension(text, style.Size, out width)) throw new NotSupportedException("无法识别的长度 " + text);
            }
            else if (!TryReadDimension(style.Size, out width)) throw new NotSupportedException("\\" + name + " 后面缺少长度");
            if (name == "hskip" || name == "mskip") SkipGlue();
            return SpaceAtom(width, style);
        }

        /// <summary>Skips the stretch and shrink of glue: "plus 1fill minus 2pt".</summary>
        private void SkipGlue()
        {
            while (true)
            {
                SkipIgnorable();
                string word = null;
                if (String.CompareOrdinal(source, position, "plus", 0, 4) == 0) word = "plus";
                else if (String.CompareOrdinal(source, position, "minus", 0, 5) == 0) word = "minus";
                if (word == null) return;
                position += word.Length;
                SkipIgnorable();
                if (position < source.Length && (source[position] == '-' || source[position] == '+')) position++;
                while (position < source.Length && (Char.IsDigit(source[position]) || source[position] == '.')) position++;
                SkipIgnorable();
                while (position < source.Length && IsLetter(source[position])) position++;
            }
        }

        private Atom RuleCommand(MathStyle style)
        {
            double raise = 0, width, height;
            string optional = ReadBracketText();
            if (optional != null && !TryParseDimension(optional, style.Size, out raise)) throw new NotSupportedException("无法识别的长度 " + optional);
            string w = ReadTexGroupText(), h = ReadTexGroupText();
            if (!TryParseDimension(w, style.Size, out width) || !TryParseDimension(h, style.Size, out height))
                throw new NotSupportedException("\\rule 的长度有误");
            return new Atom(AtomClass.Ord, new MathRule(width, height + raise, -raise, style.Ink), style);
        }

        private Atom SymbolCommand(MathSymbol symbol, MathStyle style)
        {
            if (symbol.Large)
            {
                // Big operators are larger in display style and centered on the math axis.
                MathStyle big = style.Copy();
                big.Scale = style.Scale * (style.IsDisplay ? (symbol.Limits ? 1.4 : 1.75) : (symbol.Limits ? 1.05 : 1.2));
                MathGlyph glyph = Glyph(symbol.Text, symbol.Text, MathVariant.Roman, MathFace, big, !symbol.Limits);
                Atom op = new Atom(AtomClass.Op, new MathCentered(glyph, style.Size), style);
                op.Limits = symbol.Limits;
                return op;
            }
            if (symbol.Letter)
            {
                bool slanted;
                string mapped = LatexSymbols.MapCharacter(symbol.Text, style.Variant, out slanted);
                return new Atom(AtomClass.Ord, Glyph(mapped, symbol.Text, style.Variant, MathFace, style, slanted), style);
            }
            return SymbolAtom(symbol.Text, symbol.Class, style);
        }

        private Atom Operator(string text, bool limits, MathStyle style)
        {
            MathBox box;
            if (text.IndexOf(' ') >= 0)
            {
                MathRow row = new MathRow();
                string[] words = text.Split(' ');
                for (int i = 0; i < words.Length; i++)
                {
                    if (i > 0) row.Add(new MathSpace(3 * style.Size / 18, style.Size));
                    row.Add(Glyph(words[i], words[i], MathVariant.Roman, MathFace, style, false));
                }
                box = row;
            }
            else box = Glyph(text, text, MathVariant.Roman, MathFace, style, false);
            Atom atom = new Atom(AtomClass.Op, box, style);
            atom.Limits = limits;
            return atom;
        }

        private Atom OperatorName(MathStyle style)
        {
            bool limits = TryConsume('*');
            Atom atom = new Atom(AtomClass.Op, ParseArgument(style.WithVariant(MathVariant.Roman)), style);
            atom.Limits = limits;
            return atom;
        }

        private Atom Substack(MathStyle style)
        {
            if (!TryConsume('{')) return null;
            TableData table = ParseCells(style, null, false, true, 0);
            Expect('}');
            return new Atom(AtomClass.Ord, BuildTable("substack", table, style, "c", null, 0.5, 0, 0, 0.6, 0.2, false, false), style);
        }

        // ------------------------------------------------------------------ environments

        private MathStyle CellStyle(MathStyle style)
        {
            return style.WithLevel(style.Level == MathLevel.Display ? MathLevel.Text : style.Level, false);
        }

        private Atom Environment(MathStyle style)
        {
            string name = ReadTexGroupText().Trim();
            switch (name)
            {
                case "matrix":
                case "pmatrix":
                case "bmatrix":
                case "Bmatrix":
                case "vmatrix":
                case "Vmatrix":
                case "matrix*":
                case "pmatrix*":
                case "bmatrix*":
                case "Bmatrix*":
                case "vmatrix*":
                case "Vmatrix*":
                    {
                        string align = "c";
                        if (name.EndsWith("*", StringComparison.Ordinal))
                        {
                            string option = (ReadBracketText() ?? "c").Trim();
                            if (option == "l" || option == "r" || option == "c") align = option;
                        }
                        MathStyle cell = CellStyle(style);
                        TableData table = ParseCells(cell, name, false, false, 0);
                        MathBox box = BuildTable("matrix", table, cell, align, null, 1.0, 0, 0, 0.84, 0.36, false, false);
                        string kind = name.TrimEnd('*');
                        if (kind == "matrix") return new Atom(AtomClass.Ord, box, style);
                        string left = kind == "pmatrix" ? "(" : kind == "bmatrix" ? "[" : kind == "Bmatrix" ? "{" : kind == "vmatrix" ? "|" : "‖";
                        string right = kind == "pmatrix" ? ")" : kind == "bmatrix" ? "]" : kind == "Bmatrix" ? "}" : kind == "vmatrix" ? "|" : "‖";
                        return new Atom(AtomClass.Inner, Fence(box, left, right, style), style);
                    }
                case "smallmatrix":
                    {
                        MathStyle cell = style.WithLevel(MathLevel.Script, false);
                        TableData table = ParseCells(cell, name, false, false, 0);
                        return new Atom(AtomClass.Ord, BuildTable("smallmatrix", table, cell, "c", null, 0.55, 0, 0, 0.6, 0.25, false, false), style);
                    }
                case "array":
                case "darray":
                case "subarray":
                    {
                        ReadBracketText();
                        bool[] lines;
                        string align = ParseColumnSpec(ReadTexGroupText(), out lines);
                        MathStyle cell = name == "darray" ? style.WithLevel(MathLevel.Display, false) : name == "subarray" ? style.WithLevel(MathLevel.Script, false) : CellStyle(style);
                        TableData table = ParseCells(cell, name, false, false, 0);
                        bool small = name == "subarray";
                        return new Atom(AtomClass.Ord, BuildTable("array", table, cell, align, lines, small ? 0.55 : 1.0, 0, small ? 0 : 0.5,
                            small ? 0.6 : 0.84, small ? 0.25 : 0.36, false, false), style);
                    }
                case "cases":
                case "dcases":
                case "rcases":
                case "drcases":
                    {
                        MathStyle cell = name.StartsWith("d", StringComparison.Ordinal) ? style.WithLevel(MathLevel.Display, false) : CellStyle(style);
                        TableData table = ParseCells(cell, name, false, false, 0);
                        MathBox box = BuildTable("cases", table, cell, "ll", null, 1.0, 0, 0, 1.0, 0.43, false, false);
                        bool rightBrace = name.EndsWith("rcases", StringComparison.Ordinal);
                        return new Atom(AtomClass.Inner, Fence(box, rightBrace ? "." : "{", rightBrace ? "}" : ".", style), style);
                    }
                case "aligned":
                case "align":
                case "align*":
                case "split":
                case "flalign":
                case "flalign*":
                case "alignat":
                case "alignat*":
                case "alignedat":
                    {
                        bool at = name.StartsWith("alignat", StringComparison.Ordinal) || name == "alignedat";
                        if (at) ReadTexGroupText();
                        MathStyle cell = style.WithLevel(MathLevel.Display, false);
                        TableData table = ParseCells(cell, name, true, false, 0);
                        return new Atom(AtomClass.Ord, BuildTable("aligned", table, cell, AlignedColumns, null, at ? 0 : 1.0, 0.3, 0, 0.84, 0.36, true, false), style);
                    }
                case "gathered":
                case "gather":
                case "gather*":
                case "equation":
                case "equation*":
                case "displaymath":
                    {
                        MathStyle cell = style.WithLevel(MathLevel.Display, false);
                        TableData table = ParseCells(cell, name, false, false, 0);
                        if (table.Rows.Count == 1 && table.Rows[0].Count == 1)
                        {
                            if (table.Tags[0] != null && pendingTag == null)
                            {
                                pendingTag = table.Tags[0];
                                pendingTagStarred = table.TagStars[0];
                            }
                            return new Atom(AtomClass.Ord, table.Rows[0][0], style);
                        }
                        return new Atom(AtomClass.Ord, BuildTable("gathered", table, cell, "c", null, 1.0, 0.3, 0, 0.84, 0.36, false, false), style);
                    }
                case "multline":
                case "multline*":
                    {
                        MathStyle cell = style.WithLevel(MathLevel.Display, false);
                        TableData table = ParseCells(cell, name, false, false, 0);
                        return new Atom(AtomClass.Ord, BuildTable("multline", table, cell, "c", null, 1.0, 0.3, 0, 0.84, 0.36, false, true), style);
                    }
                case "eqnarray":
                case "eqnarray*":
                    {
                        MathStyle cell = style.WithLevel(MathLevel.Display, false);
                        TableData table = ParseCells(cell, name, false, false, 0);
                        return new Atom(AtomClass.Ord, BuildTable("eqnarray", table, cell, "rcl", null, 0.5, 0.3, 0, 0.84, 0.36, false, false), style);
                    }
            }
            throw new NotSupportedException("暂不支持的环境 " + name);
        }

        /// <summary>Column alignments of an array: l c r (p{..} counts as l), | for vertical lines, @{..} and !{..} ignored.</summary>
        internal static string ParseColumnSpec(string spec, out bool[] lines)
        {
            StringBuilder align = new StringBuilder();
            List<bool> before = new List<bool>();
            bool line = false;
            string text = spec ?? "";
            int guard = 0;
            for (int i = 0; i < text.Length && guard++ < 1000; i++)
            {
                char c = text[i];
                if (c == '|')
                {
                    line = true;
                    continue;
                }
                if (c == 'l' || c == 'c' || c == 'r' || c == 'p' || c == 'm' || c == 'b' || c == 'X')
                {
                    before.Add(line);
                    line = false;
                    align.Append(c == 'c' || c == 'r' ? c : 'l');
                    if ((c == 'p' || c == 'm' || c == 'b') && i + 1 < text.Length && text[i + 1] == '{') i = SkipGroup(text, i + 1);
                    continue;
                }
                if ((c == '@' || c == '!' || c == '>' || c == '<') && i + 1 < text.Length && text[i + 1] == '{')
                {
                    i = SkipGroup(text, i + 1);
                    continue;
                }
                if (c == '*' && i + 1 < text.Length && text[i + 1] == '{')
                {
                    int countEnd = SkipGroup(text, i + 1);
                    int count;
                    string countText = text.Substring(i + 2, Math.Max(0, countEnd - i - 2));
                    if (countEnd + 1 < text.Length && text[countEnd + 1] == '{' &&
                        Int32.TryParse(countText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) && count > 0 && count <= 20)
                    {
                        int patternEnd = SkipGroup(text, countEnd + 1);
                        string pattern = text.Substring(countEnd + 2, Math.Max(0, patternEnd - countEnd - 2));
                        StringBuilder repeated = new StringBuilder();
                        for (int k = 0; k < count; k++) repeated.Append(pattern);
                        text = repeated.ToString() + text.Substring(Math.Min(text.Length, patternEnd + 1));
                        i = -1;
                    }
                    continue;
                }
            }
            before.Add(line);
            lines = before.ToArray();
            return align.Length == 0 ? "c" : align.ToString();
        }

        /// <summary>Index of the brace closing the group that opens at <paramref name="open"/>.</summary>
        private static int SkipGroup(string text, int open)
        {
            int level = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') level++;
                else if (text[i] == '}')
                {
                    level--;
                    if (level == 0) return i;
                }
            }
            return text.Length - 1;
        }

        // ------------------------------------------------------------------ text

        private static Typeface TextFaceFor(string name, Typeface current)
        {
            switch (name)
            {
                case "textbf": return TextBoldFace;
                case "textit":
                case "textsl": return TextItalicFace;
                case "emph": return current == TextItalicFace ? TextFace : TextItalicFace;
                case "textsf": return SansFace;
                case "texttt": return MonoFace;
                case "textrm":
                case "textnormal":
                case "textup":
                case "textmd":
                case "textsc": return TextFace;
            }
            return current;
        }

        private Atom TextCommand(string name, MathStyle style)
        {
            return new Atom(AtomClass.Ord, TextArgument(style, TextFaceFor(name, TextFace)), style);
        }

        /// <summary>A text-mode argument: {words, with $math$} or a single character.</summary>
        private MathBox TextArgument(MathStyle style, Typeface face)
        {
            SkipIgnorable();
            if (position >= source.Length) return new MathRow();
            if (source[position] != '{')
            {
                string token = ReadGroupText();
                return TextRun(token.StartsWith("\\", StringComparison.Ordinal) ? "" : token, face, style);
            }
            position++;
            MathBox content = ParseText(style, face);
            if (position < source.Length && source[position] == '}') position++;
            return content;
        }

        private static readonly Dictionary<string, string> TextSymbols = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { " ", " " }, { "%", "%" }, { "&", "&" }, { "#", "#" }, { "$", "$" }, { "_", "_" }, { "{", "{" }, { "}", "}" }, { "textbackslash", "\\" },
            { "textasciitilde", "~" }, { "textasciicircum", "^" }, { "textdegree", "°" }, { "textendash", "–" }, { "textemdash", "—" },
            { "ldots", "…" }, { "dots", "…" }, { "textellipsis", "…" }, { "S", "§" }, { "P", "¶" }, { "dag", "†" }, { "ddag", "‡" },
            { "copyright", "©" }, { "textregistered", "®" }, { "texttrademark", "™" }, { "pounds", "£" }, { "euro", "€" }, { "yen", "¥" },
            { "AA", "Å" }, { "aa", "å" }, { "ss", "ß" }, { "o", "ø" }, { "O", "Ø" }, { "ae", "æ" }, { "AE", "Æ" }, { "oe", "œ" }, { "OE", "Œ" },
            { "i", "ı" }, { "j", "ȷ" }, { "l", "ł" }, { "L", "Ł" }, { "textmu", "μ" }, { "textohm", "Ω" }, { "textcelsius", "℃" },
            { "LaTeX", "LaTeX" }, { "TeX", "TeX" }, { "textless", "<" }, { "textgreater", ">" }, { "textbar", "|" }, { "textperiodcentered", "·" }
        };

        private static readonly Dictionary<char, char> TextAccents = new Dictionary<char, char>
        {
            { '\'', '́' }, { '`', '̀' }, { '^', '̂' }, { '"', '̈' }, { '~', '̃' }, { '=', '̄' }, { '.', '̇' },
            { 'u', '̆' }, { 'v', '̌' }, { 'H', '̋' }, { 'c', '̧' }, { 'r', '̊' }
        };

        private MathBox ParseText(MathStyle style, Typeface face)
        {
            Enter();
            try
            {
                MathRow row = new MathRow();
                StringBuilder run = new StringBuilder();
                MathStyle current = style;
                while (position < source.Length)
                {
                    char c = source[position];
                    if (c == '}') break;
                    if (c == '{')
                    {
                        FlushText(row, run, face, current);
                        position++;
                        row.Add(ParseText(current, face));
                        if (position < source.Length && source[position] == '}') position++;
                        continue;
                    }
                    if (c == '$')
                    {
                        FlushText(row, run, face, current);
                        position++;
                        row.Add(ParseRow(current.WithVariant(MathVariant.Default), StopDollar, false));
                        if (position < source.Length && source[position] == '$') position++;
                        continue;
                    }
                    if (c == '%')
                    {
                        // The comment takes its line break with it, as in TeX: \text{a% ⏎ b} is "ab", not "a b".
                        position = CommentEnd(position);
                        continue;
                    }
                    if (c == '\\')
                    {
                        TextCommandInText(row, run, ref face, ref current);
                        continue;
                    }
                    position++;
                    if (c == '~')
                    {
                        run.Append(' ');
                        continue;
                    }
                    if (Char.IsWhiteSpace(c))
                    {
                        if (run.Length == 0 || run[run.Length - 1] != ' ') run.Append(' ');
                        continue;
                    }
                    char next = position < source.Length ? source[position] : '\0';
                    if (c == '-' && next == '-')
                    {
                        position++;
                        if (position < source.Length && source[position] == '-')
                        {
                            position++;
                            run.Append('—');
                        }
                        else run.Append('–');
                        continue;
                    }
                    if (c == '`' && next == '`')
                    {
                        position++;
                        run.Append('“');
                        continue;
                    }
                    if (c == '\'' && next == '\'')
                    {
                        position++;
                        run.Append('”');
                        continue;
                    }
                    run.Append(c);
                }
                FlushText(row, run, face, current);
                return row;
            }
            finally
            {
                Exit();
            }
        }

        private void FlushText(MathRow row, StringBuilder run, Typeface face, MathStyle style)
        {
            if (run.Length == 0) return;
            row.Add(TextRun(run.ToString(), face, style));
            run.Length = 0;
        }

        private void TextCommandInText(MathRow row, StringBuilder run, ref Typeface face, ref MathStyle current)
        {
            string name = PeekCommand();
            SkipCommand(name);
            if (String.IsNullOrEmpty(name)) return;
            char accent;
            if (name.Length == 1 && TextAccents.TryGetValue(name[0], out accent))
            {
                string letter = ReadTexGroupText();
                if (letter.Length > 0 && letter[0] != '\\') run.Append((letter + accent).Normalize(NormalizationForm.FormC));
                return;
            }
            string symbol;
            if (TextSymbols.TryGetValue(name, out symbol))
            {
                run.Append(symbol);
                // A command word swallows the space after it: \LaTeX\ is.
                if (IsLetter(name[0]) && position < source.Length && source[position] == ' ') position++;
                return;
            }
            double em = current.Size;
            switch (name)
            {
                case "text":
                case "textrm":
                case "textnormal":
                case "textup":
                case "textmd":
                case "textsc":
                case "textbf":
                case "textit":
                case "textsl":
                case "emph":
                case "textsf":
                case "texttt":
                case "mbox":
                case "hbox":
                    FlushText(row, run, face, current);
                    row.Add(TextArgument(current, TextFaceFor(name, face)));
                    return;
                case "\\":
                case "newline":
                case "cr":
                    if (run.Length == 0 || run[run.Length - 1] != ' ') run.Append(' ');
                    return;
                case ",":
                case "thinspace": Space(row, run, face, current, 3 * em / 18); return;
                case ":":
                case ">":
                case "medspace": Space(row, run, face, current, 4 * em / 18); return;
                case ";":
                case "thickspace": Space(row, run, face, current, 5 * em / 18); return;
                case "!": Space(row, run, face, current, -3 * em / 18); return;
                case "quad": Space(row, run, face, current, em); return;
                case "qquad": Space(row, run, face, current, 2 * em); return;
                case "enspace": Space(row, run, face, current, em / 2); return;
                case "hspace":
                    {
                        TryConsume('*');
                        string text = ReadTexGroupText();
                        double width;
                        if (!TryParseDimension(text, em, out width)) throw new NotSupportedException("无法识别的长度 " + text);
                        Space(row, run, face, current, width);
                        return;
                    }
                case "color":
                    {
                        FlushText(row, run, face, current);
                        string model = ReadBracketText();
                        current = current.WithInk(ReadColor(model, ReadTexGroupText()));
                        return;
                    }
                case "textcolor":
                    {
                        FlushText(row, run, face, current);
                        string model = ReadBracketText();
                        Brush ink = ReadColor(model, ReadTexGroupText());
                        row.Add(TextArgument(current.WithInk(ink), face));
                        return;
                    }
            }
            MathSymbol math;
            if (LatexSymbols.Commands.TryGetValue(name, out math) && !math.Large)
            {
                run.Append(math.Text);
                return;
            }
            throw new NotSupportedException("文字中暂不支持的命令 \\" + name);
        }

        private void Space(MathRow row, StringBuilder run, Typeface face, MathStyle style, double width)
        {
            FlushText(row, run, face, style);
            row.Add(new MathSpace(width, style.Size));
        }
    }
}

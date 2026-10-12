using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexMarkdownReader.Core;
using CodexMarkdownReader.Markdown;
using CodexMarkdownReader.Rendering;
using CodexMarkdownReader.UI;

namespace CodexMarkdownReader.Tests
{
    // Formula engine checks: F04–F06 of the v2.0.2 audit, environments, tags, macros, chemistry and units.
    internal static partial class SelfTest
    {
        private static FrameworkElement Typeset(string latex, bool display)
        {
            return new LatexMathLayout(latex, Brushes.Black, display).Build(20);
        }

        /// <summary>The structure of a displayed formula, e.g. frac(1|2); "!" and the reason when it cannot be typeset.</summary>
        private static string Shape(string latex)
        {
            try { return LatexMathLayout.Describe(Typeset(latex, true), false); }
            catch (Exception ex) { return "!" + ex.Message; }
        }

        /// <summary>The structure with spaces shown as ‹mu›, in text style (inline) or display style.</summary>
        private static string Spaced(string latex, bool display)
        {
            try { return LatexMathLayout.Describe(Typeset(latex, display), true); }
            catch (Exception ex) { return "!" + ex.Message; }
        }

        private static List<string> GlyphsOf(string latex)
        {
            try { return LatexMathLayout.Glyphs(Typeset(latex, true)); }
            catch (Exception) { return new List<string>(); }
        }

        private static T BoxOf<T>(string latex, bool display) where T : MathBox
        {
            FrameworkElement element = Typeset(latex, display);
            element.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            return LatexMathLayout.Find<T>(element);
        }

        private static string FallbackReason(string latex)
        {
            Border host = FlowDocumentRenderer.CreateMathVisual(latex, true, Brushes.Black, 16) as Border;
            TextBlock source = host == null ? null : host.Child as TextBlock;
            return source == null ? null : source.ToolTip as string;
        }

        private static void MathSyntaxChecks()
        {
            // F04: one token is one argument, as in TeX.
            Check("[F04] \\frac12 与 \\frac{1}{2} 排版相同（" + Shape("\\frac12") + "）", Shape("\\frac12") == "frac(1|2)" && Shape("\\frac{1}{2}") == "frac(1|2)");
            Check("[F04] x^23 是 x² 后跟 3，与 x^{2}3 相同（" + Shape("x^23") + "）", Shape("x^23") == "x^(2)3" && Shape("x^{2}3") == "x^(2)3");
            Check("[F04] \\sqrt2x 与 \\frac a b 也只取一个记号", Shape("\\sqrt2x") == "sqrt(2)x" && Shape("\\frac a b") == "frac(a|b)" && Shape("\\frac\\alpha2") == "frac(α|2)");
            // F05: spaces before ^ and _ are ignored.
            Check("[F05] x _i ^2 与 x_i^2 相同（" + Shape("x _i ^2") + "）", Shape("x _i ^2") == "x^(2)_(i)" && Shape("x_i^2") == "x^(2)_(i)");
            Check("[F05] \\sum _{i=1} ^n 与 \\sum\\limits 的上下限", Shape("\\sum _{i=1} ^n a_i") == "limits(∑|n|i=1)a_(i)" &&
                Spaced("\\sum_{i=1}^n", false) == "∑^(n)_(i=1)" && Spaced("\\sum\\limits_{i=1}^n", false) == "limits(∑|n|i=1)");
            Check("[F05] 行内 \\lim 的下标在右下，独立公式在正下方", Shape("\\lim_{x\\to0}") == "limits(lim||x→0)" && Spaced("\\lim_{x\\to0}", false) == "lim_(x→0)");

            // F06: alphabets.
            List<string> roman = GlyphsOf("\\mathrm{ABC}+ABC");
            Check("[F06] \\mathrm 正体、默认斜体（" + String.Join(" ", roman.ToArray()) + "）", roman.Contains("ABC:Roman") && roman.Contains("A:Default") && roman.Contains("C:Default"));
            List<string> styles = GlyphsOf("\\mathbf{A}\\mathcal{L}\\mathsf{ABC}\\mathtt{ABC}\\mathbb{R}\\boldsymbol{\\alpha}\\mathfrak{g}");
            Check("[F06] 粗体、花体、无衬线、等宽、黑板粗体、粗斜体与哥特体（" + String.Join(" ", styles.ToArray()) + "）",
                styles.Contains("A:Bold") && styles.Contains("L:Script") && styles.Contains("ABC:Sans") && styles.Contains("ABC:Mono") &&
                styles.Contains("R:Blackboard") && styles.Contains("α:BoldItalic") && styles.Contains("g:Fraktur"));
            MathGlyph real = BoxOf<MathGlyph>("\\mathbb{R}", true);
            MathGlyph italic = BoxOf<MathGlyph>("x", true);
            Check("[F06] 字母映射到数学字母区：ℝ 与 𝑥", real != null && real.Text == "ℝ" && italic != null && italic.Text == Char.ConvertFromUtf32(0x1D465));
            List<string> differential = GlyphsOf("\\mathrm{d}x");
            Check("[F06] \\mathrm{d}x：d 正体、x 斜体", differential.Count == 2 && differential[0] == "d:Roman" && differential[1] == "x:Default");
            Check("[F06] \\text 中可以嵌入公式", Shape("\\text{当 $x>0$ 时}") == "当 x>0 时");
            Check("公式中的中文按文字排版并连在一起", Shape("速度=\\frac{路程}{时间}") == "速度=frac(路程|时间)");

            // Spacing between atom classes (TeX's table); unary minus.
            Check("二元运算符两侧中等间距，一元负号紧贴（" + Spaced("a-b", false) + "）", Spaced("a-b", false) == "a‹4›−‹4›b" && Spaced("-b", false) == "−b" &&
                Spaced("a=-b", false) == "a‹5›=‹5›−b" && Spaced("(-b)", false) == "(−b)");
            Check("上标中的运算符不加间距", Spaced("x^{a-b}", false) == "x^(a−b)");
            Check("显式空格 \\, \\quad 与 \\!", Spaced("a\\,b\\quad c\\!d", false) == "a‹3›b‹18›c‹-3›d");
            Check("函数名后的间距：\\sin x 有细空，\\sin(x) 没有", Spaced("\\sin x", false) == "sin‹3›x" && Spaced("\\sin(x)", false) == "sin(x)");
            Check("% 之后到行末是注释", Shape("a % 注释\n + b") == "a+b");
            Check("撇号：f'(x) 与 f''", Shape("f'(x)") == "f′(x)" && Shape("f''") == "f″");
            Check("\\not= 与 \\not\\in", Shape("a\\not=b") == "a≠b" && Shape("x\\not\\in A") == "x∉A");
            Check("\\over 与 \\choose", Shape("{a \\over b}") == "frac(a|b)" && Shape("{n \\choose k}") == "\\left(stack(n|k)\\right)");
            Check("根指数、二项式", Shape("\\sqrt[3]{x}") == "root(3|x)" && Shape("\\binom{n}{k}") == "\\left(stack(n|k)\\right)");
        }

        private static void MathStructureChecks()
        {
            Check("\\left( \\right) 包住分数（" + Shape("\\left(\\frac{a}{b}\\right)") + "）", Shape("\\left(\\frac{a}{b}\\right)") == "\\left(frac(a|b)\\right)");
            MathDelimited fenced = BoxOf<MathDelimited>("\\left(\\frac{a}{b}\\right)", true);
            MathFraction inner = BoxOf<MathFraction>("\\left(\\frac{a}{b}\\right)", true);
            Check("\\left( 的括号随内容伸缩（括号高 " + (fenced == null ? "?" : fenced.DelimiterHeight.ToString("0.0", CultureInfo.InvariantCulture)) + "）",
                fenced != null && inner != null && fenced.DelimiterHeight >= 0.85 * (inner.Ascent + inner.Descent));
            Check("\\middle 分隔", Shape("\\left\\{x\\middle|x>0\\right\\}") == "\\left{x\\middle|x>0\\right}");
            MathDelimiter big = BoxOf<MathDelimiter>("\\Big(", false);
            Check("\\big \\Big \\bigg \\Bigg 固定高度", big != null && Math.Abs(big.TargetHeight - 1.8 * 20 * 1.0) < 0.01);
            MathFontVariants variants = MathFontVariants.Instance;
            MathDelimiter matrixParen = BoxOf<MathDelimiter>("\\begin{pmatrix}a&b\\\\c&d\\end{pmatrix}", true);
            MathDelimiter tallParen = BoxOf<MathDelimiter>("\\left(\\begin{matrix}1\\\\2\\\\3\\\\4\\\\5\\\\6\\\\7\\\\8\\end{matrix}\\right)", true);
            Check("较高的括号使用 Cambria Math 自带的放大字形，再高时用字体部件拼接（" + (variants == null ? "未读到 MATH 表" : "MATH 表 " + variants.VerticalCount + " 个可伸缩字形") +
                "，矩阵括号 " + (matrixParen == null ? "?" : matrixParen.FontPieces.ToString(CultureInfo.InvariantCulture)) + " 段，八行 " +
                (tallParen == null ? "?" : tallParen.FontPieces.ToString(CultureInfo.InvariantCulture)) + " 段）",
                variants != null && matrixParen != null && matrixParen.FontPieces >= 1 && tallParen != null && tallParen.FontPieces > 1 &&
                tallParen.Ascent + tallParen.Descent >= 0.9 * tallParen.TargetHeight);
            MathDelimiter normalParen = BoxOf<MathDelimiter>("\\left(x\\right)", false);
            MathDelimiter bigParen = BoxOf<MathDelimiter>("\\big(", false);
            Check("\\big( 比普通括号大", normalParen != null && bigParen != null &&
                bigParen.Ascent + bigParen.Descent > 1.1 * (normalParen.Ascent + normalParen.Descent));

            Check("pmatrix（" + Shape("\\begin{pmatrix}a&b\\\\c&d\\end{pmatrix}") + "）", Shape("\\begin{pmatrix}a&b\\\\c&d\\end{pmatrix}") == "\\left(matrix[a&b\\\\c&d]\\right)");
            Check("bmatrix、vmatrix、Bmatrix 与 matrix", Shape("\\begin{bmatrix}1&0\\\\0&1\\end{bmatrix}") == "\\left[matrix[1&0\\\\0&1]\\right]" &&
                Shape("\\begin{vmatrix}a\\end{vmatrix}") == "\\left|matrix[a]\\right|" && Shape("\\begin{Bmatrix}a\\end{Bmatrix}") == "\\left{matrix[a]\\right}" &&
                Shape("\\begin{matrix}a&b\\end{matrix}") == "matrix[a&b]");
            Check("cases 分段函数（" + Shape("f(x)=\\begin{cases}x,&x>0\\\\0,&x\\le0\\end{cases}") + "）",
                Shape("f(x)=\\begin{cases}x,&x>0\\\\0,&x\\le0\\end{cases}") == "f(x)=\\left{cases[x,&x>0\\\\0,&x≤0]\\right.");
            Check("aligned 对齐（" + Shape("\\begin{aligned}a&=b+c\\\\d&=e\\end{aligned}") + "）", Shape("\\begin{aligned}a&=b+c\\\\d&=e\\end{aligned}") == "aligned[a&=b+c\\\\d&=e]");
            Check("align、gather、split、multline 与 array", Shape("\\begin{align}x&=1\\\\y&=2\\end{align}") == "aligned[x&=1\\\\y&=2]" &&
                Shape("\\begin{gather}a\\\\b\\end{gather}") == "gathered[a\\\\b]" && Shape("\\begin{split}a&=b\\end{split}") == "aligned[a&=b]" &&
                Shape("\\begin{multline}a\\\\b\\\\c\\end{multline}") == "multline[a\\\\b\\\\c]" &&
                Shape("\\begin{array}{c|c}1&2\\\\\\hline 3&4\\end{array}") == "array[1&2\\\\3&4]");
            Check("独立公式中的 \\\\ 换行", Shape("a=b\\\\c=d") == "gathered[a=b\\\\c=d]" && Shape("a&=b\\\\c&=d") == "aligned[a&=b\\\\c&=d]");
            Check("\\substack", Shape("\\sum_{\\substack{i<n\\\\j<m}}") == "limits(∑||substack[i<n\\\\j<m])");
            Check("上下括号与标注", Shape("\\overbrace{a+b}^{n}") == "limits(overbrace(a+b)|n|)" && Shape("\\underbrace{x}_{k}") == "limits(underbrace(x)||k)");
            Check("重音与上划线", Shape("\\hat{x}\\vec{v}\\bar{y}\\overline{AB}\\tilde{a}\\dot{x}\\ddot{x}") == "hat(x)vec(v)bar(y)overline(AB)tilde(a)dot(x)ddot(x)");
            Check("\\overset 继承关系符的间距", Spaced("a\\overset{def}{=}b", false) == "a‹5›overset(=|def|)‹5›b" && Spaced("a\\stackrel{!}{=}b", false) == "a‹5›overset(=|!|)‹5›b");
            Check("带条件的箭头：\\xrightarrow 与 \\xlongequal", Shape("A\\xrightarrow[below]{above}B") == "Aarrow:right(above|below)B" &&
                Shape("2H_2+O_2\\xlongequal{点燃}2H_2O") == "2H_(2)+O_(2)arrow:equal(点燃|)2H_(2)O");
            Check("\\boxed、\\cancel、\\phantom 与 \\color", Shape("\\boxed{x}") == "box(x)" && Shape("\\cancel{x}") == "cancel(x)" && Shape("a\\phantom{b}c") == "aphantom(b)c");
            MathGlyph red = BoxOf<MathGlyph>("\\color{red}x", true);
            SolidColorBrush redInk = red == null ? null : red.Ink as SolidColorBrush;
            Check("\\color{red} 改变颜色", redInk != null && redInk.Color.R > 150 && redInk.Color.G < 120);

            // Tags.
            LatexMathLayout tagged = new LatexMathLayout("E=mc^2\\tag{1}\\label{eq:energy}", Brushes.Black, true);
            string body = LatexMathLayout.Describe(tagged.Build(20), false);
            Check("\\tag 编号显示在右侧，不混进公式（" + body + "，" + tagged.EquationTag + "）", tagged.EquationTag == "(1)" && body == "E=mc^(2)");
            LatexMathLayout starred = new LatexMathLayout("x\\tag*{A}", Brushes.Black, true);
            starred.Build(20);
            Check("\\tag* 不加括号", starred.EquationTag == "A");
            Check("行内公式的 \\tag 放在公式后面", Spaced("x\\tag{2}", false).EndsWith("(2)", StringComparison.Ordinal));
            LatexMathLayout single = new LatexMathLayout("\\begin{align}a&=b\\tag{3}\\end{align}", Brushes.Black, true);
            single.Build(20);
            Check("单行 align 的 \\tag 同样显示在右侧", single.EquationTag == "(3)");
            Check("多行公式的每行编号", Shape("\\begin{align}a&=b\\tag{1}\\\\c&=d\\tag{2}\\end{align}") == "aligned[a&=b#(1)\\\\c&=d#(2)]");
            MathContext labels = MathContext.ForDocument(MarkdownParser.Parse("$$a=b\\tag{2.1}\\label{eq:x}$$\n\n见 $\\eqref{eq:x}$ 与 $\\ref{eq:x}$，以及 $\\eqref{none}$"));
            using (MathContext.Use(labels))
            {
                Check("\\eqref 与 \\ref 引用其他公式的编号", Shape("\\eqref{eq:x}") == "(2.1)" && Shape("\\ref{eq:x}") == "2.1" && Shape("\\eqref{none}") == "(??)");
            }

            // Macros are shared by the formulas of one document, in order.
            MathContext macros = new MathContext();
            using (MathContext.Use(macros))
            {
                Shape("\\newcommand{\\R}{\\mathbb{R}}\\newcommand{\\pair}[2]{(#1,#2)}\\DeclareMathOperator{\\Tr}{Tr}\\def\\sq#1{#1^2}");
                MathGlyph defined = BoxOf<MathGlyph>("\\R", true);
                Check("\\newcommand 定义的命令在后面的公式中可用", Shape("x\\in\\R") == "x∈R" && defined != null && defined.Text == "ℝ");
                Check("带参数的命令、\\DeclareMathOperator 与 \\def", Shape("\\pair{a}{b}") == "(a,b)" && Spaced("\\Tr A", false) == "Tr‹3›A" && Shape("\\sq{y}") == "y^(2)");
                Check("自我引用的命令不会卡死，改为显示原文", Shape("\\def\\loop{\\loop}\\loop").StartsWith("!", StringComparison.Ordinal) &&
                    Shape("\\def\\grow{\\grow\\grow}\\grow").StartsWith("!", StringComparison.Ordinal));
            }
            Check("其他文档看不到这些命令", Shape("\\pair{a}{b}").StartsWith("!", StringComparison.Ordinal));

            // Not supported: the source is shown with the reason.
            string unknown = FallbackReason("\\foo{x}");
            Check("不支持的命令显示原文并说明原因（" + unknown + "）", unknown != null && unknown.Contains("暂不支持的命令 \\foo"));
            string environment = FallbackReason("\\begin{CD}a\\end{CD}");
            Check("不支持的环境同样显示原文", environment != null && environment.Contains("暂不支持的环境 CD"));
            Check("可以排版的公式没有原文提示", FallbackReason("\\frac{-b\\pm\\sqrt{b^2-4ac}}{2a}") == null);
        }

        private static void ChemistryChecks()
        {
            Check("\\ce{H2O}：下标数字、正体元素（" + Shape("\\ce{H2O}") + "）", Shape("\\ce{H2O}") == "H_(2)O" &&
                GlyphsOf("\\ce{H2O}").Contains("H:Roman") && GlyphsOf("\\ce{H2O}").Contains("O:Roman"));
            Check("离子电荷与下标叠放：SO4^2-、NH4+、Fe^{3+}、e-（" + Shape("\\ce{SO4^2-}") + "）", Shape("\\ce{SO4^2-}") == "SO^(2−)_(4)" &&
                Shape("\\ce{NH4+}") == "NH^(+)_(4)" && Shape("\\ce{Fe^{3+}}") == "Fe^(3+)" && Shape("\\ce{e-}") == "e^(−)");
            Check("配离子（" + Shape("\\ce{[Cu(NH3)4]^2+}") + "）", Shape("\\ce{[Cu(NH3)4]^2+}") == "[Cu(NH_(3))_(4)]^(2+)");
            Check("化学方程式：系数、加号与箭头（" + Shape("\\ce{2H2 + O2 -> 2H2O}") + "）", Shape("\\ce{2H2 + O2 -> 2H2O}") == "2H_(2)+O_(2)arrow:right(|)2H_(2)O");
            Check("可逆反应与箭头上下的条件（" + Shape("\\ce{A <=>[cat.][\\Delta] B}") + "）", Shape("\\ce{A <=>[cat.][\\Delta] B}") == "Aarrow:eq(cat.|Δ)B" &&
                Shape("\\ce{A <=>> B}") == "Aarrow:eqright(|)B" && Shape("\\ce{A <-> B}") == "Aarrow:leftright(|)B" && Shape("\\ce{A->B}") == "Aarrow:right(|)B");
            Check("箭头条件也按化学式排版（" + Shape("\\ce{A ->[H2O] B}") + "）", Shape("\\ce{A ->[H2O] B}") == "Aarrow:right(H_(2)O|)B" &&
                Shape("\\ce{A ->[{加热}] B}") == "Aarrow:right(加热|)B");
            Check("结晶水与加合物", Shape("\\ce{CuSO4*5H2O}") == "CuSO_(4)⋅5H_(2)O" && Shape("\\ce{CuSO4.5H2O}") == "CuSO_(4)⋅5H_(2)O");
            Check("核素的质量数与原子序数", Shape("\\ce{^{14}_{6}C}") == "^(14)_(6)C" && Shape("\\ce{^{227}_{90}Th+}") == "^(227)_(90)Th^(+)");
            MathScripts nuclide = BoxOf<MathScripts>("\\ce{A -> ^{14}_{7}N}", true);
            string nuclideShape = nuclide == null ? "?" : nuclide.Describe(false);
            Check("箭头后的核素：质量数和原子序数不会挂到箭头上（" + nuclideShape + "）", nuclideShape == "^(14)_(7)" &&
                Shape("\\ce{^{14}_{6}C -> ^{14}_{7}N + e-}") == "^(14)_(6)Carrow:right(|)^(14)_(7)N+e^(−)");
            Check("气体与沉淀符号", Shape("\\ce{CO2 ^}") == "CO_(2)↑" && Shape("\\ce{AgCl v}") == "AgCl↓");
            Check("化学键与聚集状态", Shape("\\ce{CH3-CH2-OH}") == "CH_(3)−CH_(2)−OH" && Shape("\\ce{CH2=CH2}") == "CH_(2)=CH_(2)" &&
                Shape("\\ce{H2O(l)}") == "H_(2)O(l)" && Shape("\\ce{NaCl(aq)}") == "NaCl(aq)");
            Check("自由基与分数系数", Shape("\\ce{NDI^{.-}}") == "NDI^(•−)" && Shape("\\ce{1/2O2}") == "frac(1|2)O_(2)");
            Check("\\ce 可以和普通公式混排", Shape("\\Delta H = \\ce{H2O(l)} - \\ce{H2O(g)}") == "ΔH=H_(2)O(l)−H_(2)O(g)");
            Check("\\ce 的翻译结果（H2O → TeX）", ScienceNotation.Chemistry("H2O") == "\\mathrm{H}_{2}\\mathrm{O}");

            // Outside formulas: the Pandoc / Typora way of writing H~2~O and Fe^3+^.
            var formulas = Blocks("水 H~2~O、氢氧化钙 Ca(OH)~2~、铁离子 Fe^3+^、浓度 10^-3^ mol/L");
            Check("正文里的 H~2~O、Ca(OH)~2~、Fe^3+^、10^-3^ 显示为上下标（" + Plain("H~2~O 与 Fe^3+^") + "）",
                CountInlinesDeep(formulas, InlineKind.Subscript) == 2 && CountInlinesDeep(formulas, InlineKind.Superscript) == 2 && Plain("H~2~O 与 Fe^3+^") == "H2O 与 Fe3+");
            Check("词间的 ~删除~ 仍是删除线；x^2 + y^2、^_^ 与 [^注] 保持原样",
                CountInlinesDeep(Blocks("这是 ~删除~ 文字"), InlineKind.Strikethrough) == 1 && CountInlinesDeep(Blocks("这是 ~删除~ 文字"), InlineKind.Subscript) == 0 &&
                CountInlinesDeep(Blocks("x^2 + y^2 与 ^_^ 以及 [^注]"), InlineKind.Superscript) == 0 && Plain("x^2 + y^2 与 ^_^").Contains("x^2 + y^2 与 ^_^") &&
                CountInlinesDeep(Blocks("H~~2~~O"), InlineKind.Subscript) == 0);
        }

        private static void UnitChecks()
        {
            Check("\\SI 与 \\qty：数值、细空和单位（" + Spaced("\\SI{9.81}{\\metre\\per\\second\\squared}", false) + "）",
                Spaced("\\SI{9.81}{\\metre\\per\\second\\squared}", false) == "9.81‹3›m‹3›s^(−2)" && Shape("\\qty{3}{\\kilo\\gram}") == "3kg");
            Check("\\si 与 \\unit：单位宏和直接写法", Shape("\\si{\\kilo\\joule\\per\\mole}") == "kJmol^(−1)" && Shape("\\unit{kg.m/s^2}") == "kgm/s^(2)");
            Check("\\num：五位以上分组，科学计数法（" + Spaced("\\num{12345.6789}", false) + "）", Spaced("\\num{12345.6789}", false) == "12‹3›345.6789" &&
                Shape("\\num{1.5e-3}") == "1.5×10^(−3)" && Shape("\\num{2.5+-0.1}") == "2.5±0.1");
            Check("温度与角度：摄氏度前有空格，角度没有", Spaced("\\SI{25}{\\celsius}", false) == "25‹3›°C" && Shape("\\ang{30}") == "30°" &&
                Shape("\\ang{12;30;15}") == "12°30′15″" && Spaced("\\SI{30}{\\degree}", false) == "30°");
            Check("\\pu（mhchem 单位）（" + Shape("\\pu{1.2e-3 mol L-1}") + "）", Shape("\\pu{1.2e-3 mol L-1}") == "1.2×10^(−3)molL^(−1)" &&
                Shape("\\pu{123 kJ/mol}") == "123kJ/mol" && Shape("\\pu{25 °C}") == "25°C");
            Check("范围：\\numrange 与 \\qtyrange", Shape("\\numrange{10}{20}") == "10–20" && Shape("\\qtyrange{10}{20}{\\celsius}") == "10–20°C");
            Check("\\micro 与 \\ohm 为正体希腊字母", GlyphsOf("\\si{\\micro\\metre}").Contains("μm:Roman") || GlyphsOf("\\si{\\micro\\metre}").Contains("μ:Roman"));
            Check("不认识的单位显示原文", FallbackReason("\\si{\\foo}") != null);
            Check("公式里直接写 \\celsius、\\degree、\\ohm、\\micro 与 \\AA（" + Shape("25\\celsius,\\ 1.5\\AA") + "）",
                Shape("25\\celsius") == "25°C" && Shape("90\\degree") == "90°" && Shape("10\\ohm") == "10Ω" && Shape("1.5\\AA") == "1.5Å" &&
                Shape("3\\micro m") == "3μm" && Shape("5\\permil") == "5‰");
        }

        private static void MathExportChecks(string temp)
        {
            MarkdownDocument document = MarkdownParser.Parse("$$E=mc^2\\tag{1}$$\n\n行内 $\\ce{H2O}$ 和 $\\foo$。");
            string html = new HtmlExporter(null, MathImageRenderer.Render).Export(document, "公式");
            Check("HTML 导出：编号公式带右侧编号", html.Contains("class=\"math-block math-tagged\"") && html.Contains("<span class=\"math-tag\">(1)</span>"));
            Check("HTML 导出：行内公式按基线对齐", html.Contains("class=\"math math-inline\"") && html.Contains("style=\"vertical-align:-"));
            Check("HTML 导出：无法排版的公式保留为可复制的原文", html.Contains("<code class=\"math-source\">$\\foo$</code>"));
            RenderResult preview = Render("$$E=mc^2\\tag{7}$$", null);
            Check("预览：编号公式显示编号", CountAutomationId(preview.Document, "FormulaTag") == 1 && CountAutomationId(preview.Document, "BlockMathFormula") == 1);

            // Robustness: random formula fragments never end the process, hang or throw anything unexpected.
            string folder = Path.Combine(temp, "math-fuzz");
            Directory.CreateDirectory(folder);
            int exit;
            Dictionary<string, string> fuzz = RunProbe(folder, "fuzz", "20261011 3000", "math-fuzz", out exit);
            Check("随机拼凑的 3000 个公式：都能排版或显示原文，不会卡死或出现意外错误（" + Outcome(exit) + "，" + Field(fuzz, "summary") + "）",
                exit == 0 && Field(fuzz, "unexpected") == "0" && Field(fuzz, "total") == "3000");
            StringBuilder many = new StringBuilder();
            for (int i = 0; i < 150; i++)
                many.Append("第 ").Append(i).Append(" 行 $\\frac{a_").Append(i).Append("}{b^2}$ 与 $\\ce{H2SO4}$\n\n$$\\sum_{k=1}^{n}\\frac{1}{k^2}=\\frac{\\pi^2}{6}\\tag{")
                    .Append(i).Append("}$$\n\n");
            Dictionary<string, string> speed = RunRenderProbe(folder, "many-formulas", many.ToString(), out exit);
            long ms;
            bool timed = Int64.TryParse(Field(speed, "ms"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ms);
            Check("450 个公式的文档在 8 秒内完成解析、预览与导出（实测 " + (timed ? ms.ToString(CultureInfo.InvariantCulture) + " ms" : Outcome(exit)) + "）",
                exit == 0 && timed && ms < 8000);
        }

        /// <summary>
        /// "--render-probe file gallery" (development aid): typesets every line of the file into one picture, file + ".png".
        /// Lines: "D|caption|latex" (displayed), "I|caption|latex" (inline style), "T|caption|text with $formulas$"
        /// (formulas inside a line of text, to check baselines) and "H|heading|". In a formula, ⏎ stands for a line break,
        /// so that comments (\num{123%⏎}) can be shown as well.
        /// </summary>
        private static int GalleryProbe(string file)
        {
            try
            {
                SolidColorBrush ink = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28));
                ink.Freeze();
                SolidColorBrush faint = new SolidColorBrush(Color.FromRgb(0x8A, 0x92, 0x9B));
                faint.Freeze();
                StackPanel rows = new StackPanel();
                foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    string[] parts = line.Split(new char[] { '|' }, 3);
                    if (parts.Length < 3) continue;
                    if (parts[0] == "H")
                    {
                        TextBlock heading = new TextBlock();
                        heading.Text = parts[1];
                        heading.FontFamily = ReaderTypography.Body;
                        heading.FontSize = 15;
                        heading.FontWeight = FontWeights.SemiBold;
                        heading.Foreground = ink;
                        heading.Margin = new Thickness(0, 14, 0, 4);
                        rows.Children.Add(heading);
                        continue;
                    }
                    Grid row = new Grid();
                    row.Margin = new Thickness(0, 3, 0, 3);
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    TextBlock caption = new TextBlock();
                    caption.Text = parts[1];
                    caption.FontFamily = ReaderTypography.Body;
                    caption.FontSize = 12;
                    caption.Foreground = faint;
                    caption.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(caption);
                    FrameworkElement content = parts[0] == "T" ? InlineSample(parts[2], ink)
                        : FlowDocumentRenderer.CreateMathVisual(parts[2].Replace("⏎", "\n"), parts[0] == "D", ink, 16);
                    content.HorizontalAlignment = HorizontalAlignment.Left;
                    content.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(content, 1);
                    row.Children.Add(content);
                    rows.Children.Add(row);
                    Border rule = new Border();
                    rule.Height = 1;
                    rule.Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xEE, 0xF0));
                    rows.Children.Add(rule);
                }
                Border page = new Border();
                page.Background = Brushes.White;
                page.Padding = new Thickness(16);
                page.Child = rows;
                page.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
                Size size = page.DesiredSize;
                page.Arrange(new Rect(size));
                page.UpdateLayout();
                RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * 2), (int)Math.Ceiling(size.Height * 2), 192, 192, PixelFormats.Pbgra32);
                bitmap.Render(page);
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream stream = File.Create(file + ".png")) encoder.Save(stream);
                File.WriteAllText(file + ".result", "size=" + size.Width.ToString("0", CultureInfo.InvariantCulture) + "x" + size.Height.ToString("0", CultureInfo.InvariantCulture), new UTF8Encoding(false));
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(file + ".result", "error=" + ex, new UTF8Encoding(false)); } catch { }
                return 3;
            }
        }

        /// <summary>A line of text with inline formulas between $ signs, aligned like the preview.</summary>
        private static FrameworkElement InlineSample(string text, Brush ink)
        {
            TextBlock block = new TextBlock();
            block.FontFamily = ReaderTypography.Body;
            block.Language = ReaderTypography.Language;
            block.FontSize = 15.5;
            block.Foreground = ink;
            string[] pieces = text.Split('$');
            for (int i = 0; i < pieces.Length; i++)
            {
                if (i % 2 == 0)
                {
                    if (pieces[i].Length > 0) block.Inlines.Add(new Run(pieces[i]));
                    continue;
                }
                FrameworkElement formula = FlowDocumentRenderer.CreateMathVisual(pieces[i], false, ink, 15.5);
                InlineUIContainer container = new InlineUIContainer(formula);
                FlowDocumentRenderer.AlignInlineFormula(container, formula, 15.5);
                block.Inlines.Add(container);
            }
            return block;
        }

        private static readonly string[] FuzzTokens =
        {
            "x", "y", "2", "13", "+", "-", "=", "<", "(", ")", "[", "]", "{", "}", "{", "}", "^", "_", "'", "&", "\\\\", " ", "%", "$", "\\",
            "\\frac", "\\dfrac", "\\sqrt", "\\sqrt[", "\\left(", "\\right)", "\\left.", "\\middle|", "\\big(", "\\alpha", "\\sum", "\\int",
            "\\lim", "\\text{a b}", "\\text{", "\\mathrm{", "\\mathbf", "\\hat", "\\overbrace", "\\underbrace", "\\xrightarrow[", "\\xlongequal",
            "\\over", "\\choose", "\\color{red}", "\\textcolor{blue}", "\\boxed", "\\cancel", "\\not", "\\tag{1}", "\\label{a}", "\\eqref{a}",
            "\\begin{matrix}", "\\end{matrix}", "\\begin{cases}", "\\end{cases}", "\\begin{aligned}", "\\end{aligned}", "\\begin{array}{c|c}",
            "\\end{array}", "\\hline", "\\newcommand{\\q}[1]{#1#1}", "\\q", "\\def\\w{\\w}", "\\ce{", "\\ce{H2O}", "\\ce{SO4^2-}", "->", "<=>",
            "\\pu{", "\\SI{1}{", "\\num{12345}", "\\si{\\kilo", "\\metre", "\\per", "\\squared}", "\\operatorname*{", "\\substack{", "\\displaystyle",
            "\\scriptstyle", "\\,", "\\quad", "\\!", "\\hspace{1em}", "\\kern", "3pt", "\\rule{1em}{2pt}", "\\unicode{x2192}", "\\phantom",
            "\\overset{a}", "\\stackrel", "\\genfrac{(}{)}{0pt}{}", "\\pmod", "\\abs", "\\dv", "\\pdv", "中", "α", "≤", "\U0001D465", "#", "~", "\\{", "\\}",
            // White space inside formulas (F11), uncertainties (F12), macro arguments and \let (F13, F14), labels (F15).
            "\t", "\n", "\r\n", "\u3000", "\u00A0", "m", "s", "\\si{m\ts}", "\\pu{1 m\u3000s}", "\\num{1.2+-0.1e3}", "\\SI{2+-0.1}{",
            "\\let\\b\\sqrt", "\\let", "\\b", "\\renewcommand{\\sqrt}[1]{\\b{#1}}", "\\newcommand{\\j}[2]{#1#2}", "\\j", "\\label{", "\\ref{a}",
            // Macro arguments, comments and escapes (F16, F20, F22); numbers, units and chemistry text (F17–F19, F21, F24).
            "\\newcommand{\\k}{ab}", "\\k", "\\text{\\#1}", "\\#", "%}\n", "\\of{NaCl}", "\\raiseto{0.5}", "\\highlight{red}", "\\tothe{n}",
            "\\pu{1 J // mol}", "//", "\\num{1 234e5}", "\\num{-e10}", "\\ce{{a b}}",
            // ± in products and ranges (F17), comments in [arguments] (F20), exact unit powers (F18).
            "\\num{2+-0.1x3}", "\\numrange{1+-0.1}{2}", "\\ang{1+-0.1}", "\\newcommand{\\oq}[1][a%]\n+b]{#1}", "\\oq", "\\oq[", "[a%]\n",
            "\\tothe{0.0000000000001}", "\\raiseto{n+1}", "\\raiseto{0.12345678901234567890123456789}", "\\cubed",
            // A power of ten in a divisor (F17); comments next to command names and in other arguments (F20).
            "\\num{1/2e3}", "\\num{1/1.2+-0.1e3}", "\\alpha%\n", "\\f[\\alpha%\nx]", "\\newcommand{\\f}[1][x]{#1}", "\\text{a%\nb}",
            "\\tag{A%\n}", "\\label{q%\n}", "\\begin{matrix%\n}", "\\url{a%20b}",
            // Comments in the numbers of \num, \SI and the like (F25).
            "\\num{1%\n23}", "\\SI{1%\n2}{%}", "\\numrange{1%\n}{2}", "\\ang{1%\n;2}", "\\numlist{1%\n;2}"
        };

        /// <summary>"--render-probe file math-fuzz": typesets and measures random formula fragments; any exception other than
        /// "cannot be typeset" is reported.</summary>
        private static int MathFuzzProbe(string file)
        {
            try
            {
                string[] parameters = File.ReadAllText(file, Encoding.UTF8).Split(' ');
                Random random = new Random(Int32.Parse(parameters[0], CultureInfo.InvariantCulture));
                int count = Int32.Parse(parameters[1], CultureInfo.InvariantCulture);
                int ok = 0, unsupported = 0, unexpected = 0;
                string first = "";
                for (int n = 0; n < count; n++)
                {
                    StringBuilder latex = new StringBuilder();
                    int tokens = 1 + random.Next(24);
                    for (int t = 0; t < tokens; t++) latex.Append(FuzzTokens[random.Next(FuzzTokens.Length)]);
                    string formula = latex.ToString();
                    try
                    {
                        MathContext context = new MathContext();
                        using (MathContext.Use(context))
                        {
                            LatexMathLayout layout = new LatexMathLayout(formula, Brushes.Black, random.Next(2) == 0);
                            FrameworkElement element = layout.Build(16);
                            element.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
                            element.Arrange(new Rect(element.DesiredSize));
                            LatexMathLayout.Describe(element, true);
                        }
                        ok++;
                    }
                    catch (NotSupportedException) { unsupported++; }
                    catch (InvalidOperationException) { unsupported++; }
                    catch (Exception ex)
                    {
                        unexpected++;
                        if (first.Length == 0) first = formula.Replace("\n", "\\n") + " → " + ex.GetType().Name + ": " + ex.Message;
                    }
                }
                File.WriteAllLines(file + ".result", new string[]
                {
                    "total=" + count.ToString(CultureInfo.InvariantCulture),
                    "unexpected=" + unexpected.ToString(CultureInfo.InvariantCulture),
                    "summary=排版 " + ok.ToString(CultureInfo.InvariantCulture) + "，显示原文 " + unsupported.ToString(CultureInfo.InvariantCulture) +
                        "，意外 " + unexpected.ToString(CultureInfo.InvariantCulture) + (first.Length > 0 ? "，首个：" + first : "")
                }, new UTF8Encoding(false));
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(file + ".result", "error=" + ex.GetType().Name + ": " + ex.Message, new UTF8Encoding(false)); } catch { }
                return 3;
            }
        }
    }
}

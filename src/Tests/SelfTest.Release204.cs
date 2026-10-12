using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using CodexMarkdownReader.Markdown;
using CodexMarkdownReader.Rendering;
using MdBlock = CodexMarkdownReader.Markdown.Block;

namespace CodexMarkdownReader.Tests
{
    // Regression checks for the v2.0.3 re-audit (F11–F15), with the audit's own inputs, and for the range written
    // with ~ in Chinese text.
    internal static partial class SelfTest
    {
        // ------------------------------------------------------------------ F11: white space inside units

        private static void UnitWhitespaceChecks(string temp)
        {
            string folder = Path.Combine(temp, "review203-units");
            Directory.CreateDirectory(folder);
            // In a child process first: on 2.0.3 these never return, and the self-test has to report that instead of
            // hanging the build. Only when they finish are they typeset here as well.
            string markdown =
                "$$\n\\si{m\ns^{-1}}\n$$\n\n" +
                "$$\n\\pu{1 m\r\ns^{-1}}\n$$\n\n" +
                "行内 $\\pu{1 m\ts^{-1}}$、$\\SI{2}{m\u00A0s}$、$\\qty{3}{kg\tm}$、$\\si{J\u3000mol-1}$ 与 $\\pu{5 m\u2009s-2}$\n";
            int exit;
            Dictionary<string, string> probe = RunRenderProbe(folder, "units", markdown, out exit);
            long ms;
            bool timed = Int64.TryParse(Field(probe, "ms"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ms);
            bool finished = exit == 0 && timed && ms < 8000;
            Check("[F11] 单位里的 Tab、换行、CRLF、不换行空格、全角空格与窄空格：预览和导出正常结束（" +
                (timed ? ms.ToString(CultureInfo.InvariantCulture) + " ms" : Outcome(exit)) + "）", finished);
            if (!finished) return;

            string reference = Spaced("\\pu{1 m s^{-1}}", false);
            Check("[F11] Tab、换行和 CRLF 与普通空格的效果相同（" + reference + "）", reference == "1‹3›m‹3›s^(−1)" &&
                Spaced("\\pu{1 m\ts^{-1}}", false) == reference && Spaced("\\pu{1 m\ns^{-1}}", false) == reference &&
                Spaced("\\pu{1 m\r\ns^{-1}}", false) == reference);
            string[,] pairs =
            {
                { "\\si{m\ns^{-1}}", "\\si{m s^{-1}}" },
                { "\\SI{2}{m\u00A0s}", "\\SI{2}{m s}" },
                { "\\qty{3}{kg\tm}", "\\qty{3}{kg m}" },
                { "\\si{J\u3000mol-1}", "\\si{J mol-1}" },
                { "\\pu{5 m\u2009s-2}", "\\pu{5 m s-2}" },
                { "\\si{\\kilo\\gram\tm}", "\\si{\\kilo\\gram m}" }
            };
            List<string> different = new List<string>();
            for (int k = 0; k < pairs.GetLength(0); k++)
            {
                string actual = Spaced(pairs[k, 0], false), expected = Spaced(pairs[k, 1], false);
                if (actual != expected || expected.StartsWith("!", StringComparison.Ordinal)) different.Add(pairs[k, 1] + " → " + actual);
            }
            Check("[F11] \\si、\\SI、\\qty 与 \\pu 中的各种空白都等于普通空格" + (different.Count == 0 ? "" : "（不同：" + String.Join("；", different.ToArray()) + "）"),
                different.Count == 0);
        }

        // ------------------------------------------------------------------ F12: uncertainty and a common exponent

        private static void UncertaintyChecks()
        {
            Check("[F12] \\num{1.23+-0.04e5}：共同指数作用于数值和不确定度（" + Shape("\\num{1.23+-0.04e5}") + "）",
                Shape("\\num{1.23+-0.04e5}") == "(1.23±0.04)×10^(5)" && Shape("\\num{-1.23+-0.04e-5}") == "(−1.23±0.04)×10^(−5)");
            Check("[F12] 紧凑写法 1.23(4)e5 不加括号；没有指数和单位的 2.5+-0.1 也不加",
                Shape("\\num{1.23(4)e5}") == "1.23(4)×10^(5)" && Shape("\\num{2.5+-0.1}") == "2.5±0.1");
            Check("[F12] 后面跟单位时整个数值加括号：\\SI、\\qty、\\pu 与 \\qtyrange（" + Shape("\\SI{2.5+-0.1}{\\celsius}") + "，" +
                Shape("\\pu{1.23+-0.04e5 m}") + "）",
                Shape("\\SI{2.5+-0.1}{\\celsius}") == "(2.5±0.1)°C" && Shape("\\qty{1.23+-0.04e5}{\\metre}") == "(1.23±0.04)×10^(5)m" &&
                Shape("\\pu{1.23+-0.04e5 m}") == "(1.23±0.04)×10^(5)m" && Shape("\\qtyrange{1+-0.1}{2+-0.1}{\\metre}") == "(1±0.1)–(2±0.1)m" &&
                Shape("\\SI{1.23(4)e5}{\\metre}") == "1.23(4)×10^(5)m");
        }

        // ------------------------------------------------------------------ F13, F14: macro arguments and \let

        private static void MacroSemanticsChecks()
        {
            Check("[F13] 命令作为宏参数：\\join{\\alpha}{x} 是 αx（" + Shape("\\newcommand{\\join}[2]{#1#2}\\join{\\alpha}{x}") + "）",
                Shape("\\newcommand{\\join}[2]{#1#2}\\join{\\alpha}{x}") == "αx");
            Check("[F13] 宏体中命令后紧跟参数、参数后紧跟字母（" + Shape("\\newcommand{\\f}[1]{\\alpha#1}\\f{x}") + "）",
                Shape("\\newcommand{\\f}[1]{\\alpha#1}\\f{x}") == "αx" && Shape("\\newcommand{\\g}[1]{#1y}\\g{\\beta}") == "βy" &&
                Shape("\\def\\h#1{\\beta#1}\\h{x}") == "βx");
            Check("[F13] 宏里的 \\\\ 换行仍是换行", Shape("\\newcommand{\\rows}[1]{a\\\\#1}\\rows{b}") == Shape("a\\\\b") &&
                Shape("a\\\\b").StartsWith("gathered[", StringComparison.Ordinal));

            Check("[F14] \\let 保存当时的定义：\\def\\A{x}\\let\\B\\A\\def\\A{y}\\B 是 x（" + Shape("\\def\\A{x}\\let\\B\\A\\def\\A{y}\\B") + "）",
                Shape("\\def\\A{x}\\let\\B\\A\\def\\A{y}\\B") == "x");
            Check("[F14] 先保存再重定义 \\sqrt 不会循环（" + Shape("\\let\\oldsqrt\\sqrt\\renewcommand{\\sqrt}[1]{\\oldsqrt{#1}}\\sqrt{x}") + "）",
                Shape("\\let\\oldsqrt\\sqrt\\renewcommand{\\sqrt}[1]{\\oldsqrt{#1}}\\sqrt{x}") == Shape("\\sqrt{x}") &&
                Shape("\\sqrt{x}") == "sqrt(x)");
            Check("[F14] \\let 保留参数个数，也可以指向符号和字符",
                Shape("\\newcommand{\\p}[2]{(#1,#2)}\\let\\q\\p\\renewcommand{\\p}[1]{#1}\\q{a}{b}") == "(a,b)" &&
                Shape("\\let\\B=\\alpha\\B+\\B") == "α+α" && Shape("\\let\\c=y\\c") == "y");
        }

        // ------------------------------------------------------------------ F15: labels and references

        private static string LabelOf(MathContext context, string key)
        {
            string tag;
            return context.Labels.TryGetValue(key, out tag) ? tag : null;
        }

        private static void LabelChecks(string temp)
        {
            string commented = "x=1% \\tag{99}\\label{a}\n\\tag{1}\\label{a}";
            LatexMathLayout shown = new LatexMathLayout(commented, Brushes.Black, true, new MathContext());
            shown.Build(20);
            MathContext comment = MathContext.ForDocument(MarkdownParser.Parse("$$\n" + commented + "\n$$\n\n见 $\\eqref{a}$"));
            using (MathContext.Use(comment))
            {
                Check("[F15] 注释里的 \\tag 与 \\label 不算：公式编号 " + shown.EquationTag + "，引用 " + Shape("\\eqref{a}"),
                    shown.EquationTag == "(1)" && LabelOf(comment, "a") == "1" && Shape("\\eqref{a}") == "(1)");
            }

            MathContext macro = MathContext.ForDocument(MarkdownParser.Parse(
                "$$\n\\newcommand{\\id}[1]{\\tag{#1}\\label{eq:#1}}\nx=1\\id{A}\n$$\n\n见 $\\eqref{eq:A}$"));
            using (MathContext.Use(macro))
            {
                Check("[F15] 宏生成的 \\label 按展开后的结果登记（" + Shape("\\eqref{eq:A}") + "）",
                    LabelOf(macro, "eq:A") == "A" && LabelOf(macro, "eq:#1") == null && Shape("\\eqref{eq:A}") == "(A)");
            }
            Check("[F15] 收集标签时定义的命令不会提前生效：正文仍是先定义、后使用", !macro.Macros.ContainsKey("id"));

            MathContext rows = MathContext.ForDocument(MarkdownParser.Parse(
                "先看 $\\eqref{q}$ 和 $\\ref{p}$。\n\n$$\\begin{align}a&=b\\tag{1}\\label{p}\\\\c&=d\\tag{2}\\label{q}\\end{align}$$"));
            using (MathContext.Use(rows))
            {
                Check("[F15] 多行公式每行的标签对应本行编号，引用可以写在公式之前（" + Shape("\\eqref{q}") + "）",
                    LabelOf(rows, "p") == "1" && LabelOf(rows, "q") == "2" && Shape("\\eqref{q}") == "(2)" && Shape("\\ref{p}") == "1");
            }
            Check("没有引用的文档不预先排版公式", MathContext.ForDocument(MarkdownParser.Parse("$$a\\tag{1}\\label{x}$$")).Labels.Count == 0);

            // Collecting the labels typesets the formulas once more; a long paper with references stays quick.
            string folder = Path.Combine(temp, "review203-labels");
            Directory.CreateDirectory(folder);
            StringBuilder paper = new StringBuilder();
            for (int i = 0; i < 150; i++)
                paper.Append("$$\\sum_{k=1}^{n}\\frac{1}{k^2}=\\frac{\\pi^2}{6}\\tag{").Append(i).Append("}\\label{eq:").Append(i).Append("}$$\n\n见 $\\eqref{eq:")
                    .Append(i * 7 % 150).Append("}$ 与 $\\ce{H2SO4}$\n\n");
            int exit;
            Dictionary<string, string> speed = RunRenderProbe(folder, "references", paper.ToString(), out exit);
            long ms;
            bool timed = Int64.TryParse(Field(speed, "ms"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ms);
            Check("150 个编号公式与 150 处引用的文档在 8 秒内完成解析、预览与导出（实测 " + (timed ? ms.ToString(CultureInfo.InvariantCulture) + " ms" : Outcome(exit)) + "）",
                exit == 0 && timed && ms < 8000);
        }

        // ------------------------------------------------------------------ ranges written with ~

        private static void RangeTextChecks()
        {
            List<MdBlock> prices = Blocks("温度 5~10元，3~5个");
            Check("正文里的 5~10元，3~5个 是范围，不是下标也不是删除线（" + Plain("温度 5~10元，3~5个") + "）",
                CountInlinesDeep(prices, InlineKind.Subscript) == 0 && CountInlinesDeep(prices, InlineKind.Strikethrough) == 0 &&
                Plain("温度 5~10元，3~5个") == "温度 5~10元，3~5个");
            List<MdBlock> days = Blocks("需要 1~2天和3~4天");
            Check("1~2天和3~4天 同样保持原样", CountInlinesDeep(days, InlineKind.Subscript) == 0 && CountInlinesDeep(days, InlineKind.Strikethrough) == 0 &&
                Plain("需要 1~2天和3~4天") == "需要 1~2天和3~4天");
            Check("H~2~O、R~总~ 与 ~删除~ 不受影响", CountInlinesDeep(Blocks("H~2~O 与 R~总~"), InlineKind.Subscript) == 2 &&
                CountInlinesDeep(Blocks("这是 ~删除~ 文字"), InlineKind.Strikethrough) == 1);
        }
    }
}

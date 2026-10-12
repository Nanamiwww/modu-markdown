using System;
using System.Collections.Generic;
using System.Globalization;
using CodexMarkdownReader.Markdown;
using CodexMarkdownReader.Rendering;

namespace CodexMarkdownReader.Tests
{
    // Regression checks for the v2.0.7 re-audit: comments in the numbers of \num, \SI and the like (F25), and every case
    // of the audit's lists with the correct form it gave.
    internal static partial class SelfTest
    {
        // ------------------------------------------------------------------ F25: comments in number arguments

        private static void NumberCommentChecks()
        {
            // The audit's inputs: % must be followed by a real line break.
            string number = Shape("\\num{123%\n}");
            string range = Shape("\\numrange{10%\n}{20}");
            Check("[F25] \\num{123% ⏎ } 是 123，\\numrange{10% ⏎ }{20} 是 10–20，不再显示原文（" + number + "，" + range + "）",
                number == "123" && number == Shape("\\num{123}") && range == "10–20" && range == Shape("\\numrange{10}{20}"));
            Check("[F25] 直接写与放进宏体的结果相同",
                Shape("\\newcommand{\\f}{\\num{123%\n}}\\f") == number && Shape("\\newcommand{\\f}{\\numrange{10%\n}{20}}\\f") == range);
            Check("[F25] 注释夹在数字中间：\\num、\\SI 的数值、\\numrange 与 \\ang",
                Shape("\\num{1% ignored\n23}") == "123" && Shape("\\SI{1% ignored\n23}{\\metre}") == "123m" &&
                Shape("\\numrange{1% ignored\n0}{20}") == "10–20" && Shape("\\ang{1% ignored\n2;30;15}") == "12°30′15″");
            Check("[F25] \\qty、\\SIrange、\\numlist、\\SIlist 与 \\tablenum 的数值同样跳过注释；五位以上仍分组",
                Shape("\\qty{5%\n}{\\metre}") == "5m" && Shape("\\SIrange{1%\n0}{2%\n0}{\\metre}") == Shape("\\SIrange{10}{20}{\\metre}") &&
                Shape("\\numlist{1%\n;2;3}") == Shape("\\numlist{1;2;3}") && Shape("\\SIlist{1%\n;2}{\\metre}") == Shape("\\SIlist{1;2}{\\metre}") &&
                Shape("\\tablenum{3%\n}") == "3" && Spaced("\\num{12%\n345}", false) == Spaced("\\num{12345}", false));

            // % stays a percent sign where it is text: units, \ce and \pu.
            string unit = Shape("\\SI{50}{%}"), unitRange = Shape("\\SIrange{1}{2}{%}");
            Check("[F25] 单位、\\ce 与 \\pu 里的 % 仍是百分号（" + unit + "，" + unitRange + "）",
                unit.Contains("50%") && unitRange.Contains("%") && !unitRange.StartsWith("!", StringComparison.Ordinal) &&
                Shape("\\ce{98% H2SO4}").Contains("98%") && Shape("\\pu{5 %}").Contains("5%"));

            // The exported HTML has the formulas as pictures, not as their source.
            MarkdownDocument document = MarkdownParser.Parse("$$\n\\num{123%\n}\n$$\n\n$$\n\\numrange{10%\n}{20}\n$$\n");
            string html = new HtmlExporter(null, MathImageRenderer.Render).Export(document, "数值注释");
            int pictures = 0;
            for (int at = html.IndexOf("<img class=\"math", StringComparison.Ordinal); at >= 0; at = html.IndexOf("<img class=\"math", at + 1, StringComparison.Ordinal)) pictures++;
            Check("[F25] HTML 导出时两个公式都是图片，没有退回原文（" + pictures.ToString(CultureInfo.InvariantCulture) + " 张）",
                pictures == 2 && !html.Contains("<code class=\"math-source\""));
        }

        // ------------------------------------------------------------------ the lists of the v2.0.7 re-audit

        // { input, the correct form given by the audit }: its F25 cases and every variant it tried.
        private static readonly string[,] Audit207Cases =
        {
            { "\\num{123%\n}", "\\num{123}" },
            { "\\numrange{10%\n}{20}", "\\numrange{10}{20}" },
            { "\\newcommand{\\f}{\\num{123%\n}}\\f", "\\num{123}" },
            { "\\num{1/-2e-3}", "1/(-2\\times10^{-3})" },
            { "\\num{1.2+-0.1e3/2.3+-0.2e-4}", "(1.2\\pm0.1)\\times10^3/((2.3\\pm0.2)\\times10^{-4})" },
            { "\\num{1/2.30(5)e3}", "1/(2.30(5)\\times10^3)" },
            { "\\newcommand{\\f}[1][x]{#1}\\f[\\alpha% first\n% second\nx]", "\\alpha x" },
            { "\\newcommand{\\f}[1][x]{#1}\\f[\\alpha% first\n\\beta]", "\\alpha\\beta" },
            { "\\newcommand{\\f}{\\mathrm% first\nx}\\f", "\\mathrm{x}" },
            { "\\newcommand{\\f}{\\text{a% first\nb}}\\f", "\\text{ab}" },
            { "\\newcommand{\\f}{\\text{50\\%}}\\f", "\\text{50\\%}" },
            { "\\begin{array}{r%}\nl}a&b\\end{array}", "\\begin{array}{rl}a&b\\end{array}" },
            { "\\textcolor{r% }\ned}{x}", "\\textcolor{red}{x}" },
            { "\\rule{1% }\nem}{2pt}", "\\rule{1em}{2pt}" },
            { "\\num{1% ignored\n23}", "\\num{123}" },
            { "\\SI{1% ignored\n23}{\\metre}", "\\SI{123}{\\metre}" },
            { "\\numrange{1% ignored\n0}{20}", "\\numrange{10}{20}" },
            { "\\ang{1% ignored\n2;30;15}", "\\ang{12;30;15}" }
        };

        private static void ExtendedAudit207Checks()
        {
            List<string> different = new List<string>();
            for (int k = 0; k < Audit207Cases.GetLength(0); k++)
            {
                string actual = Shape(Audit207Cases[k, 0]), expected = Shape(Audit207Cases[k, 1]);
                if (actual != expected || expected.StartsWith("!", StringComparison.Ordinal))
                    different.Add(Audit207Cases[k, 0].Replace("\n", "⏎") + " → " + actual + "，应为 " + expected);
            }
            Check("[复审] F25 与扩展清单的 " + Audit207Cases.GetLength(0).ToString(CultureInfo.InvariantCulture) + " 个样例都与报告给的正确写法排版相同" +
                (different.Count == 0 ? "" : "（不同：" + String.Join("；", different.ToArray()) + "）"), different.Count == 0);
        }
    }
}

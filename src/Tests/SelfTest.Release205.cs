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
    // Regression checks for the v2.0.4 re-audit (F16–F24), with the audit's own inputs and its correct references.
    internal static partial class SelfTest
    {
        // ------------------------------------------------------------------ F16, F20, F22: macros

        private static void MacroArgumentChecks()
        {
            // F16: LaTeX hands \auditab to \frac unexpanded, so its whole expansion is the numerator.
            string fraction = Shape("\\newcommand{\\auditab}{ab}\\frac\\auditab2");
            Check("[F16] 宏作为分数参数：\\frac\\auditab2 是 ab/2（" + fraction + "）", fraction == "frac(ab|2)" && fraction == Shape("\\frac{ab}{2}"));
            Check("[F16] \\sqrt、\\overset 的参数同样取整个宏；上标仍按 TeX 先展开再取第一个记号（x^\\auditab 是 x^a b）",
                Shape("\\newcommand{\\auditab}{ab}\\sqrt\\auditab") == "sqrt(ab)" &&
                Spaced("\\newcommand{\\eqdef}{=}a\\overset{d}\\eqdef b", false) == Spaced("a\\overset{d}{=}b", false) &&
                Shape("\\newcommand{\\auditab}{ab}x^\\auditab") == "x^(a)b");

            // F20: % starts a comment when a definition or a macro argument is read, as in TeX.
            string commented = Shape("\\newcommand{\\auditf}{a%}\n+b}\\auditf");
            Check("[F20] 宏定义里 % 之后是注释，注释中的 } 不结束定义（" + commented + "）",
                commented == "a+b" && commented == Shape("\\newcommand{\\auditf}{a+b}\\auditf"));
            Check("[F20] 宏参数里的注释同样跳过；\\% 仍是百分号",
                Shape("\\newcommand{\\pair}[1]{(#1)}\\pair{a%}\n+b}") == "(a+b)" && Shape("\\newcommand{\\pc}{50\\%}\\pc") == "50%");
            string percent = Shape("\\ce{98% H2SO4}");
            Check("[F20] \\ce 里的 % 仍按百分号显示（" + percent + "）", !percent.StartsWith("!", StringComparison.Ordinal) && percent.Contains("98%"));

            // F22: \# in a macro body is a character, not the parameter #1.
            string hash = Shape("\\newcommand{\\auditf}[1]{\\text{\\#1}:#1}\\auditf{x}");
            Check("[F22] 宏体中的 \\#1 是文字，不被当成参数（" + hash + "）", hash == "#1:x" && hash == Shape("\\text{\\#1}:x"));
            Check("[F22] 宏体中的 \\\\ 换行后紧跟参数仍正常", Shape("\\newcommand{\\rows}[1]{a\\\\#1}\\rows{b}") == Shape("a\\\\b"));
        }

        // ------------------------------------------------------------------ F17, F18, F19, F21, F24: numbers, units, chemistry

        private static void ScienceInputChecks()
        {
            // F17: spaces, \, and ~ group digits; the whole number is understood or the source is shown.
            string grouped = Shape("\\num{1 234e5}");
            Check("[F17] \\num{1 234e5} 是 1234 × 10⁵（" + grouped + "）", grouped == "1234×10^(5)" && grouped == Shape("\\num{1234e5}") &&
                Shape("\\num{1\\,234e5}") == grouped && Shape("\\num{1~234e5}") == grouped);
            Check("[F17] \\SI 的数值同样处理；五位以上照常分组（" + Spaced("\\num{12 345}", false) + "）",
                Shape("\\SI{1 234e5}{\\mole}") == "1234×10^(5)mol" && Spaced("\\num{12 345}", false) == "12‹3›345");
            Check("[F17] 只写指数的 \\num{e5}、\\num{-e10}", Shape("\\num{e5}") == "10^(5)" && Shape("\\num{-e10}") == "−10^(10)");
            Check("[F17] 比较符、商与 \\times 10^n 的写法", Shape("\\num{<0.1}") == "<0.1" && Shape("\\num{1/3}") == "1/3" &&
                Shape("\\num{1.2\\times10^{3}}") == "1.2×10^(3)");
            string number = FallbackReason("\\num{1.2.3}");
            Check("[F17] 无法完整识别的数值显示原文并说明原因（" + number + "）", number != null && number.Contains("无法识别的数值"));

            // F18: decimal powers are kept.
            Check("[F18] \\raiseto{0.5} 保留小数幂（" + Shape("\\si{\\raiseto{0.5}\\second}") + "）",
                Shape("\\si{\\raiseto{0.5}\\second}") == "s^(0.5)" && Shape("\\si{\\raiseto{0.5}\\second}") == Shape("\\mathrm{s}^{0.5}"));
            Check("[F18] \\per、\\tothe 与 \\squared 和小数幂一起计算", Shape("\\si{\\per\\raiseto{0.5}\\second}") == "s^(−0.5)" &&
                Shape("\\SI{2}{\\metre\\tothe{0.5}}") == "2m^(0.5)" && Shape("\\si{\\raiseto{0.5}\\second\\squared}") == "s");

            // F19: \of is shown as a subscript, siunitx's default.
            string qualified = Shape("\\SI{1}{\\mole\\of{NaCl}}");
            Check("[F19] \\SI{1}{\\mole\\of{NaCl}} 保留限定词（" + qualified + "）", qualified == "1mol_(NaCl)" &&
                qualified == Shape("1\\,\\mathrm{mol}_{\\mathrm{NaCl}}") && Shape("\\si{\\kilogram\\of{metal}}") == "kg_(metal)");
            MathGlyph highlighted = BoxOf<MathGlyph>("\\si{\\highlight{red}\\metre}", true);
            SolidColorBrush highlightInk = highlighted == null ? null : highlighted.Ink as SolidColorBrush;
            Check("[F19] \\highlight 给后面的单位上色", highlightInk != null && highlightInk.Color.R > 150 && highlightInk.Color.G < 120);
            string prefix = FallbackReason("\\si{\\kilo}"), per = FallbackReason("\\si{\\metre\\per}"), squared = FallbackReason("\\si{\\squared\\metre}"),
                of = FallbackReason("\\si{\\of{x}}");
            Check("[F19] 悬空的词头、\\per、\\squared 与 \\of 不再被悄悄丢掉，显示原文并说明原因",
                prefix != null && prefix.Contains("词头") && per != null && per.Contains("\\per") && squared != null && squared.Contains("\\squared") &&
                of != null && of.Contains("\\of"));
            Check("[F19] 宏与字面单位混写：\\kilo m 是 km，\\joule\\per mol 是 J mol⁻¹",
                Shape("\\si{\\kilo m}") == "km" && Shape("\\si{\\joule\\per mol}") == Shape("\\si{\\joule\\per\\mole}") &&
                Shape("\\si{m\\squared}") == "m^(2)");

            // F21: {text} inside \ce is upright text with its spaces.
            string text = Shape("\\ce{{Gluconic Acid} + H2O2}");
            List<string> glyphs = GlyphsOf("\\ce{{Gluconic Acid} + H2O2}");
            Check("[F21] \\ce 里的 {Gluconic Acid} 是正体文字，保留空格（" + text + "）",
                text == "Gluconic Acid+H_(2)O_(2)" && text == Shape("\\text{Gluconic Acid}+\\ce{H2O2}") && glyphs.Contains("Gluconic Acid:Roman"));

            // F24: // in \pu is a fraction.
            string fraction = Shape("\\pu{75.3 J // mol K}");
            Check("[F24] \\pu{75.3 J // mol K} 排成分式（" + fraction + "）", fraction == "75.3frac(J|molK)" &&
                fraction == Shape("75.3\\,\\frac{\\mathrm{J}}{\\mathrm{mol}\\,\\mathrm{K}}") && Shape("\\pu{123 kJ//mol}") == "123frac(kJ|mol)" &&
                Shape("\\pu{123 kJ/mol}") == "123kJ/mol");
        }

        // ------------------------------------------------------------------ F23: long paragraphs of bold text

        private static void LongEmphasisChecks(string temp)
        {
            List<MdBlock> bold = Blocks(Repeat("**x** ", 6000));
            int strong = CountInlinesDeep(bold, InlineKind.Strong);
            Check("[F23] 同一段落 6000 组 **x** 全部是粗体（" + strong.ToString(CultureInfo.InvariantCulture) + "）", strong == 6000);
            List<MdBlock> mixed = Blocks(Repeat("*a* **b** ~~c~~ ", 3000));
            Check("[F23] 斜体、粗体与删除线混排 3000 组也全部识别", CountInlinesDeep(mixed, InlineKind.Emphasis) == 3000 &&
                CountInlinesDeep(mixed, InlineKind.Strong) == 3000 && CountInlinesDeep(mixed, InlineKind.Strikethrough) == 3000);

            // Matching every pair must not bring back the slow search that a fixed limit used to hide.
            string folder = Path.Combine(temp, "review204-emphasis");
            Directory.CreateDirectory(folder);
            int exit;
            Dictionary<string, string> fields = RunProbe(folder, "strong", Repeat("**x** ", 50000), "parse", out exit);
            long ms;
            bool timed = Int64.TryParse(Field(fields, "ms"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ms);
            Check("[F23] 5 万组 **粗体** 在 1.5 秒内解析完（实测 " + (timed ? ms.ToString(CultureInfo.InvariantCulture) + " ms" : Outcome(exit)) + "）",
                exit == 0 && timed && ms < 1500);
        }
    }
}

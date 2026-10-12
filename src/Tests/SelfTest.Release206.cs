using System;
using System.Collections.Generic;

namespace CodexMarkdownReader.Tests
{
    // Regression checks for the v2.0.5 re-audit: its three residual inputs (F17, F18, F20) and every case of its extended
    // list, each compared with the correct form the audit gave.
    internal static partial class SelfTest
    {
        // ------------------------------------------------------------------ F17: ± inside a product, a quotient or a range

        private static void ProductUncertaintyChecks()
        {
            // \times 10^3 written out is the same number as e3, so it gets the same brackets.
            string explicitPower = Shape("\\num{1.2+-0.1\\times10^{3}}");
            Check("[F17] \\num{1.2+-0.1\\times10^{3}} 是 (1.2 ± 0.1) × 10³，与 \\num{1.2+-0.1e3} 相同（" + explicitPower + "）",
                explicitPower == "(1.2±0.1)×10^(3)" && explicitPower == Shape("\\num{1.2+-0.1e3}") &&
                Spaced("\\num{1.2+-0.1\\times10^{3}}", false) == Spaced("\\num{1.2+-0.1e3}", false) &&
                Shape("\\num{1.2 +- 0.1 \\times 10^3}") == explicitPower && Shape("\\num{1.2+-0.1×10^{3}}") == explicitPower);
            string product = Shape("\\num{2+-0.1x3}");
            Check("[F17] 乘积 \\num{2+-0.1x3} 是 (2 ± 0.1) × 3（" + product + "）",
                product == "(2±0.1)×3" && product == Shape("(2\\pm0.1)\\times3") && Shape("\\num{3x2+-0.1}") == "3×(2±0.1)");
            string quotient = Shape("\\num{1.2+-0.1/3}");
            Check("[F17] 商 \\num{1.2+-0.1/3} 是 (1.2 ± 0.1)/3，除数带 ± 也加括号（" + quotient + "）",
                quotient == "(1.2±0.1)/3" && Shape("\\num{3/1.2+-0.1}") == "3/(1.2±0.1)" && Shape("\\num{1/3}") == "1/3");
            string range = Shape("\\numrange{1+-0.1}{2+-0.1}");
            string angle = Shape("\\ang{1.2+-0.1}");
            Check("[F17] 无单位的 \\numrange 两端与 \\ang 的度数带 ± 时加括号（" + range + "，" + angle + "）",
                range == "(1±0.1)–(2±0.1)" && angle == "(1.2±0.1)°" && Shape("\\numrange{10}{20}") == "10–20" && Shape("\\ang{12;30;15}") == "12°30′15″");
            string quantity = Shape("\\SI{1.2+-0.1\\times10^{3}}{\\metre}");
            Check("[F17] 带单位的 \\SI{1.2+-0.1\\times10^{3}}{\\metre} 同样加括号（" + quantity + "）",
                quantity == "(1.2±0.1)×10^(3)m" && quantity == Shape("\\SI{1.2+-0.1e3}{\\metre}"));
            Check("[F17] 不需要括号的写法不变：2.5+-0.1、1.23(4)e5、1.2\\times10^{3}、2x3",
                Shape("\\num{2.5+-0.1}") == "2.5±0.1" && Shape("\\num{1.23(4)e5}") == "1.23(4)×10^(5)" &&
                Shape("\\num{1.2\\times10^{3}}") == "1.2×10^(3)" && Shape("\\num{2x3}") == "2×3");
        }

        // ------------------------------------------------------------------ F20: comments in [optional] arguments

        private static void BracketCommentChecks()
        {
            // The audit's input: % must be followed by a real line break.
            string call = Shape("\\newcommand{\\auditopt}[1][x]{#1}\\auditopt[a%]\n+b]");
            Check("[F20] 可选参数里 % 之后是注释，注释中的 ] 不结束参数（" + call + "）", call == "a+b" && call == Shape("a+b"));
            string preset = Shape("\\newcommand{\\auditopt}[1][a%]\n+b]{#1}\\auditopt");
            Check("[F20] 可选参数默认值里的注释同样跳过（" + preset + "）", preset == "a+b");
            Check("[F20] 注释中的 { 不计入层级；Windows 换行（CRLF）的注释也跳过",
                Shape("\\newcommand{\\auditopt}[1][x]{#1}\\auditopt[a%{\n+b]") == "a+b" &&
                Shape("\\newcommand{\\auditopt}[1][x]{#1}\\auditopt[a%]\r\n+b]") == "a+b" &&
                Shape("\\newcommand{\\auditopt}[1][a%]\r\n+b]{#1}\\auditopt") == "a+b");
            Check("[F20] 必选参数里的注释仍然跳过；\\% 仍是百分号，不是注释",
                Shape("\\newcommand{\\auditopt}[1]{#1}\\auditopt{a%}\n+b}") == "a+b" &&
                Shape("\\newcommand{\\pc}[1][50\\%]{#1}\\pc") == "50%" && Shape("\\newcommand{\\pc}[1][x]{#1}\\pc[50\\%]") == "50%");

            // Every [argument] is read the same way: \sqrt[...], the options of \num, the space after \\.
            string root = Shape("\\sqrt[3%]\n]{x}");
            Check("[F20] \\sqrt 的根指数、\\num 的选项与 \\\\[…] 里的注释也跳过（" + root + "）",
                root == "root(3|x)" && Shape("\\num[round-mode=places%]\n]{1.5}") == Shape("\\num{1.5}") &&
                Shape("\\begin{aligned}a\\\\[1em%]\n]b\\end{aligned}") == Shape("\\begin{aligned}a\\\\[1em]b\\end{aligned}"));
            string percent = Shape("\\ce{98% H2SO4}"), physical = Shape("\\pu{5 %}"), unit = Shape("\\SI{5}{%}");
            Check("[F20] \\ce、\\pu 与单位命令花括号里的 % 仍按百分号显示（" + percent + "，" + physical + "，" + unit + "）",
                percent.Contains("98%") && physical.Contains("5%") && unit.Contains("5%") && !percent.StartsWith("!", StringComparison.Ordinal) &&
                !physical.StartsWith("!", StringComparison.Ordinal) && !unit.StartsWith("!", StringComparison.Ordinal));
        }

        // ------------------------------------------------------------------ F18: the exact digits of unit powers

        private static void PowerPrecisionChecks()
        {
            string tiny = Shape("\\si{\\per\\raiseto{0.0000000000001}\\second}");
            Check("[F18] \\per\\raiseto{0.0000000000001} 是 s 的 -0.0000000000001 次方，不是 s⁰（" + tiny + "）",
                tiny == Shape("\\mathrm{s}^{-0.0000000000001}") && tiny.Contains("0.0000000000001") && !tiny.StartsWith("!", StringComparison.Ordinal));
            Check("[F18] \\per 只改正负号，写几位就显示几位（32 位小数）；负的小数幂取倒数变为正",
                Shape("\\si{\\per\\raiseto{0.12345678901234567890123456789012}\\second}") == Shape("\\mathrm{s}^{-0.12345678901234567890123456789012}") &&
                Shape("\\si{\\per\\raiseto{-0.5}\\second}") == Shape("\\mathrm{s}^{0.5}") && Shape("\\si{\\per\\raiseto{0}\\second}") == Shape("\\mathrm{s}^{0}"));
            Check("[F18] \\squared、\\tothe 与小数幂精确相乘，不截断",
                Shape("\\si{\\raiseto{0.0000000000001}\\second\\squared}") == Shape("\\mathrm{s}^{0.0000000000002}") &&
                Shape("\\si{\\per\\second\\tothe{0.0000000000001}}") == Shape("\\mathrm{s}^{-0.0000000000001}") &&
                Shape("\\si{\\raiseto{0.5}\\second\\squared}") == "s" && Shape("\\si{\\per\\raiseto{0.5}\\second}") == "s^(−0.5)");
            string tooLong = FallbackReason("\\si{\\raiseto{0.1234567890123456789012345678}\\second\\squared}");
            string unreadable = FallbackReason("\\si{\\raiseto{0.12345678901234567890123456789012}\\second\\squared}");
            Check("[F18] 位数超出可精确计算的范围时显示原文并说明原因，不显示成别的值（" + tooLong + "）",
                tooLong != null && tooLong.Contains("位数超出") && unreadable != null && unreadable.Contains("位数超出"));
            Check("[F18] 符号幂次：\\per 后的 n+1 是 -(n+1)，再取负回到 n+1；与数字相乘时整体加括号",
                Shape("\\si{\\per\\raiseto{n+1}\\second}") == Shape("\\mathrm{s}^{-(n+1)}") &&
                Shape("\\si{\\per\\raiseto{-(n+1)}\\second}") == Shape("\\mathrm{s}^{n+1}") &&
                Shape("\\si{\\raiseto{n+1}\\second\\squared}") == Shape("\\mathrm{s}^{(n+1)\\cdot 2}") &&
                Shape("\\si{\\per\\raiseto{n}\\second}") == Shape("\\mathrm{s}^{-n}") && Shape("\\si{\\per\\second\\tothe{n}}") == Shape("\\mathrm{s}^{-n}"));
        }

        // ------------------------------------------------------------------ the extended list of the v2.0.5 re-audit

        private static void ExtendedAuditChecks()
        {
            // { input, the correct form given by the audit }
            string[,] cases =
            {
                { "\\num{1.2+-0.1\\times10^{3}}", "(1.2\\pm0.1)\\times10^{3}" },
                { "\\si{\\per\\raiseto{0.0000000000001}\\second}", "\\mathrm{s}^{-0.0000000000001}" },
                { "\\newcommand{\\auditopt}[1][x]{#1}\\auditopt[a%]\n+b]", "a+b" },
                { "\\newcommand{\\auditb}{ab}\\newcommand{\\audita}{\\auditb}\\frac\\audita2", "\\frac{ab}{2}" },
                { "\\newcommand{\\audita}{ab}\\let\\auditb\\audita\\frac\\auditb2", "\\frac{ab}{2}" },
                { "\\newcommand{\\auditnil}{}\\frac\\auditnil2", "\\frac{}{2}" },
                { "\\num{1 234.5 +- 6.7e-2}", "(1234.5\\pm6.7)\\times10^{-2}" },
                { "\\num{2+-0.1x3}", "(2\\pm0.1)\\times3" },
                { "\\num{-e10}", "-10^{10}" },
                { "\\num{\\leq 1\\,234e-5}", "\\le1234\\times10^{-5}" },
                { "\\si{\\per\\raiseto{-0.5}\\second}", "\\mathrm{s}^{0.5}" },
                { "\\SI{2}{\\mole\\of{NaCl}\\per\\second\\tothe{0.5}}", "2\\,\\mathrm{mol}_{\\mathrm{NaCl}}\\,\\mathrm{s}^{-0.5}" },
                { "\\newcommand{\\auditopt}[1][a%]\n+b]{#1}\\auditopt", "a+b" },
                { "\\newcommand{\\auditopt}[1][x]{#1}\\auditopt[a%{\n+b]", "a+b" },
                { "\\newcommand{\\auditopt}[1]{#1}\\auditopt{a%}\n+b}", "a+b" },
                { "\\ce{{Gluconic {Acid}} + H2O2}", "\\text{Gluconic {Acid}}+\\ce{H2O2}" },
                { "\\newcommand{\\auditf}[1]{\\text{\\##1}}\\auditf{x}", "\\text{\\#x}" },
                { "\\pu{1.2e-3 kg m2 // s2 mol}", "1.2\\times10^{-3}\\,\\frac{\\mathrm{kg}\\,\\mathrm{m}^{2}}{\\mathrm{s}^{2}\\,\\mathrm{mol}}" }
            };
            List<string> different = new List<string>();
            for (int k = 0; k < cases.GetLength(0); k++)
            {
                string actual = Shape(cases[k, 0]), expected = Shape(cases[k, 1]);
                if (actual != expected || expected.StartsWith("!", StringComparison.Ordinal))
                    different.Add(cases[k, 0].Replace("\n", "⏎") + " → " + actual + "，应为 " + expected);
            }
            Check("[复审] 剩余问题与扩展清单的 " + cases.GetLength(0) + " 个样例都与报告给的正确写法排版相同" +
                (different.Count == 0 ? "" : "（不同：" + String.Join("；", different.ToArray()) + "）"), different.Count == 0);
        }
    }
}

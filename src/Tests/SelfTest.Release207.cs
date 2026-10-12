using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using CodexMarkdownReader.Markdown;
using CodexMarkdownReader.Rendering;

namespace CodexMarkdownReader.Tests
{
    // Regression checks for the v2.0.6 boundary re-audit: a power of ten in a divisor (F17), comments between a command
    // name and letters (F20), comments in the other arguments and in \text, and every case of the audit's lists with the
    // correct form it gave, including its 120 independently computed products of unit powers (F18).
    internal static partial class SelfTest
    {
        // ------------------------------------------------------------------ F17: a power of ten in a divisor

        private static void DivisorChecks()
        {
            // 1/2e3 is 1 divided by 2 × 10³; written 1/2 × 10³ it would read as half of 10³.
            string divisor = Shape("\\num{1/2e3}");
            Check("[F17] \\num{1/2e3} 是 1/(2 × 10³)，不是 1/2 × 10³（" + divisor + "）",
                divisor == "1/(2×10^(3))" && divisor == Shape("1/(2\\times10^3)"));
            string uncertain = Shape("\\num{1/1.2+-0.1e3}");
            Check("[F17] 除数带不确定度和指数：\\num{1/1.2+-0.1e3} 是 1/((1.2 ± 0.1) × 10³)（" + uncertain + "）",
                uncertain == "1/((1.2±0.1)×10^(3))" && uncertain == Shape("1/((1.2\\pm0.1)\\times10^3)"));
            Check("[F17] 分子与分母都带指数、乘积中的商与带单位的商，除数都整体加括号",
                Shape("\\num{1e3/2e3}") == "1×10^(3)/(2×10^(3))" && Shape("\\num{3x1/2e3}") == "3×1/(2×10^(3))" &&
                Shape("\\SI{1/2e3}{\\metre}") == "1/(2×10^(3))m");
            Check("[F17] 不需要括号的写法不变：1/10³、(1.2 ± 0.1) × 10³/2、2 × 10³/4、1/(1.2 ± 0.1)、1/3",
                Shape("\\num{1/e3}") == "1/10^(3)" && Shape("\\num{1.2+-0.1e3/2}") == "(1.2±0.1)×10^(3)/2" &&
                Shape("\\num{2e3/4}") == "2×10^(3)/4" && Shape("\\num{1/1.2+-0.1}") == "1/(1.2±0.1)" && Shape("\\num{1/3}") == "1/3");
        }

        // ------------------------------------------------------------------ F20: a comment between a command name and letters

        private static void CommentBoundaryChecks()
        {
            // TeX has ended \alpha at the %, so the x on the next line is a letter of its own: αx, never \alphax.
            string optional = Shape("\\newcommand{\\f}[1][x]{#1}\\f[\\alpha%\nx]");
            Check("[F20] 可选参数里命令名后跟注释再换行，结果是 αx，不拼成 \\alphax（" + optional + "）",
                optional == "αx" && optional == Shape("\\alpha x"));
            string collision = Shape("\\newcommand{\\alphax}{z}\\newcommand{\\f}[1][x]{#1}\\f[\\alpha%\nx]");
            Check("[F20] 即使定义了 \\alphax，结果仍是 αx，不会变成 z（" + collision + "）", collision == "αx");
            Check("[F20] 可选参数的默认值、宏体、必选参数与 \\def 里的同样写法都是 αx",
                Shape("\\newcommand{\\auditopt}[1][\\alpha% comment\nx]{#1}\\auditopt") == "αx" &&
                Shape("\\newcommand{\\auditopt}{\\alpha% comment\nx}\\auditopt") == "αx" &&
                Shape("\\newcommand{\\auditopt}[1]{#1}\\auditopt{\\alpha%\nx}") == "αx" &&
                Shape("\\def\\auditdef{\\alpha%\nx}\\auditdef") == "αx");
            Check("[F20] 普通字母仍连在一起（a% ⏎ b 是 ab）；后面是数字、行首空格或 CRLF 时同样正确",
                Shape("\\newcommand{\\f}[1][x]{#1}\\f[a% comment\nb]") == "ab" &&
                Shape("\\newcommand{\\f}[1][x]{#1}\\f[\\alpha%\n2]") == "α2" &&
                Shape("\\newcommand{\\f}[1][x]{#1}\\f[\\alpha%\n   x]") == "αx" &&
                Shape("\\newcommand{\\f}[1][x]{#1}\\f[\\alpha%\r\nx]") == "αx");
            Check("[F20] 命令取后面的字母作参数时也一样：\\mathrm% ⏎ d 是正体 d",
                Shape("\\newcommand{\\g}{\\mathrm%\nd x}\\g") == Shape("\\mathrm{d}x") &&
                Shape("\\sqrt[\\alpha%\nn]{x}") == Shape("\\sqrt[\\alpha n]{x}"));
        }

        // ------------------------------------------------------------------ comments in \text and in the other arguments

        private static void CommentArgumentChecks()
        {
            // The comment takes its line break with it: no space is left behind.
            string text = Shape("\\text{a%\nb}");
            Check("[注释] \\text{a% ⏎ b} 是 ab，不再多出空格（" + text + "）", text == Shape("\\text{ab}") &&
                Shape("\\text{a%\n   b}") == Shape("\\text{ab}") && Shape("\\text{a %\nb}") == Shape("\\text{a b}"));

            LatexMathLayout tagged = new LatexMathLayout("x\\tag{A%\n}", Brushes.Black, true, new MathContext());
            tagged.Build(20);
            MathContext labels = MathContext.ForDocument(MarkdownParser.Parse("$$\nx=1\\tag{1}\\label{eq:a%\n}\n$$\n\n见 $\\eqref{eq:a}$"));
            using (MathContext.Use(labels))
            {
                Check("[注释] \\tag、\\label 与 \\eqref 参数里的注释不算内容（编号 " + tagged.EquationTag + "，引用 " + Shape("\\eqref{eq:a}") + "）",
                    tagged.EquationTag == "(A)" && LabelOf(labels, "eq:a") == "1" && Shape("\\eqref{eq:a}") == "(1)" &&
                    Shape("\\eqref{eq:a%\n}") == "(1)");
            }

            MathGlyph colored = BoxOf<MathGlyph>("\\textcolor{red%\n}{x}", true);
            SolidColorBrush ink = colored == null ? null : colored.Ink as SolidColorBrush;
            Check("[注释] 颜色、长度、环境名、字符编码与 \\genfrac 参数里的注释同样跳过",
                ink != null && ink.Color.R > 150 && ink.Color.G < 120 &&
                FallbackReason("a\\hspace{1em%\n}b") == null && Spaced("a\\hspace{1em%\n}b", false) == Spaced("a\\hspace{1em}b", false) &&
                FallbackReason("\\rule{1em%\n}{2pt}") == null &&
                Shape("\\begin{matrix%\n}a&b\\end{matrix%\n}") == Shape("\\begin{matrix}a&b\\end{matrix}") &&
                !Shape("\\begin{matrix}a&b\\end{matrix}").StartsWith("!", StringComparison.Ordinal) &&
                Shape("\\unicode{x2192%\n}") == "→" &&
                Shape("\\genfrac{(}{)}{0pt%\n}{}{a}{b}") == Shape("\\genfrac{(}{)}{0pt}{}{a}{b}"));

            // % that is text stays text: concentrations in \ce and \pu, and URLs.
            string url = Shape("\\url{a%20b}");
            Check("[注释] \\ce、\\pu 与网址里的 % 不是注释（" + url + "）",
                Shape("\\ce{98% H2SO4}").Contains("98%") && Shape("\\pu{5 %}").Contains("5%") && url.Contains("a%20b") &&
                Shape("\\href{https://example.com/a%20b}{x}") == "x");
        }

        // ------------------------------------------------------------------ the lists of the v2.0.6 re-audit

        // { input, the correct form given by the audit }: its 3 residual cases, its 2 findings and every variant it tried.
        private static readonly string[,] Audit206Cases =
        {
            { "\\num{1.2+-0.1\\times10^{3}}", "\\num{1.2+-0.1e3}" },
            { "\\si{\\per\\raiseto{0.0000000000001}\\second}", "\\mathrm{s}^{-0.0000000000001}" },
            { "\\newcommand{\\auditopt}[1][x]{#1}\\auditopt[a%]\n+b]", "a+b" },
            { "\\num{1/2e3}", "1/(2\\times10^3)" },
            { "\\num{1/1.2+-0.1e3}", "1/((1.2\\pm0.1)\\times10^3)" },
            { "\\newcommand{\\auditopt}[1][x]{#1}\\auditopt[\\alpha% comment\nx]", "\\alpha x" },
            { "\\newcommand{\\alphax}{z}\\newcommand{\\auditopt}[1][x]{#1}\\auditopt[\\alpha% comment\nx]", "\\alpha x" },
            { "\\num{1.2+-0.1e3/2}", "(1.2\\pm0.1)\\times10^3/2" },
            { "\\num{1/e3}", "1/10^3" },
            { "\\num{2+-0.1x3+-0.2}", "(2\\pm0.1)\\times(3\\pm0.2)" },
            { "\\si{\\raiseto{0.0000000000000000000000000001}\\second\\squared}", "\\mathrm{s}^{0.0000000000000000000000000002}" },
            { "\\si{\\per\\raiseto{0.12345678901234567890}\\second\\squared}", "\\mathrm{s}^{-0.2469135780246913578}" },
            { "\\si{\\per\\raiseto{n+1}\\second}", "\\mathrm{s}^{-(n+1)}" },
            { "\\si{\\raiseto{n+1}\\second\\tothe{m+2}}", "\\mathrm{s}^{(n+1)\\cdot(m+2)}" },
            { "\\newcommand{\\auditopt}[1][\\alpha% comment\nx]{#1}\\auditopt", "\\alpha x" },
            { "\\newcommand{\\auditopt}{\\alpha% comment\nx}\\auditopt", "\\alpha x" },
            { "\\sqrt[\\alpha% comment\nn]{x}", "\\sqrt[\\alpha n]{x}" },
            { "\\newcommand{\\auditopt}[1][x]{#1}\\auditopt[a% comment\nb]", "ab" },
            { "\\newcommand{\\auditopt}[1][x]{#1}\\auditopt[50\\%]", "50\\%" },
            { "\\newcommand{\\f}[1][x]{#1}\\f[\\alpha%\nx]", "\\alpha x" },
            { "\\newcommand{\\alphax}{z}\\newcommand{\\f}[1][x]{#1}\\f[\\alpha%\nx]", "\\alpha x" }
        };

        // Products of two unit powers with the results the audit computed independently, from integer products and a
        // shift of the decimal point.
        private static readonly string[,] ExactPowerCases =
        {
            { "\\si{\\raiseto{-0.004546553}\\second\\tothe{-0.0081578326}}", "\\mathrm{s}^{0.0000370900182810278}" },
            { "\\si{\\raiseto{0.28203288}\\second\\tothe{55.133345}}", "\\mathrm{s}^{15.5494160743836}" },
            { "\\si{\\raiseto{45798684}\\second\\tothe{34.220718}}", "\\mathrm{s}^{1567263849.935112}" },
            { "\\si{\\raiseto{-0.082691708}\\second\\tothe{30.226333}}", "\\mathrm{s}^{-2.499467102346764}" },
            { "\\si{\\raiseto{52988443}\\second\\tothe{-0.0042839848}}", "\\mathrm{s}^{-227001.6843876664}" },
            { "\\si{\\raiseto{457759.53}\\second\\tothe{0.04804083}}", "\\mathrm{s}^{21991.1477616099}" },
            { "\\si{\\raiseto{-0.000055347668}\\second\\tothe{78.586387}}", "\\mathrm{s}^{-0.004349573256995516}" },
            { "\\si{\\raiseto{5.6991546}\\second\\tothe{0.000565146}}", "\\mathrm{s}^{0.0032208544255716}" },
            { "\\si{\\raiseto{0.21537153}\\second\\tothe{-0.00081936723}}", "\\mathrm{s}^{-0.0001764683739569619}" },
            { "\\si{\\raiseto{-62.24932}\\second\\tothe{9412.8888}}", "\\mathrm{s}^{-585945.927035616}" },
            { "\\si{\\raiseto{0.029362379}\\second\\tothe{70.133524}}", "\\mathrm{s}^{2.059287112293596}" },
            { "\\si{\\raiseto{5652.149}\\second\\tothe{39042382}}", "\\mathrm{s}^{220673360378.918}" },
            { "\\si{\\raiseto{-9561.921}\\second\\tothe{-7489.4627}}", "\\mathrm{s}^{71613650.6698467}" },
            { "\\si{\\raiseto{0.088060516}\\second\\tothe{8.9462121}}", "\\mathrm{s}^{0.7878080537714436}" },
            { "\\si{\\raiseto{63848.281}\\second\\tothe{97.254355}}", "\\mathrm{s}^{6209523.386513755}" },
            { "\\si{\\raiseto{-0.000056953147}\\second\\tothe{180.13749}}", "\\mathrm{s}^{-0.01025939694818103}" },
            { "\\si{\\raiseto{7686.1535}\\second\\tothe{-20284843}}", "\\mathrm{s}^{-155912417021.4005}" },
            { "\\si{\\raiseto{675.42182}\\second\\tothe{8.0514654}}", "\\mathrm{s}^{5438.135414135028}" },
            { "\\si{\\raiseto{-0.1712785}\\second\\tothe{0.069294406}}", "\\mathrm{s}^{-0.011868641918071}" },
            { "\\si{\\raiseto{0.72195369}\\second\\tothe{653537.32}}", "\\mathrm{s}^{471823.6797267108}" },
            { "\\si{\\raiseto{5.360556}\\second\\tothe{-0.0094298824}}", "\\mathrm{s}^{-0.0505494126786144}" },
            { "\\si{\\raiseto{-0.000069380233}\\second\\tothe{2611.4014}}", "\\mathrm{s}^{-0.1811796375885262}" },
            { "\\si{\\raiseto{8642.388}\\second\\tothe{617842.7}}", "\\mathrm{s}^{5339636336.3676}" },
            { "\\si{\\raiseto{0.0070879554}\\second\\tothe{9151016.2}}", "\\mathrm{s}^{64861.99469027748}" },
            { "\\si{\\raiseto{-0.075634286}\\second\\tothe{-0.00035698235}}", "\\mathrm{s}^{0.0000270001051568521}" },
            { "\\si{\\raiseto{791.0647}\\second\\tothe{2422584.3}}", "\\mathrm{s}^{1916420922.50421}" },
            { "\\si{\\raiseto{34383.791}\\second\\tothe{774116.71}}", "\\mathrm{s}^{26617067166.24761}" },
            { "\\si{\\raiseto{-0.0035176597}\\second\\tothe{0.000097651907}}", "\\mathrm{s}^{-0.0000003435061778820479}" },
            { "\\si{\\raiseto{0.000029634374}\\second\\tothe{-0.06944049}}", "\\mathrm{s}^{-0.00000205782545140326}" },
            { "\\si{\\raiseto{0.062808657}\\second\\tothe{12.40232}}", "\\mathrm{s}^{0.77897306288424}" },
            { "\\si{\\raiseto{-0.00027227254}\\second\\tothe{9.9056703}}", "\\mathrm{s}^{-0.002697042012983562}" },
            { "\\si{\\raiseto{0.00055167924}\\second\\tothe{3154026.2}}", "\\mathrm{s}^{1740.010776956088}" },
            { "\\si{\\raiseto{91420126}\\second\\tothe{-4293.6894}}", "\\mathrm{s}^{-392529625952.8644}" },
            { "\\si{\\raiseto{-8983895.6}\\second\\tothe{82252.675}}", "\\mathrm{s}^{-738949445020.73}" },
            { "\\si{\\raiseto{57.959635}\\second\\tothe{0.00040100534}}", "\\mathrm{s}^{0.0232421231394509}" },
            { "\\si{\\raiseto{0.000096581629}\\second\\tothe{7429147.7}}", "\\mathrm{s}^{717.5191869476033}" },
            { "\\si{\\raiseto{-0.0021868074}\\second\\tothe{-0.26852182}}", "\\mathrm{s}^{0.000587205503037468}" },
            { "\\si{\\raiseto{95.498822}\\second\\tothe{0.15219957}}", "\\mathrm{s}^{14.53487964390654}" },
            { "\\si{\\raiseto{6.4931243}\\second\\tothe{2476725.3}}", "\\mathrm{s}^{16081685.22985479}" },
            { "\\si{\\raiseto{-3.2113247}\\second\\tothe{74975433}}", "\\mathrm{s}^{-240770459.8860951}" },
            { "\\si{\\raiseto{89431.671}\\second\\tothe{-0.00019329763}}", "\\mathrm{s}^{-17.28693005123973}" },
            { "\\si{\\raiseto{9764376.6}\\second\\tothe{47.813217}}", "\\mathrm{s}^{466866257.2455222}" },
            { "\\si{\\raiseto{-98549409}\\second\\tothe{666316.22}}", "\\mathrm{s}^{-65665069688113.98}" },
            { "\\si{\\raiseto{6.468919}\\second\\tothe{78.798428}}", "\\mathrm{s}^{509.740648059332}" },
            { "\\si{\\raiseto{21.58272}\\second\\tothe{-81122.116}}", "\\mathrm{s}^{-1750835.91543552}" },
            { "\\si{\\raiseto{-5220406.1}\\second\\tothe{0.024044493}}", "\\mathrm{s}^{-125522.0179286073}" },
            { "\\si{\\raiseto{0.09439692}\\second\\tothe{0.36776174}}", "\\mathrm{s}^{0.0347155755498408}" },
            { "\\si{\\raiseto{0.000024112624}\\second\\tothe{0.0099540695}}", "\\mathrm{s}^{0.000000240018735123368}" },
            { "\\si{\\raiseto{-80163900}\\second\\tothe{-0.0044869599}}", "\\mathrm{s}^{359692.20472761}" },
            { "\\si{\\raiseto{4694549.6}\\second\\tothe{0.004658731}}", "\\mathrm{s}^{21870.6437525576}" },
            { "\\si{\\raiseto{0.18196592}\\second\\tothe{31954.502}}", "\\mathrm{s}^{5814.63035457184}" },
            { "\\si{\\raiseto{-0.000010525271}\\second\\tothe{0.00025831849}}", "\\mathrm{s}^{-0.00000000271887211156079}" },
            { "\\si{\\raiseto{70601359}\\second\\tothe{-5.204276}}", "\\mathrm{s}^{-367428958.211084}" },
            { "\\si{\\raiseto{35.479753}\\second\\tothe{0.3162089}}", "\\mathrm{s}^{11.2190136684017}" },
            { "\\si{\\raiseto{-0.000004625965}\\second\\tothe{5.1145811}}", "\\mathrm{s}^{-0.0000236598731582615}" },
            { "\\si{\\raiseto{76247.325}\\second\\tothe{0.000043583886}}", "\\mathrm{s}^{3.32315472060495}" },
            { "\\si{\\raiseto{693.65152}\\second\\tothe{-217104.85}}", "\\mathrm{s}^{-150595109.201872}" },
            { "\\si{\\raiseto{-76261539}\\second\\tothe{0.000076914024}}", "\\mathrm{s}^{-5865.581840922936}" },
            { "\\si{\\raiseto{6777540.4}\\second\\tothe{654408.24}}", "\\mathrm{s}^{4435278284692.896}" },
            { "\\si{\\raiseto{0.58658397}\\second\\tothe{0.85836596}}", "\\mathrm{s}^{0.5035037125296612}" },
            { "\\si{\\raiseto{-0.000038233397}\\second\\tothe{-854330.76}}", "\\mathrm{s}^{32.66396711639172}" },
            { "\\si{\\raiseto{92.845025}\\second\\tothe{46.27534}}", "\\mathrm{s}^{4296.4350991835}" },
            { "\\si{\\raiseto{0.09423013}\\second\\tothe{0.000037149711}}", "\\mathrm{s}^{0.00000350062209699243}" },
            { "\\si{\\raiseto{-0.00000681595}\\second\\tothe{0.052479118}}", "\\mathrm{s}^{-0.0000003576950443321}" },
            { "\\si{\\raiseto{0.8685428}\\second\\tothe{-0.00066377382}}", "\\mathrm{s}^{-0.000576515972189496}" },
            { "\\si{\\raiseto{8328762.5}\\second\\tothe{0.000078723183}}", "\\mathrm{s}^{655.6666944510375}" },
            { "\\si{\\raiseto{-0.0099213994}\\second\\tothe{0.009196129}}", "\\mathrm{s}^{-0.0000912384687429226}" },
            { "\\si{\\raiseto{21501740}\\second\\tothe{303899.07}}", "\\mathrm{s}^{6534358789381.8}" },
            { "\\si{\\raiseto{854709.1}\\second\\tothe{-0.00029277708}}", "\\mathrm{s}^{-250.239234547428}" },
            { "\\si{\\raiseto{-49113541}\\second\\tothe{4360.725}}", "\\mathrm{s}^{-214170646077.225}" },
            { "\\si{\\raiseto{74206.036}\\second\\tothe{940.0121}}", "\\mathrm{s}^{69754571.7330356}" },
            { "\\si{\\raiseto{0.00004288124}\\second\\tothe{1019059.6}}", "\\mathrm{s}^{43.698539281904}" },
            { "\\si{\\raiseto{-6763329.3}\\second\\tothe{-0.00035506445}}", "\\mathrm{s}^{2401.417798073385}" },
            { "\\si{\\raiseto{0.0002633455}\\second\\tothe{7.6860113}}", "\\mathrm{s}^{0.00202407648880415}" },
            { "\\si{\\raiseto{755618.01}\\second\\tothe{0.000078301}}", "\\mathrm{s}^{59.16564580101}" },
            { "\\si{\\raiseto{-63059.615}\\second\\tothe{0.77465518}}", "\\mathrm{s}^{-48849.4574085557}" },
            { "\\si{\\raiseto{3042093}\\second\\tothe{-0.0053836534}}", "\\mathrm{s}^{-16377.5743225662}" },
            { "\\si{\\raiseto{29844.922}\\second\\tothe{6993282}}", "\\mathrm{s}^{208713955814.004}" },
            { "\\si{\\raiseto{-6021369.5}\\second\\tothe{0.00036412235}}", "\\mathrm{s}^{-2192.515212558325}" },
            { "\\si{\\raiseto{0.00053133723}\\second\\tothe{0.00070759445}}", "\\mathrm{s}^{0.0000003759712750263735}" },
            { "\\si{\\raiseto{18512537}\\second\\tothe{-0.00067919437}}", "\\mathrm{s}^{-12573.61090481669}" },
            { "\\si{\\raiseto{-0.000018825775}\\second\\tothe{1444776.7}}", "\\mathrm{s}^{-27.1990410794425}" },
            { "\\si{\\raiseto{4400026.1}\\second\\tothe{63.123982}}", "\\mathrm{s}^{277747168.3359302}" },
            { "\\si{\\raiseto{8.466637}\\second\\tothe{0.00002251479}}", "\\mathrm{s}^{0.00019062455406123}" },
            { "\\si{\\raiseto{-6695993.9}\\second\\tothe{-61.749235}}", "\\mathrm{s}^{413472500.8896665}" },
            { "\\si{\\raiseto{0.00099861821}\\second\\tothe{1.3145351}}", "\\mathrm{s}^{0.001312718688544171}" },
            { "\\si{\\raiseto{1000.7282}\\second\\tothe{0.0000723943}}", "\\mathrm{s}^{0.07244701752926}" },
            { "\\si{\\raiseto{-62251365}\\second\\tothe{95991030}}", "\\mathrm{s}^{-5975572645255950}" },
            { "\\si{\\raiseto{0.11383798}\\second\\tothe{-0.000030670954}}", "\\mathrm{s}^{-0.00000349151944803292}" },
            { "\\si{\\raiseto{750.59883}\\second\\tothe{410459.76}}", "\\mathrm{s}^{308090615.6180808}" },
            { "\\si{\\raiseto{-6138.1716}\\second\\tothe{0.01717976}}", "\\mathrm{s}^{-105.452314926816}" },
            { "\\si{\\raiseto{917408.34}\\second\\tothe{7716.863}}", "\\mathrm{s}^{7079514474.83742}" },
            { "\\si{\\raiseto{763820.37}\\second\\tothe{-0.00017868231}}", "\\mathrm{s}^{-136.4811881366547}" },
            { "\\si{\\raiseto{-0.00062068547}\\second\\tothe{38947623}}", "\\mathrm{s}^{-24174.22368713781}" },
            { "\\si{\\raiseto{0.082468073}\\second\\tothe{4176562.3}}", "\\mathrm{s}^{344433.0446454479}" },
            { "\\si{\\raiseto{82.380446}\\second\\tothe{2.532503}}", "\\mathrm{s}^{208.628726636338}" },
            { "\\si{\\raiseto{-5674.7492}\\second\\tothe{-1.0450091}}", "\\mathrm{s}^{5930.16455421772}" },
            { "\\si{\\raiseto{573784.29}\\second\\tothe{64890.018}}", "\\mathrm{s}^{37232872906.21722}" },
            { "\\si{\\raiseto{0.00016734786}\\second\\tothe{0.000092717734}}", "\\mathrm{s}^{0.00000001551611436894924}" },
            { "\\si{\\raiseto{-0.7960737}\\second\\tothe{1873.0424}}", "\\mathrm{s}^{-1491.07979362488}" },
            { "\\si{\\raiseto{8758109.7}\\second\\tothe{-943.68412}}", "\\mathrm{s}^{-8264889045.107964}" },
            { "\\si{\\raiseto{736.64556}\\second\\tothe{0.014992002}}", "\\mathrm{s}^{11.04379170881112}" },
            { "\\si{\\raiseto{-4150937.8}\\second\\tothe{5976.9174}}", "\\mathrm{s}^{-24809812363.13772}" },
            { "\\si{\\raiseto{6505.7962}\\second\\tothe{3.3428104}}", "\\mathrm{s}^{21747.64319764048}" },
            { "\\si{\\raiseto{5535854.3}\\second\\tothe{-0.0085528749}}", "\\mathrm{s}^{-47347.46929252707}" },
            { "\\si{\\raiseto{-0.0040221896}\\second\\tothe{6566291.4}}", "\\mathrm{s}^{-26410.86897964944}" },
            { "\\si{\\raiseto{0.096187596}\\second\\tothe{0.09641251}}", "\\mathrm{s}^{0.00927368756122596}" },
            { "\\si{\\raiseto{49877.751}\\second\\tothe{0.91017728}}", "\\mathrm{s}^{45397.59573769728}" },
            { "\\si{\\raiseto{-72866.445}\\second\\tothe{-0.00043149363}}", "\\mathrm{s}^{31.44140685824535}" },
            { "\\si{\\raiseto{0.088992225}\\second\\tothe{19.76298}}", "\\mathrm{s}^{1.7587515628305}" },
            { "\\si{\\raiseto{6771628.1}\\second\\tothe{0.000064510559}}", "\\mathrm{s}^{436.8415140711079}" },
            { "\\si{\\raiseto{-5.2217915}\\second\\tothe{0.0088386371}}", "\\mathrm{s}^{-0.04615352008036465}" },
            { "\\si{\\raiseto{591.25019}\\second\\tothe{-0.000030220658}}", "\\mathrm{s}^{-0.01786796978442502}" },
            { "\\si{\\raiseto{0.000008756977}\\second\\tothe{876084.31}}", "\\mathrm{s}^{7.67185015273087}" },
            { "\\si{\\raiseto{-14.047936}\\second\\tothe{0.0002048258}}", "\\mathrm{s}^{-0.0028773797295488}" },
            { "\\si{\\raiseto{403423}\\second\\tothe{73.382996}}", "\\mathrm{s}^{29604388.395308}" },
            { "\\si{\\raiseto{3.4009361}\\second\\tothe{-489344.53}}", "\\mathrm{s}^{-1664229.477414533}" },
            { "\\si{\\raiseto{-0.009422857}\\second\\tothe{45.580682}}", "\\mathrm{s}^{-0.429500248448474}" },
            { "\\si{\\raiseto{1118407.7}\\second\\tothe{0.068217956}}", "\\mathrm{s}^{76295.4872686612}" },
            { "\\si{\\raiseto{5.3176875}\\second\\tothe{8271122.2}}", "\\mathrm{s}^{43983243.1339125}" }
        };

        private static void ExtendedAudit206Checks()
        {
            List<string> different = new List<string>();
            for (int k = 0; k < Audit206Cases.GetLength(0); k++)
            {
                string actual = Shape(Audit206Cases[k, 0]), expected = Shape(Audit206Cases[k, 1]);
                if (actual != expected || expected.StartsWith("!", StringComparison.Ordinal))
                    different.Add(Audit206Cases[k, 0].Replace("\n", "⏎") + " → " + actual + "，应为 " + expected);
            }
            Check("[复审] 剩余问题与扩展清单的 " + Audit206Cases.GetLength(0).ToString(CultureInfo.InvariantCulture) + " 个样例都与报告给的正确写法排版相同" +
                (different.Count == 0 ? "" : "（不同：" + String.Join("；", different.ToArray()) + "）"), different.Count == 0);

            int exact = 0;
            string firstWrong = null;
            for (int k = 0; k < ExactPowerCases.GetLength(0); k++)
            {
                string actual = Shape(ExactPowerCases[k, 0]), expected = Shape(ExactPowerCases[k, 1]);
                if (actual == expected && !expected.StartsWith("!", StringComparison.Ordinal)) exact++;
                else if (firstWrong == null) firstWrong = ExactPowerCases[k, 0] + " → " + actual + "，应为 " + expected;
            }
            Check("[F18] " + ExactPowerCases.GetLength(0).ToString(CultureInfo.InvariantCulture) + " 组单位幂次相乘与独立算出的精确结果一致（" +
                exact.ToString(CultureInfo.InvariantCulture) + " 组相同" + (firstWrong == null ? "" : "；首个不同：" + firstWrong) + "）",
                exact == ExactPowerCases.GetLength(0));

            Check("[F18] 32 位小数取负保留全部位数，28 位小数乘方精确，零次幂乘负数仍是零次幂",
                Shape("\\si{\\per\\raiseto{0.12345678901234567890123456789012}\\second}") == Shape("\\mathrm{s}^{-0.12345678901234567890123456789012}") &&
                Shape("\\si{\\raiseto{0.0000000000000000000000000001}\\second\\squared}") == Shape("\\mathrm{s}^{0.0000000000000000000000000002}") &&
                Shape("\\si{\\raiseto{0}\\second\\tothe{-3}}") == Shape("\\mathrm{s}^{0}"));
            string overflow = FallbackReason("\\si{\\raiseto{79228162514264337593543950336}\\second\\squared}");
            string places = FallbackReason("\\si{\\raiseto{0.0000000000000000000000000001}\\second\\tothe{0.1}}");
            Check("[F18] 超出 decimal 范围或需要超过 28 位小数时显示原文并说明原因（" + overflow + "）",
                overflow != null && overflow.Contains("位数超出") && places != null && places.Contains("位数超出"));
        }
    }
}

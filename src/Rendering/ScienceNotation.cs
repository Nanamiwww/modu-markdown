using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexMarkdownReader.Rendering
{
    /// <summary>
    /// Chemistry and units for the formula engine, translated to plain TeX: \ce and \pu (the common part of mhchem) and
    /// the usual siunitx commands (\num, \si / \unit, \SI / \qty, \ang, \numrange, \SIrange / \qtyrange, \numlist, \SIlist).
    /// Element symbols are upright, stoichiometric numbers become subscripts, charges superscripts, and reaction arrows
    /// are drawn to the length of their conditions.
    /// </summary>
    internal static class ScienceNotation
    {
        // ------------------------------------------------------------------ \ce

        private static readonly string[] ArrowTokens = { "<=>>", "<<=>", "<-->", "<=>", "<->", "->", "<-" };

        private static readonly Dictionary<string, string> ArrowCommands = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "->", "\\xrightarrow" }, { "<-", "\\xleftarrow" }, { "<->", "\\xleftrightarrow" }, { "<-->", "\\xrightleftarrows" },
            { "<=>", "\\xrightleftharpoons" }, { "<=>>", "\\xrightequilibrium" }, { "<<=>", "\\xleftequilibrium" }
        };

        /// <summary>\ce{...}: 2H2 + O2 -> 2H2O, SO4^2-, [Cu(NH3)4]^2+, CuSO4*5H2O, ^{14}_{6}C, A <=>[cat.][\Delta] B, CO2 ^, AgCl v.</summary>
        public static string Chemistry(string input)
        {
            string text = input ?? "";
            StringBuilder tex = new StringBuilder();
            bool previousIsPart = false;
            int i = 0;
            while (i < text.Length)
            {
                if (Char.IsWhiteSpace(text[i]))
                {
                    i++;
                    continue;
                }
                string arrow = ArrowAt(text, i);
                if (arrow != null)
                {
                    i += arrow.Length;
                    string above = ReadBracket(text, ref i);
                    string below = above == null ? null : ReadBracket(text, ref i);
                    tex.Append(ArrowCommands[arrow]);
                    if (below != null) tex.Append('[').Append(Label(below)).Append(']');
                    tex.Append('{').Append(above == null ? "" : Label(above)).Append('}');
                    previousIsPart = false;
                    continue;
                }
                int end = WordEnd(text, i);
                string word = text.Substring(i, end - i);
                i = end;
                if (word == "+") { tex.Append('+'); previousIsPart = false; continue; }
                if (word == "=") { tex.Append('='); previousIsPart = false; continue; }
                if (word == "v" || word == "(v)") { tex.Append("\\downarrow "); previousIsPart = false; continue; }
                if (word == "^" || word == "(^)") { tex.Append("\\uparrow "); previousIsPart = false; continue; }
                if (previousIsPart) tex.Append("\\ ");
                tex.Append(Species(word));
                previousIsPart = true;
            }
            return tex.ToString();
        }

        private static string ArrowAt(string text, int index)
        {
            foreach (string token in ArrowTokens)
                if (String.CompareOrdinal(text, index, token, 0, token.Length) == 0) return token;
            return null;
        }

        /// <summary>An arrow label in [brackets] right after the arrow (brackets and braces nest), or null.</summary>
        private static string ReadBracket(string text, ref int index)
        {
            if (index >= text.Length || text[index] != '[') return null;
            int level = 0, start = index + 1;
            for (int i = index; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\') { i++; continue; }
                if (c == '[' || c == '{') level++;
                else if (c == ']' || c == '}')
                {
                    level--;
                    if (level == 0 && c == ']')
                    {
                        index = i + 1;
                        return text.Substring(start, i - start);
                    }
                }
            }
            index = text.Length;
            return text.Substring(Math.Min(start, text.Length));
        }

        /// <summary>Arrow conditions are chemistry too (H2O, \Delta), except {text} and $math$.</summary>
        private static string Label(string label)
        {
            string trimmed = label.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '{' && trimmed[trimmed.Length - 1] == '}') return "\\text{" + trimmed.Substring(1, trimmed.Length - 2) + "}";
            if (trimmed.Length >= 2 && trimmed[0] == '$' && trimmed[trimmed.Length - 1] == '$') return trimmed.Substring(1, trimmed.Length - 2);
            return Chemistry(trimmed);
        }

        /// <summary>End of a word: white space or an arrow at the top level of brackets, braces and $math$.</summary>
        private static int WordEnd(string text, int start)
        {
            int level = 0;
            bool math = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\') { i++; continue; }
                if (c == '$') { math = !math; continue; }
                if (math) continue;
                if (c == '{' || c == '(' || c == '[') level++;
                else if (c == '}' || c == ')' || c == ']') level = Math.Max(0, level - 1);
                else if (level == 0 && Char.IsWhiteSpace(c)) return i;
                else if (level == 0 && i > start && ArrowAt(text, i) != null) return i;
            }
            return text.Length;
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        /// <summary>Length of a leading stoichiometric coefficient: 2, 0.5, 1/2, or n / x / y / z before a formula.</summary>
        private static int CoefficientEnd(string word)
        {
            Match number = Regex.Match(word, @"^\d+(?:\.\d+)?(?:/\d+)?");
            if (number.Success && number.Length > 0)
            {
                if (number.Length == word.Length) return number.Length;
                char next = word[number.Length];
                if (IsAsciiLetter(next) || next == '(' || next == '[' || next == '\\' || next == '{' || next == '$' || next == '^') return number.Length;
                return 0;
            }
            if (word.Length >= 2 && "nxyz".IndexOf(word[0]) >= 0 && (Char.IsUpper(word[1]) || word[1] == '(' || word[1] == '[')) return 1;
            return 0;
        }

        private static string Coefficient(string coefficient)
        {
            int slash = coefficient.IndexOf('/');
            if (slash > 0) return "\\tfrac{" + coefficient.Substring(0, slash) + "}{" + coefficient.Substring(slash + 1) + "}";
            return coefficient;
        }

        /// <summary>One chemical formula: element symbols upright, numbers after them as subscripts, charges as superscripts.</summary>
        private static string Species(string word)
        {
            StringBuilder tex = new StringBuilder();
            int n = word.Length, i = CoefficientEnd(word);
            if (i > 0) tex.Append(Coefficient(word.Substring(0, i)));
            // Mass and atomic numbers before the symbol (^{14}_{6}C) get an empty base of their own, so that they do not
            // attach to whatever comes before: an arrow, a plus sign or a coefficient.
            if (i < n && (word[i] == '^' || word[i] == '_')) tex.Append("{}");
            bool afterAtom = false;   // after an element, ) or ]: digits are subscripts
            while (i < n)
            {
                char c = word[i];
                if (IsAsciiLetter(c))
                {
                    int start = i;
                    while (i < n && IsAsciiLetter(word[i])) i++;
                    tex.Append("\\mathrm{").Append(word, start, i - start).Append('}');
                    afterAtom = true;
                    continue;
                }
                if (Char.IsDigit(c))
                {
                    int start = i;
                    while (i < n && Char.IsDigit(word[i])) i++;
                    if (afterAtom) tex.Append("_{").Append(word, start, i - start).Append('}');
                    else tex.Append(word, start, i - start);
                    continue;
                }
                switch (c)
                {
                    case '(':
                    case '[':
                        tex.Append(c);
                        afterAtom = false;
                        i++;
                        continue;
                    case ')':
                    case ']':
                        tex.Append(c);
                        afterAtom = true;
                        i++;
                        continue;
                    case '{':
                        {
                            // mhchem: {Gluconic Acid} is upright text, spaces kept, not parsed as a formula.
                            int close = MatchBrace(word, i);
                            string inner = word.Substring(i + 1, Math.Max(0, close - i - 1));
                            if (close < i + 1 || word[close] != '}') inner = word.Substring(i + 1);
                            tex.Append("\\text{").Append(TextPercent(inner)).Append('}');
                            i = close + 1;
                            afterAtom = true;
                            continue;
                        }
                    case '^':
                        {
                            i++;
                            string content = ScriptArgument(word, ref i, true);
                            tex.Append("^{").Append(Charge(content)).Append('}');
                            afterAtom = true;
                            continue;
                        }
                    case '_':
                        {
                            i++;
                            string content = ScriptArgument(word, ref i, false);
                            tex.Append("_{").Append(content).Append('}');
                            afterAtom = true;
                            continue;
                        }
                    case '+':
                    case '-':
                        {
                            int j = i;
                            while (j < n && (word[j] == '+' || word[j] == '-')) j++;
                            bool atEnd = j >= n || word[j] == ')' || word[j] == ']' || word[j] == '(';
                            if (!atEnd)
                            {
                                // Inside a formula: a single bond (CH3-CH3) or a plus sign.
                                tex.Append(c == '-' ? "{-}" : "+");
                                afterAtom = false;
                                i++;
                                continue;
                            }
                            // A charge at the end of the formula: NH4+, OH-, SO4^2- is handled by ^.
                            tex.Append("^{").Append(word, i, j - i).Append('}');
                            i = j;
                            afterAtom = true;
                            continue;
                        }
                    case '=':
                        tex.Append("{=}");
                        afterAtom = false;
                        i++;
                        continue;
                    case '#':
                        tex.Append("{\\equiv}");
                        afterAtom = false;
                        i++;
                        continue;
                    case '.':
                    case '*':
                    case '·':
                    case '•':
                        {
                            // An addition compound or hydrate (CuSO4*5H2O); a following number is a coefficient.
                            bool joins = i + 1 < n && (Char.IsDigit(word[i + 1]) || IsAsciiLetter(word[i + 1]) || word[i + 1] == '(' || word[i + 1] == '[');
                            i++;
                            if (!joins)
                            {
                                tex.Append(c == '.' ? "." : "\\cdot ");
                                continue;
                            }
                            tex.Append("\\cdot ");
                            int start = i;
                            while (i < n && Char.IsDigit(word[i])) i++;
                            tex.Append(word, start, i - start);
                            afterAtom = false;
                            continue;
                        }
                    case '$':
                        {
                            int close = word.IndexOf('$', i + 1);
                            if (close < 0) close = n;
                            tex.Append('{').Append(word, i + 1, close - i - 1).Append('}');
                            i = Math.Min(n, close + 1);
                            afterAtom = true;
                            continue;
                        }
                    case '\\':
                        {
                            // A command (\alpha, \Delta, \text{...}): copied with its braced arguments.
                            int j = i + 1;
                            if (j < n && IsAsciiLetter(word[j])) { while (j < n && IsAsciiLetter(word[j])) j++; }
                            else j = Math.Min(n, j + 1);
                            while (j < n && word[j] == '{') j = MatchBrace(word, j) + 1;
                            tex.Append(word, i, j - i).Append(' ');
                            i = j;
                            afterAtom = true;
                            continue;
                        }
                    case ',':
                    case ':':
                    case ';':
                    case '/':
                    case '\'':
                        tex.Append(c);
                        afterAtom = false;
                        i++;
                        continue;
                    case '%':
                        tex.Append("\\%");
                        i++;
                        continue;
                    case '&':
                    case '~':
                    case '"':
                        i++;
                        continue;
                }
                // Other characters (μ, °, Chinese ...) are set upright.
                int length = Char.IsHighSurrogate(c) && i + 1 < n ? 2 : 1;
                tex.Append("\\mathrm{").Append(word, i, length).Append('}');
                i += length;
                afterAtom = true;
            }
            return tex.ToString();
        }

        /// <summary>% inside \ce is a percent sign (\ce{98% H2SO4}), also in its {text}: escaped so \text keeps it.</summary>
        private static string TextPercent(string text)
        {
            if (text.IndexOf('%') < 0) return text;
            StringBuilder escaped = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    escaped.Append(c).Append(text[i + 1]);
                    i++;
                    continue;
                }
                if (c == '%') escaped.Append('\\');
                escaped.Append(c);
            }
            return escaped.ToString();
        }

        private static int MatchBrace(string text, int open)
        {
            int level = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '\\') { i++; continue; }
                if (text[i] == '{') level++;
                else if (text[i] == '}')
                {
                    level--;
                    if (level == 0) return i;
                }
            }
            return text.Length - 1;
        }

        /// <summary>The argument of ^ or _ : {group}, a charge such as 2+ or 3-, roman numerals, or one character.</summary>
        private static string ScriptArgument(string word, ref int i, bool charge)
        {
            int n = word.Length;
            if (i >= n) return "";
            if (word[i] == '{')
            {
                int close = MatchBrace(word, i);
                string inner = word.Substring(i + 1, Math.Max(0, close - i - 1));
                i = close + 1;
                return inner;
            }
            int start = i;
            if (charge)
            {
                while (i < n && (Char.IsDigit(word[i]) || word[i] == '.')) i++;
                while (i < n && (word[i] == '+' || word[i] == '-')) i++;
                if (i > start) return word.Substring(start, i - start);
                while (i < n && "IVX".IndexOf(word[i]) >= 0) i++;
                if (i > start) return word.Substring(start, i - start);
            }
            else
            {
                while (i < n && Char.IsDigit(word[i])) i++;
                if (i > start) return word.Substring(start, i - start);
            }
            if (word[i] == '\\')
            {
                int j = i + 1;
                while (j < n && IsAsciiLetter(word[j])) j++;
                string command = word.Substring(i, Math.Max(1, j - i));
                i = Math.Max(i + 1, j);
                return command;
            }
            i++;
            return word.Substring(start, 1);
        }

        /// <summary>Charges and oxidation states: 2+ stays, a dot is the radical dot, letters (II, x) are upright.</summary>
        private static string Charge(string content)
        {
            StringBuilder tex = new StringBuilder();
            int n = content.Length;
            for (int i = 0; i < n; i++)
            {
                char c = content[i];
                if (c == '.' || c == '•' || c == '·')
                {
                    // The radical dot: the text bullet, which Cambria Math draws larger than the operator \bullet.
                    tex.Append("{•}");
                    continue;
                }
                if (IsAsciiLetter(c))
                {
                    int start = i;
                    while (i < n && IsAsciiLetter(content[i])) i++;
                    tex.Append("\\mathrm{").Append(content, start, i - start).Append('}');
                    i--;
                    continue;
                }
                if (c == '\\')
                {
                    int j = i + 1;
                    while (j < n && IsAsciiLetter(content[j])) j++;
                    if (j == i + 1 && j < n) j++;
                    tex.Append(content, i, j - i).Append(' ');
                    i = j - 1;
                    continue;
                }
                tex.Append(c);
            }
            return tex.ToString();
        }

        // ------------------------------------------------------------------ numbers

        private static readonly Regex NumberPattern = new Regex(
            @"^\s*(?<sign>[+\-−]?)\s*(?<int>\d*)(?:[.,](?<frac>\d*))?(?:\s*\((?<unc>\d+)\))?(?:\s*(?:\+-|±|\\pm)\s*(?<pm>\d+(?:[.,]\d*)?))?(?:\s*[eEdD]\s*(?<exp>[+\-−]?\d+))?",
            RegexOptions.CultureInvariant);

        /// <summary>Length of the number at the start of the text (0 if there is none). A number may be only an exponent: e10.</summary>
        private static int NumberLength(string text, out Match match)
        {
            match = NumberPattern.Match(text);
            if (!match.Success) return 0;
            if (match.Groups["int"].Length == 0 && match.Groups["frac"].Length == 0 && match.Groups["exp"].Length == 0) return 0;
            return match.Length;
        }

        /// <summary>
        /// A number in TeX. A value with a ± uncertainty is bracketed when a power of ten follows, or when
        /// <paramref name="bracket"/> says that something else applies to the whole value (a unit, another factor, a
        /// divisor, the other end of a range), as siunitx does when it prints the uncertainty separately:
        /// (1.23 ± 0.04) × 10⁵ and (2.5 ± 0.1) °C, not 1.23 ± 0.04 × 10⁵, which reads as if the power applied to the
        /// uncertainty alone. The compact form 1.23(4) × 10⁵ needs none.
        /// </summary>
        private static string FormatNumber(Match match, bool group, bool bracket)
        {
            StringBuilder value = new StringBuilder();
            string sign = match.Groups["sign"].Value;
            if (sign.Length > 0) value.Append(sign == "+" ? "+" : "-");
            string whole = match.Groups["int"].Value, fraction = match.Groups["frac"].Value;
            bool hasMantissa = whole.Length > 0 || fraction.Length > 0;
            string exponent = match.Groups["exp"].Value.Replace('−', '-');
            bool plusMinus = false;
            if (hasMantissa)
            {
                value.Append(group ? GroupDigits(whole.Length == 0 ? "0" : whole, true) : (whole.Length == 0 ? "0" : whole));
                if (match.Groups["frac"].Success && fraction.Length > 0) value.Append('.').Append(group ? GroupDigits(fraction, false) : fraction);
                if (match.Groups["unc"].Success && match.Groups["unc"].Length > 0) value.Append('(').Append(match.Groups["unc"].Value).Append(')');
                if (match.Groups["pm"].Success && match.Groups["pm"].Length > 0)
                {
                    value.Append("\\pm ").Append(match.Groups["pm"].Value.Replace(',', '.'));
                    plusMinus = true;
                }
            }
            StringBuilder tex = new StringBuilder();
            if (plusMinus && (exponent.Length > 0 || bracket)) tex.Append('(').Append(value).Append(')');
            else tex.Append(value);
            if (exponent.Length > 0)
            {
                if (hasMantissa) tex.Append("\\times ");
                tex.Append("10^{").Append(exponent.StartsWith("+", StringComparison.Ordinal) ? exponent.Substring(1) : exponent).Append('}');
            }
            return tex.ToString();
        }

        /// <summary>siunitx groups five or more digits in threes with thin spaces: 12345.678 9 → 12 345.678 9.</summary>
        private static string GroupDigits(string digits, bool fromRight)
        {
            if (digits.Length < 5) return digits;
            StringBuilder grouped = new StringBuilder();
            for (int i = 0; i < digits.Length; i++)
            {
                int position = fromRight ? digits.Length - i : i;
                if (i > 0 && position % 3 == 0) grouped.Append("\\,");
                grouped.Append(digits[i]);
            }
            return grouped.ToString();
        }

        /// <summary>\num{...}: 12345.6789, 1 234.5, 1.5e-3, e10, 1.2(3), 2.5+-0.1, 2x3, 1/3, &lt; 0.1, 1.2\times10^3.</summary>
        public static string Number(string input)
        {
            return Number(input, false);
        }

        /// <summary>
        /// A number. <paramref name="bracket"/>: something applies to the whole value (a unit, the other end of a
        /// range), so a value with ± is bracketed; so is every factor with ± of a product or quotient:
        /// (1.2 ± 0.1) × 10³, (2 ± 0.1) × 3, (1.2 ± 0.1)/3, and a divisor with a power of ten as a whole: 1/(2 × 10³).
        /// The whole input has to be understood: text that is not part of a number is not passed on as mathematics
        /// (1 234e5 would become 1234 times the variable e to the 5), the formula is shown as written instead.
        /// </summary>
        private static string Number(string input, bool bracket)
        {
            string text = NormalizeNumber(input);
            if (text.Length == 0) return "";
            StringBuilder tex = new StringBuilder();
            text = Comparator(text, tex);
            string[] factors = Regex.Split(text, @"x|\\times|×");
            bool product = factors.Length > 1;
            for (int k = 0; k < factors.Length; k++)
            {
                if (k > 0) tex.Append("\\times ");
                string factor = factors[k];
                Match power = PowerOfTen.Match(factor);
                if (power.Success)
                {
                    tex.Append("10^{").Append(power.Groups[1].Value.Replace('−', '-')).Append('}');
                    continue;
                }
                int slash = factor.IndexOf('/');
                if (slash >= 0)
                {
                    // The divisor is one number: 1/2e3 is 1/(2 × 10³), which 1/2 × 10³ would not say (that reads as
                    // half of 10³). A dividend needs no brackets: 2 × 10³/4 reads left to right as it should.
                    string divisor = SingleNumber(factor.Substring(slash + 1), true, input);
                    if (divisor.IndexOf("\\times", StringComparison.Ordinal) >= 0) divisor = "(" + divisor + ")";
                    tex.Append(SingleNumber(factor.Substring(0, slash), true, input)).Append('/').Append(divisor);
                    continue;
                }
                tex.Append(SingleNumber(factor, bracket || product, input));
            }
            return tex.ToString();
        }

        private static readonly Regex PowerOfTen = new Regex(@"^10\^\{?([+\-−]?\d+)\}?$", RegexOptions.CultureInvariant);

        private static string SingleNumber(string text, bool bracket, string input)
        {
            Match match;
            int length = NumberLength(text, out match);
            if (length == 0 || length < text.Length) throw new NotSupportedException("无法识别的数值 " + (input ?? "").Trim());
            return FormatNumber(match, true, bracket);
        }

        /// <summary>siunitx ignores the spaces of a number: plain spaces, \, and ~ (1 234 567, 1\,234).</summary>
        private static string NormalizeNumber(string input)
        {
            string text = (input ?? "").Replace("\\,", "").Replace("\\ ", "").Replace("~", "");
            StringBuilder plain = new StringBuilder(text.Length);
            foreach (char c in text) if (!Char.IsWhiteSpace(c)) plain.Append(c);
            return plain.ToString();
        }

        /// <summary>Comparators siunitx accepts in front of a number, longest first.</summary>
        private static readonly string[,] Comparators =
        {
            { "<=", "\\le " }, { ">=", "\\ge " }, { "\\leq", "\\le " }, { "\\geq", "\\ge " }, { "\\le", "\\le " }, { "\\ge", "\\ge " },
            { "\\approx", "\\approx " }, { "\\sim", "\\sim " }, { "\\ll", "\\ll " }, { "\\gg", "\\gg " }, { "\\neq", "\\ne " },
            { "\\ne", "\\ne " }, { "≈", "\\approx " }, { "≤", "\\le " }, { "≥", "\\ge " }, { "<", "<" }, { ">", ">" }, { "=", "=" }
        };

        /// <summary>Writes a leading comparator (&lt; 0.1, \approx 3) and returns the rest of the number.</summary>
        private static string Comparator(string text, StringBuilder tex)
        {
            for (int k = 0; k < Comparators.GetLength(0); k++)
            {
                string token = Comparators[k, 0];
                if (!text.StartsWith(token, StringComparison.Ordinal)) continue;
                // \le must not match the start of a longer command such as \left.
                if (token[0] == '\\' && text.Length > token.Length && Char.IsLetter(text[token.Length])) continue;
                tex.Append(Comparators[k, 1]);
                return text.Substring(token.Length);
            }
            return text;
        }

        // ------------------------------------------------------------------ units

        private static readonly Dictionary<string, string> Prefixes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "quecto", "q" }, { "ronto", "r" }, { "yocto", "y" }, { "zepto", "z" }, { "atto", "a" }, { "femto", "f" }, { "pico", "p" },
            { "nano", "n" }, { "micro", "μ" }, { "milli", "m" }, { "centi", "c" }, { "deci", "d" }, { "deca", "da" }, { "deka", "da" },
            { "hecto", "h" }, { "kilo", "k" }, { "mega", "M" }, { "giga", "G" }, { "tera", "T" }, { "peta", "P" }, { "exa", "E" },
            { "zetta", "Z" }, { "yotta", "Y" }, { "ronna", "R" }, { "quetta", "Q" }, { "kibi", "Ki" }, { "mebi", "Mi" }, { "gibi", "Gi" }, { "tebi", "Ti" }
        };

        private static readonly Dictionary<string, string> UnitNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "ampere", "A" }, { "candela", "cd" }, { "kelvin", "K" }, { "kilogram", "kg" }, { "gram", "g" }, { "metre", "m" }, { "meter", "m" },
            { "mole", "mol" }, { "second", "s" }, { "becquerel", "Bq" }, { "degreeCelsius", "°C" }, { "celsius", "°C" }, { "coulomb", "C" },
            { "farad", "F" }, { "gray", "Gy" }, { "hertz", "Hz" }, { "henry", "H" }, { "joule", "J" }, { "katal", "kat" }, { "lumen", "lm" },
            { "lux", "lx" }, { "newton", "N" }, { "ohm", "Ω" }, { "pascal", "Pa" }, { "radian", "rad" }, { "siemens", "S" }, { "sievert", "Sv" },
            { "steradian", "sr" }, { "tesla", "T" }, { "volt", "V" }, { "watt", "W" }, { "weber", "Wb" }, { "day", "d" }, { "hectare", "ha" },
            { "hour", "h" }, { "litre", "L" }, { "liter", "L" }, { "minute", "min" }, { "tonne", "t" }, { "astronomicalunit", "au" },
            { "atomicmassunit", "u" }, { "dalton", "Da" }, { "electronvolt", "eV" }, { "arcminute", "′" }, { "arcsecond", "″" }, { "degree", "°" },
            { "percent", "%" }, { "bar", "bar" }, { "barn", "b" }, { "bel", "B" }, { "decibel", "dB" }, { "knot", "kn" },
            { "millimetremercury", "mmHg" }, { "nauticalmile", "M" }, { "neper", "Np" }, { "angstrom", "Å" }, { "atmosphere", "atm" },
            { "atm", "atm" }, { "molar", "M" }, { "Molar", "M" }, { "torr", "Torr" }, { "calorie", "cal" }, { "gauss", "G" }, { "erg", "erg" },
            { "debye", "D" }, { "micron", "μm" }, { "ppm", "ppm" }, { "ppb", "ppb" }, { "permille", "‰" }, { "elementarycharge", "e" },
            { "planckbar", "ℏ" }, { "electronmass", "m_e" }, { "bohr", "a_0" }, { "hartree", "E_h" }, { "clight", "c_0" }
        };

        private sealed class UnitPart
        {
            /// <summary>The unit in TeX: \mathrm{kg}, or literal units written between the macros.</summary>
            public string Symbol;
            public bool Literal;
            public string Power;
            /// <summary>\of{NaCl}: shown as a subscript, siunitx's default.</summary>
            public string Qualifier;
            /// <summary>\highlight{red}: the colour of this unit.</summary>
            public string Color;
        }

        /// <summary>
        /// \si{...} / \unit{...}: \kilo\gram\per\metre\squared or the literal form kg.m/s^2. Nothing is dropped silently: a
        /// prefix, \per or power with no unit after it, or a \squared with none before it, shows the formula as written.
        /// </summary>
        public static string Units(string input)
        {
            string text = (input ?? "").Trim();
            if (text.IndexOf('\\') < 0) return LiteralUnits(text, "\\,", "\\,");
            List<UnitPart> parts = new List<UnitPart>();
            string prefix = "";
            string pendingPower = null;   // \square, \cubic, \raiseto{...}
            bool per = false;
            string color = null;          // \highlight{...} colours the next unit
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c != '\\')
                {
                    // Literal pieces between macros.
                    int start = i;
                    while (i < text.Length && text[i] != '\\') i++;
                    string piece = text.Substring(start, i - start).Trim();
                    if (piece == "/") { per = true; continue; }
                    if (piece.Length == 0 || piece == "." || piece == "~") continue;
                    UnitPart raw = new UnitPart();
                    raw.Literal = true;
                    if (per || pendingPower != null)
                    {
                        // \per mol: the power needs a single unit to apply to.
                        if (!IsSingleUnit(piece)) throw new NotSupportedException("单位写法暂不支持：\\per、\\square 等后面应是一个单位");
                        raw.Symbol = UnitSymbol((prefix + piece).Replace('µ', 'μ'));
                        raw.Literal = false;
                        raw.Power = Exponent(pendingPower, per);
                    }
                    else raw.Symbol = LiteralUnits(prefix + piece, "\\,", "\\,");
                    raw.Color = color;
                    parts.Add(raw);
                    prefix = "";
                    pendingPower = null;
                    per = false;
                    color = null;
                    continue;
                }
                int nameStart = ++i;
                while (i < text.Length && (Char.IsLetter(text[i]))) i++;
                string name = text.Substring(nameStart, i - nameStart);
                switch (name)
                {
                    case "per": per = true; continue;
                    case "square": pendingPower = "2"; continue;
                    case "cubic": pendingPower = "3"; continue;
                    case "squared": MultiplyLast(parts, "2", name); continue;
                    case "cubed": MultiplyLast(parts, "3", name); continue;
                    case "tothe": MultiplyLast(parts, Power(Group(text, ref i), name), name); continue;
                    case "raiseto": pendingPower = Power(Group(text, ref i), name); continue;
                    case "of":
                        {
                            string qualifier = Group(text, ref i).Trim();
                            if (parts.Count == 0) throw new NotSupportedException("\\of 前面缺少单位");
                            if (qualifier.Length > 0) parts[parts.Count - 1].Qualifier = qualifier;
                            continue;
                        }
                    case "highlight":
                        color = Group(text, ref i).Trim();
                        if (color.Length == 0) color = null;
                        continue;
                }
                string symbol;
                if (Prefixes.TryGetValue(name, out symbol))
                {
                    prefix += symbol;
                    continue;
                }
                if (!UnitNames.TryGetValue(name, out symbol)) throw new NotSupportedException("单位中暂不支持的命令 \\" + name);
                UnitPart part = new UnitPart();
                part.Symbol = UnitSymbol(prefix + symbol);
                part.Power = Exponent(pendingPower, per);
                part.Color = color;
                parts.Add(part);
                prefix = "";
                pendingPower = null;
                per = false;
                color = null;
            }
            if (prefix.Length > 0) throw new NotSupportedException("单位词头后面缺少单位");
            if (per) throw new NotSupportedException("\\per 后面缺少单位");
            if (pendingPower != null) throw new NotSupportedException("\\square、\\cubic 或 \\raiseto 后面缺少单位");
            if (color != null) throw new NotSupportedException("\\highlight 后面缺少单位");
            StringBuilder tex = new StringBuilder();
            foreach (UnitPart part in parts)
            {
                if (tex.Length > 0) tex.Append("\\,");
                StringBuilder unit = new StringBuilder();
                bool scripted = part.Power != null || part.Qualifier != null;
                // Several literal units, or m_e, take their scripts as a whole.
                if (scripted && (part.Literal || part.Symbol.IndexOf('_') >= 0)) unit.Append('{').Append(part.Symbol).Append('}');
                else unit.Append(part.Symbol);
                if (part.Qualifier != null) unit.Append("_{\\mathrm{").Append(Escape(part.Qualifier).Replace(" ", "\\ ")).Append("}}");
                if (part.Power != null) unit.Append("^{").Append(part.Power).Append('}');
                if (part.Color != null) tex.Append("{\\color{").Append(part.Color).Append('}').Append(unit).Append('}');
                else tex.Append(unit);
            }
            return tex.ToString();
        }

        private static bool IsSingleUnit(string piece)
        {
            foreach (char c in piece) if (!IsUnitCharacter(c)) return false;
            return piece.Length > 0;
        }

        /// <summary>The argument of \raiseto or \tothe: 2, -1, 0.5 (a decimal power is kept, never replaced by 1), or a symbol such as n.</summary>
        private static string Power(string text, string command)
        {
            string power = (text ?? "").Trim().Replace('−', '-');
            if (power.StartsWith("+", StringComparison.Ordinal)) power = power.Substring(1);
            if (power.Length == 0 || power == "-") throw new NotSupportedException("\\" + command + " 缺少幂次");
            return power;
        }

        /// <summary>The power of a unit after \square / \cubic / \raiseto and \per; null for the first power.</summary>
        private static string Exponent(string pendingPower, bool per)
        {
            string power = pendingPower ?? "1";
            if (per) power = Negate(power);
            return power == "1" ? null : power;
        }

        /// <summary>
        /// The power after \per. A written number only changes its sign, digits and all: \raiseto{0.0000000000001}
        /// becomes -0.0000000000001 exactly, never a rounded value. Symbols work the same way (n becomes -n); a sum is
        /// bracketed, since -n+1 would be a different power: \per\raiseto{n+1} is -(n+1).
        /// </summary>
        private static string Negate(string power)
        {
            string p = power.StartsWith("+", StringComparison.Ordinal) ? power.Substring(1) : power;
            if (DecimalPower.IsMatch(p))
            {
                if (p.StartsWith("-", StringComparison.Ordinal)) return p.Substring(1);
                return p.Trim('0', '.').Length == 0 ? p : "-" + p;   // zero has no sign
            }
            if (IsSum(p)) return "-(" + p + ")";
            if (!p.StartsWith("-", StringComparison.Ordinal)) return "-" + p;
            string rest = p.Substring(1);
            // -(n+1) negated is n+1 again.
            if (rest.Length > 1 && rest[0] == '(' && ClosingBracket(rest) == rest.Length - 1) return rest.Substring(1, rest.Length - 2);
            return rest;
        }

        /// <summary>The index of the ) that closes the ( at the start of the text; -1 if there is none.</summary>
        private static int ClosingBracket(string text)
        {
            int level = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '(') level++;
                else if (text[i] == ')' && --level == 0) return i;
            }
            return -1;
        }

        /// <summary>
        /// True when the power is a sum or a difference at its top level (n+1, a-b, -n+1), which needs brackets once it is
        /// negated or multiplied; a leading sign, and signs inside braces or brackets, do not count.
        /// </summary>
        private static bool IsSum(string power)
        {
            int level = 0;
            for (int i = 0; i < power.Length; i++)
            {
                char c = power[i];
                if (c == '{' || c == '(' || c == '[') level++;
                else if (c == '}' || c == ')' || c == ']') level--;
                else if (level == 0 && i > 0 && (c == '+' || c == '-' || c == '−' || c == '±' || c == '∓')) return true;
                else if (level == 0 && c == '\\' && (String.CompareOrdinal(power, i, "\\pm", 0, 3) == 0 || String.CompareOrdinal(power, i, "\\mp", 0, 3) == 0))
                    return true;
            }
            return false;
        }

        /// <summary>A power as a factor of a product: a sum or a negative power is bracketed, 2·(n+1) and n·(-1).</summary>
        private static string AsFactor(string power, bool first)
        {
            return IsSum(power) || (!first && power.StartsWith("-", StringComparison.Ordinal)) ? "(" + power + ")" : power;
        }

        private static void MultiplyLast(List<UnitPart> parts, string factor, string command)
        {
            if (parts.Count == 0) throw new NotSupportedException("\\" + command + " 前面缺少单位");
            UnitPart last = parts[parts.Count - 1];
            last.Power = CombinePower(last.Power, factor);
        }

        /// <summary>
        /// A power times another: m^{-1} squared is m^{-2}, s^{0.5} squared is s. Numbers are multiplied exactly; when
        /// the result could need more digits than can be kept exactly, the formula is shown as written. A product of
        /// numbers with d₁ and d₂ significant digits has at most d₁ + d₂ of them, and a decimal holds any 28-digit
        /// integer with up to 28 places after the point, so the product is exact within those limits.
        /// </summary>
        private static string CombinePower(string current, string factor)
        {
            decimal a = 1, b;
            if (current != null && !TryPower(current, out a))
            {
                // A symbol such as n: n squared is n·2, n to the -1 is -n.
                if (TryPower(factor, out b))
                {
                    if (b == 1) return current;
                    if (b == -1) return Negate(current);
                    if (b == 0) return "0";
                }
                return AsFactor(current, true) + "\\cdot " + AsFactor(factor, false);
            }
            if (!TryPower(factor, out b))
            {
                if (a == 1) return factor;
                if (a == -1) return Negate(factor);
                if (a == 0) return "0";
                return AsFactor(current, true) + "\\cdot " + AsFactor(factor, false);
            }
            if (Scale(a) + Scale(b) > 28 || SignificantDigits(a) + SignificantDigits(b) > 28)
                throw new NotSupportedException("单位幂次 " + (current ?? "1") + " × " + factor + " 的位数超出可精确计算的范围");
            decimal product = a * b;
            if (product == 0) return "0";
            return product == 1 ? null : FormatPower(product);
        }

        private static readonly Regex DecimalPower = new Regex(@"^[+\-]?(\d+(\.\d*)?|\.\d+)$", RegexOptions.CultureInvariant);

        /// <summary>
        /// A power written as a number (2, -1, 0.5), as an exact decimal; false for a symbol such as n. A number with
        /// more digits than a decimal keeps exactly shows the formula as written instead of a rounded power.
        /// </summary>
        private static bool TryPower(string text, out decimal value)
        {
            value = 0;
            string power = (text ?? "").Trim();
            if (!DecimalPower.IsMatch(power)) return false;
            if (!Decimal.TryParse(power, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) ||
                PlainPower(FormatPower(value)) != PlainPower(power))
                throw new NotSupportedException("单位幂次 " + power + " 的位数超出可精确计算的范围");
            return true;
        }

        /// <summary>A decimal written without a + sign, leading zeros or trailing fraction zeros, for comparing two spellings.</summary>
        private static string PlainPower(string power)
        {
            string p = power.StartsWith("+", StringComparison.Ordinal) ? power.Substring(1) : power;
            bool negative = p.StartsWith("-", StringComparison.Ordinal);
            if (negative) p = p.Substring(1);
            int dot = p.IndexOf('.');
            string whole = (dot < 0 ? p : p.Substring(0, dot)).TrimStart('0');
            string fraction = dot < 0 ? "" : p.Substring(dot + 1).TrimEnd('0');
            string plain = (whole.Length == 0 ? "0" : whole) + (fraction.Length > 0 ? "." + fraction : "");
            return negative && plain != "0" ? "-" + plain : plain;
        }

        private static int Scale(decimal value)
        {
            return (Decimal.GetBits(value)[3] >> 16) & 0xFF;
        }

        private static int SignificantDigits(decimal value)
        {
            string digits = FormatPower(Math.Abs(value)).Replace(".", "").TrimStart('0');
            return digits.Length;
        }

        /// <summary>All the digits a decimal can hold (28 after the point): nothing is rounded away.</summary>
        private static string FormatPower(decimal value)
        {
            return value.ToString("0.############################", CultureInfo.InvariantCulture);
        }

        private static string Group(string text, ref int i)
        {
            while (i < text.Length && Char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length || text[i] != '{') return "";
            int close = MatchBrace(text, i);
            string inner = text.Substring(i + 1, Math.Max(0, close - i - 1));
            i = close + 1;
            return inner;
        }

        /// <summary>A unit symbol set upright; m_e style symbols keep their subscript.</summary>
        private static string UnitSymbol(string symbol)
        {
            int underscore = symbol.IndexOf('_');
            if (underscore > 0) return "\\mathrm{" + Escape(symbol.Substring(0, underscore)) + "}_{\\mathrm{" + Escape(symbol.Substring(underscore + 1)) + "}}";
            return "\\mathrm{" + Escape(symbol) + "}";
        }

        private static string Escape(string text)
        {
            return (text ?? "").Replace("%", "\\%").Replace("#", "\\#").Replace("&", "\\&");
        }

        private static bool IsUnitCharacter(char c)
        {
            return Char.IsLetter(c) || c == '°' || c == '%' || c == '‰' || c == '′' || c == '″' || c == 'Å' || c == '℃' || c == 'Ω' || c == 'µ';
        }

        /// <summary>Longest unit text produced for one formula; beyond it the formula is shown as source.</summary>
        private const int MaxUnitLength = 40000;

        /// <summary>
        /// Units written out: J mol-1 K-1, kJ.mol^-1, m/s^2, °C. Digits right after a unit are its exponent (mol-1, m2);
        /// "." and "*" stand for <paramref name="dot"/>, white space and "~" for <paramref name="space"/>.
        /// </summary>
        private static string LiteralUnits(string units, string dot, string space)
        {
            StringBuilder tex = new StringBuilder();
            string separator = null;
            int i = 0, n = units.Length;
            while (i < n)
            {
                if (tex.Length > MaxUnitLength) throw new InvalidOperationException("单位写法过长");
                char c = units[i];
                // Any white space separates units: tabs and line breaks of a formula written on several lines, no-break
                // spaces copied from web pages and the full-width space of Chinese input.
                if (Char.IsWhiteSpace(c) || c == '~')
                {
                    if (tex.Length > 0 && separator == null) separator = space;
                    i++;
                    continue;
                }
                if (c == '.' || c == '*' || c == '·' || c == '⋅')
                {
                    if (tex.Length > 0) separator = dot;
                    i++;
                    continue;
                }
                if (c == '/' || c == '(' || c == ')')
                {
                    tex.Append(c);
                    separator = null;
                    i++;
                    continue;
                }
                int start = i;
                while (i < n && IsUnitCharacter(units[i])) i++;
                if (i == start)
                {
                    // Something that is not a unit (a number, a symbol): kept as it is. Every pass consumes at least one
                    // character, so no input can hold this loop in place.
                    while (i < n && !Char.IsWhiteSpace(units[i]) && units[i] != '/' && units[i] != '.' && units[i] != '*' && units[i] != '~') i++;
                    if (i == start) i++;
                    if (separator != null && tex.Length > 0) tex.Append(separator);
                    tex.Append("\\mathrm{").Append(Escape(units.Substring(start, i - start))).Append('}');
                    separator = null;
                    continue;
                }
                string symbol = units.Substring(start, i - start).Replace('µ', 'μ');
                string power = null;
                if (i < n && units[i] == '^')
                {
                    i++;
                    if (i < n && units[i] == '{')
                    {
                        int close = MatchBrace(units, i);
                        power = units.Substring(i + 1, Math.Max(0, close - i - 1));
                        i = close + 1;
                    }
                    else
                    {
                        int p = i;
                        if (p < n && (units[p] == '-' || units[p] == '+' || units[p] == '−')) p++;
                        while (p < n && (Char.IsDigit(units[p]) || units[p] == '.')) p++;
                        power = units.Substring(i, p - i);
                        i = p;
                    }
                }
                else if (i < n && (Char.IsDigit(units[i]) || ((units[i] == '-' || units[i] == '+' || units[i] == '−') && i + 1 < n && Char.IsDigit(units[i + 1]))))
                {
                    int p = i + 1;
                    while (p < n && Char.IsDigit(units[p])) p++;
                    power = units.Substring(i, p - i);
                    i = p;
                }
                if (separator != null && tex.Length > 0) tex.Append(separator);
                separator = null;
                tex.Append(UnitSymbol(symbol));
                if (!String.IsNullOrEmpty(power)) tex.Append("^{").Append(power.Replace('−', '-')).Append('}');
            }
            return tex.ToString();
        }

        private static bool IsAngleUnit(string units)
        {
            string trimmed = (units ?? "").Trim();
            return trimmed == "°" || trimmed == "′" || trimmed == "″" || trimmed == "\\degree" || trimmed == "\\arcminute" || trimmed == "\\arcsecond";
        }

        /// <summary>\SI{9.81}{\metre\per\second\squared} / \qty{...}{...}: the number, a thin space and the units (no space before ° ′ ″).</summary>
        public static string Quantity(string number, string units, string before)
        {
            string unitTex = Units(units);
            string tex = Number(number, unitTex.Length > 0);
            if (unitTex.Length > 0) tex += (IsAngleUnit(units) ? "" : "\\,") + unitTex;
            if (!String.IsNullOrEmpty(before)) tex = Units(before) + "\\," + tex;
            return tex;
        }

        /// <summary>\pu{...} (mhchem): 123 kJ/mol, 1.2e-3 mol L-1, 25 °C, -10 kJ.mol-1.</summary>
        public static string PhysicalUnit(string input)
        {
            string text = (input ?? "").Trim();
            Match match;
            int length = NumberLength(text, out match);
            string units = length > 0 ? text.Substring(length).Trim() : text;
            string unitTex = PhysicalUnits(units);
            string number = length > 0 ? FormatNumber(match, false, unitTex.Length > 0) : "";
            if (number.Length == 0) return unitTex;
            if (unitTex.Length == 0) return number;
            return number + (IsAngleUnit(units) ? "" : "\\,") + unitTex;
        }

        /// <summary>The units of \pu. As in mhchem, J // mol K is a fraction with J over mol K (J/mol stays on one line).</summary>
        private static string PhysicalUnits(string units)
        {
            int slashes = units.IndexOf("//", StringComparison.Ordinal);
            if (slashes < 0) return units.IndexOf('\\') >= 0 ? Units(units) : LiteralUnits(units, "\\cdot ", "\\,");
            string over = units.Substring(0, slashes).Trim(), under = units.Substring(slashes + 2).Trim();
            if (over.Length == 0 || under.Length == 0 || under.IndexOf("//", StringComparison.Ordinal) >= 0)
                throw new NotSupportedException("\\pu 中 // 的写法有误");
            return "\\frac{" + PhysicalUnits(over) + "}{" + PhysicalUnits(under) + "}";
        }

        /// <summary>\ang{30} → 30°, \ang{12;30;15} → 12°30′15″.</summary>
        public static string Angle(string input)
        {
            string[] parts = (input ?? "").Split(';');
            string[] marks = { "°", "′", "″" };
            StringBuilder tex = new StringBuilder();
            for (int i = 0; i < parts.Length && i < 3; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0) continue;
                tex.Append(Number(part, true)).Append("{").Append(marks[i]).Append('}');
            }
            return tex.ToString();
        }

        /// <summary>\numrange{10}{20} → 10–20; values with ± are bracketed: (1 ± 0.1)–(2 ± 0.1).</summary>
        public static string NumberRange(string from, string to)
        {
            return Number(from, true) + "\\text{–}" + Number(to, true);
        }

        public static string QuantityRange(string from, string to, string units)
        {
            string unitTex = Units(units);
            string range = NumberRange(from, to);
            return unitTex.Length == 0 ? range : range + (IsAngleUnit(units) ? "" : "\\,") + unitTex;
        }

        /// <summary>\numlist{1;2;3} → 1, 2, 3; \SIlist{1;2}{\metre} → 1 m, 2 m.</summary>
        public static string NumberList(string list, string units)
        {
            StringBuilder tex = new StringBuilder();
            foreach (string item in (list ?? "").Split(';'))
            {
                if (item.Trim().Length == 0) continue;
                if (tex.Length > 0) tex.Append(",\\ ");
                tex.Append(units == null ? Number(item) : Quantity(item, units, null));
            }
            return tex.ToString();
        }
    }
}

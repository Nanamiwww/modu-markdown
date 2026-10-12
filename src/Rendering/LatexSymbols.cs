using System;
using System.Collections.Generic;

namespace CodexMarkdownReader.Rendering
{
    /// <summary>TeX's atom classes; they decide the spacing between neighbours.</summary>
    internal enum AtomClass { Ord, Op, Bin, Rel, Open, Close, Punct, Inner }

    /// <summary>Math alphabets (\mathrm, \mathbf, \mathbb ...), mapped to the Unicode mathematical alphanumeric symbols.</summary>
    internal enum MathVariant { Default, Roman, Italic, Bold, BoldItalic, Sans, SansBold, SansItalic, Mono, Script, BoldScript, Fraktur, BoldFraktur, Blackboard }

    internal sealed class MathSymbol
    {
        public readonly string Text;
        public readonly AtomClass Class;
        /// <summary>A big operator (∑, ∫): drawn larger in display style.</summary>
        public readonly bool Large;
        /// <summary>Takes limits above and below in display style (∑ yes, ∫ no).</summary>
        public readonly bool Limits;
        /// <summary>A Greek letter: follows the math alphabet (italic by default for lowercase).</summary>
        public readonly bool Letter;

        public MathSymbol(string text, AtomClass cls, bool large, bool limits, bool letter)
        {
            Text = text;
            Class = cls;
            Large = large;
            Limits = limits;
            Letter = letter;
        }
    }

    /// <summary>The symbol, delimiter and operator-name tables of the formula engine.</summary>
    internal static class LatexSymbols
    {
        public static readonly Dictionary<string, MathSymbol> Commands = new Dictionary<string, MathSymbol>(StringComparer.Ordinal);
        /// <summary>Classes of operator characters typed directly (≤, →, ± ...).</summary>
        public static readonly Dictionary<string, AtomClass> CharacterClasses = new Dictionary<string, AtomClass>(StringComparer.Ordinal);
        /// <summary>Delimiter names after \left, \right and \big: the character to draw.</summary>
        public static readonly Dictionary<string, string> Delimiters = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Function names set upright: \sin, \log ... (value: the text; limits in display style for \lim, \max ...).</summary>
        public static readonly Dictionary<string, string> Functions = new Dictionary<string, string>(StringComparer.Ordinal);
        public static readonly HashSet<string> LimitFunctions = new HashSet<string>(StringComparer.Ordinal);

        static LatexSymbols()
        {
            Greek("alpha α beta β gamma γ delta δ epsilon ϵ varepsilon ε zeta ζ eta η theta θ vartheta ϑ iota ι kappa κ varkappa ϰ " +
                "lambda λ mu μ nu ν xi ξ omicron ο pi π varpi ϖ rho ρ varrho ϱ sigma σ varsigma ς tau τ upsilon υ phi ϕ varphi φ " +
                "chi χ psi ψ omega ω Gamma Γ Delta Δ Theta Θ Lambda Λ Xi Ξ Pi Π Sigma Σ Upsilon Υ Phi Φ Psi Ψ Omega Ω " +
                "Alpha Α Beta Β Epsilon Ε Zeta Ζ Eta Η Iota Ι Kappa Κ Mu Μ Nu Ν Omicron Ο Rho Ρ Tau Τ Chi Χ partial ∂");
            string[] capitals = { "Gamma", "Delta", "Theta", "Lambda", "Xi", "Pi", "Sigma", "Upsilon", "Phi", "Psi", "Omega" };
            foreach (string name in capitals)
            {
                // \varGamma ...: italic capitals.
                string upright = Commands[name].Text;
                Commands["var" + name] = new MathSymbol(MapCharacter(upright, MathVariant.Italic), AtomClass.Ord, false, false, false);
            }
            Add(AtomClass.Ord, "digamma ϝ ell ℓ hbar ℏ hslash ℏ imath ı jmath ȷ wp ℘ Re ℜ Im ℑ aleph ℵ beth ℶ gimel ℷ daleth ℸ nabla ∇ " +
                "infty ∞ forall ∀ exists ∃ nexists ∄ emptyset ∅ varnothing ∅ neg ¬ lnot ¬ top ⊤ bot ⊥ angle ∠ measuredangle ∡ " +
                "sphericalangle ∢ triangle △ triangledown ▽ square □ Box □ blacksquare ■ blacktriangle ▲ blacktriangledown ▼ " +
                "lozenge ◊ blacklozenge ⧫ bigstar ★ diamondsuit ♢ heartsuit ♡ clubsuit ♣ spadesuit ♠ flat ♭ natural ♮ sharp ♯ " +
                "surd √ checkmark ✓ maltese ✠ prime ′ backprime ‵ complement ∁ eth ð mho ℧ Finv Ⅎ Game ⅁ S § P ¶ dag † ddag ‡ " +
                "copyright © circledR ® yen ¥ pounds £ euro € degree ° vdots ⋮ vert | Vert ‖ " +
                "backslash \\ AA Å aa å O Ø o ø ae æ AE Æ oe œ OE Œ ss ß i ı j ȷ L Ł l ł diagup ╱ diagdown ╲ Diamond ◇ " +
                "circledS Ⓢ varclubsuit ♧ vardiamondsuit ♦ varheartsuit ♥ varspadesuit ♤");
            Commands["Bbbk"] = new MathSymbol(Char.ConvertFromUtf32(0x1D55C), AtomClass.Ord, false, false, false);
            Commands["star"] = new MathSymbol("⋆", AtomClass.Bin, false, false, false);
            Add(AtomClass.Inner, "ldots … cdots ⋯ ddots ⋱ dots … dotsc … dotsb ⋯ dotsm ⋯ dotsi ⋯ dotso … iddots ⋰ adots ⋰ mathellipsis …");
            Add(AtomClass.Punct, "colon : cdotp ⋅ ldotp .");
            Add(AtomClass.Open, "lbrace { lbrack [ langle ⟨ lfloor ⌊ lceil ⌈ ulcorner ⌜ llcorner ⌞ lgroup ⟮ llbracket ⟦ lmoustache ⎰ lvert | lVert ‖");
            Add(AtomClass.Close, "rbrace } rbrack ] rangle ⟩ rfloor ⌋ rceil ⌉ urcorner ⌝ lrcorner ⌟ rgroup ⟯ rrbracket ⟧ rmoustache ⎱ rvert | rVert ‖");
            Add(AtomClass.Bin, "pm ± mp ∓ times × div ÷ cdot ⋅ ast ∗ circ ∘ bullet ∙ oplus ⊕ ominus ⊖ otimes ⊗ oslash ⊘ odot ⊙ " +
                "cup ∪ cap ∩ sqcup ⊔ sqcap ⊓ vee ∨ lor ∨ wedge ∧ land ∧ setminus ∖ smallsetminus ∖ wr ≀ diamond ⋄ " +
                "bigtriangleup △ bigtriangledown ▽ triangleleft ◁ triangleright ▷ lhd ⊲ rhd ⊳ unlhd ⊴ unrhd ⊵ dagger † ddagger ‡ " +
                "amalg ⨿ uplus ⊎ ltimes ⋉ rtimes ⋊ leftthreetimes ⋋ rightthreetimes ⋌ Cup ⋓ Cap ⋒ doublecup ⋓ doublecap ⋒ " +
                "boxplus ⊞ boxminus ⊟ boxtimes ⊠ boxdot ⊡ veebar ⊻ barwedge ⊼ doublebarwedge ⩞ curlywedge ⋏ curlyvee ⋎ " +
                "circledcirc ⊚ circledast ⊛ circleddash ⊝ dotplus ∔ divideontimes ⋇ centerdot ⋅ intercal ⊺");
            Add(AtomClass.Rel, "le ≤ leq ≤ ge ≥ geq ≥ ne ≠ neq ≠ approx ≈ equiv ≡ sim ∼ simeq ≃ cong ≅ propto ∝ in ∈ notin ∉ ni ∋ owns ∋ " +
                "subset ⊂ supset ⊃ subseteq ⊆ supseteq ⊇ subsetneq ⊊ supsetneq ⊋ sqsubset ⊏ sqsupset ⊐ sqsubseteq ⊑ sqsupseteq ⊒ " +
                "ll ≪ gg ≫ lll ⋘ ggg ⋙ prec ≺ succ ≻ preceq ⪯ succeq ⪰ parallel ∥ nparallel ∦ perp ⊥ mid ∣ nmid ∤ vdash ⊢ dashv ⊣ " +
                "models ⊨ vDash ⊨ Vdash ⊩ Vvdash ⊪ nvdash ⊬ nvDash ⊭ asymp ≍ doteq ≐ bowtie ⋈ Join ⋈ smile ⌣ frown ⌢ " +
                "leqslant ⩽ geqslant ⩾ lesssim ≲ gtrsim ≳ lessapprox ⪅ gtrapprox ⪆ approxeq ≊ eqsim ≂ backsim ∽ backsimeq ⋍ " +
                "thicksim ∼ thickapprox ≈ leqq ≦ geqq ≧ lneq ⪇ gneq ⪈ lneqq ≨ gneqq ≩ lnsim ⋦ gnsim ⋧ lessgtr ≶ gtrless ≷ " +
                "lesseqgtr ⋚ gtreqless ⋛ nless ≮ ngtr ≯ nleq ≰ ngeq ≱ nsim ≁ ncong ≇ nsubseteq ⊈ nsupseteq ⊉ nprec ⊀ nsucc ⊁ " +
                "triangleq ≜ coloneqq ≔ coloneq ≔ eqqcolon ≕ eqcolon ≕ Coloneqq ⩴ doteqdot ≑ Doteq ≑ risingdotseq ≓ fallingdotseq ≒ " +
                "circeq ≗ bumpeq ≏ Bumpeq ≎ eqcirc ≖ Subset ⋐ Supset ⋑ therefore ∴ because ∵ varpropto ∝ between ≬ pitchfork ⋔ " +
                "vartriangleleft ⊲ vartriangleright ⊳ trianglelefteq ⊴ trianglerighteq ⊵ shortmid ∣ shortparallel ∥ " +
                "smallsmile ⌣ smallfrown ⌢ notni ∌ " +
                "to → rightarrow → gets ← leftarrow ← leftrightarrow ↔ Rightarrow ⇒ Leftarrow ⇐ Leftrightarrow ⇔ mapsto ↦ " +
                "longmapsto ⟼ longrightarrow ⟶ longleftarrow ⟵ longleftrightarrow ⟷ Longrightarrow ⟹ Longleftarrow ⟸ " +
                "Longleftrightarrow ⟺ uparrow ↑ downarrow ↓ updownarrow ↕ Uparrow ⇑ Downarrow ⇓ Updownarrow ⇕ nearrow ↗ " +
                "searrow ↘ swarrow ↙ nwarrow ↖ hookrightarrow ↪ hookleftarrow ↩ rightharpoonup ⇀ rightharpoondown ⇁ " +
                "leftharpoonup ↼ leftharpoondown ↽ rightleftharpoons ⇌ leftrightharpoons ⇋ rightleftarrows ⇄ leftrightarrows ⇆ " +
                "rightrightarrows ⇉ leftleftarrows ⇇ upuparrows ⇈ downdownarrows ⇊ twoheadrightarrow ↠ twoheadleftarrow ↞ " +
                "rightarrowtail ↣ leftarrowtail ↢ looparrowright ↬ looparrowleft ↫ curvearrowright ↷ curvearrowleft ↶ " +
                "circlearrowright ↻ circlearrowleft ↺ multimap ⊸ leadsto ↝ rightsquigarrow ⇝ leftrightsquigarrow ↭ Rrightarrow ⇛ " +
                "Lleftarrow ⇚ Lsh ↰ Rsh ↱ upharpoonright ↾ upharpoonleft ↿ downharpoonright ⇂ downharpoonleft ⇃ restriction ↾ " +
                "nleftarrow ↚ nrightarrow ↛ nLeftarrow ⇍ nRightarrow ⇏ nleftrightarrow ↮ nLeftrightarrow ⇎");
            Large(true, "sum ∑ prod ∏ coprod ∐ bigcup ⋃ bigcap ⋂ bigsqcup ⨆ bigvee ⋁ bigwedge ⋀ bigoplus ⨁ bigotimes ⨂ bigodot ⨀ biguplus ⨄");
            Large(false, "int ∫ iint ∬ iiint ∭ iiiint ⨌ oint ∮ oiint ∯ oiiint ∰ intop ∫");
            Commands["smallint"] = new MathSymbol("∫", AtomClass.Op, false, false, false);

            foreach (string name in new string[] { "arccos", "arcsin", "arctan", "arg", "cos", "cosh", "cot", "coth", "csc", "deg", "dim", "exp", "hom",
                "ker", "lg", "ln", "log", "sec", "sin", "sinh", "tan", "tanh", "arcsec", "arccsc", "arccot", "sech", "csch", "sgn", "tr", "Tr", "rank",
                "erf", "erfc", "diag", "Res" })
                Functions[name] = name;
            foreach (string name in new string[] { "det", "gcd", "inf", "lim", "max", "min", "Pr", "sup" })
            {
                Functions[name] = name;
                LimitFunctions.Add(name);
            }
            Functions["liminf"] = "lim inf";
            Functions["limsup"] = "lim sup";
            Functions["injlim"] = "inj lim";
            Functions["projlim"] = "proj lim";
            Functions["argmax"] = "arg max";
            Functions["argmin"] = "arg min";
            foreach (string name in new string[] { "liminf", "limsup", "injlim", "projlim", "argmax", "argmin" }) LimitFunctions.Add(name);

            Pairs(Delimiters, "( ( ) ) [ [ ] ] < ⟨ > ⟩ | | / / . . lbrace { rbrace } { { } } lbrack [ rbrack ] vert | Vert ‖ lvert | rvert | " +
                "lVert ‖ rVert ‖ langle ⟨ rangle ⟩ lfloor ⌊ rfloor ⌋ lceil ⌈ rceil ⌉ backslash \\ uparrow ↑ downarrow ↓ updownarrow ↕ " +
                "Uparrow ⇑ Downarrow ⇓ Updownarrow ⇕ lgroup ⟮ rgroup ⟯ llbracket ⟦ rrbracket ⟧ lmoustache ⎰ rmoustache ⎱ " +
                "ulcorner ⌜ urcorner ⌝ llcorner ⌞ lrcorner ⌟ ⟨ ⟨ ⟩ ⟩ ‖ ‖ ⌊ ⌊ ⌋ ⌋ ⌈ ⌈ ⌉ ⌉");
            Delimiters["\\|"] = "‖";

            Classes(AtomClass.Bin, "+ − ± ∓ × ÷ · ⋅ ∗ ∘ ∙ ⊕ ⊖ ⊗ ⊘ ⊙ ∪ ∩ ⊔ ⊓ ∨ ∧ ∖ ⋄ † ‡ ⊎ ⊞ ⊟ ⊠ ⊡");
            Classes(AtomClass.Rel, "= < > ≤ ≥ ≠ ≈ ≡ ∼ ≃ ≅ ∝ ∈ ∉ ∋ ⊂ ⊃ ⊆ ⊇ ⊊ ⊋ ≪ ≫ ≺ ≻ ∥ ⊥ ∣ ⊢ ⊨ ≍ ≐ ⩽ ⩾ ≲ ≳ ≦ ≧ ≜ ≔ ∴ ∵ " +
                "→ ← ↔ ⇒ ⇐ ⇔ ↦ ⟶ ⟵ ⟷ ⟹ ⟸ ⟺ ↑ ↓ ↕ ⇑ ⇓ ⇕ ↗ ↘ ↙ ↖ ⇌ ⇋ ⇄ ⇆ ⇀ ↽ ↪ ↩ ≮ ≯ ≰ ≱ ⊄ ⊅ ⊈ ⊉ ∤ ∦ ≉ ≢ ≁ ≄ ≇");
            Classes(AtomClass.Punct, ", ; ，；");
            Classes(AtomClass.Open, "( [ { ⟨ ⌊ ⌈ （ 【 ［");
            Classes(AtomClass.Close, ") ] } ⟩ ⌋ ⌉ ! ? ） 】 ］");
            Classes(AtomClass.Inner, "… ⋯");
        }

        private static IEnumerable<KeyValuePair<string, string>> Split(string spec)
        {
            string[] parts = spec.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i += 2) yield return new KeyValuePair<string, string>(parts[i], parts[i + 1]);
        }

        private static void Add(AtomClass cls, string spec)
        {
            foreach (KeyValuePair<string, string> pair in Split(spec)) Commands[pair.Key] = new MathSymbol(pair.Value, cls, false, false, false);
        }

        private static void Greek(string spec)
        {
            foreach (KeyValuePair<string, string> pair in Split(spec)) Commands[pair.Key] = new MathSymbol(pair.Value, AtomClass.Ord, false, false, true);
        }

        private static void Large(bool limits, string spec)
        {
            foreach (KeyValuePair<string, string> pair in Split(spec)) Commands[pair.Key] = new MathSymbol(pair.Value, AtomClass.Op, true, limits, false);
        }

        private static void Pairs(Dictionary<string, string> map, string spec)
        {
            foreach (KeyValuePair<string, string> pair in Split(spec)) map[pair.Key] = pair.Value;
        }

        private static void Classes(AtomClass cls, string characters)
        {
            foreach (string item in characters.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // "，；" lists several characters in one item.
                if (item.Length > 1 && !Char.IsSurrogate(item[0])) foreach (char c in item) CharacterClasses[c.ToString()] = cls;
                else CharacterClasses[item] = cls;
            }
        }

        // ------------------------------------------------------------------ math alphabets

        private static readonly int[] GreekCapitalBase = { 0, 0x1D6E2, 0x1D6A8, 0x1D71C, 0, 0x1D756, 0x1D790 };

        /// <summary>
        /// Maps one character (a letter, digit or Greek letter, possibly a surrogate pair) to the given math alphabet.
        /// Other characters are returned unchanged.
        /// </summary>
        public static string MapCharacter(string text, MathVariant variant)
        {
            bool slanted;
            return MapCharacter(text, variant, out slanted);
        }

        public static string MapCharacter(string text, MathVariant variant, out bool slanted)
        {
            slanted = false;
            if (String.IsNullOrEmpty(text) || text.Length != 1) return text;
            char c = text[0];
            if (c >= 'A' && c <= 'Z') return Letter(c - 'A', true, variant, out slanted);
            if (c >= 'a' && c <= 'z') return Letter(c - 'a', false, variant, out slanted);
            if (c >= '0' && c <= '9') return Digit(c - '0', variant);
            int index;
            bool upper;
            if (GreekIndex(c, out index, out upper)) return GreekLetter(c, index, upper, variant, out slanted);
            return text;
        }

        private static string Letter(int index, bool upper, MathVariant variant, out bool slanted)
        {
            slanted = false;
            char plain = (char)((upper ? 'A' : 'a') + index);
            int code;
            switch (variant)
            {
                case MathVariant.Roman:
                    return plain.ToString();
                case MathVariant.Default:
                case MathVariant.Italic:
                    slanted = true;
                    if (!upper && plain == 'h') return "ℎ";
                    code = (upper ? 0x1D434 : 0x1D44E) + index;
                    break;
                case MathVariant.Bold: code = (upper ? 0x1D400 : 0x1D41A) + index; break;
                case MathVariant.BoldItalic: slanted = true; code = (upper ? 0x1D468 : 0x1D482) + index; break;
                case MathVariant.Sans: code = (upper ? 0x1D5A0 : 0x1D5BA) + index; break;
                case MathVariant.SansBold: code = (upper ? 0x1D5D4 : 0x1D5EE) + index; break;
                case MathVariant.SansItalic: slanted = true; code = (upper ? 0x1D608 : 0x1D622) + index; break;
                case MathVariant.Mono: code = (upper ? 0x1D670 : 0x1D68A) + index; break;
                case MathVariant.Script:
                    {
                        string hole = Hole(plain, upper ? "Bℬ Eℰ Fℱ Hℋ Iℐ Lℒ Mℳ Rℛ" : "eℯ gℊ oℴ");
                        if (hole != null) return hole;
                        code = (upper ? 0x1D49C : 0x1D4B6) + index;
                        break;
                    }
                case MathVariant.BoldScript: code = (upper ? 0x1D4D0 : 0x1D4EA) + index; break;
                case MathVariant.Fraktur:
                    {
                        string hole = upper ? Hole(plain, "Cℭ Hℌ Iℑ Rℜ Zℨ") : null;
                        if (hole != null) return hole;
                        code = (upper ? 0x1D504 : 0x1D51E) + index;
                        break;
                    }
                case MathVariant.BoldFraktur: code = (upper ? 0x1D56C : 0x1D586) + index; break;
                case MathVariant.Blackboard:
                    {
                        string hole = upper ? Hole(plain, "Cℂ Hℍ Nℕ Pℙ Qℚ Rℝ Zℤ") : null;
                        if (hole != null) return hole;
                        code = (upper ? 0x1D538 : 0x1D552) + index;
                        break;
                    }
                default: return plain.ToString();
            }
            return Char.ConvertFromUtf32(code);
        }

        private static string Hole(char letter, string table)
        {
            foreach (string item in table.Split(' ')) if (item.Length == 2 && item[0] == letter) return item.Substring(1);
            return null;
        }

        private static string Digit(int digit, MathVariant variant)
        {
            int start;
            switch (variant)
            {
                case MathVariant.Bold:
                case MathVariant.BoldItalic: start = 0x1D7CE; break;
                case MathVariant.Blackboard: start = 0x1D7D8; break;
                case MathVariant.Sans: start = 0x1D7E2; break;
                case MathVariant.SansBold: start = 0x1D7EC; break;
                case MathVariant.Mono: start = 0x1D7F6; break;
                default: return ((char)('0' + digit)).ToString();
            }
            return Char.ConvertFromUtf32(start + digit);
        }

        /// <summary>Position in the Greek part of the math alphabets: Α..Ω (with ϴ) and ∇, α..ω and ∂ ϵ ϑ ϰ ϕ ϱ ϖ.</summary>
        private static bool GreekIndex(char c, out int index, out bool upper)
        {
            upper = false;
            index = -1;
            if (c >= 'Α' && c <= 'Ρ') { upper = true; index = c - 'Α'; }
            else if (c == 'ϴ') { upper = true; index = 17; }
            else if (c >= 'Σ' && c <= 'Ω') { upper = true; index = c - 'Σ' + 18; }
            else if (c == '∇') { upper = true; index = 25; }
            else if (c >= 'α' && c <= 'ω') index = c - 'α';
            else if (c == '∂') index = 25;
            else if (c == 'ϵ') index = 26;
            else if (c == 'ϑ') index = 27;
            else if (c == 'ϰ') index = 28;
            else if (c == 'ϕ') index = 29;
            else if (c == 'ϱ') index = 30;
            else if (c == 'ϖ') index = 31;
            return index >= 0;
        }

        private static string GreekLetter(char c, int index, bool upper, MathVariant variant, out bool slanted)
        {
            slanted = false;
            int block;
            switch (variant)
            {
                case MathVariant.Default:
                    // TeX: lowercase Greek (and ∂) italic, capitals upright.
                    if (upper) return c.ToString();
                    block = 1;
                    break;
                case MathVariant.Italic: block = 1; break;
                case MathVariant.Bold: block = 2; break;
                case MathVariant.BoldItalic: block = 3; break;
                case MathVariant.SansBold: block = 5; break;
                default: return c.ToString();
            }
            slanted = block == 1 || block == 3;
            int code = GreekCapitalBase[block] + (upper ? index : 26 + index);
            return Char.ConvertFromUtf32(code);
        }
    }
}

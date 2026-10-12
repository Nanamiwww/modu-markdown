using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexMarkdownReader.Rendering
{
    /// <summary>Layout constants in em of the current size (TeX's values; Cambria Math is close to them).</summary>
    internal static class MathMetrics
    {
        public const double AxisHeight = 0.25;
        public const double XHeight = 0.45;
        public const double DefaultRule = 0.04;

        /// <summary>Thickness of fraction bars and radical overlines: at least one device-independent pixel.</summary>
        public static double Rule(double em) { return Math.Max(1.0, 0.05 * em); }

        public static Pen Stroke(Brush ink, double thickness)
        {
            Pen pen = new Pen(ink, thickness);
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.LineJoin = PenLineJoin.Round;
            return pen;
        }

        /// <summary>A filled arrowhead with its tip at <paramref name="tip"/>, pointing right (direction 1) or left (-1).
        /// Barbs: 0 both, 1 upper only (harpoon), 2 lower only.</summary>
        public static void ArrowHead(DrawingContext dc, Brush ink, Point tip, double direction, double length, double halfWidth, int barbs)
        {
            double backX = tip.X - direction * length, notchX = tip.X - direction * length * 0.72;
            StreamGeometry head = new StreamGeometry();
            using (StreamGeometryContext g = head.Open())
            {
                g.BeginFigure(tip, true, true);
                if (barbs == 0)
                {
                    g.LineTo(new Point(backX, tip.Y - halfWidth), true, false);
                    g.LineTo(new Point(notchX, tip.Y), true, false);
                    g.LineTo(new Point(backX, tip.Y + halfWidth), true, false);
                }
                else if (barbs == 1)
                {
                    g.LineTo(new Point(backX, tip.Y - halfWidth), true, false);
                    g.LineTo(new Point(notchX, tip.Y + halfWidth * 0.18), true, false);
                }
                else
                {
                    g.LineTo(new Point(backX, tip.Y + halfWidth), true, false);
                    g.LineTo(new Point(notchX, tip.Y - halfWidth * 0.18), true, false);
                }
            }
            head.Freeze();
            dc.DrawGeometry(ink, null, head);
        }
    }

    /// <summary>
    /// A box of the formula layout, measured like TeX: an advance width plus the height above and the depth below the
    /// baseline. Composite boxes place their children by baseline; rules, radicals, stretchy delimiters and arrows are
    /// drawn in OnRender.
    /// </summary>
    internal abstract class MathBox : Panel
    {
        public double Ascent;
        public double Descent;
        public double Advance;
        /// <summary>How far the ink of a slanted (italic) letter leans past its advance; superscripts move right by it.</summary>
        public double Italic;

        protected static readonly Size Unbounded = new Size(Double.PositiveInfinity, Double.PositiveInfinity);

        protected override Size MeasureOverride(Size availableSize)
        {
            try
            {
                MeasureChildren();
                Layout();
            }
            catch (Exception)
            {
                // A layout error must not take the window down: the box just stays empty.
                Advance = 0;
                Ascent = 0;
                Descent = 0;
            }
            return new Size(Math.Max(0, Advance), Math.Max(0, Ascent + Descent));
        }

        protected virtual void MeasureChildren()
        {
            foreach (UIElement child in Children) child.Measure(Unbounded);
        }

        /// <summary>Computes Ascent, Descent, Advance and the children's positions (children are already measured).</summary>
        protected abstract void Layout();

        protected override Size ArrangeOverride(Size finalSize)
        {
            try { Place(); }
            catch (Exception) { }
            return finalSize;
        }

        protected virtual void Place() { }

        protected sealed override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            try { Draw(dc); }
            catch (Exception) { }
        }

        /// <summary>Draws rules, radical signs, delimiters, arrows and glyphs (children draw themselves).</summary>
        protected virtual void Draw(DrawingContext dc) { }

        /// <summary>Arranges a child with its baseline at (x, baseline) of this box.</summary>
        protected static void PlaceAt(MathBox child, double x, double baseline)
        {
            child.Arrange(new Rect(x, baseline - child.Ascent, Math.Max(0, child.Advance), Math.Max(0, child.Ascent + child.Descent)));
        }

        /// <summary>Self-test: the structure as text, for example frac(1|2) or x^(2)_(i).</summary>
        public abstract void Describe(StringBuilder text, bool spaces);

        public string Describe(bool spaces)
        {
            StringBuilder text = new StringBuilder();
            Describe(text, spaces);
            return text.ToString();
        }

        public virtual bool IsEmpty { get { return false; } }
    }

    /// <summary>Advance width and ink extents of a text run, cached by font, size and text.</summary>
    internal sealed class GlyphMetrics
    {
        public double Width, Baseline, InkTop, InkBottom, InkLeft, InkRight;

        private static readonly Dictionary<string, GlyphMetrics> Cache = new Dictionary<string, GlyphMetrics>(StringComparer.Ordinal);
        private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("zh-CN");

        public static FormattedText Format(string text, Typeface face, double size, Brush ink)
        {
            return new FormattedText(text, Culture, FlowDirection.LeftToRight, face, Math.Max(1, Math.Min(1000, size)), ink ?? Brushes.Black, 1.0);
        }

        public static GlyphMetrics Get(string text, Typeface face, double size)
        {
            if (String.IsNullOrEmpty(text)) return new GlyphMetrics();
            string key = face.FontFamily.Source + "|" + face.Style.ToString() + "|" + face.Weight.ToString() + "|" +
                size.ToString("R", CultureInfo.InvariantCulture) + "|" + text;
            GlyphMetrics metrics;
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out metrics)) return metrics;
            }
            FormattedText formatted = Format(text, face, size, Brushes.Black);
            metrics = new GlyphMetrics();
            metrics.Width = formatted.WidthIncludingTrailingWhitespace;
            metrics.Baseline = formatted.Baseline;
            Rect ink = Rect.Empty;
            try
            {
                Geometry outline = formatted.BuildGeometry(new Point(0, 0));
                if (outline != null) ink = outline.Bounds;
            }
            catch { ink = Rect.Empty; }
            if (ink.IsEmpty)
            {
                metrics.InkTop = metrics.Baseline;
                metrics.InkBottom = metrics.Baseline;
                metrics.InkLeft = 0;
                metrics.InkRight = metrics.Width;
            }
            else
            {
                metrics.InkTop = ink.Top;
                metrics.InkBottom = ink.Bottom;
                metrics.InkLeft = ink.Left;
                metrics.InkRight = ink.Right;
            }
            lock (Cache)
            {
                if (Cache.Count > 20000) Cache.Clear();
                Cache[key] = metrics;
            }
            return metrics;
        }
    }

    /// <summary>A run of characters in one font: a letter, a number, an operator, a function name or text.</summary>
    internal sealed class MathGlyph : MathBox
    {
        public readonly string Text;
        /// <summary>The characters as written in the source (before mapping to math alphabets); used by the self-test.</summary>
        public readonly string Original;
        public readonly MathVariant Variant;
        public readonly double Em;
        public readonly bool Slanted;
        public readonly Typeface Face;
        public readonly Brush Ink;
        public double InkLeft, InkRight;
        private double baseline;
        private FormattedText formatted;

        public MathGlyph(string text, string original, MathVariant variant, Typeface face, double em, Brush ink, bool slanted)
        {
            Text = text ?? "";
            Original = original;
            Variant = variant;
            Face = face;
            Em = em;
            Ink = ink;
            Slanted = slanted;
        }

        protected override void Layout()
        {
            GlyphMetrics metrics = GlyphMetrics.Get(Text, Face, Em);
            baseline = metrics.Baseline;
            Advance = metrics.Width;
            Ascent = Math.Max(0, metrics.Baseline - metrics.InkTop);
            Descent = Math.Max(0, metrics.InkBottom - metrics.Baseline);
            InkLeft = metrics.InkLeft;
            InkRight = metrics.InkRight;
            Italic = Slanted ? Math.Max(0, metrics.InkRight - metrics.Width) : 0;
        }

        protected override void Draw(DrawingContext dc)
        {
            if (Text.Length == 0) return;
            if (formatted == null) formatted = GlyphMetrics.Format(Text, Face, Em, Ink);
            dc.DrawText(formatted, new Point(0, Ascent - baseline));
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            text.Append(Original ?? Text);
        }
    }

    /// <summary>Horizontal space (automatic spacing between atoms, \, \quad, \hspace ...). May be negative.</summary>
    internal sealed class MathSpace : MathBox
    {
        public readonly double Amount;
        private readonly double em;

        public MathSpace(double amount, double em)
        {
            Amount = amount;
            this.em = em;
        }

        protected override void Layout()
        {
            Advance = Amount;
            Ascent = 0;
            Descent = 0;
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            if (!spaces || em <= 0) return;
            text.Append('‹').Append(Math.Round(Amount / em * 18).ToString(CultureInfo.InvariantCulture)).Append('›');
        }
    }

    /// <summary>A horizontal list of boxes on a common baseline.</summary>
    internal sealed class MathRow : MathBox
    {
        private double[] offsets = new double[0];

        public void Add(MathBox box)
        {
            if (box != null) Children.Add(box);
        }

        public int Count { get { return Children.Count; } }

        public MathBox this[int index] { get { return (MathBox)Children[index]; } }

        public override bool IsEmpty { get { return Children.Count == 0; } }

        protected override void Layout()
        {
            int count = Children.Count;
            offsets = new double[count];
            double x = 0, ascent = 0, descent = 0;
            for (int i = 0; i < count; i++)
            {
                MathBox child = (MathBox)Children[i];
                offsets[i] = x;
                x += child.Advance;
                if (child is MathSpace) continue;
                ascent = Math.Max(ascent, child.Ascent);
                descent = Math.Max(descent, child.Descent);
            }
            Advance = x;
            Ascent = ascent;
            Descent = descent;
            Italic = count > 0 ? ((MathBox)Children[count - 1]).Italic : 0;
        }

        protected override void Place()
        {
            int count = Math.Min(Children.Count, offsets.Length);
            for (int i = 0; i < count; i++) PlaceAt((MathBox)Children[i], offsets[i], Ascent);
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            foreach (UIElement child in Children) ((MathBox)child).Describe(text, spaces);
        }
    }

    /// <summary>Superscript and subscript beside a nucleus (TeX rule 18). With an empty nucleus, scripts are right-aligned
    /// so that {}^{14}_{6}C reads as an isotope.</summary>
    internal sealed class MathScripts : MathBox
    {
        private readonly MathBox nucleus, superscript, subscript;
        private readonly double em;
        private readonly bool display, cramped;
        private double nucleusX, supX, subX, supShift, subShift;

        public MathScripts(MathBox nucleus, MathBox superscript, MathBox subscript, double em, bool display, bool cramped)
        {
            this.nucleus = nucleus;
            this.superscript = superscript;
            this.subscript = subscript;
            this.em = em;
            this.display = display;
            this.cramped = cramped;
            Children.Add(nucleus);
            if (superscript != null) Children.Add(superscript);
            if (subscript != null) Children.Add(subscript);
        }

        protected override void Layout()
        {
            double xHeight = MathMetrics.XHeight * em;
            double scriptEm = em * 0.71;
            bool character = nucleus is MathGlyph;
            double u = character ? 0 : nucleus.Ascent - 0.35 * scriptEm;
            double v = character ? 0 : nucleus.Descent + 0.05 * scriptEm;
            if (superscript == null)
            {
                v = Math.Max(v, Math.Max(0.15 * em, subscript.Ascent - 0.8 * xHeight));
            }
            else
            {
                double sup1 = display ? 0.413 * em : (cramped ? 0.289 * em : 0.363 * em);
                u = Math.Max(u, Math.Max(sup1, superscript.Descent + 0.25 * xHeight));
                if (subscript != null)
                {
                    v = Math.Max(v, 0.247 * em);
                    double theta = MathMetrics.DefaultRule * em;
                    double gap = (u - superscript.Descent) - (subscript.Ascent - v);
                    if (gap < 4 * theta)
                    {
                        v = 4 * theta - (u - superscript.Descent) + subscript.Ascent;
                        double psi = 0.8 * xHeight - (u - superscript.Descent);
                        if (psi > 0)
                        {
                            u += psi;
                            v -= psi;
                        }
                    }
                }
            }
            supShift = u;
            subShift = v;
            double supWidth = superscript == null ? 0 : superscript.Advance;
            double subWidth = subscript == null ? 0 : subscript.Advance;
            double scriptSpace = 0.05 * em;
            if (nucleus.IsEmpty)
            {
                double width = Math.Max(supWidth, subWidth);
                nucleusX = width;
                supX = width - supWidth;
                subX = width - subWidth;
                Advance = width + 0.02 * em;
            }
            else
            {
                nucleusX = 0;
                supX = nucleus.Advance + nucleus.Italic;
                subX = nucleus.Advance;
                Advance = Math.Max(supX + supWidth, subX + subWidth) + scriptSpace;
            }
            double ascent = nucleus.Ascent, descent = nucleus.Descent;
            if (superscript != null)
            {
                ascent = Math.Max(ascent, superscript.Ascent + u);
                descent = Math.Max(descent, superscript.Descent - u);
            }
            if (subscript != null)
            {
                ascent = Math.Max(ascent, subscript.Ascent - v);
                descent = Math.Max(descent, subscript.Descent + v);
            }
            Ascent = ascent;
            Descent = descent;
            Italic = 0;
        }

        protected override void Place()
        {
            PlaceAt(nucleus, nucleusX, Ascent);
            if (superscript != null) PlaceAt(superscript, supX, Ascent - supShift);
            if (subscript != null) PlaceAt(subscript, subX, Ascent + subShift);
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            nucleus.Describe(text, spaces);
            if (superscript != null) { text.Append("^("); superscript.Describe(text, spaces); text.Append(')'); }
            if (subscript != null) { text.Append("_("); subscript.Describe(text, spaces); text.Append(')'); }
        }
    }

    /// <summary>A fraction (TeX rule 15); without a bar it is a stack, as in \binom.</summary>
    internal sealed class MathFraction : MathBox
    {
        private readonly MathBox numerator, denominator;
        private readonly double em, thickness;
        private readonly bool display;
        private readonly Brush ink;
        private double numX, denX, numShift, denShift, barTop;

        public MathFraction(MathBox numerator, MathBox denominator, double em, bool display, double thickness, Brush ink)
        {
            this.numerator = numerator;
            this.denominator = denominator;
            this.em = em;
            this.display = display;
            this.thickness = thickness;
            this.ink = ink;
            SnapsToDevicePixels = true;
            Children.Add(numerator);
            Children.Add(denominator);
        }

        public bool HasBar { get { return thickness > 0; } }

        protected override void Layout()
        {
            double axis = MathMetrics.AxisHeight * em;
            double pad = 0.12 * em;
            double width = Math.Max(numerator.Advance, denominator.Advance);
            double u = display ? 0.677 * em : 0.394 * em;
            double v = display ? 0.686 * em : 0.345 * em;
            if (thickness > 0)
            {
                double clearance = display ? 3 * thickness : thickness;
                u = Math.Max(u, clearance + axis + thickness / 2 + numerator.Descent);
                v = Math.Max(v, clearance - axis + thickness / 2 + denominator.Ascent);
            }
            else
            {
                double clearance = (display ? 7 : 3) * MathMetrics.DefaultRule * em;
                double gap = (u - numerator.Descent) - (denominator.Ascent - v);
                if (gap < clearance)
                {
                    u += (clearance - gap) / 2;
                    v += (clearance - gap) / 2;
                }
            }
            numShift = u;
            denShift = v;
            numX = pad + (width - numerator.Advance) / 2;
            denX = pad + (width - denominator.Advance) / 2;
            Advance = width + 2 * pad;
            Ascent = Math.Max(u + numerator.Ascent, axis + thickness / 2);
            Descent = Math.Max(v + denominator.Descent, thickness / 2 - axis);
            barTop = Ascent - axis - thickness / 2;
        }

        protected override void Place()
        {
            PlaceAt(numerator, numX, Ascent - numShift);
            PlaceAt(denominator, denX, Ascent + denShift);
        }

        protected override void Draw(DrawingContext dc)
        {
            if (thickness <= 0) return;
            double inset = 0.06 * em;
            dc.DrawRectangle(ink, null, new Rect(inset, barTop, Math.Max(0, Advance - 2 * inset), thickness));
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            text.Append(thickness > 0 ? "frac(" : "stack(");
            numerator.Describe(text, spaces);
            text.Append('|');
            denominator.Describe(text, spaces);
            text.Append(')');
        }
    }

    /// <summary>A radical sign drawn to the height of its contents (TeX rule 11), with an optional index.</summary>
    internal sealed class MathRadical : MathBox
    {
        private readonly MathBox body, index;
        private readonly double em;
        private readonly bool display;
        private readonly Brush ink;
        private double signX, signWidth, bodyX, indexX, indexShift, top, bottom, rule;

        public MathRadical(MathBox body, MathBox index, double em, bool display, Brush ink)
        {
            this.body = body;
            this.index = index;
            this.em = em;
            this.display = display;
            this.ink = ink;
            Children.Add(body);
            if (index != null) Children.Add(index);
        }

        protected override void Layout()
        {
            rule = MathMetrics.Rule(em);
            double phi = display ? MathMetrics.XHeight * em : rule;
            double clearance = rule + phi / 4;
            double innerAscent = Math.Max(body.Ascent, 0.6 * em) + clearance + rule;
            double innerDescent = Math.Max(body.Descent, 0.1 * em) + 0.04 * em;
            double height = innerAscent + innerDescent;
            signWidth = em * Math.Min(1.0, 0.56 + 0.06 * Math.Max(0, height / em - 1.2));
            signX = 0;
            double ascent = innerAscent;
            if (index != null && !index.IsEmpty)
            {
                double kern = 0.05 * em;
                double reach = 0.5 * signWidth;
                signX = Math.Max(0, index.Advance + kern - reach);
                indexX = signX + reach - kern - index.Advance;
                // TeX raises the index so that its bottom sits at 60% of the sign's height.
                indexShift = -innerDescent + 0.6 * height + index.Descent;
                ascent = Math.Max(ascent, indexShift + index.Ascent);
            }
            bodyX = signX + signWidth + 0.03 * em;
            Advance = bodyX + body.Advance + 0.08 * em;
            Ascent = ascent;
            Descent = innerDescent;
            top = Ascent - innerAscent;
            bottom = Ascent + innerDescent;
        }

        protected override void Place()
        {
            PlaceAt(body, bodyX, Ascent);
            if (index != null) PlaceAt(index, indexX, Ascent - indexShift);
        }

        protected override void Draw(DrawingContext dc)
        {
            double h = bottom - top;
            double lineY = top + rule / 2;
            Point tickStart = new Point(signX + 0.02 * em, top + 0.62 * h);
            Point tickTop = new Point(signX + 0.22 * signWidth, top + 0.54 * h);
            Point valley = new Point(signX + 0.5 * signWidth, bottom - rule * 0.6);
            Point peak = new Point(signX + signWidth, lineY);
            Point end = new Point(Advance - 0.03 * em, lineY);
            StreamGeometry sign = new StreamGeometry();
            using (StreamGeometryContext g = sign.Open())
            {
                g.BeginFigure(tickStart, false, false);
                g.LineTo(tickTop, true, true);
                g.LineTo(valley, true, true);
                g.LineTo(peak, true, true);
                g.LineTo(end, true, true);
            }
            sign.Freeze();
            Pen pen = new Pen(ink, rule);
            pen.LineJoin = PenLineJoin.Round;
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Flat;
            dc.DrawGeometry(null, pen, sign);
            Pen heavy = new Pen(ink, rule * 2.2);
            heavy.StartLineCap = PenLineCap.Round;
            heavy.EndLineCap = PenLineCap.Round;
            dc.DrawLine(heavy, tickTop, new Point(valley.X - rule * 0.3, valley.Y - rule * 0.6));
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            if (index != null && !index.IsEmpty)
            {
                text.Append("root(");
                index.Describe(text, spaces);
                text.Append('|');
            }
            else text.Append("sqrt(");
            body.Describe(text, spaces);
            text.Append(')');
        }
    }

    /// <summary>
    /// A delimiter centered on the math axis: the font's glyph when that is tall enough, otherwise one of the larger
    /// versions in Cambria Math's MATH table (a size variant or stacked pieces); without that table the glyph is scaled
    /// up a little or the shape is drawn to the requested height. "." is the empty delimiter.
    /// </summary>
    internal sealed class MathDelimiter : MathBox
    {
        public readonly string Kind;
        public double TargetHeight;
        /// <summary>\big, \Big, \bigg, \Bigg: always larger than the normal glyph, even when that is tall enough.</summary>
        public bool Enlarged;
        private readonly double em;
        private readonly Brush ink;
        private readonly Typeface face;
        private bool useGlyph;
        private double glyphSize, glyphOriginY, stem;
        private FormattedText formatted;
        private StretchedGlyph stretched;

        /// <summary>Self-test: the delimiter came from the font's larger versions (1 = a size variant, more = stacked pieces).</summary>
        public int FontPieces { get { return stretched == null ? 0 : stretched.Pieces; } }

        public MathDelimiter(string kind, double em, Brush ink, Typeface face)
        {
            Kind = String.IsNullOrEmpty(kind) ? "." : kind;
            this.em = em;
            this.ink = ink;
            this.face = face;
        }

        protected override void Layout()
        {
            double axis = MathMetrics.AxisHeight * em;
            stem = Math.Max(1.0, 0.075 * em);
            formatted = null;
            stretched = null;
            if (Kind == ".")
            {
                useGlyph = false;
                Advance = 0.12 * em;
                Ascent = 0;
                Descent = 0;
                return;
            }
            GlyphMetrics natural = GlyphMetrics.Get(Kind, face, em);
            double naturalHeight = Math.Max(0.5 * em, natural.InkBottom - natural.InkTop);
            double target = TargetHeight;
            if (target > naturalHeight * 1.02 || Enlarged)
            {
                MathFontVariants variants = MathFontVariants.Instance;
                if (variants != null) stretched = variants.Vertical(Kind, em, target);
                if (stretched != null)
                {
                    useGlyph = false;
                    Advance = stretched.Width;
                    Ascent = axis + stretched.Height / 2;
                    Descent = stretched.Height / 2 - axis;
                    return;
                }
            }
            if (Enlarged && target <= naturalHeight * 1.02) target = naturalHeight * 1.2;
            useGlyph = target <= naturalHeight * 1.02 || (target <= naturalHeight * 1.6 && Kind != "|" && Kind != "‖");
            if (useGlyph)
            {
                glyphSize = target <= naturalHeight * 1.02 ? em : em * target / naturalHeight;
                GlyphMetrics metrics = GlyphMetrics.Get(Kind, face, glyphSize);
                double inkHeight = metrics.InkBottom - metrics.InkTop;
                Advance = metrics.Width;
                Ascent = axis + inkHeight / 2;
                Descent = inkHeight / 2 - axis;
                glyphOriginY = (Ascent - axis) - (metrics.InkTop + metrics.InkBottom) / 2;
                return;
            }
            double h = target;
            double ratio = Math.Min(6, h / em);
            switch (Kind)
            {
                case "(":
                case ")": Advance = em * (0.32 + 0.04 * ratio); break;
                case "{":
                case "}": Advance = em * 0.5; break;
                case "|": Advance = em * 0.3; break;
                case "‖": Advance = em * 0.45; break;
                case "⟨":
                case "⟩": Advance = em * (0.36 + 0.03 * ratio); break;
                case "/":
                case "\\": Advance = em * (0.3 + 0.1 * ratio); break;
                case "[":
                case "]":
                case "⌊":
                case "⌋":
                case "⌈":
                case "⌉": Advance = em * 0.38; break;
                case "⟦":
                case "⟧": Advance = em * 0.46; break;
                default: Advance = Math.Max(0.3 * em, natural.Width); break;
            }
            Ascent = axis + h / 2;
            Descent = h / 2 - axis;
        }

        protected override void Draw(DrawingContext dc)
        {
            if (Kind == ".") return;
            if (stretched != null)
            {
                stretched.Draw(dc, ink, 0, 0);
                return;
            }
            if (useGlyph)
            {
                if (formatted == null) formatted = GlyphMetrics.Format(Kind, face, glyphSize, ink);
                dc.DrawText(formatted, new Point(0, glyphOriginY));
                return;
            }
            double w = Advance, top = 0, bottom = Ascent + Descent, h = bottom - top, mid = (top + bottom) / 2;
            double t = stem;
            switch (Kind)
            {
                case "(":
                case ")":
                    {
                        bool mirror = Kind == ")";
                        double tip = w - 0.05 * em, outer = 0.06 * em, inner = outer + t;
                        double c1 = (8 * outer - 2 * tip) / 6, c2 = (8 * inner - 2 * tip) / 6, k = 0.16 * h;
                        StreamGeometry paren = new StreamGeometry();
                        using (StreamGeometryContext g = paren.Open())
                        {
                            g.BeginFigure(P(mirror, w, tip, top), true, true);
                            g.BezierTo(P(mirror, w, c1, top + k), P(mirror, w, c1, bottom - k), P(mirror, w, tip, bottom), true, true);
                            g.LineTo(P(mirror, w, tip - 0.25 * t, bottom - 0.1 * t), true, true);
                            g.BezierTo(P(mirror, w, c2, bottom - k), P(mirror, w, c2, top + k), P(mirror, w, tip - 0.25 * t, top + 0.1 * t), true, true);
                        }
                        paren.Freeze();
                        dc.DrawGeometry(ink, null, paren);
                        break;
                    }
                case "[":
                case "]":
                case "⌊":
                case "⌋":
                case "⌈":
                case "⌉":
                case "⟦":
                case "⟧":
                    {
                        bool mirror = Kind == "]" || Kind == "⌋" || Kind == "⌉" || Kind == "⟧";
                        double x0 = 0.1 * em, reach = w - x0 - 0.05 * em, bar = t * 0.8;
                        FillRect(dc, mirror, w, x0, top, t, h);
                        if (Kind == "⟦" || Kind == "⟧") FillRect(dc, mirror, w, x0 + 2.2 * t, top, t * 0.6, h);
                        if (Kind != "⌊" && Kind != "⌋") FillRect(dc, mirror, w, x0, top, reach, bar);
                        if (Kind != "⌈" && Kind != "⌉") FillRect(dc, mirror, w, x0, bottom - bar, reach, bar);
                        break;
                    }
                case "{":
                case "}":
                    {
                        bool mirror = Kind == "}";
                        double pen = t * 0.9, xr = w - 0.06 * em, xm = w * 0.5, xp = 0.06 * em, r = Math.Min(0.25 * em, h / 6);
                        StreamGeometry brace = new StreamGeometry();
                        using (StreamGeometryContext g = brace.Open())
                        {
                            g.BeginFigure(P(mirror, w, xr, top + pen / 2), false, false);
                            g.QuadraticBezierTo(P(mirror, w, xm, top + pen / 2), P(mirror, w, xm, top + r), true, true);
                            g.LineTo(P(mirror, w, xm, mid - r), true, true);
                            g.QuadraticBezierTo(P(mirror, w, xm, mid), P(mirror, w, xp, mid), true, true);
                            g.QuadraticBezierTo(P(mirror, w, xm, mid), P(mirror, w, xm, mid + r), true, true);
                            g.LineTo(P(mirror, w, xm, bottom - r), true, true);
                            g.QuadraticBezierTo(P(mirror, w, xm, bottom - pen / 2), P(mirror, w, xr, bottom - pen / 2), true, true);
                        }
                        brace.Freeze();
                        dc.DrawGeometry(null, MathMetrics.Stroke(ink, pen), brace);
                        break;
                    }
                case "|":
                    dc.DrawRectangle(ink, null, new Rect(w / 2 - t * 0.4, top, t * 0.8, h));
                    break;
                case "‖":
                    dc.DrawRectangle(ink, null, new Rect(w / 3 - t * 0.4, top, t * 0.8, h));
                    dc.DrawRectangle(ink, null, new Rect(2 * w / 3 - t * 0.4, top, t * 0.8, h));
                    break;
                case "⟨":
                case "⟩":
                    {
                        bool mirror = Kind == "⟩";
                        double pen = t * 0.8;
                        StreamGeometry angle = new StreamGeometry();
                        using (StreamGeometryContext g = angle.Open())
                        {
                            g.BeginFigure(P(mirror, w, w - 0.08 * em, top + pen / 2), false, false);
                            g.LineTo(P(mirror, w, 0.08 * em, mid), true, true);
                            g.LineTo(P(mirror, w, w - 0.08 * em, bottom - pen / 2), true, true);
                        }
                        angle.Freeze();
                        dc.DrawGeometry(null, MathMetrics.Stroke(ink, pen), angle);
                        break;
                    }
                case "/":
                    dc.DrawLine(MathMetrics.Stroke(ink, t * 0.8), new Point(w - 0.06 * em, top), new Point(0.06 * em, bottom));
                    break;
                case "\\":
                    dc.DrawLine(MathMetrics.Stroke(ink, t * 0.8), new Point(0.06 * em, top), new Point(w - 0.06 * em, bottom));
                    break;
                default:
                    {
                        // Other delimiters (arrows): the glyph stretched vertically.
                        GlyphMetrics metrics = GlyphMetrics.Get(Kind, face, em);
                        double inkHeight = Math.Max(1, metrics.InkBottom - metrics.InkTop);
                        FormattedText glyph = GlyphMetrics.Format(Kind, face, em, ink);
                        double scale = h / inkHeight;
                        dc.PushTransform(new TranslateTransform((w - metrics.Width) / 2, top - metrics.InkTop * scale));
                        dc.PushTransform(new ScaleTransform(1, scale));
                        dc.DrawText(glyph, new Point(0, 0));
                        dc.Pop();
                        dc.Pop();
                        break;
                    }
            }
        }

        private static Point P(bool mirror, double width, double x, double y)
        {
            return new Point(mirror ? width - x : x, y);
        }

        private void FillRect(DrawingContext dc, bool mirror, double width, double x, double y, double w, double h)
        {
            double left = mirror ? width - x - w : x;
            dc.DrawRectangle(ink, null, new Rect(left, y, Math.Max(0, w), Math.Max(0, h)));
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            text.Append(Kind);
        }
    }

    /// <summary>\left ... \middle ... \right: delimiters sized to the contents (TeX: 90.1% of the extent around the axis,
    /// at most 0.5em short of it).</summary>
    internal sealed class MathDelimited : MathBox
    {
        private readonly List<MathBox> parts = new List<MathBox>();
        private readonly List<MathBox> segments = new List<MathBox>();
        private readonly List<MathDelimiter> delimiters = new List<MathDelimiter>();
        private readonly double em;
        private double[] offsets = new double[0];

        public MathDelimited(string left, IList<MathBox> content, IList<string> middles, string right, double em, Brush ink, Typeface face)
        {
            this.em = em;
            AddDelimiter(left, ink, face);
            for (int i = 0; i < content.Count; i++)
            {
                if (i > 0) AddDelimiter(i - 1 < middles.Count ? middles[i - 1] : ".", ink, face);
                segments.Add(content[i]);
                parts.Add(content[i]);
                Children.Add(content[i]);
            }
            AddDelimiter(right, ink, face);
        }

        private void AddDelimiter(string kind, Brush ink, Typeface face)
        {
            MathDelimiter delimiter = new MathDelimiter(kind, em, ink, face);
            delimiters.Add(delimiter);
            parts.Add(delimiter);
            Children.Add(delimiter);
        }

        public double DelimiterHeight { get { return delimiters.Count > 0 ? delimiters[0].Ascent + delimiters[0].Descent : 0; } }

        protected override void MeasureChildren()
        {
            double axis = MathMetrics.AxisHeight * em, above = 0, below = 0;
            foreach (MathBox segment in segments)
            {
                segment.Measure(Unbounded);
                above = Math.Max(above, segment.Ascent - axis);
                below = Math.Max(below, segment.Descent + axis);
            }
            double half = Math.Max(above, below);
            double target = Math.Max(2 * half * 0.901, 2 * half - 0.5 * em);
            foreach (MathDelimiter delimiter in delimiters)
            {
                if (Math.Abs(delimiter.TargetHeight - target) > 0.01)
                {
                    delimiter.TargetHeight = target;
                    delimiter.InvalidateMeasure();
                }
                delimiter.Measure(Unbounded);
            }
        }

        protected override void Layout()
        {
            offsets = new double[parts.Count];
            double x = 0, ascent = 0, descent = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                offsets[i] = x;
                x += parts[i].Advance;
                ascent = Math.Max(ascent, parts[i].Ascent);
                descent = Math.Max(descent, parts[i].Descent);
            }
            Advance = x;
            Ascent = ascent;
            Descent = descent;
        }

        protected override void Place()
        {
            int count = Math.Min(parts.Count, offsets.Length);
            for (int i = 0; i < count; i++) PlaceAt(parts[i], offsets[i], Ascent);
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            text.Append("\\left").Append(delimiters[0].Kind);
            for (int i = 0; i < segments.Count; i++)
            {
                if (i > 0) text.Append("\\middle").Append(delimiters[i].Kind);
                segments[i].Describe(text, spaces);
            }
            text.Append("\\right").Append(delimiters[delimiters.Count - 1].Kind);
        }
    }

    /// <summary>A box shifted so that its vertical center sits on the math axis (big operators, TeX rule 13).</summary>
    internal sealed class MathCentered : MathBox
    {
        private readonly MathBox child;
        private readonly double em;
        private double shift;

        public MathCentered(MathBox child, double em)
        {
            this.child = child;
            this.em = em;
            Children.Add(child);
        }

        public MathBox Child { get { return child; } }

        protected override void Layout()
        {
            shift = (child.Ascent - child.Descent) / 2 - MathMetrics.AxisHeight * em;
            Ascent = child.Ascent - shift;
            Descent = child.Descent + shift;
            Advance = child.Advance;
            Italic = child.Italic;
        }

        protected override void Place()
        {
            PlaceAt(child, 0, Ascent + shift);
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            child.Describe(text, spaces);
        }
    }

    /// <summary>Limits above and below a nucleus (TeX rule 13a): \sum_{i=1}^{n} in display style, \overset, \underbrace labels.</summary>
    internal sealed class MathUnderOver : MathBox
    {
        private readonly MathBox nucleus, over, under;
        private readonly double em;
        private readonly string kind;
        private double nucleusX, overX, underX, overShift, underShift;

        public MathUnderOver(MathBox nucleus, MathBox over, MathBox under, double em, string kind)
        {
            this.nucleus = nucleus;
            this.over = over;
            this.under = under;
            this.em = em;
            this.kind = kind ?? "limits";
            Children.Add(nucleus);
            if (over != null) Children.Add(over);
            if (under != null) Children.Add(under);
        }

        protected override void Layout()
        {
            double width = nucleus.Advance;
            if (over != null) width = Math.Max(width, over.Advance);
            if (under != null) width = Math.Max(width, under.Advance);
            nucleusX = (width - nucleus.Advance) / 2;
            double ascent = nucleus.Ascent, descent = nucleus.Descent;
            if (over != null)
            {
                overX = (width - over.Advance) / 2 + nucleus.Italic / 2;
                double gap = Math.Max(0.111 * em, 0.2 * em - over.Descent);
                overShift = nucleus.Ascent + gap + over.Descent;
                ascent = overShift + over.Ascent + 0.1 * em;
            }
            if (under != null)
            {
                underX = (width - under.Advance) / 2 - nucleus.Italic / 2;
                double gap = Math.Max(0.167 * em, 0.6 * em - under.Ascent);
                underShift = nucleus.Descent + gap + under.Ascent;
                descent = underShift + under.Descent + 0.1 * em;
            }
            Advance = width;
            Ascent = ascent;
            Descent = descent;
        }

        protected override void Place()
        {
            PlaceAt(nucleus, nucleusX, Ascent);
            if (over != null) PlaceAt(over, overX, Ascent - overShift);
            if (under != null) PlaceAt(under, underX, Ascent + underShift);
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            text.Append(kind).Append('(');
            nucleus.Describe(text, spaces);
            text.Append('|');
            if (over != null) over.Describe(text, spaces);
            text.Append('|');
            if (under != null) under.Describe(text, spaces);
            text.Append(')');
        }
    }

    /// <summary>An accent, line, arrow or brace drawn above or below its nucleus.</summary>
    internal sealed class MathAccent : MathBox
    {
        public readonly string Kind;
        private readonly MathBox nucleus;
        private readonly double em;
        private readonly Brush ink;
        private readonly bool wide, under;
        private double nucleusX, markX, markY, markWidth, markHeight, rule;

        public MathAccent(MathBox nucleus, string kind, double em, Brush ink)
        {
            this.nucleus = nucleus;
            Kind = kind;
            this.em = em;
            this.ink = ink;
            under = kind.StartsWith("under", StringComparison.Ordinal) || kind == "utilde";
            wide = kind.StartsWith("wide", StringComparison.Ordinal) || kind.StartsWith("over", StringComparison.Ordinal) || under;
            SnapsToDevicePixels = kind == "overline" || kind == "underline";
            Children.Add(nucleus);
        }

        protected override void Layout()
        {
            rule = MathMetrics.Rule(em);
            double xHeight = MathMetrics.XHeight * em;
            double width = nucleus.Advance;
            double center = width / 2;
            MathGlyph glyph = SingleGlyph(nucleus);
            if (!wide && glyph != null) center = (glyph.InkLeft + glyph.InkRight) / 2 + (glyph.Slanted ? 0.06 * em : 0);
            double gap = 0.06 * em;
            switch (Kind)
            {
                case "hat":
                case "check": markWidth = 0.42 * em; markHeight = 0.16 * em; break;
                case "widehat":
                case "widecheck": markWidth = Math.Max(0.42 * em, width * 0.95); markHeight = 0.16 * em + 0.03 * em * Math.Min(5, width / em); gap = 0.08 * em; break;
                case "tilde": markWidth = 0.46 * em; markHeight = 0.12 * em; break;
                case "widetilde":
                case "utilde": markWidth = Math.Max(0.46 * em, width * 0.95); markHeight = 0.12 * em + 0.02 * em * Math.Min(5, width / em); gap = 0.08 * em; break;
                case "bar": markWidth = Math.Max(0.4 * em, glyph != null ? (glyph.InkRight - glyph.InkLeft) * 0.9 : width * 0.8); markHeight = rule; break;
                case "vec": markWidth = 0.5 * em; markHeight = 0.2 * em; break;
                case "dot": markWidth = 0.12 * em; markHeight = 0.11 * em; break;
                case "ddot": markWidth = 0.34 * em; markHeight = 0.11 * em; break;
                case "dddot": markWidth = 0.56 * em; markHeight = 0.11 * em; break;
                case "ddddot": markWidth = 0.78 * em; markHeight = 0.11 * em; break;
                case "breve": markWidth = 0.36 * em; markHeight = 0.12 * em; break;
                case "acute":
                case "grave": markWidth = 0.22 * em; markHeight = 0.17 * em; break;
                case "mathring": markWidth = 0.2 * em; markHeight = 0.2 * em; break;
                case "overline":
                case "underline": markWidth = width; markHeight = rule; gap = 3 * rule; break;
                case "overbrace":
                case "underbrace": markWidth = width; markHeight = 0.3 * em; gap = 0.1 * em; break;
                default: markWidth = Math.Max(width, 0.8 * em); markHeight = 0.24 * em; gap = 0.08 * em; break;   // arrows
            }
            double total = Math.Max(width, wide ? markWidth : 0);
            nucleusX = (total - width) / 2;
            markX = wide ? (total - markWidth) / 2 : nucleusX + center - markWidth / 2;
            Advance = total;
            if (under)
            {
                markY = nucleus.Descent + gap;
                Ascent = nucleus.Ascent;
                Descent = markY + markHeight + (Kind == "underline" ? rule : 0);
            }
            else
            {
                double baseTop = wide ? nucleus.Ascent : Math.Max(nucleus.Ascent, xHeight);
                markY = baseTop + gap + markHeight;
                Ascent = markY + (Kind == "overline" ? rule : 0.02 * em);
                Descent = nucleus.Descent;
            }
            Italic = nucleus.Italic;
        }

        private static MathGlyph SingleGlyph(MathBox box)
        {
            MathGlyph glyph = box as MathGlyph;
            if (glyph != null) return glyph;
            MathRow row = box as MathRow;
            if (row != null && row.Count == 1) return SingleGlyph(row[0]);
            return null;
        }

        protected override void Place()
        {
            PlaceAt(nucleus, nucleusX, Ascent);
        }

        protected override void Draw(DrawingContext dc)
        {
            // Mark box in this element's coordinates: top..bottom, left..right.
            double top = under ? Ascent + markY : Ascent - markY;
            double bottom = top + markHeight;
            double left = markX, right = markX + markWidth, center = (left + right) / 2;
            double t = Math.Max(0.9, 0.055 * em);
            switch (Kind)
            {
                case "hat":
                case "widehat":
                    Poly(dc, t, new Point(left, bottom), new Point(center, top), new Point(right, bottom));
                    break;
                case "check":
                case "widecheck":
                    Poly(dc, t, new Point(left, top), new Point(center, bottom), new Point(right, top));
                    break;
                case "tilde":
                case "widetilde":
                case "utilde":
                    {
                        StreamGeometry wave = new StreamGeometry();
                        using (StreamGeometryContext g = wave.Open())
                        {
                            g.BeginFigure(new Point(left, top + 0.75 * markHeight), false, false);
                            g.BezierTo(new Point(left + markWidth * 0.3, top - 0.35 * markHeight), new Point(right - markWidth * 0.3, bottom + 0.35 * markHeight),
                                new Point(right, top + 0.25 * markHeight), true, true);
                        }
                        wave.Freeze();
                        dc.DrawGeometry(null, MathMetrics.Stroke(ink, t), wave);
                        break;
                    }
                case "bar":
                case "overline":
                case "underline":
                    dc.DrawRectangle(ink, null, new Rect(left, top, Math.Max(0, markWidth), rule));
                    break;
                case "dot":
                case "ddot":
                case "dddot":
                case "ddddot":
                    {
                        int dots = Kind == "dot" ? 1 : Kind == "ddot" ? 2 : Kind == "dddot" ? 3 : 4;
                        double r = 0.055 * em, spacing = 0.22 * em;
                        double first = center - spacing * (dots - 1) / 2;
                        for (int i = 0; i < dots; i++) dc.DrawEllipse(ink, null, new Point(first + i * spacing, (top + bottom) / 2), r, r);
                        break;
                    }
                case "breve":
                    {
                        StreamGeometry cup = new StreamGeometry();
                        using (StreamGeometryContext g = cup.Open())
                        {
                            g.BeginFigure(new Point(left, top), false, false);
                            g.BezierTo(new Point(left, bottom + markHeight * 0.3), new Point(right, bottom + markHeight * 0.3), new Point(right, top), true, true);
                        }
                        cup.Freeze();
                        dc.DrawGeometry(null, MathMetrics.Stroke(ink, t * 0.9), cup);
                        break;
                    }
                case "acute":
                    dc.DrawLine(MathMetrics.Stroke(ink, t), new Point(left, bottom), new Point(right, top));
                    break;
                case "grave":
                    dc.DrawLine(MathMetrics.Stroke(ink, t), new Point(left, top), new Point(right, bottom));
                    break;
                case "mathring":
                    dc.DrawEllipse(null, MathMetrics.Stroke(ink, t * 0.8), new Point(center, (top + bottom) / 2), markWidth / 2 - t / 2, markHeight / 2 - t / 2);
                    break;
                case "overbrace":
                case "underbrace":
                    DrawBrace(dc, left, right, top, bottom, Kind == "underbrace", t);
                    break;
                default:
                    DrawArrow(dc, left, right, (top + bottom) / 2, t);
                    break;
            }
        }

        private void Poly(DrawingContext dc, double thickness, Point a, Point b, Point c)
        {
            StreamGeometry line = new StreamGeometry();
            using (StreamGeometryContext g = line.Open())
            {
                g.BeginFigure(a, false, false);
                g.LineTo(b, true, true);
                g.LineTo(c, true, true);
            }
            line.Freeze();
            dc.DrawGeometry(null, MathMetrics.Stroke(ink, thickness), line);
        }

        private void DrawArrow(DrawingContext dc, double left, double right, double y, double t)
        {
            bool toRight = Kind.Contains("right") || Kind == "vec";
            bool toLeft = Kind.Contains("left");
            double head = 0.24 * em, half = 0.1 * em;
            Pen pen = new Pen(ink, t);
            dc.DrawLine(pen, new Point(left + (toLeft ? head * 0.5 : 0), y), new Point(right - (toRight ? head * 0.5 : 0), y));
            if (toRight) MathMetrics.ArrowHead(dc, ink, new Point(right, y), 1, head, half, 0);
            if (toLeft) MathMetrics.ArrowHead(dc, ink, new Point(left, y), -1, head, half, 0);
        }

        private void DrawBrace(DrawingContext dc, double left, double right, double top, double bottom, bool down, double t)
        {
            // A horizontal brace: hooks at the ends, the point in the middle (pointing away from the nucleus).
            double edge = down ? top : bottom;
            double far = down ? bottom : top;
            double stem = (top + bottom) / 2;
            double mid = (left + right) / 2;
            double r = Math.Min(0.25 * em, (right - left) / 6);
            StreamGeometry brace = new StreamGeometry();
            using (StreamGeometryContext g = brace.Open())
            {
                g.BeginFigure(new Point(left + t / 2, edge), false, false);
                g.QuadraticBezierTo(new Point(left + t / 2, stem), new Point(left + r, stem), true, true);
                g.LineTo(new Point(mid - r, stem), true, true);
                g.QuadraticBezierTo(new Point(mid, stem), new Point(mid, far), true, true);
                g.QuadraticBezierTo(new Point(mid, stem), new Point(mid + r, stem), true, true);
                g.LineTo(new Point(right - r, stem), true, true);
                g.QuadraticBezierTo(new Point(right - t / 2, stem), new Point(right - t / 2, edge), true, true);
            }
            brace.Freeze();
            dc.DrawGeometry(null, MathMetrics.Stroke(ink, t), brace);
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            text.Append(Kind).Append('(');
            nucleus.Describe(text, spaces);
            text.Append(')');
        }
    }

    /// <summary>Rows and columns: matrices, arrays, cases and the alignment environments. Centered on the math axis.</summary>
    internal sealed class MathTable : MathBox
    {
        public readonly string Kind;
        private readonly List<List<MathBox>> rows;
        private readonly List<MathBox> tags;
        private readonly List<int> lines;
        private readonly string align;
        private readonly bool[] columnLines;
        private readonly double em, columnGap, rowGap, outerPad, strutAscent, strutDescent;
        private readonly bool pairs, multline;
        private readonly Brush ink;
        private double[] columnX, columnWidth, rowBaseline, lineY, verticalX;
        private double tableWidth, height;

        public MathTable(string kind, List<List<MathBox>> rows, List<MathBox> tags, List<int> lines, string align, bool[] columnLines,
            double em, double columnGap, double rowGap, double outerPad, double strutAscent, double strutDescent, bool pairs, bool multline, Brush ink)
        {
            Kind = kind;
            this.rows = rows;
            this.tags = tags;
            this.lines = lines;
            this.align = String.IsNullOrEmpty(align) ? "c" : align;
            this.columnLines = columnLines;
            this.em = em;
            this.columnGap = columnGap;
            this.rowGap = rowGap;
            this.outerPad = outerPad;
            this.strutAscent = strutAscent;
            this.strutDescent = strutDescent;
            this.pairs = pairs;
            this.multline = multline;
            this.ink = ink;
            SnapsToDevicePixels = true;
            foreach (List<MathBox> row in rows) foreach (MathBox cell in row) Children.Add(cell);
            if (tags != null) foreach (MathBox tag in tags) if (tag != null) Children.Add(tag);
        }

        public int RowCount { get { return rows.Count; } }

        private int Columns
        {
            get
            {
                int count = 0;
                foreach (List<MathBox> row in rows) count = Math.Max(count, row.Count);
                return count;
            }
        }

        private double Gap(int column)
        {
            if (column <= 0) return 0;
            if (pairs) return column % 2 == 1 ? 0 : columnGap * em;
            return columnGap * em;
        }

        private char AlignOf(int row, int column)
        {
            if (multline)
            {
                if (rows.Count > 1 && row == 0) return 'l';
                if (rows.Count > 1 && row == rows.Count - 1) return 'r';
                return 'c';
            }
            return align[Math.Min(column, align.Length - 1)];
        }

        private int LinesBefore(int row)
        {
            return lines != null && row < lines.Count ? lines[row] : 0;
        }

        protected override void Layout()
        {
            int columns = Columns, count = rows.Count;
            columnWidth = new double[columns];
            columnX = new double[columns];
            rowBaseline = new double[count];
            double[] rowAscent = new double[count], rowDescent = new double[count];
            for (int i = 0; i < count; i++)
            {
                rowAscent[i] = strutAscent * em;
                rowDescent[i] = strutDescent * em;
                for (int j = 0; j < rows[i].Count; j++)
                {
                    MathBox cell = rows[i][j];
                    columnWidth[j] = Math.Max(columnWidth[j], cell.Advance);
                    rowAscent[i] = Math.Max(rowAscent[i], cell.Ascent);
                    rowDescent[i] = Math.Max(rowDescent[i], cell.Descent);
                }
            }
            double x = outerPad * em;
            verticalX = new double[columns + 1];
            for (int j = 0; j < columns; j++)
            {
                double gap = Gap(j);
                x += gap;
                verticalX[j] = j == 0 ? x / 2 : x - gap / 2;
                columnX[j] = x;
                x += columnWidth[j];
            }
            verticalX[columns] = x + outerPad * em / 2;
            double width = x + outerPad * em;
            double lineGap = 0.15 * em;
            List<double> linePositions = new List<double>();
            double y = 0;
            for (int i = 0; i < count; i++)
            {
                int before = LinesBefore(i);
                for (int k = 0; k < before; k++)
                {
                    y += i == 0 && k == 0 ? 0 : lineGap;
                    linePositions.Add(y);
                    y += lineGap;
                }
                rowBaseline[i] = y + rowAscent[i];
                y = rowBaseline[i] + rowDescent[i];
                if (i < count - 1) y += rowGap * em;
            }
            int after = LinesBefore(count);
            for (int k = 0; k < after; k++)
            {
                y += lineGap;
                linePositions.Add(y);
            }
            lineY = linePositions.ToArray();
            height = y;
            tableWidth = width;
            double tagWidth = 0;
            if (tags != null) foreach (MathBox tag in tags) if (tag != null) tagWidth = Math.Max(tagWidth, tag.Advance);
            if (tagWidth > 0) width += 1.5 * em + tagWidth;
            double axis = MathMetrics.AxisHeight * em;
            Advance = width;
            Ascent = height / 2 + axis;
            Descent = height / 2 - axis;
        }

        protected override void Place()
        {
            if (rowBaseline == null) return;
            for (int i = 0; i < rows.Count && i < rowBaseline.Length; i++)
            {
                for (int j = 0; j < rows[i].Count && j < columnX.Length; j++)
                {
                    MathBox cell = rows[i][j];
                    char mode = AlignOf(i, j);
                    double cellX = multline ? 0 : columnX[j];
                    double width = multline ? tableWidth : columnWidth[j];
                    if (mode == 'c') cellX += (width - cell.Advance) / 2;
                    else if (mode == 'r') cellX += width - cell.Advance;
                    PlaceAt(cell, cellX, rowBaseline[i]);
                }
                if (tags != null && i < tags.Count && tags[i] != null) PlaceAt(tags[i], Advance - tags[i].Advance, rowBaseline[i]);
            }
        }

        protected override void Draw(DrawingContext dc)
        {
            double rule = Math.Max(1.0, 0.04 * em);
            if (lineY != null)
                foreach (double y in lineY) dc.DrawRectangle(ink, null, new Rect(0, y - rule / 2, Math.Max(0, tableWidth), rule));
            if (columnLines != null && verticalX != null)
                for (int j = 0; j < columnLines.Length && j < verticalX.Length; j++)
                    if (columnLines[j]) dc.DrawRectangle(ink, null, new Rect(verticalX[j] - rule / 2, 0, rule, height));
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            text.Append(Kind).Append('[');
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) text.Append("\\\\");
                for (int j = 0; j < rows[i].Count; j++)
                {
                    if (j > 0) text.Append('&');
                    rows[i][j].Describe(text, spaces);
                }
                if (tags != null && i < tags.Count && tags[i] != null)
                {
                    text.Append('#');
                    tags[i].Describe(text, spaces);
                }
            }
            text.Append(']');
        }
    }

    /// <summary>A frame, background, strike-out or visibility change around a box: \boxed, \colorbox, \cancel, \phantom, \smash, \llap ...</summary>
    internal sealed class MathEnclosure : MathBox
    {
        public readonly string Kind;
        private readonly MathBox child;
        private readonly double em, amount;
        private readonly Brush ink, fill;
        private double childX, childShift, pad, border;

        public MathEnclosure(MathBox child, string kind, double em, Brush ink, Brush fill, double amount)
        {
            this.child = child;
            Kind = kind;
            this.em = em;
            this.ink = ink;
            this.fill = fill;
            this.amount = amount;
            if (kind == "phantom" || kind == "hphantom" || kind == "vphantom") child.Opacity = 0;
            Children.Add(child);
        }

        protected override void Layout()
        {
            pad = 0;
            border = 0;
            childX = 0;
            childShift = 0;
            Advance = child.Advance;
            Ascent = child.Ascent;
            Descent = child.Descent;
            Italic = child.Italic;
            switch (Kind)
            {
                case "box":
                case "colorbox":
                case "fcolorbox":
                    pad = 0.3 * em;
                    border = Kind == "colorbox" ? 0 : Math.Max(1.0, 0.04 * em);
                    childX = pad + border;
                    Advance = child.Advance + 2 * (pad + border);
                    Ascent = child.Ascent + pad + border;
                    Descent = child.Descent + pad + border;
                    Italic = 0;
                    break;
                case "hphantom":
                    Ascent = 0;
                    Descent = 0;
                    break;
                case "vphantom":
                    Advance = 0;
                    break;
                case "smash":
                    Ascent = 0;
                    Descent = 0;
                    break;
                case "smasht":
                    Ascent = 0;
                    break;
                case "smashb":
                    Descent = 0;
                    break;
                case "llap":
                    childX = -child.Advance;
                    Advance = 0;
                    break;
                case "rlap":
                    Advance = 0;
                    break;
                case "clap":
                    childX = -child.Advance / 2;
                    Advance = 0;
                    break;
                case "raise":
                    childShift = amount;
                    Ascent = Math.Max(0, child.Ascent + amount);
                    Descent = Math.Max(0, child.Descent - amount);
                    break;
            }
        }

        protected override void Place()
        {
            PlaceAt(child, childX, Ascent - childShift);
        }

        protected override void Draw(DrawingContext dc)
        {
            if (Kind == "colorbox" || Kind == "fcolorbox")
            {
                Rect area = new Rect(0, 0, Math.Max(0, Advance), Math.Max(0, Ascent + Descent));
                if (fill != null) dc.DrawRectangle(fill, null, area);
            }
            double t = Math.Max(0.9, 0.05 * em);
            double width = Advance, height = Ascent + Descent;
            // Strike-outs reach a little past the box, as in the cancel package, so that they stand out from the letters.
            double ox = 0.06 * em, oy = 0.1 * em;
            switch (Kind)
            {
                case "box":
                case "fcolorbox":
                    {
                        Pen frame = new Pen(ink, border);
                        dc.DrawRectangle(null, frame, new Rect(border / 2, border / 2, Math.Max(0, width - border), Math.Max(0, height - border)));
                        break;
                    }
                case "cancel":
                    dc.DrawLine(MathMetrics.Stroke(ink, t), new Point(-ox, height + oy), new Point(width + ox, -oy));
                    break;
                case "cancelto":
                    {
                        // An arrow from the lower left to the value written at the upper right.
                        Point start = new Point(-ox, height + oy), tip = new Point(width + ox + 0.04 * em, -oy - 0.06 * em);
                        double dx = tip.X - start.X, dy = tip.Y - start.Y, length = Math.Sqrt(dx * dx + dy * dy);
                        if (length <= 0) break;
                        double ux = dx / length, uy = dy / length, head = Math.Min(0.26 * em, length * 0.45), half = 0.09 * em;
                        Point back = new Point(tip.X - ux * head, tip.Y - uy * head);
                        dc.DrawLine(MathMetrics.Stroke(ink, t), start, new Point(tip.X - ux * head * 0.7, tip.Y - uy * head * 0.7));
                        StreamGeometry arrow = new StreamGeometry();
                        using (StreamGeometryContext g = arrow.Open())
                        {
                            g.BeginFigure(tip, true, true);
                            g.LineTo(new Point(back.X - uy * half, back.Y + ux * half), true, false);
                            g.LineTo(new Point(tip.X - ux * head * 0.72, tip.Y - uy * head * 0.72), true, false);
                            g.LineTo(new Point(back.X + uy * half, back.Y - ux * half), true, false);
                        }
                        arrow.Freeze();
                        dc.DrawGeometry(ink, null, arrow);
                        break;
                    }
                case "bcancel":
                    dc.DrawLine(MathMetrics.Stroke(ink, t), new Point(-ox, -oy), new Point(width + ox, height + oy));
                    break;
                case "xcancel":
                    dc.DrawLine(MathMetrics.Stroke(ink, t), new Point(-ox, height + oy), new Point(width + ox, -oy));
                    dc.DrawLine(MathMetrics.Stroke(ink, t), new Point(-ox, -oy), new Point(width + ox, height + oy));
                    break;
                case "sout":
                    {
                        double y = Ascent - MathMetrics.AxisHeight * em;
                        dc.DrawLine(new Pen(ink, t), new Point(0, y), new Point(width, y));
                        break;
                    }
                case "not":
                    {
                        double cx = width / 2, half = 0.28 * em;
                        double axisY = Ascent - MathMetrics.AxisHeight * em;
                        dc.DrawLine(MathMetrics.Stroke(ink, t), new Point(cx + half * 0.45, axisY - half * 1.15), new Point(cx - half * 0.45, axisY + half * 1.15));
                        break;
                    }
            }
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            text.Append(Kind).Append('(');
            child.Describe(text, spaces);
            text.Append(')');
        }
    }

    /// <summary>Extensible arrows with labels: \xrightarrow, \xlongequal, the reaction arrows of \ce.</summary>
    internal sealed class MathArrow : MathBox
    {
        public readonly string Kind;
        private readonly MathBox above, below;
        private readonly double em;
        private readonly Brush ink;
        private double length, aboveX, belowX, aboveShift, belowShift, side;

        public MathArrow(string kind, MathBox above, MathBox below, double em, Brush ink)
        {
            Kind = kind;
            this.above = above != null && !above.IsEmpty ? above : null;
            this.below = below != null && !below.IsEmpty ? below : null;
            this.em = em;
            this.ink = ink;
            if (this.above != null) Children.Add(this.above);
            if (this.below != null) Children.Add(this.below);
        }

        private bool IsDoubleArrow { get { return Kind != "right" && Kind != "left" && Kind != "leftright" && Kind != "mapsto"; } }

        protected override void Layout()
        {
            double axis = MathMetrics.AxisHeight * em;
            double labels = 0;
            if (above != null) labels = Math.Max(labels, above.Advance);
            if (below != null) labels = Math.Max(labels, below.Advance);
            side = 0.1 * em;
            length = Math.Max(Kind == "equal" ? 1.4 * em : 1.8 * em, labels + 0.8 * em);
            double half = IsDoubleArrow ? 0.24 * em : 0.13 * em;
            double top = axis + half, bottom = axis - half;
            double ascent = top, descent = -bottom;
            if (above != null)
            {
                aboveShift = top + 0.12 * em + above.Descent;
                ascent = aboveShift + above.Ascent;
                aboveX = side + (length - above.Advance) / 2;
            }
            if (below != null)
            {
                belowShift = -bottom + 0.12 * em + below.Ascent;
                descent = belowShift + below.Descent;
                belowX = side + (length - below.Advance) / 2;
            }
            Advance = length + 2 * side;
            Ascent = Math.Max(ascent, 0);
            Descent = Math.Max(descent, 0);
        }

        protected override void Place()
        {
            if (above != null) PlaceAt(above, aboveX, Ascent - aboveShift);
            if (below != null) PlaceAt(below, belowX, Ascent + belowShift);
        }

        protected override void Draw(DrawingContext dc)
        {
            double t = Math.Max(0.9, 0.05 * em);
            double y = Ascent - MathMetrics.AxisHeight * em;
            double left = side, right = side + length;
            double head = 0.28 * em, half = 0.12 * em, gap = 0.1 * em;
            Pen pen = new Pen(ink, t);
            switch (Kind)
            {
                case "right":
                    dc.DrawLine(pen, new Point(left, y), new Point(right - head * 0.5, y));
                    MathMetrics.ArrowHead(dc, ink, new Point(right, y), 1, head, half, 0);
                    break;
                case "left":
                    dc.DrawLine(pen, new Point(left + head * 0.5, y), new Point(right, y));
                    MathMetrics.ArrowHead(dc, ink, new Point(left, y), -1, head, half, 0);
                    break;
                case "leftright":
                    dc.DrawLine(pen, new Point(left + head * 0.5, y), new Point(right - head * 0.5, y));
                    MathMetrics.ArrowHead(dc, ink, new Point(right, y), 1, head, half, 0);
                    MathMetrics.ArrowHead(dc, ink, new Point(left, y), -1, head, half, 0);
                    break;
                case "mapsto":
                    dc.DrawLine(pen, new Point(left, y - half), new Point(left, y + half));
                    dc.DrawLine(pen, new Point(left, y), new Point(right - head * 0.5, y));
                    MathMetrics.ArrowHead(dc, ink, new Point(right, y), 1, head, half, 0);
                    break;
                case "Right":
                case "Left":
                case "LeftRight":
                    {
                        bool toRight = Kind != "Left", toLeft = Kind != "Right";
                        double inset = head * 0.55;
                        dc.DrawLine(pen, new Point(left + (toLeft ? inset : 0), y - gap), new Point(right - (toRight ? inset : 0), y - gap));
                        dc.DrawLine(pen, new Point(left + (toLeft ? inset : 0), y + gap), new Point(right - (toRight ? inset : 0), y + gap));
                        Pen chevron = MathMetrics.Stroke(ink, t);
                        if (toRight)
                        {
                            dc.DrawLine(chevron, new Point(right - head, y - gap * 2.2), new Point(right, y));
                            dc.DrawLine(chevron, new Point(right - head, y + gap * 2.2), new Point(right, y));
                        }
                        if (toLeft)
                        {
                            dc.DrawLine(chevron, new Point(left + head, y - gap * 2.2), new Point(left, y));
                            dc.DrawLine(chevron, new Point(left + head, y + gap * 2.2), new Point(left, y));
                        }
                        break;
                    }
                case "equal":
                    dc.DrawLine(pen, new Point(left, y - gap * 0.85), new Point(right, y - gap * 0.85));
                    dc.DrawLine(pen, new Point(left, y + gap * 0.85), new Point(right, y + gap * 0.85));
                    break;
                case "arrows":
                    dc.DrawLine(pen, new Point(left, y - gap), new Point(right - head * 0.5, y - gap));
                    MathMetrics.ArrowHead(dc, ink, new Point(right, y - gap), 1, head, half * 0.9, 0);
                    dc.DrawLine(pen, new Point(left + head * 0.5, y + gap), new Point(right, y + gap));
                    MathMetrics.ArrowHead(dc, ink, new Point(left, y + gap), -1, head, half * 0.9, 0);
                    break;
                default:
                    {
                        // Equilibrium harpoons; eqright / eqleft draw the minor direction shorter.
                        double shortStart = left + length * 0.22, shortEnd = right - length * 0.22;
                        double upperLeft = Kind == "eqleft" ? shortStart : left, upperRight = Kind == "eqleft" ? shortEnd : right;
                        double lowerLeft = Kind == "eqright" ? shortStart : left, lowerRight = Kind == "eqright" ? shortEnd : right;
                        dc.DrawLine(pen, new Point(upperLeft, y - gap), new Point(upperRight - t, y - gap));
                        MathMetrics.ArrowHead(dc, ink, new Point(upperRight, y - gap), 1, head, half * 1.2, 1);
                        dc.DrawLine(pen, new Point(lowerLeft + t, y + gap), new Point(lowerRight, y + gap));
                        MathMetrics.ArrowHead(dc, ink, new Point(lowerLeft, y + gap), -1, head, half * 1.2, 2);
                        break;
                    }
            }
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            text.Append("arrow:").Append(Kind).Append('(');
            if (above != null) above.Describe(text, spaces);
            text.Append('|');
            if (below != null) below.Describe(text, spaces);
            text.Append(')');
        }
    }

    /// <summary>A filled rectangle (\rule) or an invisible strut.</summary>
    internal sealed class MathRule : MathBox
    {
        private readonly double width, height, depth;
        private readonly Brush ink;

        public MathRule(double width, double height, double depth, Brush ink)
        {
            this.width = width;
            this.height = height;
            this.depth = depth;
            this.ink = ink;
        }

        protected override void Layout()
        {
            Advance = Math.Max(0, width);
            Ascent = height;
            Descent = depth;
        }

        protected override void Draw(DrawingContext dc)
        {
            if (ink != null && width > 0 && height + depth > 0) dc.DrawRectangle(ink, null, new Rect(0, 0, width, height + depth));
        }

        public override void Describe(StringBuilder text, bool spaces)
        {
            if (ink != null) text.Append("rule");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace CodexMarkdownReader.Rendering
{
    /// <summary>
    /// The larger delimiters of Cambria Math, read from the font's OpenType MATH table: ready-made size variants and,
    /// past the largest one, pieces stacked to any height (TeX does the same with its extension font). When the font or
    /// the table cannot be read, MathDelimiter draws the shapes itself.
    /// </summary>
    internal sealed class MathFontVariants
    {
        private sealed class Part
        {
            public ushort Glyph;
            public double Start, End, Full;   // in em
            public bool Extender;
        }

        private sealed class Construction
        {
            public ushort[] Glyphs;
            public double[] Sizes;            // in em
            public Part[] Parts;              // bottom to top; null without an assembly
        }

        private static readonly object Gate = new object();
        private static MathFontVariants instance;
        private static bool attempted;

        private readonly GlyphTypeface font;
        private readonly double minOverlap;   // in em
        private readonly Dictionary<ushort, Construction> vertical;
        private readonly Dictionary<string, Rect> inks = new Dictionary<string, Rect>(StringComparer.Ordinal);

        private MathFontVariants(GlyphTypeface font, double minOverlap, Dictionary<ushort, Construction> vertical)
        {
            this.font = font;
            this.minOverlap = minOverlap;
            this.vertical = vertical;
        }

        /// <summary>The variants of Cambria Math, or null when the font or its MATH table is not available.</summary>
        public static MathFontVariants Instance
        {
            get
            {
                lock (Gate)
                {
                    if (!attempted)
                    {
                        attempted = true;
                        instance = Load();
                    }
                    return instance;
                }
            }
        }

        public int VerticalCount { get { return vertical.Count; } }

        /// <summary>
        /// The delimiter at least <paramref name="height"/> tall (all in device-independent pixels at size
        /// <paramref name="em"/>): the first size variant that is tall enough, otherwise the stacked pieces; null when
        /// the font has nothing for this character.
        /// </summary>
        public StretchedGlyph Vertical(string text, double em, double height)
        {
            ushort glyph;
            Construction construction;
            if (em <= 0 || !TryGlyph(text, out glyph) || !vertical.TryGetValue(glyph, out construction)) return null;
            int count = construction.Glyphs.Length;
            bool assembly = construction.Parts != null && construction.Parts.Length > 0;
            // The caller only asks when the normal glyph is too small, so the base glyph itself is skipped: \big( is
            // always larger than (, as in TeX.
            int last = -1;
            for (int i = 0; i < count; i++)
            {
                if (construction.Glyphs[i] == glyph) continue;
                last = i;
                if (construction.Sizes[i] * em >= height * 0.98) return Single(construction.Glyphs[i], em, false);
            }
            if (assembly) return Assemble(construction.Parts, em, height);
            // Too tall for the largest size, which cannot be stacked (angle brackets): the largest, with room around it.
            return last >= 0 ? Single(construction.Glyphs[last], em, true) : null;
        }

        private bool TryGlyph(string text, out ushort glyph)
        {
            glyph = 0;
            if (String.IsNullOrEmpty(text) || text.Length > 2) return false;
            int code;
            try { code = Char.ConvertToUtf32(text, 0); }
            catch (ArgumentException) { return false; }
            if (Char.IsSurrogatePair(text, 0) ? text.Length != 2 : text.Length != 1) return false;
            return font.CharacterToGlyphMap.TryGetValue(code, out glyph);
        }

        private Rect Ink(ushort glyph, double em)
        {
            string key = glyph.ToString(CultureInfo.InvariantCulture) + "|" + em.ToString("R", CultureInfo.InvariantCulture);
            Rect ink;
            lock (inks)
            {
                if (inks.TryGetValue(key, out ink)) return ink;
            }
            Geometry outline = font.GetGlyphOutline(glyph, em, em);
            ink = outline == null ? Rect.Empty : outline.Bounds;
            lock (inks)
            {
                if (inks.Count > 4000) inks.Clear();
                inks[key] = ink;
            }
            return ink;
        }

        private double AdvanceOf(ushort glyph, double em)
        {
            double width;
            return font.AdvanceWidths.TryGetValue(glyph, out width) ? width * em : 0;
        }

        /// <summary>
        /// Room between stacked or oversized delimiters and the contents: the pieces of Cambria Math are narrower than
        /// its size variants, and their hooks would otherwise touch the first and last rows of a tall matrix.
        /// </summary>
        private const double Clearance = 0.1;

        private StretchedGlyph Single(ushort glyph, double em, bool padded)
        {
            Rect ink = Ink(glyph, em);
            if (ink.IsEmpty || ink.Height <= 0) return null;
            double advance = AdvanceOf(glyph, em);
            // The glyph's baseline sits -ink.Top below the top of its ink.
            if (padded)
                return new StretchedGlyph(font, em, new ushort[] { glyph }, new double[] { -ink.Top }, new double[] { advance },
                    ink.Width + 2 * Clearance * em, ink.Height, Clearance * em - ink.Left);
            return new StretchedGlyph(font, em, new ushort[] { glyph }, new double[] { -ink.Top }, new double[] { advance },
                Math.Max(advance, ink.Right), ink.Height, 0);
        }

        /// <summary>Stacks the pieces bottom to top, repeating the extenders as often as needed (OpenType MATH, GlyphAssembly).</summary>
        private StretchedGlyph Assemble(Part[] parts, double em, double height)
        {
            double target = height / em;
            bool extensible = false;
            foreach (Part part in parts) if (part.Extender) extensible = true;
            List<Part> sequence = null;
            for (int repeats = 0; repeats <= 200; repeats++)
            {
                sequence = new List<Part>();
                foreach (Part part in parts)
                {
                    if (!part.Extender) sequence.Add(part);
                    else for (int r = 0; r < repeats; r++) sequence.Add(part);
                }
                if (sequence.Count == 0) continue;
                double reach = -minOverlap * (sequence.Count - 1);
                foreach (Part part in sequence) reach += part.Full;
                if (reach >= target || !extensible) break;
            }
            if (sequence == null || sequence.Count == 0) return null;
            int joints = sequence.Count - 1;
            double sum = 0;
            foreach (Part part in sequence) sum += part.Full;
            // The slack is spread over the joints: each overlap grows from the minimum up to what its connectors allow.
            double extra = joints > 0 ? Math.Max(0, sum - minOverlap * joints - target) / joints : 0;
            double[] overlaps = new double[Math.Max(0, joints)];
            double total = sum;
            for (int k = 0; k < joints; k++)
            {
                double allowed = Math.Max(minOverlap, Math.Min(sequence[k].End, sequence[k + 1].Start));
                overlaps[k] = Math.Min(allowed, minOverlap + extra);
                total -= overlaps[k];
            }
            double heightPx = total * em;
            ushort[] glyphs = new ushort[sequence.Count];
            double[] baselines = new double[sequence.Count];
            double[] advances = new double[sequence.Count];
            double bottom = heightPx, inkLeft = Double.PositiveInfinity, inkRight = Double.NegativeInfinity;
            for (int k = 0; k < sequence.Count; k++)
            {
                Part part = sequence[k];
                Rect ink = Ink(part.Glyph, em);
                if (ink.IsEmpty) return null;
                glyphs[k] = part.Glyph;
                baselines[k] = bottom - ink.Bottom;
                advances[k] = AdvanceOf(part.Glyph, em);
                inkLeft = Math.Min(inkLeft, ink.Left);
                inkRight = Math.Max(inkRight, ink.Right);
                if (k < joints) bottom -= (part.Full - overlaps[k]) * em;
            }
            return new StretchedGlyph(font, em, glyphs, baselines, advances, inkRight - inkLeft + 2 * Clearance * em, heightPx, Clearance * em - inkLeft);
        }

        // ------------------------------------------------------------------ reading the font

        private static MathFontVariants Load()
        {
            try
            {
                Typeface typeface = new Typeface(new FontFamily("Cambria Math"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                GlyphTypeface font;
                if (!typeface.TryGetGlyphTypeface(out font) || !IsCambriaMath(font)) return null;
                byte[] data;
                using (Stream stream = font.GetFontStream())
                {
                    if (stream == null || stream.Length <= 0 || stream.Length > 64L * 1024 * 1024) return null;
                    data = new byte[stream.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int n = stream.Read(data, read, data.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read < data.Length) return null;
                }
                foreach (int face in Faces(data))
                {
                    int math = FindTable(data, face, "MATH"), head = FindTable(data, face, "head"), maxp = FindTable(data, face, "maxp");
                    if (math < 0 || head < 0) continue;
                    if (maxp >= 0 && U16(data, maxp + 4) != font.GlyphCount) continue;
                    double units = U16(data, head + 18);
                    if (units <= 0) continue;
                    MathFontVariants variants = Parse(font, data, math, units);
                    if (variants != null && variants.vertical.Count > 0) return variants;
                }
            }
            catch (Exception)
            {
                // Unreadable or unexpected font data: the delimiters are drawn instead.
            }
            return null;
        }

        private static bool IsCambriaMath(GlyphTypeface font)
        {
            foreach (string name in font.FamilyNames.Values)
                if (String.Equals(name, "Cambria Math", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static List<int> Faces(byte[] data)
        {
            List<int> faces = new List<int>();
            if (Tag(data, 0) == "ttcf")
            {
                long count = Math.Min(64, U32(data, 8));
                for (int i = 0; i < count; i++) faces.Add((int)U32(data, 12 + 4 * i));
            }
            else faces.Add(0);
            return faces;
        }

        private static int FindTable(byte[] data, int face, string tag)
        {
            int count = U16(data, face + 4);
            for (int i = 0; i < count; i++)
            {
                int record = face + 12 + 16 * i;
                if (Tag(data, record) == tag) return (int)U32(data, record + 8);
            }
            return -1;
        }

        private static MathFontVariants Parse(GlyphTypeface font, byte[] data, int math, double units)
        {
            int variantsOffset = U16(data, math + 8);
            if (variantsOffset == 0) return null;
            int variants = math + variantsOffset;
            double overlap = U16(data, variants) / units;
            int coverageOffset = U16(data, variants + 2);
            int count = U16(data, variants + 6);
            if (coverageOffset == 0 || count == 0) return null;
            int[] covered = Coverage(data, variants + coverageOffset, count);
            Dictionary<ushort, Construction> vertical = new Dictionary<ushort, Construction>();
            for (int i = 0; i < count; i++)
            {
                int offset = U16(data, variants + 10 + 2 * i);
                if (covered[i] < 0 || offset == 0) continue;
                vertical[(ushort)covered[i]] = ReadConstruction(data, variants + offset, units);
            }
            return new MathFontVariants(font, overlap, vertical);
        }

        /// <summary>The glyph of each coverage index (OpenType coverage formats 1 and 2); -1 where none.</summary>
        private static int[] Coverage(byte[] data, int at, int count)
        {
            int[] glyphs = new int[count];
            for (int i = 0; i < count; i++) glyphs[i] = -1;
            int format = U16(data, at);
            if (format == 1)
            {
                int n = U16(data, at + 2);
                for (int i = 0; i < n && i < count; i++) glyphs[i] = U16(data, at + 4 + 2 * i);
            }
            else if (format == 2)
            {
                int ranges = U16(data, at + 2);
                for (int r = 0; r < ranges; r++)
                {
                    int record = at + 4 + 6 * r;
                    int start = U16(data, record), end = U16(data, record + 2), index = U16(data, record + 4);
                    for (int g = start; g <= end; g++)
                    {
                        int k = index + g - start;
                        if (k >= 0 && k < count) glyphs[k] = g;
                    }
                }
            }
            return glyphs;
        }

        private static Construction ReadConstruction(byte[] data, int at, double units)
        {
            Construction construction = new Construction();
            int assembly = U16(data, at);
            int n = U16(data, at + 2);
            construction.Glyphs = new ushort[n];
            construction.Sizes = new double[n];
            for (int i = 0; i < n; i++)
            {
                construction.Glyphs[i] = U16(data, at + 4 + 4 * i);
                construction.Sizes[i] = U16(data, at + 6 + 4 * i) / units;
            }
            if (assembly != 0)
            {
                int start = at + assembly;
                int parts = U16(data, start + 4);
                construction.Parts = new Part[parts];
                for (int i = 0; i < parts; i++)
                {
                    int record = start + 6 + 10 * i;
                    Part part = new Part();
                    part.Glyph = U16(data, record);
                    part.Start = U16(data, record + 2) / units;
                    part.End = U16(data, record + 4) / units;
                    part.Full = U16(data, record + 6) / units;
                    part.Extender = (U16(data, record + 8) & 1) != 0;
                    construction.Parts[i] = part;
                }
            }
            return construction;
        }

        private static ushort U16(byte[] data, int at)
        {
            return (ushort)((data[at] << 8) | data[at + 1]);
        }

        private static long U32(byte[] data, int at)
        {
            return ((long)data[at] << 24) | ((long)data[at + 1] << 16) | ((long)data[at + 2] << 8) | data[at + 3];
        }

        private static string Tag(byte[] data, int at)
        {
            return new string(new char[] { (char)data[at], (char)data[at + 1], (char)data[at + 2], (char)data[at + 3] });
        }
    }

    /// <summary>A delimiter made of one size variant or of stacked pieces, drawn from the top of its ink.</summary>
    internal sealed class StretchedGlyph
    {
        private readonly GlyphTypeface font;
        private readonly double em;
        private readonly ushort[] glyphs;
        private readonly double[] baselines;
        private readonly double[] advances;
        private readonly double offset;
        /// <summary>Advance width and the height of the ink.</summary>
        public readonly double Width, Height;

        public StretchedGlyph(GlyphTypeface font, double em, ushort[] glyphs, double[] baselines, double[] advances,
            double width, double height, double offset)
        {
            this.font = font;
            this.em = em;
            this.glyphs = glyphs;
            this.baselines = baselines;
            this.advances = advances;
            this.offset = offset;
            Width = width;
            Height = height;
        }

        /// <summary>Number of glyphs: 1 for a size variant, more for stacked pieces.</summary>
        public int Pieces { get { return glyphs.Length; } }

        public void Draw(DrawingContext dc, Brush ink, double x, double top)
        {
            for (int i = 0; i < glyphs.Length; i++)
            {
                Point origin = new Point(x + offset, top + baselines[i]);
                GlyphRun run = null;
                try
                {
                    run = new GlyphRun(font, 0, false, em, 1.0f, new ushort[] { glyphs[i] }, origin, new double[] { advances[i] },
                        null, null, null, null, null, null);
                }
                catch (Exception)
                {
                    run = null;
                }
                if (run != null)
                {
                    dc.DrawGlyphRun(ink, run);
                    continue;
                }
                Geometry outline = font.GetGlyphOutline(glyphs[i], em, em);
                if (outline == null) continue;
                dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
                dc.DrawGeometry(ink, null, outline);
                dc.Pop();
            }
        }
    }
}

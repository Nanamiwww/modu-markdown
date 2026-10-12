using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace CodexMarkdownReader.Rendering
{
    /// <summary>
    /// Decoded, frozen bitmaps keyed by path + size + timestamp so re-rendering while typing
    /// never touches the disk again. Small images keep their pixel size; large ones are
    /// decoded smaller so that neither one image nor the whole cache can use unbounded memory.
    /// </summary>
    public sealed class ImageCache
    {
        public const int MaxDecodeWidth = 2000;
        public const int MaxDecodeHeight = 10000;
        public const long MaxDecodePixels = 16L * 1000 * 1000;
        /// <summary>Larger images are not decoded at all (shown as a placeholder).</summary>
        public const long MaxSourcePixels = 250L * 1000 * 1000;
        public const long MaxFileBytes = 40L * 1024 * 1024;
        public const long MaxCacheBytes = 256L * 1024 * 1024;
        private const int MaxEntries = 96;

        private sealed class Entry
        {
            public BitmapSource Bitmap;
            public int PixelWidth;
            public int PixelHeight;
            public long Bytes;
            public long Tick;
        }

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private long tick;
        private long cachedBytes;

        public long CachedBytes { get { lock (entries) return cachedBytes; } }

        public bool TryGet(string path, out BitmapSource bitmap, out int pixelWidth, out int pixelHeight)
        {
            bitmap = null; pixelWidth = 0; pixelHeight = 0;
            if (String.IsNullOrEmpty(path) || LinkPolicy.IsDevicePath(path)) return false;
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaxFileBytes) return false;
            }
            catch { return false; }
            string key = info.FullName + "|" + info.Length.ToString() + "|" + info.LastWriteTimeUtc.Ticks.ToString();
            Entry entry;
            lock (entries)
            {
                if (entries.TryGetValue(key, out entry))
                {
                    entry.Tick = ++tick;
                    bitmap = entry.Bitmap; pixelWidth = entry.PixelWidth; pixelHeight = entry.PixelHeight;
                    return true;
                }
            }
            entry = Decode(info.FullName);
            if (entry == null) return false;
            lock (entries)
            {
                entry.Tick = ++tick;
                Entry replaced;
                if (entries.TryGetValue(key, out replaced)) cachedBytes -= replaced.Bytes;
                entries[key] = entry;
                cachedBytes += entry.Bytes;
                if (entries.Count > MaxEntries || cachedBytes > MaxCacheBytes) Trim(entry);
            }
            bitmap = entry.Bitmap; pixelWidth = entry.PixelWidth; pixelHeight = entry.PixelHeight;
            return true;
        }

        /// <summary>Drops the least recently used images until both the count and the memory budget fit.</summary>
        private void Trim(Entry keep)
        {
            List<KeyValuePair<string, Entry>> list = new List<KeyValuePair<string, Entry>>(entries);
            list.Sort(delegate(KeyValuePair<string, Entry> a, KeyValuePair<string, Entry> b) { return a.Value.Tick.CompareTo(b.Value.Tick); });
            int targetCount = entries.Count > MaxEntries ? MaxEntries / 2 : entries.Count;
            foreach (KeyValuePair<string, Entry> pair in list)
            {
                if (entries.Count <= targetCount && cachedBytes <= MaxCacheBytes) break;
                if (ReferenceEquals(pair.Value, keep)) continue;
                entries.Remove(pair.Key);
                cachedBytes -= pair.Value.Bytes;
            }
        }

        public void Clear()
        {
            lock (entries) { entries.Clear(); cachedBytes = 0; }
        }

        /// <summary>
        /// Scale factor (at most 1) that keeps the decoded bitmap within the width, height and total pixel limits.
        /// Returns 0 when the image is too large to decode at all.
        /// </summary>
        internal static double DecodeScale(int sourceWidth, int sourceHeight)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0) return 0;
            long pixels = (long)sourceWidth * sourceHeight;
            if (pixels > MaxSourcePixels) return 0;
            double scale = 1.0;
            scale = Math.Min(scale, (double)MaxDecodeWidth / sourceWidth);
            scale = Math.Min(scale, (double)MaxDecodeHeight / sourceHeight);
            scale = Math.Min(scale, Math.Sqrt((double)MaxDecodePixels / pixels));
            return scale;
        }

        private static Entry Decode(string path)
        {
            try
            {
                int sourceWidth, sourceHeight;
                using (FileStream probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    BitmapDecoder decoder = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                    BitmapFrame frame = decoder.Frames[0];
                    sourceWidth = frame.PixelWidth;
                    sourceHeight = frame.PixelHeight;
                }
                // The size is checked from the header, before any pixel memory is allocated.
                double scale = DecodeScale(sourceWidth, sourceHeight);
                if (scale <= 0) return null;

                BitmapImage bitmap = new BitmapImage();
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    // Only shrink: DecodePixelWidth also enlarges small images, which blew icons up to full width.
                    // Both sides are set: with the width alone a very narrow image (1 px wide) cannot get any
                    // shorter, because its width is already as small as it can be.
                    if (scale < 1.0)
                    {
                        bitmap.DecodePixelWidth = Math.Max(1, (int)Math.Floor(sourceWidth * scale));
                        bitmap.DecodePixelHeight = Math.Max(1, (int)Math.Floor(sourceHeight * scale));
                    }
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                bitmap.Freeze();
                // Whatever the decoder did, never keep a bitmap beyond the limits.
                if (bitmap.PixelWidth > MaxDecodeWidth || bitmap.PixelHeight > MaxDecodeHeight ||
                    (long)bitmap.PixelWidth * bitmap.PixelHeight > MaxDecodePixels) return null;
                Entry entry = new Entry();
                entry.Bitmap = bitmap;
                // Report the original pixel size so the layout matches browsers (pixels, not DPI metadata).
                entry.PixelWidth = sourceWidth;
                entry.PixelHeight = sourceHeight;
                entry.Bytes = (long)bitmap.PixelWidth * bitmap.PixelHeight * Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
                return entry;
            }
            catch
            {
                return null;
            }
        }
    }
}

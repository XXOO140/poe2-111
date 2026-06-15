using System.Drawing;
using System.Drawing.Imaging;
using Tesseract;

namespace PoeAncientsPriceHelper;

internal sealed record OcrRow(string NormalizedName, string RawText, int CenterY, int Multiplier = 1);

internal sealed class OcrScanner : IDisposable
{
    // Two independent engines so the two segmentation passes can run concurrently — Tesseract
    // engines are single-threaded internally, but separate instances on separate threads are fine.
    private readonly TesseractEngine _engineCol;
    private readonly TesseractEngine _engineSparse;
    private readonly RapidOcrService? _rapidOcr;
    private readonly Action<string>? _log;
    private readonly object _logLock = new();
    private const float MinConfidence = 10f;
    private const int UpscaleFactor = 2;
    private const int MinNameLength = 2;
    private const int MinWordLength = 2;

    public OcrScanner(string tessdataDir, Action<string>? log = null)
    {
        _log = log;
        
        // 初始化 Tesseract OCR
        _engineCol = new TesseractEngine(tessdataDir, "chi_sim+chi_tra+eng", EngineMode.Default);
        _engineSparse = new TesseractEngine(tessdataDir, "chi_sim+chi_tra+eng", EngineMode.Default);
        log?.Invoke("Tesseract OCR 引擎初始化: 简体中文+繁体中文+英文");
        
        // 初始化 RapidOCR (PP-OCRv6 中文模型)
        _rapidOcr = new RapidOcrService(log);
    }

    // 不裁剪左边，保留完整的物品名（包括 1x, 2x 等数量标记）
    internal const double IconColumnFraction = 0.0;
    internal const double RightTrimFraction = 0.02;

    public IReadOnlyList<OcrRow> Scan(Bitmap regionBitmap)
    {
        int leftCut = Math.Max(1, (int)(regionBitmap.Width * IconColumnFraction));
        int rightCut = (int)(regionBitmap.Width * RightTrimFraction);
        int cropW = Math.Max(1, regionBitmap.Width - leftCut - rightCut);
        using var cropped = CropBitmap(regionBitmap, leftCut, 0, cropW, regionBitmap.Height);
        using var inverted = Preprocess(cropped);
        using var upscaled = Upscale(inverted, UpscaleFactor);
        byte[] png = ToPng(upscaled);
        int height = regionBitmap.Height;

        // 如果 RapidOCR 可用，使用它（中文识别更好）
        if (_rapidOcr != null && _rapidOcr.IsAvailable)
        {
            using var originalUpscaled = Upscale(cropped, 2);
            return ScanWithRapidOcr(originalUpscaled, height);
        }
        
        // 否则使用 Tesseract OCR
        return ScanWithTesseract(png, height, upscaled);
    }
    
    /// <summary>
    /// 使用 RapidOCR 进行识别
    /// </summary>
    private IReadOnlyList<OcrRow> ScanWithRapidOcr(Bitmap image, int regionHeight)
    {
        var rows = new List<OcrRow>();
        
        try
        {
            var result = _rapidOcr!.Recognize(image);
            
            if (result.Success && result.Items.Count > 0)
            {
                foreach (var item in result.Items)
                {
                    var normalizedRaw = NormalizeName(item.Text);
                    var multiplier = ExtractMultiplier(normalizedRaw);
                    var normalized = StripLeadingNoise(normalizedRaw);
                    
                    if (normalized.Length >= MinNameLength && HasLongWord(normalized, MinWordLength))
                    {
                        // RapidOCR 的 Y 坐标需要除以缩放因子 (UpscaleFactor=2)
                        int centerY = item.CenterY / UpscaleFactor;
                        rows.Add(new OcrRow(normalized, item.Text.Trim(), centerY, multiplier));
                    }
                }
                
                _log?.Invoke($"RapidOCR 识别到 {result.Items.Count} 行，过滤后 {rows.Count} 行");
            }
            else
            {
                _log?.Invoke($"RapidOCR 识别失败或无结果: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"RapidOCR 识别异常: {ex.Message}");
        }
        
        return rows;
    }
    
    /// <summary>
    /// 使用 Tesseract OCR 进行识别
    /// </summary>
    private IReadOnlyList<OcrRow> ScanWithTesseract(byte[] png, int height, Bitmap upscaled)
    {
        // Two segmentation passes merged by row position, run CONCURRENTLY (one engine each) to
        // halve latency. SingleColumn reads ordinary lists cleanly; SparseText rescues panels whose
        // strong beveled row dividers make the other modes see only the top line. At each row keep
        // whichever pass produced the fuller text.
        var tCol = Task.Run(() => RunPass(_engineCol, png, PageSegMode.SingleColumn, height));
        var tSparse = Task.Run(() => RunPass(_engineSparse, png, PageSegMode.SparseText, height));
        Task.WaitAll(tCol, tSparse);
        var rows = MergeByPosition(tCol.Result, tSparse.Result);

        // When OCR catches few rows, dump the exact image fed to Tesseract for inspection.
        if (rows.Count <= 2)
        {
            try { upscaled.Save(Path.Combine(AppContext.BaseDirectory, "debug_ocr.png"), System.Drawing.Imaging.ImageFormat.Png); }
            catch { /* best-effort diagnostic */ }
        }
        return rows;
    }

    private IReadOnlyList<OcrRow> RunPass(TesseractEngine engine, byte[] png, PageSegMode mode, int regionHeight)
    {
        using var pix = Pix.LoadFromMemory(png);
        using var page = engine.Process(pix, mode);
        return ExtractRows(page, regionHeight, UpscaleFactor);
    }

    private static IReadOnlyList<OcrRow> MergeByPosition(IReadOnlyList<OcrRow> a, IReadOnlyList<OcrRow> b)
    {
        const int Tol = 25;   // px: reads within this vertical distance are the same row
        static int Letters(string s) { int c = 0; foreach (var ch in s) if (char.IsLetter(ch)) c++; return c; }

        var result = new List<OcrRow>(a);
        foreach (var rb in b)
        {
            int idx = -1;
            for (int i = 0; i < result.Count; i++)
                if (Math.Abs(result[i].CenterY - rb.CenterY) <= Tol) { idx = i; break; }
            if (idx < 0) result.Add(rb);
            else if (Letters(rb.NormalizedName) > Letters(result[idx].NormalizedName)) result[idx] = rb;
        }
        result.Sort((x, y) => x.CenterY.CompareTo(y.CenterY));
        return result;
    }

    private static Bitmap CropBitmap(Bitmap src, int x, int y, int w, int h)
    {
        var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(dst);
        g.DrawImage(src, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
        return dst;
    }

    private IReadOnlyList<OcrRow> ExtractRows(Page page, int bitmapHeight, int scale = 1)
    {
        var rows = new List<OcrRow>();
        var diag = new List<string>();
        using var iter = page.GetIterator();
        iter.Begin();
        do
        {
            if (!iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out var box)) continue;
            var text = iter.GetText(PageIteratorLevel.TextLine);
            float conf = iter.GetConfidence(PageIteratorLevel.TextLine);
            // Bounding box coords are in upscaled space — divide back to original coords
            int centerY = Math.Clamp((box.Y1 + (box.Y2 - box.Y1) / 2) / scale, 0, bitmapHeight - 1);

            string? reject = null;
            string normalized = "";
            int multiplier = 1;
            if (string.IsNullOrWhiteSpace(text)) reject = "empty";
            else if (conf < MinConfidence) reject = "lowconf";
            else
            {
                var normalizedRaw = NormalizeName(text);
                multiplier = ExtractMultiplier(normalizedRaw);
                normalized = StripLeadingNoise(normalizedRaw);
                if (normalized.Length < MinNameLength) reject = "short";
                else if (!HasLongWord(normalized, MinWordLength)) reject = "noword";
            }

            if (reject is null)
                rows.Add(new OcrRow(normalized, text.Trim(), centerY, multiplier));
            diag.Add($"y={centerY} conf={conf:0} '{(text ?? "").Trim()}'{(reject is null ? "" : $" REJ:{reject}")}");
        }
        while (iter.Next(PageIteratorLevel.TextLine));

        // Diagnostic: when few rows survive, show every line Tesseract actually produced so we
        // can tell "Tesseract only saw 1 line" from "saw 5 but the filters dropped 4".
        // Runs on a pass thread — serialize so two concurrent passes don't race the logger.
        if (rows.Count <= 2 && diag.Count > 0)
            lock (_logLock) { _log?.Invoke($"OCR raw {diag.Count} lines → " + string.Join(" | ", diag)); }

        return rows;
    }

    private static Bitmap Upscale(Bitmap src, int factor)
    {
        var dst = new Bitmap(src.Width * factor, src.Height * factor, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, 0, 0, dst.Width, dst.Height);
        return dst;
    }

    // The list shows a stack quantity as "Nx" before the item name ("1x", "2x", "14x").
    // Capture it so the price can be multiplied by the stack size. Read from the raw
    // normalized string BEFORE StripLeadingNoise removes the marker. Returns 1 when absent.
    internal static int ExtractMultiplier(string normalized)
    {
        var m = Regex.Match(normalized, @"(?<![a-z0-9])(\d{1,3})\s*x(?![a-z0-9])");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n >= 1)
            return Math.Min(n, 999);
        return 1;
    }

    // Strip leading noise: short/numeric tokens ("e", "l8"), then anything before the first
    // quantity marker ("1x", "11x"), then remaining leading non-alpha chars.
    // e.g. "krogin 1x ancient rune of decay"  → "ancient rune of decay"
    // e.g. "e l8 n 1x the greatwolf"          → "the greatwolf"
    // 支持中文：保留中文字母开头的内容
    internal static string StripLeadingNoise(string normalized)
    {
        var s = Regex.Replace(normalized, @"^(?:\S{1,2}\s+|\S*\d\S*\s+)+", "");
        // If a quantity marker still exists, drop everything before (and including) it
        var qm = Regex.Match(s, @"(?<!\w)\d+\s*x\s+");
        if (qm.Success) s = s.Substring(qm.Index + qm.Length);
        // 保留中文字母开头的内容，只去掉非字母非中文的前缀
        s = Regex.Replace(s, @"^[^a-z\u4e00-\u9fff\u3400-\u4dbf]+", "");
        return s.Trim();
    }

    private static bool HasLongWord(string normalized, int minLen)
    {
        int run = 0;
        int chineseCharCount = 0;
        foreach (char c in normalized)
        {
            // 中文字符单独计算（每个中文字都是一个完整的"词"）
            if (c >= '\u4e00' && c <= '\u9fff' || c >= '\u3400' && c <= '\u4dbf')
            {
                chineseCharCount++;
                if (chineseCharCount >= 2) return true; // 2个中文字就认为是有效词
                run = 0;
            }
            else if (char.IsLetter(c)) 
            { 
                if (++run >= minLen) return true;
                chineseCharCount = 0;
            }
            else 
            {
                run = 0;
                chineseCharCount = 0;
            }
        }
        return false;
    }

    // Invert: PoE list panel has light text on dark background.
    // Tesseract works better with dark-on-light.
    private static Bitmap Preprocess(Bitmap src)
    {
        var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(dst);
        g.DrawImage(src, 0, 0);
        InvertBitmap(dst);
        return dst;
    }

    private static void InvertBitmap(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            int len = data.Stride * bmp.Height;
            var buf = new byte[len];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, len);
            for (int i = 0; i < buf.Length; i++) buf[i] = (byte)(255 - buf[i]);
            System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, len);
        }
        finally { bmp.UnlockBits(data); }
    }

    private static byte[] ToPng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    internal static string NormalizeName(string text)
    {
        var s = text.ToLowerInvariant();
        // 保留中文字符、英文字符、数字和空格
        s = Regex.Replace(s, @"[^\w\s\u4e00-\u9fff\u3400-\u4dbf]", " ");
        // 合并中文字符之间的空格 (例如 "符 文 合 金" -> "符文合金")
        // 多次执行直到没有变化
        string prev;
        do
        {
            prev = s;
            s = Regex.Replace(s, @"([\u4e00-\u9fff\u3400-\u4dbf])\s+([\u4e00-\u9fff\u3400-\u4dbf])", "$1$2");
        } while (s != prev);
        // 去除中文末尾的单个字符噪声
        s = Regex.Replace(s, @"([\u4e00-\u9fff\u3400-\u4dbf]{2,})[\u4e00-\u9fff\u3400-\u4dbf]$", "$1");
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    public void Dispose() 
    { 
        _engineCol.Dispose(); 
        _engineSparse.Dispose();
        _rapidOcr?.Dispose();
    }
}

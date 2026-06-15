using System.Drawing;
using System.Drawing.Imaging;
using RapidOcrNet;
using SkiaSharp;

namespace PoeAncientsPriceHelper;

/// <summary>
/// RapidOCR 识别服务 - 使用 PP-OCRv6 中文模型
/// </summary>
internal sealed class RapidOcrService : IDisposable
{
    private readonly RapidOcr _engine;
    private readonly Action<string>? _log;
    private bool _initialized;
    
    public RapidOcrService(Action<string>? log = null)
    {
        _log = log;
        
        try
        {
            _engine = new RapidOcr();
            _engine.InitModels();
            _initialized = true;
            Log("[信息] RapidOCR 服务初始化完成 (PP-OCRv6 中文模型)");
        }
        catch (Exception ex)
        {
            Log($"[错误] RapidOCR 初始化失败: {ex.Message}");
            _initialized = false;
            _engine = null!;
        }
    }
    
    public bool IsAvailable => _initialized;
    
    public RapidOcrResult Recognize(Bitmap image)
    {
        if (!IsAvailable)
        {
            return new RapidOcrResult { Success = false, Message = "RapidOCR 不可用" };
        }
        
        try
        {
            using var skBitmap = BitmapToSKBitmap(image);
            var result = _engine.Detect(skBitmap, RapidOcrOptions.Default);
            
            if (result == null)
            {
                return new RapidOcrResult { Success = false, Message = "识别结果为空" };
            }
            
            var items = new List<RapidOcrItem>();
            
            if (result.TextBlocks != null)
            {
                foreach (var block in result.TextBlocks)
                {
                    var text = block.Text ?? "";
                    var score = block.BoxScore;
                    
                    int centerY = 0;
                    if (block.BoxPoints != null && block.BoxPoints.Length >= 4)
                    {
                        centerY = (int)((block.BoxPoints[0].Y + block.BoxPoints[2].Y) / 2);
                    }
                    
                    items.Add(new RapidOcrItem
                    {
                        Text = text,
                        Confidence = score,
                        CenterY = centerY
                    });
                }
            }
            
            Log($"[信息] RapidOCR 识别到 {items.Count} 行文字");
            
            return new RapidOcrResult
            {
                Success = true,
                Items = items,
                Count = items.Count
            };
        }
        catch (Exception ex)
        {
            Log($"[错误] RapidOCR 识别失败: {ex.Message}");
            return new RapidOcrResult { Success = false, Message = ex.Message };
        }
    }
    
    private SKBitmap BitmapToSKBitmap(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Seek(0, SeekOrigin.Begin);
        return SKBitmap.Decode(ms);
    }
    
    private void Log(string message)
    {
        _log?.Invoke(message);
    }
    
    public void Dispose()
    {
        _engine?.Dispose();
    }
}

internal sealed class RapidOcrResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<RapidOcrItem> Items { get; set; } = new();
    public int Count { get; set; }
}

internal sealed class RapidOcrItem
{
    public string Text { get; set; } = "";
    public float Confidence { get; set; }
    public int CenterY { get; set; }
}

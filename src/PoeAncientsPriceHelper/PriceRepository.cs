using System.Collections.ObjectModel;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PoeAncientsPriceHelper;

// DivineValue  = price in divine orbs (primaryValue from API)
// ExaltedValue = DivineValue * core.rates.exalted (computed, for display when < 1 divine)
internal sealed record PriceEntry(decimal DivineValue, decimal ExaltedValue);

internal sealed class PriceRepository : IDisposable
{
    private readonly HttpClient _http;
    private volatile IReadOnlyDictionary<string, PriceEntry> _prices =
        new ReadOnlyDictionary<string, PriceEntry>(new Dictionary<string, PriceEntry>());
    private System.Threading.Timer? _timer;
    private AppConfig? _config;
    
    // 中英文映射表：key=英文标准化名, value=中文名
    private Dictionary<string, string> _cnToEn = new();
    // 反向映射：key=中文标准化名, value=英文标准化名
    private Dictionary<string, string> _enToCn = new();
    
    // 本地缓存
    private static readonly string CacheFilePath = Path.Combine(AppContext.BaseDirectory, "prices_cache.json");
    private static readonly string MappingCacheFilePath = Path.Combine(AppContext.BaseDirectory, "item_names_cn.json");
    public int SyncCount { get; private set; } = 0;
    public string SyncStatus { get; private set; } = "等待同步";
    public bool IsSyncing { get; private set; } = false;
    
    // 日志
    private static readonly string LogDir = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly string LogFilePath = Path.Combine(LogDir, "price_sync.log");
    private static readonly object LogLock = new object();

    public IReadOnlyDictionary<string, PriceEntry> Prices => _prices;
    public DateTime? LastFetchedAt { get; private set; }
    public int ItemCount => _prices.Count;

    // 同步间隔常量
    private static readonly TimeSpan SyncIntervalSuccess = TimeSpan.FromMinutes(30);  // 成功后30分钟
    private static readonly TimeSpan SyncIntervalFailure = TimeSpan.FromMinutes(5);   // 失败后5分钟

    // Raised after every successful fetch (initial + each 30-min background refresh) so the UI can
    // refresh its "last fetch" label — which otherwise stays frozen at the startup time. Fires on a
    // thread-pool thread; subscribers must marshal to the UI thread.
    public event Action? PricesUpdated;

    private static readonly string[] ExchangeTypes = ["Verisium", "Runes", "Expedition", "Currency"];

    public PriceRepository(HttpClient http) 
    {
        _http = http;
        Log("========================================");
        Log("PriceRepository 初始化开始");
        LoadChineseMapping();
        LoadLocalCache();
        Log("PriceRepository 初始化完成");
        Log("========================================");
    }
    
    // 日志方法
    private static void Log(string message)
    {
        var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        Console.WriteLine(logLine);
        try
        {
            lock (LogLock)
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogFilePath, logLine + Environment.NewLine);
            }
        }
        catch { /* 忽略日志写入错误 */ }
    }

    private void LoadChineseMapping()
    {
        try
        {
            var mappingPath = Path.Combine(AppContext.BaseDirectory, "item_names_cn.json");
            if (!File.Exists(mappingPath))
            {
                Log("[错误] 中文映射文件未找到: item_names_cn.json");
                return;
            }
            
            Log($"加载中文映射文件: {mappingPath}");
            var json = File.ReadAllText(mappingPath);
            var mapping = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            if (mapping is null) 
            {
                Log("[错误] 中文映射文件解析失败");
                return;
            }
            
            // mapping 格式: key=中文名(简体/繁体), value=英文名
            foreach (var kvp in mapping)
            {
                var cnKey = NormalizeName(kvp.Key);
                var enKey = NormalizeName(kvp.Value);
                if (!string.IsNullOrEmpty(enKey) && !string.IsNullOrEmpty(cnKey))
                {
                    _cnToEn[cnKey] = enKey;   // 中文 -> 英文
                    _enToCn[enKey] = cnKey;   // 英文 -> 中文
                }
            }
            Log($"[成功] 加载 {_cnToEn.Count} 个中文映射");
        }
        catch (Exception ex)
        {
            Log($"[错误] 加载中文映射失败: {ex.Message}");
        }
    }
    
    private void LoadLocalCache()
    {
        try
        {
            if (!File.Exists(CacheFilePath))
            {
                Log("[信息] 本地缓存文件不存在，将进行首次同步");
                return;
            }
            
            Log($"加载本地缓存: {CacheFilePath}");
            var json = File.ReadAllText(CacheFilePath);
            var cache = JsonConvert.DeserializeObject<PriceCache>(json);
            if (cache is null || cache.Items.Count == 0)
            {
                Log("[信息] 本地缓存为空");
                return;
            }
            
            // 检查缓存是否过期 (30分钟)
            var cacheAge = DateTime.Now - cache.Timestamp;
            if (cacheAge.TotalMinutes > 30)
            {
                Log($"[信息] 本地缓存已过期 ({cacheAge.TotalMinutes:F0} 分钟前)");
                // 仍然加载过期缓存，但会在后台更新
            }
            
            // 加载缓存数据
            var dict = new Dictionary<string, PriceEntry>();
            foreach (var (key, entry) in cache.Items)
            {
                dict[key] = new PriceEntry(entry.DivineValue, entry.ExaltedValue);
            }
            
            _prices = new ReadOnlyDictionary<string, PriceEntry>(dict);
            LastFetchedAt = cache.Timestamp;
            SyncCount = cache.SyncCount;
            
            Log($"[成功] 从本地缓存加载 {dict.Count} 个物品 (同步次数 #{SyncCount})");
        }
        catch (Exception ex)
        {
            Log($"[错误] 加载本地缓存失败: {ex.Message}");
        }
    }
    
    private void SaveLocalCache()
    {
        try
        {
            var cache = new PriceCache
            {
                Timestamp = LastFetchedAt ?? DateTime.Now,
                SyncCount = SyncCount,
                Items = new Dictionary<string, PriceCacheEntry>()
            };
            
            foreach (var (key, entry) in _prices)
            {
                cache.Items[key] = new PriceCacheEntry
                {
                    DivineValue = entry.DivineValue,
                    ExaltedValue = entry.ExaltedValue
                };
            }
            
            var json = JsonConvert.SerializeObject(cache, Formatting.Indented);
            File.WriteAllText(CacheFilePath, json);
            
            Log($"[成功] 保存 {cache.Items.Count} 个物品到本地缓存");
        }
        catch (Exception ex)
        {
            Log($"[错误] 保存本地缓存失败: {ex.Message}");
        }
    }

    // 尝试将中文物品名翻译为英文标准化名
    public string? TryTranslateToEnglish(string chineseName)
    {
        var cnKey = NormalizeName(chineseName);
        if (_cnToEn.TryGetValue(cnKey, out var enKey))
        {
            Log($"[映射] {chineseName} -> {cnKey} -> {enKey}");
            return enKey;
        }
        Log($"[映射] {chineseName} -> {cnKey} -> 未找到");
        return null;
    }

    public async Task InitialFetchAsync(AppConfig config)
    {
        _config = config;
        await FetchAndMergeAsync(config);
    }

    // 启动自动同步：首次立即同步，成功后30分钟，失败后5分钟重试
    public void StartAutoRefresh(AppConfig config)
    {
        _config = config;
        _timer?.Dispose();
        
        // 首次同步在 InitialFetchAsync 中完成
        // 设置定时器：成功后30分钟，失败后5分钟
        _timer = new System.Threading.Timer(async _ => 
        {
            await SyncWithRetryAsync();
        }, null, SyncIntervalSuccess, SyncIntervalSuccess);
    }
    
    // 带重试的同步方法
    private async Task SyncWithRetryAsync()
    {
        if (_config is null) 
        {
            Log("[错误] 同步失败: 配置为空");
            return;
        }
        
        try
        {
            IsSyncing = true;
            SyncStatus = "同步中...";
            Log("开始同步价格数据...");
            PricesUpdated?.Invoke();
            
            await FetchAndMergeAsync(_config);
            
            // 成功：设置30分钟后再次同步
            SyncStatus = "同步成功";
            Log($"[成功] 同步完成，共 {_prices.Count} 个物品，30分钟后再次同步");
            _timer?.Change(SyncIntervalSuccess, SyncIntervalSuccess);
        }
        catch (Exception ex)
        {
            // 失败：设置5分钟后重试
            SyncStatus = $"同步失败，5分钟后重试";
            Log($"[错误] 同步失败: {ex.Message}");
            Log($"[信息] 5分钟后重试同步");
            _timer?.Change(SyncIntervalFailure, SyncIntervalFailure);
        }
        finally
        {
            IsSyncing = false;
            PricesUpdated?.Invoke();
        }
    }

    private async Task FetchAndMergeAsync(AppConfig config)
    {
        Log($"开始从 poe.ninja 获取价格数据 (联赛: {config.LeagueName})...");
        
        var dict = new Dictionary<string, PriceEntry>();
        foreach (var type in ExchangeTypes)
        {
            Log($"  获取类别: {type}");
            var entries = await FetchTypeAsync(config.LeagueName, type);
            Log($"  {type}: 获取 {entries.Count} 个物品");
            foreach (var (name, entry) in entries)
                dict[name] = entry;
        }
        
        Log($"应用自定义价格覆盖...");
        ApplyCustomOverride(dict, config.CustomPricesPath);
        
        // 添加中文物品名映射
        Log($"添加中文映射...");
        var cnDict = new Dictionary<string, PriceEntry>();
        foreach (var (enKey, entry) in dict)
        {
            // 如果英文名有对应的中文名，添加中文条目
            if (_enToCn.TryGetValue(enKey, out var cnKey))
            {
                cnDict[cnKey] = entry;
            }
        }
        // 合并中文物品名
        foreach (var (cnKey, entry) in cnDict)
        {
            dict[cnKey] = entry;
        }
        
        _prices = new ReadOnlyDictionary<string, PriceEntry>(dict);
        LastFetchedAt = DateTime.Now;
        SyncCount++;
        
        Log($"价格数据更新完成: {dict.Count} 个物品 (英文: {dict.Count - cnDict.Count}, 中文: {cnDict.Count})");
        
        // 保存到本地缓存
        SaveLocalCache();
        
        PricesUpdated?.Invoke();
    }

    private async Task<Dictionary<string, PriceEntry>> FetchTypeAsync(string league, string type)
    {
        var slug = league.Replace(" ", "").ToLowerInvariant();
        var typeSlug = type.ToLowerInvariant();
        var url = $"https://poe.ninja/poe2/api/economy/exchange/current/overview?league={Uri.EscapeDataString(league)}&type={type}";

        Log($"请求 URL: {url}");
        
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("Referer",
            $"https://poe.ninja/poe2/economy/{slug}/{typeSlug}");

        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            Log($"[错误] {type}: HTTP 状态码 {(int)resp.StatusCode}");
            return [];
        }

        var json = await resp.Content.ReadAsStringAsync();
        Log($"  {type}: 收到 {json.Length} 字节数据");
        return ParseResponse(json);
    }

    // API shape (exchange/current/overview):
    //   items[]   → { id, name }             — display name lookup
    //   lines[]   → { id, primaryValue }     — price in the league's PRIMARY currency
    //   core.primary  → "divine" | "exalted" — which currency primaryValue is denominated in
    //   core.rates    → { exalted, divine, chaos } — how many of each currency equal 1 primary
    // The primary currency differs by league: Softcore prices in divines, Hardcore prices in
    // exalted (divine is too valuable there). So derive both divine- and exalted-denominated
    // values from primaryValue via the rates, rather than assuming primaryValue is divines.
    private static Dictionary<string, PriceEntry> ParseResponse(string json)
    {
        var result = new Dictionary<string, PriceEntry>();
        try
        {
            var obj = JObject.Parse(json);

            // id → display name
            var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (obj["items"] is JArray itemsArr)
                foreach (var item in itemsArr)
                {
                    var id = item["id"]?.Value<string>();
                    var name = item["name"]?.Value<string>();
                    if (id is not null && name is not null) nameMap[id] = name;
                }
            
            Log($"  解析到 {nameMap.Count} 个物品名称");

            // rates[x] = how many x equal 1 unit of the primary currency. When the primary IS
            // divine/exalted, its own rate is implicitly 1 (and absent from the rates object).
            var core = obj["core"];
            var primary = core?["primary"]?.Value<string>() ?? "divine";
            var rates = core?["rates"];
            var divinePerPrimary = primary == "divine" ? 1m : rates?["divine"]?.Value<decimal>() ?? 0m;
            var exaltedPerPrimary = primary == "exalted" ? 1m : rates?["exalted"]?.Value<decimal>() ?? 1m;
            
            Log($"  主要货币: {primary}, 神圣石汇率: {divinePerPrimary}, 崇高石汇率: {exaltedPerPrimary}");

            if (obj["lines"] is not JArray lines) 
            {
                Log($"  [警告] 没有价格数据");
                return result;
            }
            
            foreach (var line in lines)
            {
                var id = line["id"]?.Value<string>();
                if (id is null || !nameMap.TryGetValue(id, out var name)) continue;
                var primaryValue = line["primaryValue"]?.Value<decimal>() ?? 0m;
                var divineValue = primaryValue * divinePerPrimary;
                var exaltedValue = Math.Round(primaryValue * exaltedPerPrimary, 1);
                var key = NormalizeName(name);
                if (!string.IsNullOrEmpty(key))
                    result[key] = new PriceEntry(divineValue, exaltedValue);
            }
            
            Log($"  解析到 {result.Count} 个价格条目");
        }
        catch (Exception ex)
        {
            Log($"[错误] 解析响应失败: {ex.Message}");
        }
        return result;
    }

    private static void ApplyCustomOverride(Dictionary<string, PriceEntry> dict, string path)
    {
        try
        {
            var fullPath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppContext.BaseDirectory, path);
            if (!File.Exists(fullPath)) 
            {
                Log($"[信息] 自定义价格文件不存在: {fullPath}");
                return;
            }
            
            Log($"加载自定义价格文件: {fullPath}");
            var json = File.ReadAllText(fullPath);
            var overrides = JsonConvert.DeserializeObject<Dictionary<string, CustomPriceEntry>>(json);
            if (overrides is null) 
            {
                Log("[警告] 自定义价格文件为空");
                return;
            }
            
            foreach (var (rawKey, entry) in overrides)
            {
                var key = NormalizeName(rawKey);
                if (!string.IsNullOrEmpty(key))
                    dict[key] = new PriceEntry(entry.DivineValue, entry.ExaltedValue);
            }
            
            Log($"[成功] 应用 {overrides.Count} 个自定义价格");
        }
        catch (Exception ex)
        {
            Log($"[错误] 应用自定义价格失败: {ex.Message}");
        }
    }

    internal static string NormalizeName(string name)
    {
        var s = name.ToLowerInvariant();
        // 保留中文字符、英文字符、数字和空格
        s = Regex.Replace(s, @"[^\w\s\u4e00-\u9fff\u3400-\u4dbf]", " ");
        // 合并中文字符之间的空格
        string prev;
        do
        {
            prev = s;
            s = Regex.Replace(s, @"([\u4e00-\u9fff\u3400-\u4dbf])\s+([\u4e00-\u9fff\u3400-\u4dbf])", "$1$2");
        } while (s != prev);
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private sealed class CustomPriceEntry
    {
        public decimal DivineValue { get; set; }
        public decimal ExaltedValue { get; set; }
    }
    
    // 本地缓存数据结构
    private sealed class PriceCache
    {
        public DateTime Timestamp { get; set; }
        public int SyncCount { get; set; }
        public Dictionary<string, PriceCacheEntry> Items { get; set; } = new();
    }
    
    private sealed class PriceCacheEntry
    {
        public decimal DivineValue { get; set; }
        public decimal ExaltedValue { get; set; }
    }
}

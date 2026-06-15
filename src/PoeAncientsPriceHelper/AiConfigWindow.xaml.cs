using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;

namespace PoeAncientsPriceHelper;

public partial class AiConfigWindow : Window
{
    private readonly AiRecognitionService _aiService;
    
    public AiConfigWindow()
    {
        InitializeComponent();
        
        _aiService = new AiRecognitionService();
        
        // 加载当前配置
        LoadCurrentConfig();
    }
    
    private void LoadCurrentConfig()
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "ai_config.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = Newtonsoft.Json.JsonConvert.DeserializeObject<AiConfig>(json);
                if (config != null)
                {
                    // 设置 OCR 引擎选择
                    if (config.OcrEngine == "AI")
                    {
                        AiRadio.IsChecked = true;
                    }
                    else
                    {
                        TesseractRadio.IsChecked = true;
                    }
                    
                    ApiEndpointTextBox.Text = config.ApiEndpoint ?? "https://api.openai.com/v1/chat/completions";
                    ApiKeyPasswordBox.Password = config.ApiKey ?? "";
                    ModelTextBox.Text = config.Model ?? "gpt-4o-mini";
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"加载配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private async void TestAiButton_Click(object sender, RoutedEventArgs e)
    {
        var endpoint = ApiEndpointTextBox.Text?.Trim();
        var apiKey = ApiKeyPasswordBox.Password?.Trim();
        var model = ModelTextBox.Text?.Trim();
        
        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
        {
            TestResultText.Text = "请先填写 API 端点和密钥";
            TestResultText.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }
        
        TestResultText.Text = "正在测试...";
        TestResultText.Foreground = System.Windows.Media.Brushes.Yellow;
        TestAiButton.IsEnabled = false;
        
        try
        {
            // 创建测试图片 (简单的文字图片)
            using var testImage = CreateTestImage();
            
            // 创建临时 AI 服务进行测试
            using var testAiService = new AiRecognitionService();
            testAiService.UpdateConfig(endpoint, apiKey, model ?? "gpt-4o-mini", true);
            
            var result = await testAiService.RecognizeItemsAsync(testImage);
            
            if (result.Success)
            {
                TestResultText.Text = $"✅ 测试成功!\n识别到 {result.Items.Count} 个物品:\n";
                foreach (var item in result.Items)
                {
                    TestResultText.Text += $"- {item.Name} (x{item.Quantity})\n";
                }
                TestResultText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            else
            {
                TestResultText.Text = $"❌ 测试失败: {result.Message}";
                TestResultText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }
        catch (Exception ex)
        {
            TestResultText.Text = $"❌ 测试异常: {ex.Message}";
            TestResultText.Foreground = System.Windows.Media.Brushes.Red;
        }
        finally
        {
            TestAiButton.IsEnabled = true;
        }
    }
    
    private Bitmap CreateTestImage()
    {
        // 创建一个简单的测试图片
        var bitmap = new Bitmap(400, 200);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(System.Drawing.Color.Black);
        
        using var font = new System.Drawing.Font("Microsoft YaHei", 16);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        
        g.DrawString("2x 神圣石", font, brush, 50, 50);
        g.DrawString("5x 混沌石", font, brush, 50, 100);
        
        return bitmap;
    }
    
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 确定选择的 OCR 引擎
            string ocrEngine = AiRadio.IsChecked == true ? "AI" : "Tesseract";
            
            var config = new AiConfig
            {
                Enabled = ocrEngine == "AI",
                UsePaddleOcr = false,
                OcrEngine = ocrEngine,
                ApiEndpoint = ApiEndpointTextBox.Text?.Trim() ?? "",
                ApiKey = ApiKeyPasswordBox.Password?.Trim() ?? "",
                Model = ModelTextBox.Text?.Trim() ?? "gpt-4o-mini"
            };
            
            var configPath = Path.Combine(AppContext.BaseDirectory, "ai_config.json");
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(configPath, json);
            
            System.Windows.MessageBox.Show($"配置已保存\nOCR 引擎: {ocrEngine}\n重启程序后生效", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

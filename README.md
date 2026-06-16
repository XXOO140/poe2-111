# PoE2 物价助手 - 中文汉化版

基于 [PoeAncientsPriceHelper](https://github.com/pedro-quiterio/PoeAncientsPriceHelper) 的中文汉化版本。

## 原作者

- **作者**: pedro-quiterio
- **原仓库**: https://github.com/pedro-quiterio/PoeAncientsPriceHelper
- **原版许可**: 开源

感谢原作者的优秀作品！

---

## 功能特性

- **OCR 识别** - 自动识别屏幕上的物品名称
- **中文支持** - 支持简体中文、繁体中文、英文
- **RapidOCR** - 使用 PaddleOCR 模型，中文识别更准确
- **实时价格** - 从 poe.ninja 获取最新价格
- **本地缓存** - 价格数据本地存储，30分钟自动同步
- **中英映射** - 928 个物品的中英文对照
- **日志系统** - 详细的运行日志，方便排错

---

## 系统要求

- Windows 10/11 64位
- 不需要安装 Python 或 .NET

---

## 下载安装

### 方法一：下载发布版本

1. 访问 [Releases](https://github.com/XXOO140/poe2-111/releases)
2. 下载最新版本的 zip 文件
3. 解压到任意目录
4. 双击 `PoeAncientsPriceHelper.exe` 运行

### 方法二：从源码编译

```bash
git clone https://github.com/XXOO140/poe2-111.git
cd poe2-111
dotnet publish src/PoeAncientsPriceHelper/ -c Release -r win-x64 --self-contained true -o publish
```

---

## 使用教程

### 第一步：启动程序

双击 `PoeAncientsPriceHelper.exe`

### 第二步：校准区域

1. 打开游戏
2. 打开货币/奖励列表（背包里的物品列表）
3. 按 **F4** 或点击 **"校准区域"** 按钮
4. 用鼠标拖拽框选游戏里的列表区域

```
游戏窗口
┌─────────────────────────────────┐
│  ┌───────────────────────────┐  │
│  │ 5x 混沌石                 │  │ ← 框选这个区域
│  │ 3x 崇高石                 │  │
│  │ 1x 神圣石                 │  │
│  └───────────────────────────┘  │
└─────────────────────────────────┘
```

**注意**: 校准时必须在游戏里打开物品列表，框选显示物品的区域！

### 第三步：启动扫描

按 **F5** 或点击 **"启动"** 按钮

- 按钮变红 = 正在运行
- 再按 F5 = 停止扫描

### 第四步：使用

1. 游戏中打开物品列表
2. 软件自动识别物品并显示价格
3. 价格会叠加在游戏画面上

---

## 快捷键

| 按键 | 功能 |
|------|------|
| **F4** | 校准区域 |
| **F5** | 启动/停止扫描 |
| **Esc** | 隐藏价格覆盖层 |

---

## 界面说明

```
┌─────────────────────────────────────────┐
│  PoE2 物价助手                           │
├─────────────────────────────────────────┤
│  联赛: [Standard ▼]                     │
│  区域: x=100 y=200 800×600              │
│  校准键: [F4] [重新绑定]                 │
│  启动/停止键: [F5] [重新绑定]            │
│  [校准区域]                              │
│  ┌─────────────────────────────────┐    │
│  │ 价格同步: 同步成功               │    │
│  │ 上次同步: 2026-06-12 10:30:00   │    │
│  │ 同步次数: 5                     │    │
│  │ 物品数量: 79                    │    │
│  └─────────────────────────────────┘    │
│  已加载 79 个物品 · 上次获取 06月12日    │
│  [启动]                                 │
│  [手动同步价格]                          │
│                    v1.1.4  [请我喝咖啡]  │
└─────────────────────────────────────────┘
```

---

## 同步机制

| 状态 | 行为 |
|------|------|
| 启动时 | 自动首次同步 |
| 同步成功 | 30分钟后再次同步 |
| 同步失败 | 5分钟后重试 |
| 手动同步 | 点击"手动同步价格"按钮 |

---

## 日志文件

遇到问题时查看日志（在 `logs/` 文件夹中）：

| 文件 | 内容 |
|------|------|
| `app.log` | 应用程序日志（启动、关闭、热键、校准等） |
| `price_sync.log` | 价格同步日志（同步过程、错误、重试等） |
| `scan_log.txt` | 扫描引擎日志（OCR识别、物品匹配等） |

---

## 文件结构

```
publish/
├── PoeAncientsPriceHelper.exe  # 主程序
├── item_names_cn.json          # 中英文映射表 (928个)
├── models/v6/                  # RapidOCR 模型
│   ├── det.onnx                # 检测模型
│   └── rec.onnx                # 识别模型
├── logs/                       # 日志目录
└── Start.cmd                   # 启动脚本
```

---

## 常见问题

### Q: 识别不到物品怎么办？

A: 检查以下几点：
1. 校准时是否打开了游戏物品列表
2. 校准区域是否准确框选了物品显示区域
3. 查看 `logs/scan_log.txt` 确认 OCR 是否正常工作

### Q: 价格显示不正确？

A: 
1. 点击"手动同步价格"刷新数据
2. 检查 `logs/price_sync.log` 查看同步状态
3. 确认网络连接正常

### Q: 程序无法启动？

A:
1. 确认是 Windows 10/11 64位系统
2. 检查是否有杀毒软件拦截
3. 查看 `logs/app.log` 查看错误信息

### Q: 如何在其他电脑使用？

A:
1. 复制整个 `publish` 文件夹
2. 粘贴到目标电脑
3. 双击 `PoeAncientsPriceHelper.exe` 运行

---

## 中英文映射表

软件内置 928 个物品的中英文映射，数据来源：PoE2_Ninja_CN

| 简体中文 | 繁体中文 | 英文 |
|----------|----------|------|
| 神圣石 | 神聖石 | Divine Orb |
| 崇高石 | 崇高石 | Exalted Orb |
| 混沌石 | 混沌石 | Chaos Orb |
| 符文合金 | 符文合金 | Runic Alloy |
| 氣旋合金 | 飓风合金 | Cyclonic Alloy |
| 煥彩合金 | 棱镜合金 | Prismatic Alloy |
| 秘能合金 | 神秘合金 | Mystic Alloy |
| ... | ... | ... |

完整映射请查看 `item_names_cn.json`

---

## 技术栈

- **语言**: C# / .NET 8
- **UI框架**: WPF + WinForms
- **OCR引擎**: RapidOCR (PaddleOCR 模型) + Tesseract
- **价格数据**: poe.ninja API

---

## 致谢

- [pedro-quiterio](https://github.com/pedro-quiterio) - 原作者
- [poe.ninja](https://poe.ninja) - 价格数据
- [Tesseract](https://github.com/tesseract-ocr/tesseract) - OCR引擎
- [RapidOCR](https://github.com/RapidAI/RapidOCR) - 中文OCR引擎
- [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) - OCR模型
- [PoE2_Ninja_CN](https://github.com/richardchien/poe-ninja-cn) - 中文翻译数据

---

## 许可

本项目基于原作者的开源项目进行汉化修改。

原项目: https://github.com/pedro-quiterio/PoeAncientsPriceHelper

---

## 支持

如有问题或建议，请提交 Issue：
https://github.com/XXOO140/poe2-111/issues

# CLIQuotaMonitor

<div align="center">

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET 8.0](https://img.shields.io/badge/.NET-8.0--windows-purple.svg)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-lightgrey.svg)
![Build](https://github.com/fengxiaocan/CLIQuotaMonitor/actions/workflows/ci.yml/badge.svg)

**Windows 桌面原生 AI CLI 额度监控悬浮窗**  
实时追踪 OpenAI Codex、xAI Grok、Google Antigravity CLI 的可用额度与重置倒计时。

[功能特性](#-功能特性) • [安装与运行](#-安装与运行) • [交互说明](#-交互说明) • [构建与测试](#-构建与测试) • [架构与扩展](#-架构与扩展) • [设计文档](#-设计文档)

</div>

---

## 📖 项目简介

**CLIQuotaMonitor** 是专为 Windows 10 / 11 开发者打造的轻量级桌面悬浮工具，用于统一监控本机多个 AI CLI 工具（OpenAI Codex、xAI Grok、Google Antigravity 等）的账户剩余额度、周期限额及重置时间。

无需频繁在终端中反复运行 `/status` 或 `/usage`，悬浮窗随手可见，助力开发者精准掌握额度节奏。

---

## ✨ 功能特性

- ⚡ **零侵入与非接管设计**：采用独立的后台子进程与 Windows ConPTY (虚拟终端) 交互，不注入、不修改、不接管用户日常开发中正在使用的 CLI 会话。
- 🛡️ **纯本地与安全至上**：无需配置任何第三方 API Key 或中转服务器，直接复用用户本机已有 CLI 的认证状态；日志输出自带敏感信息脱敏处理器（Redactor）。
- 🪟 **现代沉浸式悬浮窗**：
  - **多模式切换**：支持展开（完整信息）、紧凑、迷你折叠三种展示模式；
  - **自由定制**：随意拖拽吸附、跨屏幕 DPI 自适应、位置记忆；
  - **主题与透明度**：支持浅色/深色/跟随系统主题，支持鼠标滚轮平滑调节透明度及点击穿透模式；
  - **任务栏托盘**：完整的系统托盘菜单与低额度 Toast 桌面通知预警。
- ⏱️ **智能调度与节能休眠**：
  - 多 Provider 错峰并发查询，避免瞬时 CPU 与网络拥堵；
  - 支持工作时间刷新策略（在非工作时间或周末自动暂停轮询，手动刷新随时可用）；
  - UI 独立计算重置倒计时（秒级刷新），不产生额外的 CLI 进程启动开销。
- 🔄 **弹性缓存与优雅降级**：网络抖动或 CLI 输出格式波动时，自动降级展示带有 `Stale` 标识的最近有效缓存，杜绝界面红字报错与闪烁。

---

## 🖥️ 支持的 CLI 工具

| CLI Provider | 常用命令 | 默认检测方式 | 监控指标 |
| :--- | :--- | :--- | :--- |
| **OpenAI Codex CLI** | `codex` | ConPTY / Command (`/status`) | 5h 周期额度、Weekly 额度、重置倒计时 |
| **xAI Grok CLI** | `grok` | ConPTY (`/usage`) | 2h 额度、Daily 额度、Session Tokens |
| **Google Antigravity CLI** | `agy` | ConPTY / Command | 每日额度、模型配额、重置时间 |

> 💡 系统内置可扩展的 `IQuotaProvider` 与 `IQuotaParser` 接口，可轻松接入 Claude Code、Gemini CLI、Cursor CLI 等其他工具。

---

## 🚀 安装与运行

### 系统要求

- **操作系统**：Windows 10 (1809 及以上版本) 或 Windows 11 (x64)
- **运行环境**：[.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)（如果使用独立发布包则无需单独安装）
- **依赖工具**：本机已安装并登录至少一个受支持的 CLI (`codex`, `grok`, `agy`)

### 快速开始

1. 从 [Releases](https://github.com/fengxiaocan/CLIQuotaMonitor/releases) 页面下载最新发布的压缩包；
2. 解压至任意目录，直接双击运行 `CLIQuotaMonitor.App.exe`；
3. 程序首次启动将自动从 `PATH`、`npm`、`AppData` 与 `Program Files` 探测 CLI 路径，托盘区将出现监控图标。

> 📦 **自动部署脚本**：项目 `packaging/` 目录下提供了 `Install.ps1`，可一键将应用安装到 `%LOCALAPPDATA%\CLIQuotaMonitor` 并创建开始菜单快捷方式。

---

## 🎮 交互说明

- **移动与拖拽**：鼠标左键长按悬浮窗背景即可拖动至屏幕任意位置，松开即刻持久化保存坐标。
- **折叠 / 展开**：单击顶部折叠按钮或双击标题栏，快速在完整卡片与迷你指示条间切换。
- **调节透明度**：按住 `Ctrl` 键配合滚轮滚动，或在设置面板中直接滑动调节透明度。
- **手动立即刷新**：单击悬浮窗右上角刷新按钮，或在托盘菜单中选择「立即刷新」。
- **进入设置面板**：右键点击托盘图标选择「设置」，或双击悬浮窗空白处打开设置面板，可自定义：
  - 各 CLI Provider 的启用状态、可执行文件绝对路径及附加查询参数；
  - 自动刷新周期（默认 5 分钟）与超时时间；
  - 工作时间段及工作日规则；
  - 开机自启、置顶状态、点击穿透以及界面主题。

---

## 🛠️ 构建与测试

本项目采用标准 .NET 8.0 解决方案组织，无任何第三方商业闭源依赖。

### 1. 编译与单元测试

```powershell
# 还原依赖
dotnet restore CLIQuotaMonitor.sln

# 编译解决方案 (Release 配置)
dotnet build CLIQuotaMonitor.sln -c Release --no-restore

# 运行所有单元测试 (包含 Core, Providers, App 契约测试)
dotnet test CLIQuotaMonitor.sln -c Release --no-build
```

### 2. 发布独立免安装运行文件 (Publish)

```powershell
dotnet publish src/CLIQuotaMonitor.App/CLIQuotaMonitor.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o artifacts/publish
```

发布完成后，进入 `artifacts/publish/` 运行 `CLIQuotaMonitor.App.exe` 即可。

---

## 🏗️ 架构与扩展

### 方案设计与分层架构

```text
WPF App / Tray UI Layer
        │ 只消费不可变的 QuotaSnapshot
        ▼
QuotaManager (Core) ── 单 Provider 并发锁、错峰调度、工作时间策略、缓存回退
        │
        ▼
IQuotaProvider (Providers) ── Codex / Grok / Antigravity 适配器
        │
        ├─ IQuotaParser ────────── 动态提取额度、重置时刻、清理 ANSI 控制符
        └─ IQueryExecutor (Infra) ─ Command / ConPTY 路由
                                      │
                                      ├─ CommandQueryExecutor (标准管道重定向)
                                      └─ ConPtyQueryExecutor (基于 Windows Pseudo Console API)
```

### 模块结构

- [`src/CLIQuotaMonitor.Core`](src/CLIQuotaMonitor.Core/)：领域模型、契约接口、定时调度编排、工作时间策略与降级缓存抽象。
- [`src/CLIQuotaMonitor.Infrastructure`](src/CLIQuotaMonitor.Infrastructure/)：Windows ConPTY 原生调用、命令执行器、路径自动探测器与自启动管理。
- [`src/CLIQuotaMonitor.Providers`](src/CLIQuotaMonitor.Providers/)：三家主流 AI CLI 适配实现及正则与动态解析器。
- [`src/CLIQuotaMonitor.App`](src/CLIQuotaMonitor.App/)：WPF 桌面悬浮窗、MVVM 绑定、深浅主题切换、系统托盘与设置视窗。
- [`tests/`](tests/)：覆盖核心领域逻辑、执行器行为、各种异常文本解析的单元测试集。

### 如何接入新的 CLI Provider

1. 在 `CLIQuotaMonitor.Providers` 下新建适配类，继承 `CliQuotaProviderBase`；
2. 实现 `IQuotaParser` 接口，定义该 CLI 响应文本的解析逻辑（提取额度数值、百分比、重置时间）；
3. 在 `QuotaManager` 或依赖注入中注册该 Provider 即可自动获得错峰刷新、缓存降级与悬浮窗展示能力。

---

## 📚 设计文档

更详细的架构论证与设计记录请参阅 `docs` 目录：

- 📋 [产品需求文档 (PRD)](docs/需求文档.md)
- 📐 [技术方案设计说明书](docs/方案设计.md)
- 🎯 [需求理解与边界梳理](docs/需求理解.md)
- 📝 [开发任务推进计划](docs/开发任务计划.md)
- ✅ [质量保证与验收清单](docs/验收清单.md)

---

## 🤝 贡献与反馈

欢迎提交 Issue 或 Pull Request！
- 提交 Bug 时请附带 Windows 版本号以及相关的 CLI 输出示例（已去除敏感信息）；
- 若某种 CLI 工具推出了新的输出排版，欢迎贡献新的 Parser 测试样本。

---

## 📄 开源许可证

本项目基于 [MIT License](LICENSE) 开源发布。

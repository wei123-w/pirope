# SnipLite 截图功能迁移实施计划

> **目标：** 将图绳的框选截图流程改为 SnipLite 的交互和功能，保留图绳收纳/贴图主程序与 AI 解释。

**架构：** 图绳继续作为主程序，并将 SnipLite 截图界面作为独立的 .NET 10 自包含组件调用。SnipLite 负责覆盖层、标注、OCR、二维码/条码、窗口/控件选区、放大镜和翻译；通过一次性临时目录把截图及置顶/解释动作回传图绳。图绳继续负责图片记录、收纳和置顶；AI 解释是唯一保留的图绳 AI 操作。

**技术栈：** 图绳 .NET Framework 4.8 主程序、SnipLite .NET 10 Windows Forms 自包含组件、NuGet、SnipLite 的 SimdPaddleOCR/PP-OCRv6 Small、ZXing.Net、UI Automation。

## 全局约束

- 保留图绳托盘、图绳数据库、收纳窗口、截图文件管理和贴图收纳流程。
- 移植 SnipLite 截图交互时保留 SnipLite MIT 许可证声明；继续使用图绳名称与图标，不复制 SnipLite 名称或图标。
- AI 配置仅用于 AI 解释；翻译使用 SnipLite 翻译服务，文字提取使用离线 OCR。
- 任何迁移步骤不得清理或覆盖工作区现有未提交改动。
- 发布目录必须包含离线 OCR 模型、运行时依赖及第三方许可证。

## 实施任务

### 任务一：构建与依赖接入

**文件：** 新建 `PicRope.csproj` 与 `src/SnipLite/`；修改 `build.ps1`。

- [x] 保留图绳 `.NET Framework 4.8` 主程序与 WebView2。
- [ ] 为原样移植的 SnipLite 源码创建独立 `.NET 10 Windows Forms` 项目，避免为现代 C# 源码添加大量 .NET Framework 兼容补丁。
- [ ] 通过 NuGet `PackageReference` 接入 OCR Small 模型、OCR 核心、ZXing 与 UI Automation 依赖。
- [x] 将 SnipLite 截图源文件和资源复制到 `src/SnipLite/`，并保留独立入口以便复用其应用上下文。
- [x] 将截图组件设为自包含发布，主程序不要求用户另行安装 .NET 10 桌面运行时。

**完成条件：** 原图绳主程序与移植后的 SnipLite 源码可由 Roslyn 编译；NuGet 依赖可还原。

### 任务二：移植 SnipLite 截图核心

**文件：** 新建 `src/SnipLiteCapture/` 下的截图、标注、OCR、翻译、自动选区和贴图源文件；修改 `PicRope.csproj`。

- [ ] 移植 `CaptureForm`、`AnnotationBar`、`Annotation`、`CaptureTranslation`、`TranslationPanel` 与相关识别/选区类。
- [ ] 保留 SnipLite 物理像素坐标、多显示器、DPI、选区锚点及鼠标状态逻辑。
- [ ] 迁移 `Sdcb.SimdPaddleOCR`、中文 PP-OCRv6 Small 和 `ZXing.Net` 依赖。
- [ ] 移植 SnipLite 的截图相关设置默认值与状态，不复制其独立程序启动器和产品品牌资产。

**完成条件：** 图绳可打开 SnipLite 截图覆盖层，使用框选、窗口/控件捕获、放大镜、标注、撤销/重做、离线 OCR 及码识别。

### 任务三：将截图结果接回图绳

**文件：** 修改 `src/App.cs`、`src/ScreenCapture.cs`；新建截图适配器。

- [x] 以 SnipLite `CaptureResult` 经临时文件桥接取代旧 `RegionOverlay` 的框选结果接口。
- [ ] 将确认截图的位图继续交给图绳当前的保存、最近截图记录、收纳、置顶和取消流程。
- [ ] 保持全屏、活动窗口、延迟、网页整页、剪贴板收集及图绳数据格式不变。
- [ ] 将贴图请求的置顶图片登记回图绳，确保图绳可以恢复/重新挂回。

**完成条件：** 框选行为由 SnipLite 执行，截图确认后仍进入图绳现有收纳/贴图流程。

### 任务四：保留 AI 解释并清理其他 AI 截图入口

**文件：** 修改 `src/App.cs`、截图工具栏适配代码、`src/SettingsWindow.cs`、`src/AiService.cs`。

- [x] 在 SnipLite 工具栏接入 AI 解释操作，将所选截图交给现有图绳 AI 配置和请求逻辑。
- [ ] 移除截图工具栏中的 AI 翻译与 AI 文字提取；翻译改由 SnipLite 翻译流程执行，OCR 改用离线识别。
- [ ] 对应更新快捷键入口，避免已移除 AI 动作仍占用全局热键。
- [ ] 保留 AI 配置页供 AI 解释使用，不改变密钥的本机加密保存方式。

**完成条件：** 框选工具中 AI 解释可用；翻译与文字提取不再调用多模态 AI API。

### 任务五：许可证、依赖与发布包

**文件：** 修改 `NOTICE`、`build.ps1`、`packaging/`；新增 SnipLite 许可证及第三方依赖声明。

- [ ] 记录 SnipLite MIT 版权与依赖组件许可证。
- [x] 将发布脚本设置为包含 OCR 模型、原生运行库、ZXing、UI Automation 和自包含 .NET 运行时文件。
- [ ] 发布产物继续使用图绳名称、图标和目录结构，不生成多份测试版本。
- [ ] 检查便携包体积与安装脚本的模型/运行时部署方式。

**完成条件：** 在带 .NET Framework 4.8 的 Windows 电脑上按发布说明启动，离线 OCR 模型随程序发布且首次运行无需下载。

### 任务六：构建与流程核对

**文件：** 检查以上所有迁移文件。

- [ ] 编译正式图绳应用，不运行测试套件。
- [ ] 核对框选、自动目标选区、缩放控制点、像素放大镜、颜色取样、标注、撤销/重做、OCR 选字复制、二维码/条码、翻译、AI 解释和取消流程。
- [ ] 核对截图确认进入图绳、置顶截图回挂、图绳数据库兼容和原有截图入口。
- [ ] 核对最终发布目录的模型、原生依赖和许可证。

**完成条件：** 正式程序构建成功，关键截图流程接回图绳，发布包包含所需运行文件。

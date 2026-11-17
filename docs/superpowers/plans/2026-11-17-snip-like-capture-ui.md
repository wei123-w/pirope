# SnipLite 风格截图界面实施计划

> **执行说明：** 按任务顺序实施；每项完成后做对应的代码检查与桌面交互核对，再进入下一项。

**目标：** 将图绳框选截图覆盖层和浮动标注工具栏改成 SnipLite 风格，并把三种 AI 结果整合到覆盖层内，同时保留图绳已有操作。

**架构：** 保留 `RegionOverlay` 作为全屏覆盖层、选区和截图生命周期的协调者。选区视觉与工具栏在覆盖层内适配；新增独立的覆盖层 AI 结果控件负责显示 AI 操作状态、结果、复制、重试和关闭，继续调用图绳现有 AI 委托。SnipLite 只作为视觉与交互参考，不引入其截图流程、OCR 服务或依赖。

**技术栈：** C#、.NET Framework 4.8、Windows Forms、GDI+；项目通过 `build.ps1` 编译。

## 全局约束

- 维持 Windows 10/11、64 位及 .NET Framework 4.8 兼容性。
- 不引入 SnipLite 的 OCR 引擎、翻译服务、模型或第三方依赖。
- 保留截图、标注、快捷键、贴图置顶、本地保存、取消、确认入绳及 AI 配置行为。
- AI 翻译继续支持源语言自动检测/手动选择，目标语言默认中文简体；解释和文字提取保持原有请求语义。
- 不改动工作区原有未提交内容，不把其纳入实现提交。
- 不增加自动化测试文件或运行测试套件；按用户当前授权完成实现并执行应用构建及截图流程人工核对。

---

### 任务一：梳理 AI 请求参数与资源生命周期

**文件：**
- 查看：`src/RegionOverlay.cs`
- 查看：`src/ScreenCapture.cs`
- 查看：`src/App.cs`
- 查看：`src/AiTranslationWindow.cs`

**步骤：**

- [ ] 确认 `App.ProcessRegionCapture` 传入的 AI 委托参数、初始快捷键动作值和异常语义。
- [ ] 确认每次 AI 请求使用的截图位图由谁创建、谁释放；新面板关闭或覆盖层取消时需确保异步请求不会继续访问已释放位图。
- [ ] 确认翻译源/目标语言的现有控件选项及错误、加载、复制、重试行为，为新面板沿用这些语义。
- [ ] 保持本任务只读；整理结果供后续面板接口实现使用。

**完成条件：** 新面板的委托参数与 `ScreenCapture.Region` / `App.ProcessRegionCapture` 既有签名一致，不改动 AI 服务协议。

### 任务二：重做选区视觉、指针与浮层避让

**文件：**
- 修改：`src/RegionOverlay.cs`

**步骤：**

- [ ] 统一选区局部坐标、屏幕坐标和多显示器边界的换算；绘制尺寸标签时将其限制在覆盖层可见区域内。
- [ ] 将选择边框与八个控制点调整为 SnipLite 参考图的蓝色边框、方形控制点；保留选区外遮罩和选区内原始像素显示。
- [ ] 保留八方向缩放行为与选区拖动；按选区状态及命中位置设置十字、移动、缩放、文字和取色光标。
- [ ] 抽取浮层位置计算，使工具栏、第二行工具选项及 AI 结果面板可依据锚点矩形选择上方/下方位置，并限制在覆盖层工作区域内。
- [ ] 逐一核对屏幕四边和角落的选区，确认尺寸标记、控制点和浮层不会跑出边界。

**完成条件：** 框选视觉接近参考图，遮罩、控制点、移动、缩放和截图像素范围保持正确，边界附近的浮层可见。

### 任务三：统一浮动标注工具栏与上下文选项行

**文件：**
- 修改：`src/RegionOverlay.cs`

**步骤：**

- [ ] 按常用标注工具、AI 操作、撤销及完成/取消操作重新排列工具栏，保留当前所有工具和键盘入口。
- [ ] 统一按钮图标尺寸、间距、悬停/选中/禁用样式、提示文字和点击区域，匹配 SnipLite 白色圆角浮条。
- [ ] 将颜色、线宽及文字字号等控件收敛到工具栏下方的上下文选项行；仅当当前工具需要时显示。
- [ ] 移动/选区工具隐藏选项行；再次点击当前标注工具可收起选项行；更换工具时同步更新对应选项。
- [ ] 将工具栏与选项行作为一组定位，优先放在选区下方，空间不足则置于上方，且不遮挡控制点。

**完成条件：** 每个标注工具可用原有快捷键和点击操作启动；工具对应选项准确显示/隐藏；保存、贴图、取消、确认和撤销入口均保留。

### 任务四：新增覆盖层 AI 结果面板

**文件：**
- 新建：`src/RegionAiPanel.cs`
- 修改：`src/RegionOverlay.cs`

**面板接口：**

```csharp
internal sealed class RegionAiPanel : UserControl
{
    internal event EventHandler Closed;
    internal event EventHandler RetryRequested;
    internal string SourceLanguage { get; }
    internal string TargetLanguage { get; }
    internal void Begin(string action);
    internal void SetResult(string text);
    internal void SetFailure(string message);
}
```

**步骤：**

- [ ] 创建覆盖层子控件，显示操作标题、翻译语言选择（仅翻译操作）、状态、可滚动结果、复制、重试和关闭按钮。
- [ ] 采用清晰的浅色圆角卡片、稳定的文字层级和图绳蓝色强调色；面板尺寸随屏幕空间约束，长结果在面板内部滚动。
- [ ] 将控件添加至 `RegionOverlay`，定位在当前选区/工具栏附近，并在边界处翻转位置和限制大小。
- [ ] 为关闭、重试和复制提供事件/方法；面板关闭只隐藏结果控件，不结束截图会话。
- [ ] 确保翻译目标默认“中文简体”，源语言默认自动检测；其余语言选择沿用当前 `AiTranslationWindow` 支持的选项。

**完成条件：** 面板可在截图覆盖层内显示，加载、成功、失败三种状态可辨识，复制/重试/关闭入口可用，且翻译语言选择与原功能兼容。

### 任务五：将 AI 操作与快捷键迁入覆盖层面板

**文件：**
- 修改：`src/RegionOverlay.cs`
- 保留但不再调用：`src/AiTranslationWindow.cs`

**步骤：**

- [ ] 将 `RunAiAction` 改为覆盖层内异步流程：展示面板加载态，调用现有 AI 委托，再更新结果或错误状态。
- [ ] 翻译请求读取面板当前源/目标语言；解释继续发送自动检测与中文简体参数；文字提取继续发送空语言参数。
- [ ] 重试使用面板当前操作与语言设置重新调用同一请求路径。
- [ ] 处理连续切换操作、关闭面板、关闭覆盖层时的请求标识；过期响应不得覆盖较新的结果，位图在请求完成后释放。
- [ ] 确保构造参数 `initialAiAction` 及全局快捷键仍能触发同一个面板流程。
- [ ] 保留 `AiTranslationWindow.cs` 中旧窗口类型但移除截图覆盖层对其的调用，避免影响现有代码引用和旧桌面测试编译。

**完成条件：** 翻译、解释、文字提取及其快捷键入口都只在截图覆盖层内显示结果；网络失败时保留可读错误与重试能力，关闭面板不结束截图操作。

### 任务六：构建与桌面流程核对

**文件：**
- 检查：`src/RegionOverlay.cs`
- 检查：`src/RegionAiPanel.cs`
- 检查：`src/ScreenCapture.cs`
- 检查：`src/App.cs`

**步骤：**

- [ ] 只编译主程序，不运行测试套件。于 PowerShell 执行：

```powershell
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$root = (Get-Location).Path
$references = @(
    "/r:$frameworkPath\WPF\PresentationCore.dll",
    "/r:$frameworkPath\WPF\PresentationFramework.dll",
    "/r:$frameworkPath\WPF\WindowsBase.dll",
    "/r:$frameworkPath\System.Xaml.dll",
    "/r:$frameworkPath\System.Web.Extensions.dll",
    "/r:$frameworkPath\Microsoft.VisualBasic.dll",
    "/r:$root\vendor\WebView2\Microsoft.Web.WebView2.Core.dll",
    "/r:$root\vendor\WebView2\Microsoft.Web.WebView2.Wpf.dll",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll', '/r:System.Security.dll',
    '/r:System.Net.Http.dll', '/r:System.IO.Compression.dll',
    '/r:System.IO.Compression.FileSystem.dll'
)
$sources = (Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs').FullName
$basketResources = Get-ChildItem -LiteralPath (Join-Path $root 'src\assets') -Filter 'basket-*.png' |
    ForEach-Object { '/resource:' + $_.FullName + ',PicRope.Basket.' + $_.BaseName.Substring(7) }
$basketResources += Get-ChildItem -LiteralPath (Join-Path $root 'src\assets') -Filter 'envelope-*.png' |
    ForEach-Object { '/resource:' + $_.FullName + ',PicRope.Envelope.' + $_.BaseName.Substring(9) }
& (Join-Path $frameworkPath 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    "/out:$root\build\PicRope.exe" "/win32manifest:$root\src\app.manifest" `
    "/win32icon:$root\src\assets\PicRope.ico" `
    "/resource:$root\src\assets\PicRope.ico,PicRope.Icon" `
    $basketResources $references $sources
if ($LASTEXITCODE -ne 0) { throw '应用构建失败' }
```

- [ ] 启动构建产物，人工核对框选、选区内外画面、八向缩放、移动、工具栏上下避让、选项行显示/隐藏。
- [ ] 核对翻译、解释、文字提取的面板显示、复制、重试、关闭及失败提示；核对相应快捷键启动。
- [ ] 核对确认入绳、取消、本地保存和贴图置顶仍按原流程工作。
- [ ] 最终查看 `git diff` 和 `git status --short`，确认没有覆盖用户原有未提交文件或引入无关变更。

**完成条件：** 主程序成功编译，截图覆盖层关键交互完成桌面人工核对，原有工作区改动保持未触碰。

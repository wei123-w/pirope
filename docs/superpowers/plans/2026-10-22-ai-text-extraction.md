# AI 文字提取实施计划

> **执行说明：**按步骤逐项实现；每个步骤先增加失败测试，再写最小实现并运行相关测试。

**目标：**为框选截图增加 AI 文字提取工具栏动作和可配置的全局快捷键，并使用现有结果窗口显示纯文本提取结果。

**架构：**复用现有 `AiService.AskAsync`、`AiResultWindow`、框选 overlay 和快捷键注册流程。新增 `ExtractText` 动作提示词、`TextExtractShortcut` 配置及“AI 文字提取”结果窗口；不新增 AI 服务或 OCR 依赖。

**技术栈：**C#、.NET Framework、Windows Forms、WPF `HwndSource`、现有桌面测试程序。

## 全局约束

- 所有现有截图、标注、翻译和解释行为保持不变。
- 新快捷键默认 `Ctrl+Shift+O`，并由设置页允许修改。
- 提取结果只包含图片中清晰可见的文字，保留段落换行，不翻译、不解释、不总结。
- 继续使用现有 AI 配置、图片压缩和结果窗口交互。

---

### 任务一：快捷键配置与持久化

**文件：**
- 修改：`src/Core.cs`
- 修改：`src/SettingsWindow.cs`
- 测试：`tests/DesktopTests.cs`

- [x] 为 `SavedState` 增加 `TextExtractShortcut`，默认 `Ctrl+Shift+O`；旧配置缺省时补齐。
- [x] 将其加入设置页 AI 操作快捷键列表、草稿复制、保存和恢复默认。
- [x] 先增加测试并确认缺少属性/默认值时失败，再实现并运行 AI 设置专项测试。

### 任务二：注册快捷键并启动框选

**文件：**
- 修改：`src/WindowsNative.cs`
- 修改：`src/App.cs`
- 修改：`tests/DesktopTests.cs`

- [x] 分配新的全局快捷键编号，纳入应用快捷键注册、冲突回滚及默认值恢复。
- [x] 保存设置时同步应用新值；新快捷键触发 `CaptureScreenshot(0, IntPtr.Zero, "ExtractText")`。
- [x] 先增加注册/分发相关测试，再运行快捷键和设置专项测试。

### 任务三：AI 提取提示词与结果窗口

**文件：**
- 修改：`src/AiService.cs`
- 修改：`src/AiTranslationWindow.cs`
- 测试：`tests/DesktopTests.cs`

- [x] 为 `ExtractText` 增加专用提示词，要求提取可见文字、保留结构且只返回文本。
- [x] 新增沿用 `AiResultWindow` 的 `AiTextExtractionWindow`，标题为“文字提取”，支持加载、复制、重试和错误状态。
- [x] 先增加提示词与窗口行为测试并确认失败，再实现和运行 AI 专项测试。

### 任务四：框选工具栏接入与回归

**文件：**
- 修改：`src/RegionOverlay.cs`
- 测试：`tests/DesktopTests.cs`

- [x] 在标注工具栏增加“AI 文字提取”动作。
- [x] `RunAiAction` 对 `ExtractText` 显示提取窗口，并传入当前框选图片及现有 AI 回调。
- [x] 验证选区图片生命周期覆盖窗口首次请求与重试；不改变确认截图和取消行为。
- [x] 运行 AI 专项测试和 `build.ps1`，确认主程序构建及核心/附加测试通过。

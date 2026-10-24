# 截图标注模式实施计划

> **执行说明：** 按本计划逐项实现；每项完成后运行对应验证，再继续下一项。

**目标：** 框选截图后先进入标注模式，用户确认后才保存并直接挂到图绳，并为九种标注工具提供独立可配置的快捷键。

**架构：** 扩展现有 WebView2 图片编辑器，使其能从内存位图启动截图确认模式，并提供专用的工具条和确认/取消消息。C# 负责接入截图窗口、延迟保存与挂绳；设置状态负责保存九个编辑器内快捷键；浏览器侧负责绘图工具和快捷键。

**技术栈：** C#、WPF、WebView2、现有 Filerobot 编辑器、JavaScript、现有 PowerShell 构建与测试脚本。

## 全局约束

- 确认前不创建截图文件，不导入图绳。
- 确认后保存 PNG 并强制挂到图绳；取消或关闭不保存、不导入。
- 标注快捷键只在截图标注窗口内工作，不注册系统全局快捷键。
- 保留已有图片编辑器入口和其他截图模式；不恢复长截图，不添加 OCR、翻译、解释、贴图或带壳截图。
- 标注工具为矩形、圆形、箭头、画笔、马赛克、文字、高亮、序号和取色，默认快捷键 1 至 9。

---

### 任务 1：为标注工具快捷键添加状态默认值和兼容迁移

**文件：**
- 修改：`src/Core.cs`
- 修改：`tests/CoreTests.cs`

**接口：**
- `SavedState.AnnotationHotkeys` 使用 `Dictionary<string, string>`，键名固定为 `Rect`、`Ellipse`、`Arrow`、`Pen`、`Mosaic`、`Text`、`Highlight`、`Number`、`Eyedropper`。
- `StateStore` 读取旧 `state.json` 时为缺失或空映射补齐默认键；缺少单个键时仅补齐该键。

- [x] **步骤 1：先添加状态测试**，断言新建状态映射为 1 至 9，旧状态缺失映射与部分映射读取后均获得完整默认值。
- [x] **步骤 2：运行核心测试确认测试失败。**

```powershell
.\build.ps1
```

预期：新增测试因默认属性或迁移行为尚未实现而失败。

- [x] **步骤 3：实现默认映射和旧状态补齐。** 默认映射顺序为矩形 1、圆形 2、箭头 3、画笔 4、马赛克 5、文字 6、高亮 7、序号 8、取色 9。迁移逻辑不得重写已有有效值。
- [x] **步骤 4：再次运行 `\.\build.ps1`，确认核心测试通过且应用构建成功。**

### 任务 2：设置页录入、验证和保存标注快捷键

**文件：**
- 修改：`src/SettingsWindow.cs`
- 修改：`src/App.cs`
- 修改：`tests/CoreTests.cs` 或 `tests/DesktopTests.cs`（按现有可测试边界选择）

**接口：**
- 设置页快捷键选项沿用 `SavedState.AnnotationHotkeys` 的九个固定键名。
- 标注快捷键录入控件允许无修饰单键，也允许 Ctrl、Alt、Shift、Win 组合键；空值、未完成组合和九项之间的重复值阻止保存。
- `SettingsWindow.Copy`、`App.SaveSettings` 和失败回滚完整复制该映射。

- [x] **步骤 1：添加失败测试**，覆盖去重校验、默认值恢复，以及保存失败时原快捷键配置不变。
- [x] **步骤 2：运行相关测试确认失败。**

```powershell
.\build.ps1 -EditorTests
```

预期：新断言因设置页尚未显示或保存标注快捷键而失败。

- [x] **步骤 3：在“快捷键”页添加独立的“标注工具快捷键”分组和九个按键录入框。** 聚焦输入框后直接按键录入；退格清空；Esc 清空录入并退出当前录入状态；现有全局快捷键仍要求修饰键且逻辑不变。
- [x] **步骤 4：把九个字段纳入默认恢复、值校验、保存和异常回滚。** 只在九个编辑器快捷键之间检查重复，不将它们注册为全局快捷键。
- [x] **步骤 5：运行 `\.\build.ps1 -EditorTests` 并确认设置界面测试通过。**

### 任务 3：增加截图标注模式及九种绘图工具

**文件：**
- 修改：`src/editor/index.html`
- 修改：`src/editor/editor.css`
- 修改：`src/editor/editor.js`
- 修改：`tests/EditorUiTests.py`

**接口：**
- `window.startEditor(options)` 接收 `mode: "capture"` 和 `hotkeys`；普通图片编辑不传 `mode` 时保持原有行为。
- 截图模式通过 `window.chrome.webview.postMessage` 发送 `{action:"confirm", data:<PNG base64>}`、`{action:"cancel"}` 或 `{action:"saveCopy", data:<PNG base64>}`；普通模式继续使用既有 `save`、`copy` 和 `dirty` 消息。
- 导出内容由编辑器当前图像与新增标注合成，尺寸保持截图像素尺寸。

- [x] **步骤 1：在 `tests/EditorUiTests.py` 增加截图模式控件、工具切换和快捷键隔离测试。** 测试覆盖默认按键选择工具、输入框编辑文字时不抢快捷键、取消/确认消息分流，以及普通模式仍显示原有功能。
- [x] **步骤 2：运行编辑器界面测试确认新增断言失败。**

```powershell
python tests/EditorUiTests.py build/editor
```

预期：新增截图模式及按键测试失败。

- [x] **步骤 3：增加截图模式工具条，提供矩形、圆形、箭头、画笔、马赛克、文字、高亮、序号、取色、撤销、本地保存、取消和确认。** 截图模式隐藏裁剪、旋转、滤镜等非本次范围工具；普通图片编辑模式布局不变。
- [x] **步骤 4：将九种工具映射到现有 Filerobot 注释能力和新增画布注释。** 矩形、圆形、箭头、画笔、文字沿用原生标注；马赛克按拖选矩形像素化；高亮按拖选矩形绘制半透明填充；序号点击递增放置；取色从原始截图像素更新当前绘图颜色。
- [x] **步骤 5：维护新增标注的撤销历史并将其合成到导出 PNG。** 支持取消当前绘制、撤销已完成的自定义标注；序号每次放置形成独立历史项。
- [x] **步骤 6：在截图模式监听配置的快捷键，仅在焦点不位于输入框或可编辑元素时切换工具。** 普通编辑器仍保留原有撤销、重做、保存和复制快捷键。
- [x] **步骤 7：运行 `python tests/EditorUiTests.py build/editor` 确认测试通过。**

### 任务 4：让截图窗口接收内存位图并将确认结果接入挂绳

**文件：**
- 修改：`src/ImageEditorWindow.cs`
- 修改：`src/App.cs`
- 修改：`tests/DesktopTests.cs`

**接口：**
- 增加 `ImageEditorWindow(BitmapSource capture, StateStore store, Func<byte[], Task> confirmed, Action cancelled)` 构造路径；现有 `ImageEditorWindow(string path, ...)` 保持不变。
- 内存截图模式启动时把 PNG 数据作为 `data:image/png;base64,...` 传给 `window.startEditor`，不临时写盘。
- `App` 对框选截图等待标注窗口完成；收到确认 PNG 后调用现有保存逻辑，之后 `ProcessCapturedFile(path, "框选截图", "Hang")`。

- [x] **步骤 1：在桌面测试中增加取消和确认流程测试。** 取消断言无文件、无导入；确认断言最终 PNG 被保存并经现有流水线导入与挂绳。
- [x] **步骤 2：运行 `\.\build.ps1 -EditorTests` 确认新增桌面测试先失败。**
- [x] **步骤 3：添加内存位图启动和确认/取消消息回调。** 保证 WebView 初始化失败、导出失败时不调用保存或挂绳，并保持窗口可重试或取消。
- [x] **步骤 4：只将框选截图接入截图标注窗口；确认后使用 PNG 保存并强制调用挂绳动作。** 其他截图模式仍走原截图完成设置。
- [x] **步骤 5：确保取消、窗口关闭和初始化异常路径释放位图及 WebView 资源。**
- [x] **步骤 6：运行 `\.\build.ps1 -EditorTests` 确认桌面流程测试通过。**

### 任务 5：完整构建并回归现有截图与编辑流程

**文件：**
- 视验证结果修改上述实现和测试文件。

- [x] **步骤 1：运行完整构建及核心测试。**

```powershell
.\build.ps1
```

预期：`build\PicRope.exe` 成功生成，核心测试全部通过。

- [x] **步骤 2：运行编辑器和桌面相关测试。**

```powershell
.\build.ps1 -EditorTests
python tests/EditorUiTests.py build/editor
```

预期：新增标注流程测试与已有图片编辑器测试全部通过。

- [x] **步骤 3：检查 `git diff --check` 和最终差异。** 修复空白错误，并确认差异没有改动全局截图快捷键、长截图或其他未授权功能。

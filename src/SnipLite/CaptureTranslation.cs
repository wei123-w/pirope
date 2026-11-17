namespace SnipLite;

internal sealed partial class CaptureForm
{
    private TranslationPanel? _translationPanel;
    private string? _translationInput;
    private IReadOnlyList<TranslationBlock> _translationBlocks = Array.Empty<TranslationBlock>();
    private Bitmap? _translationSnapshot;
    private Bitmap? _translationLayer;
    private Rectangle _translationCrop;
    private CancellationTokenSource? _translationCancellation;
    private int _translationRequest;
    private ITranslationProvider _translationProvider;
    private readonly Dictionary<(string Text, string Source, string Target), TranslationResult> _translationCache = new();
    private bool TranslationVisible => _translationPanel is { Visible: true };

    private void EnsureTranslationPanel()
    {
        if (_translationPanel is not null) return;
        _translationPanel = new TranslationPanel(Ui, _settings.TranslationSourceLanguage, _settings.TranslationTargetLanguage,
            _settings.Accent, _settings.TranslationProvider) { Visible = false };
        _translationPanel.ProviderRequested += SwitchTranslationProvider;
        _translationPanel.LanguagesChanged += swapped =>
        {
            _settings.TranslationSourceLanguage = _translationPanel.SourceLanguage;
            _settings.TranslationTargetLanguage = _translationPanel.TargetLanguage;
            _settings.Save();
            if (!string.IsNullOrWhiteSpace(_translationInput)) _ = TranslateRecognizedTextAsync();
        };
        _translationPanel.ViewChanged += () => Invalidate(_selection);
        _translationPanel.RetryRequested += () =>
        {
            if (string.IsNullOrWhiteSpace(_translationInput)) _ = TranslateSelectionAsync();
            else _ = TranslateRecognizedTextAsync(force: true);
        };
        _translationPanel.MenuOpening += menu =>
        {
            _optionMenus.Add(menu);
            menu.Closed += (_, _) => _optionMenus.Remove(menu);
        };
        Controls.Add(_translationPanel);
    }

    private void SwitchTranslationProvider(TranslationProviderKind provider)
    {
        if (_translationPanel is not { Visible: true } panel || panel.Provider == provider) return;
        if (provider == TranslationProviderKind.Microsoft && !MicrosoftTranslatorCredentials.HasKey())
        {
            panel.SetStatus(() => UiText.T("请在首选项 → 常规 → 配置微软密钥"));
            return;
        }
        _translationProvider = TranslationProviders.Create(provider);
        panel.SetProvider(provider);
        _translationCache.Clear();
        // An OCR request may still be running. It will use the newly selected provider when it completes.
        if (!string.IsNullOrWhiteSpace(_translationInput)) _ = TranslateRecognizedTextAsync();
    }

    private async Task TranslateSelectionAsync()
    {
        if (!_hasSelection || _recognitionBusy || _ocrMode || _finished) return;
        CommitText(); ClearQrResult();
        var crop = Crop; if (crop.Width < 1 || crop.Height < 1) return;
        EnsureTranslationPanel(); CancelTranslation(); ClearTranslationImages();
        _translationInput = null; _translationBlocks = Array.Empty<TranslationBlock>();
        _translationCrop = crop;
        _translationPanel!.SetOriginal(""); _translationPanel.SetBusy(() => UiText.T("正在识别…"));
        _translationPanel.Visible = true; _translationPanel.BringToFront();
        _bar.TranslationMode = true; _bar.Status = ""; _showMagnifier = false;
        var request = ++_translationRequest;
        Invalidate();
        // Translate the captured background. Annotations stay editable above the translation layer.
        using var image = _screenshot.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var document = await Task.Run(() => Services.RecognizeTextAsync(image));
            if (IsDisposed || !TranslationVisible || request != _translationRequest) return;
            _translationInput = document.Text; _translationBlocks = TranslationOverlay.GetBlocks(document);
            _translationSnapshot = new Bitmap(image);
            _translationPanel.SetOriginal(document.Text);
            if (string.IsNullOrWhiteSpace(document.Text)) _translationPanel.SetError(() => UiText.T("未识别到文字，请调整选区。"));
            else await TranslateRecognizedTextAsync();
        }
        catch
        {
            if (!IsDisposed && TranslationVisible && request == _translationRequest)
                _translationPanel.SetError(() => UiText.T("识别失败，请重试或调整选区。"));
        }
        finally
        {
            if (!IsDisposed) { UpdateCursor(); Invalidate(); }
        }
    }

    private async Task TranslateRecognizedTextAsync(bool force = false)
    {
        if (!TranslationVisible || string.IsNullOrWhiteSpace(_translationInput)) return;
        _translationCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _translationCancellation = cancellation;
        var request = ++_translationRequest;
        var source = _translationPanel!.SourceLanguage; var target = _translationPanel.TargetLanguage;
        var provider = _translationProvider;
        var blocks = _translationBlocks.Count > 0 ? _translationBlocks : new[] { new TranslationBlock(_translationInput, new(0, 0, 1, 1), 1) };
        _translationLayer?.Dispose(); _translationLayer = null;
        _translationPanel.SetBusy(() => UiText.T("正在翻译…")); Invalidate(_selection);
        Exception? firstFailure = null;
        try
        {
            if (_translationInput.Length > 12_000 || blocks.Count > 80)
                throw new InvalidOperationException(UiText.T("文字较多，请缩小选区后翻译。"));
            using var workers = new SemaphoreSlim(2, 2);
            var completed = 0;
            var results = await Task.WhenAll(blocks.Select(async block =>
            {
                await workers.WaitAsync(cancellation.Token);
                try
                {
                    var key = (block.Text, source, target);
                    if (force || !_translationCache.TryGetValue(key, out var result))
                    {
                        result = await provider.TranslateAsync(block.Text, source, target, cancellation.Token);
                        cancellation.Token.ThrowIfCancellationRequested();
                        if (_translationCache.Count >= 80) _translationCache.Clear();
                        _translationCache[key] = result;
                    }
                    completed++;
                    if (!IsDisposed && TranslationVisible && request == _translationRequest) _translationPanel.SetProgress(completed, blocks.Count);
                    return result;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    firstFailure ??= ex;
                    cancellation.Cancel();
                    throw;
                }
                finally { workers.Release(); }
            }));
            if (IsDisposed || !TranslationVisible || request != _translationRequest || cancellation.IsCancellationRequested) return;
            if (_translationSnapshot is not null)
            {
                using var snapshot = new Bitmap(_translationSnapshot);
                var layer = await Task.Run(() => TranslationOverlay.Render(snapshot, blocks, results, cancellation.Token));
                if (IsDisposed || !TranslationVisible || request != _translationRequest || cancellation.IsCancellationRequested) { layer.Dispose(); return; }
                _translationLayer = layer;
            }
            _translationPanel.SetResult(new(string.Join(Environment.NewLine, results.Select(r => r.Text)), results.FirstOrDefault(r => r.DetectedLanguage is not null)?.DetectedLanguage));
            Invalidate(_selection);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested && firstFailure is null) { }
        catch (Exception ex)
        {
            if (!IsDisposed && TranslationVisible && request == _translationRequest)
            {
                var error = firstFailure ?? ex;
                _translationPanel.SetError(() => error is InvalidOperationException ? error.Message : UiText.T("翻译失败，请检查网络后重试。"));
            }
        }
        finally { if (ReferenceEquals(_translationCancellation, cancellation)) _translationCancellation = null; }
    }

    private void DrawTranslationOverlay(Graphics graphics, Point offset)
    {
        if (!TranslationVisible || _translationPanel!.ShowOriginal || _translationLayer is null) return;
        graphics.DrawImageUnscaled(_translationLayer, _translationCrop.X + offset.X, _translationCrop.Y + offset.Y);
    }

    private void LayoutTranslationPanel()
    {
        if (!TranslationVisible) return;
        var bounds = new Rectangle(_bar.MainBounds.Left, _bar.Bounds.Bottom + (int)Math.Round(4 * Ui), _bar.MainBounds.Width, _translationPanel!.Height);
        if (_translationPanel.Bounds != bounds) _translationPanel.Bounds = bounds;
    }

    private void RefreshTranslationSelection()
    {
        if (!TranslationVisible) return;
        if (!_hasSelection) CloseTranslation();
        else if (Crop != _translationCrop) _ = TranslateSelectionAsync();
    }

    private void CancelTranslation()
    {
        _translationRequest++;
        _translationCancellation?.Cancel(); _translationCancellation = null;
        _translationPanel?.CloseMenus();
    }

    private void ClearTranslationImages()
    {
        _translationLayer?.Dispose(); _translationLayer = null;
        _translationSnapshot?.Dispose(); _translationSnapshot = null;
    }

    private void CloseTranslation()
    {
        if (_translationPanel is null || !TranslationVisible) return;
        CancelTranslation(); ClearTranslationImages();
        _translationPanel.Visible = false;
        _translationInput = null; _translationBlocks = Array.Empty<TranslationBlock>(); _translationCache.Clear();
        _bar.TranslationMode = false; _bar.AttachedPanelHeight = 0;
        Focus(); UpdateCursor(); Invalidate();
    }
}

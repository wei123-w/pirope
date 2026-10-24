(() => {
  const names = ['Rect', 'Ellipse', 'Arrow', 'Pen', 'Mosaic', 'Text', 'Highlight', 'Number', 'Eyedropper'];
  const labels = { Rect: '矩形', Ellipse: '圆形', Arrow: '箭头', Pen: '画笔', Mosaic: '马赛克', Text: '文字', Highlight: '高亮', Number: '序号', Eyedropper: '取色' };
  const operations = [];
  let captureMode = false, hotkeys = {}, active = 'Rect', canvas, context, sourceImage, bounds, drawing = null, textInput = null, color = '#d64c46', busy = false;
  const originalStartEditor = window.startEditor;
  const imageLoaded = image => new Promise((resolve, reject) => { image.onload = resolve; image.onerror = reject; });
  const post = message => window.chrome.webview.postMessage(message);
  const pointAt = event => ({ x: Math.max(0, Math.min(1, (event.clientX - bounds.x) / bounds.width)), y: Math.max(0, Math.min(1, (event.clientY - bounds.y) / bounds.height)) });

  function updateBounds() {
    if (!canvas || !captureMode) return;
    const [x, y, width, height] = window.imageBounds();
    bounds = { x, y, width, height };
    const ratio = window.devicePixelRatio || 1;
    canvas.style.left = `${x}px`; canvas.style.top = `${y}px`;
    canvas.style.width = `${width}px`; canvas.style.height = `${height}px`;
    canvas.width = Math.max(1, Math.round(width * ratio)); canvas.height = Math.max(1, Math.round(height * ratio));
    context = canvas.getContext('2d'); context.setTransform(ratio, 0, 0, ratio, 0, 0);
    redraw();
  }

  function paintMosaic(ctx, op, width, height, image) {
    const x = Math.min(op.start.x, op.end.x) * width, y = Math.min(op.start.y, op.end.y) * height;
    const w = Math.max(1, Math.abs(op.end.x - op.start.x) * width), h = Math.max(1, Math.abs(op.end.y - op.start.y) * height);
    const sample = document.createElement('canvas');
    sample.width = Math.max(1, Math.round(w / 14)); sample.height = Math.max(1, Math.round(h / 14));
    const small = sample.getContext('2d');
    small.imageSmoothingEnabled = true;
    small.drawImage(image, Math.min(op.start.x, op.end.x) * image.naturalWidth, Math.min(op.start.y, op.end.y) * image.naturalHeight,
      Math.abs(op.end.x - op.start.x) * image.naturalWidth, Math.abs(op.end.y - op.start.y) * image.naturalHeight, 0, 0, sample.width, sample.height);
    ctx.save(); ctx.imageSmoothingEnabled = false; ctx.drawImage(sample, x, y, w, h); ctx.restore();
  }

  function paintOperation(ctx, op, width, height, image) {
    if (op.type === 'Mosaic') { paintMosaic(ctx, op, width, height, image); return; }
    const a = { x: op.start.x * width, y: op.start.y * height };
    const b = { x: op.end.x * width, y: op.end.y * height };
    const lineWidth = Math.max(2, Math.min(width, height) * .006);
    ctx.save(); ctx.lineWidth = lineWidth; ctx.lineCap = 'round'; ctx.lineJoin = 'round'; ctx.strokeStyle = op.color || color; ctx.fillStyle = op.color || color;
    if (op.type === 'Rect' || op.type === 'Highlight') {
      const x = Math.min(a.x, b.x), y = Math.min(a.y, b.y), w = Math.abs(a.x - b.x), h = Math.abs(a.y - b.y);
      if (op.type === 'Highlight') { ctx.globalAlpha = .34; ctx.fillStyle = '#ffdf35'; ctx.fillRect(x, y, w, h); }
      else ctx.strokeRect(x, y, w, h);
    } else if (op.type === 'Ellipse') {
      ctx.beginPath(); ctx.ellipse((a.x + b.x) / 2, (a.y + b.y) / 2, Math.abs(b.x - a.x) / 2, Math.abs(b.y - a.y) / 2, 0, 0, Math.PI * 2); ctx.stroke();
    } else if (op.type === 'Arrow') {
      const angle = Math.atan2(b.y - a.y, b.x - a.x), head = Math.max(12, lineWidth * 4);
      ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.moveTo(b.x, b.y); ctx.lineTo(b.x - head * Math.cos(angle - Math.PI / 6), b.y - head * Math.sin(angle - Math.PI / 6));
      ctx.moveTo(b.x, b.y); ctx.lineTo(b.x - head * Math.cos(angle + Math.PI / 6), b.y - head * Math.sin(angle + Math.PI / 6)); ctx.stroke();
    } else if (op.type === 'Pen') {
      ctx.beginPath(); op.points.forEach((point, index) => { const p = { x: point.x * width, y: point.y * height }; if (index) ctx.lineTo(p.x, p.y); else ctx.moveTo(p.x, p.y); }); ctx.stroke();
    } else if (op.type === 'Text') {
      ctx.font = `${Math.max(16, width * .027)}px "Microsoft YaHei", sans-serif`; ctx.textBaseline = 'top'; ctx.fillText(op.text, a.x, a.y);
    } else if (op.type === 'Number') {
      const radius = Math.max(13, Math.min(width, height) * .025);
      ctx.fillStyle = '#2463ae'; ctx.beginPath(); ctx.arc(a.x, a.y, radius, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#fff'; ctx.font = `bold ${radius * 1.15}px "Segoe UI", sans-serif`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillText(String(op.value), a.x, a.y + 1);
    }
    ctx.restore();
  }

  function redraw() {
    if (!context || !bounds) return;
    context.clearRect(0, 0, bounds.width, bounds.height);
    operations.forEach(op => paintOperation(context, op, bounds.width, bounds.height, sourceImage));
    if (drawing) paintOperation(context, drawing, bounds.width, bounds.height, sourceImage);
  }

  function choose(name) {
    if (!names.includes(name)) return;
    active = name;
    document.querySelectorAll('[data-capture-tool]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.captureTool === name)));
    canvas.style.cursor = name === 'Eyedropper' ? 'copy' : name === 'Text' ? 'text' : 'crosshair';
  }

  function commitText(value, point) {
    if (value.trim()) { operations.push({ type: 'Text', start: point, end: point, text: value.trim(), color }); redraw(); }
    textInput?.remove(); textInput = null;
  }

  function addText(point) {
    if (textInput) commitText(textInput.value, textInput._point);
    const input = document.createElement('textarea');
    input.className = 'capture-text-editor'; input.placeholder = '输入文字'; input.style.left = `${bounds.x + point.x * bounds.width}px`; input.style.top = `${bounds.y + point.y * bounds.height}px`;
    input._point = point; document.body.appendChild(input); textInput = input; input.focus();
    input.addEventListener('keydown', event => {
      if (event.isComposing || event.keyCode === 229) return;
      if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); input.remove(); textInput = null; }
      else if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); event.stopPropagation(); commitText(input.value, point); }
    });
    input.addEventListener('blur', () => { if (textInput === input) commitText(input.value, point); });
  }

  function sampleColor(point) {
    const sample = document.createElement('canvas'); sample.width = sourceImage.naturalWidth; sample.height = sourceImage.naturalHeight;
    const ctx = sample.getContext('2d', { willReadFrequently: true }); ctx.drawImage(sourceImage, 0, 0);
    const pixel = ctx.getImageData(Math.min(sample.width - 1, Math.floor(point.x * sample.width)), Math.min(sample.height - 1, Math.floor(point.y * sample.height)), 1, 1).data;
    color = `#${[pixel[0], pixel[1], pixel[2]].map(value => value.toString(16).padStart(2, '0')).join('')}`;
    choose('Rect');
  }

  function bindCanvas() {
    canvas.addEventListener('pointerdown', event => {
      if (busy || event.button !== 0) return;
      event.preventDefault(); event.stopPropagation();
      const point = pointAt(event);
      if (active === 'Eyedropper') { sampleColor(point); return; }
      if (active === 'Text') { addText(point); return; }
      if (active === 'Number') { operations.push({ type: 'Number', start: point, end: point, value: operations.filter(op => op.type === 'Number').length + 1 }); redraw(); return; }
      drawing = { type: active, start: point, end: point, color, points: [point] };
      canvas.setPointerCapture(event.pointerId); redraw();
    });
    canvas.addEventListener('pointermove', event => {
      if (!drawing) return;
      event.preventDefault(); drawing.end = pointAt(event); if (active === 'Pen') drawing.points.push(drawing.end); redraw();
    });
    canvas.addEventListener('pointerup', event => {
      if (!drawing) return;
      event.preventDefault(); drawing.end = pointAt(event);
      if (Math.abs(drawing.end.x - drawing.start.x) > .002 || Math.abs(drawing.end.y - drawing.start.y) > .002) operations.push(drawing);
      drawing = null; redraw();
    });
    canvas.addEventListener('pointercancel', () => { drawing = null; redraw(); });
  }

  function normalizeConfiguredKey(value) {
    const key = value.trim().toLowerCase();
    if (/^\d$/.test(key)) return [`digit${key}`, `numpad${key}`];
    if (/^[a-z]$/.test(key)) return [`key${key}`];
    const aliases = { oem1: 'semicolon', oemplus: 'equal', oemcomma: 'comma', oemminus: 'minus', oemperiod: 'period', oemquestion: 'slash', oem3: 'backquote', oem4: 'bracketleft', oem5: 'backslash', oem6: 'bracketright', oem7: 'quote', return: 'enter', back: 'backspace', left: 'arrowleft', right: 'arrowright', up: 'arrowup', down: 'arrowdown', esc: 'escape', win: 'meta' };
    return [aliases[key] || key];
  }

  function matchesShortcut(shortcut, event) {
    const configured = String(shortcut || '').split('+').map(part => part.trim()).filter(Boolean);
    if (!configured.length) return false;
    const configuredModifiers = configured.slice(0, -1).map(part => ({ ctrl: 'ctrl', control: 'ctrl', alt: 'alt', shift: 'shift', win: 'meta', meta: 'meta' }[part.toLowerCase()] || part.toLowerCase())).sort();
    const pressedModifiers = [];
    if (event.ctrlKey) pressedModifiers.push('ctrl'); if (event.altKey) pressedModifiers.push('alt'); if (event.shiftKey) pressedModifiers.push('shift'); if (event.metaKey) pressedModifiers.push('meta');
    pressedModifiers.sort();
    if (configuredModifiers.join('+') !== pressedModifiers.join('+')) return false;
    const code = String(event.code || '').toLowerCase();
    return normalizeConfiguredKey(configured[configured.length - 1]).includes(code);
  }

  function handleCaptureKeydown(event) {
    if (!captureMode || event.isComposing || event.target.closest('input,textarea,[contenteditable="true"]')) return false;
    const tool = names.find(name => matchesShortcut(hotkeys[name], event));
    if (tool) { event.preventDefault(); event.stopImmediatePropagation(); choose(tool); return true; }
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') { event.preventDefault(); event.stopImmediatePropagation(); operations.pop(); redraw(); return true; }
    return false;
  }

  async function exportPng() {
    const result = editor.getCurrentImgData({ extension: 'png' }, 1);
    const image = new Image(); const loaded = imageLoaded(image); image.src = result.imageData.imageBase64.startsWith('data:') ? result.imageData.imageBase64 : `data:image/png;base64,${result.imageData.imageBase64}`; await loaded;
    const output = document.createElement('canvas'); output.width = result.imageData.width; output.height = result.imageData.height;
    const ctx = output.getContext('2d'); ctx.drawImage(image, 0, 0, output.width, output.height);
    operations.forEach(op => paintOperation(ctx, op, output.width, output.height, sourceImage));
    return output.toDataURL('image/png');
  }

  async function submitCapture(action) {
    if (!captureMode || busy) return;
    if (textInput) commitText(textInput.value, textInput._point);
    if (action === 'cancel') { post({ action: 'cancel' }); return; }
    busy = true; document.body.classList.add('busy');
    try {
      const data = await exportPng();
      post({ action, data, captureSave: action === 'saveAs' });
    } catch (error) {
      busy = false; document.body.classList.remove('busy');
      post({ action: 'error', message: '导出截图失败，请重试。', detail: String(error.stack || error) });
    }
  }

  window.handleCaptureKeydown = handleCaptureKeydown;
  window.completeCaptureAction = function(result) { busy = false; document.body.classList.remove('busy'); if (result.message) status.textContent = result.message; };
  window.startEditor = async function(options) {
    if (options.mode !== 'capture') return originalStartEditor(options);
    captureMode = true; hotkeys = options.hotkeys || {};
    document.body.dataset.capture = 'true';
    document.getElementById('captureTools').hidden = false; document.getElementById('captureActions').hidden = false;
    sourceImage = new Image(); const sourceLoaded = imageLoaded(sourceImage); sourceImage.src = options.source; await sourceLoaded;
    await originalStartEditor(options);
    canvas = document.createElement('canvas'); canvas.id = 'captureCanvas'; document.body.appendChild(canvas); bindCanvas();
    document.querySelectorAll('[data-capture-tool]').forEach(button => button.addEventListener('click', () => choose(button.dataset.captureTool)));
    document.getElementById('captureUndo').addEventListener('click', () => { operations.pop(); redraw(); });
    document.getElementById('captureCancel').addEventListener('click', () => submitCapture('cancel'));
    document.getElementById('captureConfirm').addEventListener('click', () => submitCapture('confirm'));
    document.getElementById('captureSaveLocal').addEventListener('click', () => submitCapture('saveAs'));
    updateBounds(); choose('Rect');
  };
  document.addEventListener('keydown', handleCaptureKeydown, true);
  window.addEventListener('resize', updateBounds);
})();

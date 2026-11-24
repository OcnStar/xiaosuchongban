namespace Xiaosu.CompanionAI.LLM.Probe
{
    public static class WorkbenchHtml
    {
        public static string Render(int port)
        {
            return HtmlTemplate.Replace("__WORKBENCH_PORT__", port.ToString());
        }

        private const string HtmlTemplate = @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
<title>FUMI-43 · 晓苏宠伴 LLM 流式适配验证台 (C#)</title>
<style>
  :root {
    --bg: #0f141c;
    --panel: #171f2c;
    --panel-alt: #1d2738;
    --border: #2c3a52;
    --text: #e7eef8;
    --muted: #95a5be;
    --accent: #4da3ff;
    --ok: #2ecc71;
    --warn: #f39c12;
    --err: #ff6b6b;
    --mono: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  }
  * { box-sizing: border-box; }
  body {
    margin: 0; padding: 20px 24px 48px;
    background: var(--bg); color: var(--text);
    font-family: -apple-system, BlinkMacSystemFont, ""PingFang SC"", ""Microsoft YaHei"", sans-serif;
    line-height: 1.5;
  }
  header {
    display: flex; flex-wrap: wrap; justify-content: space-between; align-items: flex-start;
    gap: 12px; padding-bottom: 16px; border-bottom: 1px solid var(--border); margin-bottom: 18px;
  }
  h1 { margin: 0; font-size: 20px; font-weight: 650; }
  .sub { color: var(--muted); font-size: 13px; margin-top: 4px; }
  .badges { display: flex; flex-wrap: wrap; gap: 8px; }
  .badge {
    font-size: 12px; padding: 4px 10px; border-radius: 999px;
    background: var(--panel-alt); border: 1px solid var(--border); color: var(--text);
    font-family: var(--mono);
  }
  .badge.ok { border-color: rgba(46,204,113,.45); color: #7ef0ae; }
  .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
  @media (max-width: 1024px) { .grid { grid-template-columns: 1fr; } }
  .card {
    background: var(--panel); border: 1px solid var(--border);
    border-radius: 10px; padding: 16px; margin-bottom: 16px;
  }
  .card h2 { margin: 0 0 12px; font-size: 15px; display: flex; justify-content: space-between; align-items: center; }
  .row { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-bottom: 10px; }
  .row3 { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 10px; margin-bottom: 10px; }
  label { display: block; font-size: 12px; color: var(--muted); margin-bottom: 4px; }
  input, select, textarea {
    width: 100%; background: var(--bg); color: var(--text);
    border: 1px solid var(--border); border-radius: 6px; padding: 7px 9px; font-size: 13px;
  }
  textarea { font-family: var(--mono); resize: vertical; min-height: 62px; }
  .presets { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 12px; }
  button {
    background: var(--accent); color: #07111f; border: none; border-radius: 6px;
    padding: 7px 14px; font-size: 13px; font-weight: 600; cursor: pointer;
  }
  button.secondary { background: var(--panel-alt); color: var(--text); border: 1px solid var(--border); font-weight: 500; }
  button.danger { background: var(--err); color: #1b0505; }
  button:disabled { opacity: 0.45; cursor: not-allowed; }
  .actions { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-top: 8px; }
  .metrics { display: grid; grid-template-columns: repeat(auto-fit, minmax(125px, 1fr)); gap: 8px; margin-bottom: 12px; }
  .metric { background: var(--bg); border: 1px solid var(--border); border-radius: 6px; padding: 8px 10px; }
  .metric .k { font-size: 11px; color: var(--muted); }
  .metric .v { font-size: 15px; font-weight: 650; font-family: var(--mono); margin-top: 2px; }
  .box {
    background: var(--bg); border: 1px solid var(--border); border-radius: 6px;
    padding: 10px; font-size: 13px; min-height: 90px; max-height: 240px; overflow-y: auto;
    white-space: pre-wrap; word-break: break-word;
  }
  .mono { font-family: var(--mono); font-size: 12px; }
  .pill {
    display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 12px;
    margin: 2px 4px 2px 0; font-family: var(--mono);
    background: rgba(77,163,255,.15); border: 1px solid rgba(77,163,255,.4);
  }
  .pill.action { background: rgba(46,204,113,.14); border-color: rgba(46,204,113,.4); }
  table { width: 100%; border-collapse: collapse; font-size: 12.5px; }
  th, td { text-align: left; padding: 7px 8px; border-bottom: 1px solid var(--border); vertical-align: top; }
  th { color: var(--muted); font-weight: 500; }
  .pass { color: var(--ok); font-weight: 700; font-family: var(--mono); }
  .fail { color: var(--err); font-weight: 700; font-family: var(--mono); }
  .notice { font-size: 12px; color: var(--muted); background: rgba(77,163,255,.08); border: 1px solid rgba(77,163,255,.25); border-radius: 6px; padding: 8px 10px; margin-bottom: 12px; }
</style>
</head>
<body>
<header>
  <div>
    <h1>FUMI-43 · 本地与云端 OpenAI Chat Completions 流式适配验证台 (C#)</h1>
    <div class=""sub"">统一协议：OpenAI Chat Completions Streaming SSE · 纯 C# Runtime (Unity .NET Standard 2.1 兼容) · 仅监听本机回环 (127.0.0.1 / ::1)</div>
  </div>
  <div class=""badges"" id=""statusBadges"">
    <span class=""badge"">加载本机状态中…</span>
  </div>
</header>

<div class=""notice"">
  <strong>工程现状说明：</strong>仓库真实 Unity 工程基线（FUMI-42）尚未合入，本验证台不伪造 Unity 场景骨架；核心流式适配器位于 <code>Verification/FUMI43_LlmStreaming/Runtime/</code>（零第三方依赖 C# 9 / .NET Standard 2.1），可直接迁入后续 <code>Assets/_Project/CompanionAI/LLM/</code>。API 密钥仅从本机内存、环境变量或 macOS Keychain 读取，绝不落盘或写入日志。
</div>

<div class=""grid"">
  <div>
    <div class=""card"">
      <h2>
        <span>1. 端点流式联调（本地 OpenAI 兼容 / 可选云端）</span>
        <span id=""streamState"" class=""badge"">空闲</span>
      </h2>
      <div class=""presets"">
        <button class=""secondary"" type=""button"" onclick=""applyPreset('mock-normal')"">内置模拟 SSE (正常)</button>
        <button class=""secondary"" type=""button"" onclick=""applyPreset('mock-utf8')"">内置模拟 (UTF-8 跨包切片)</button>
        <button class=""secondary"" type=""button"" onclick=""applyPreset('mock-slow')"">内置模拟 (慢速流测取消)</button>
        <button class=""secondary"" type=""button"" onclick=""applyPreset('ollama')"">本地 Ollama (:11434)</button>
        <button class=""secondary"" type=""button"" onclick=""applyPreset('lmstudio')"">本地 LM Studio (:1234)</button>
        <button class=""secondary"" type=""button"" onclick=""applyPreset('llamacpp')"">本地 llama.cpp (:8080)</button>
        <button class=""secondary"" type=""button"" onclick=""applyPreset('cloud')"">可选云端 OpenAI 兼容</button>
      </div>

      <div class=""row"">
        <div>
          <label>服务类型 (ProviderKind)</label>
          <select id=""providerKind"">
            <option value=""local"">LocalOpenAiCompatible (本地服务，免密钥)</option>
            <option value=""cloud"">CloudOpenAiCompatible (云端服务，需本机密钥)</option>
          </select>
        </div>
        <div>
          <label>模型名称 (Model)</label>
          <input id=""model"" value=""mock-xiaosu-stream"" />
        </div>
      </div>

      <div style=""margin-bottom:10px"">
        <label>OpenAI 兼容 Base URL（自动拼接 /chat/completions）</label>
        <input id=""baseUrl"" value=""http://127.0.0.1:__WORKBENCH_PORT__/mock-openai/v1"" />
      </div>

      <div class=""row3"">
        <div>
          <label>首包超时 TTFT (ms)</label>
          <input id=""ttftTimeout"" type=""number"" value=""15000"" />
        </div>
        <div>
          <label>流式读超时 (ms)</label>
          <input id=""readTimeout"" type=""number"" value=""30000"" />
        </div>
        <div>
          <label>凭据读取方式</label>
          <select id=""secretMode"" onchange=""toggleSecretInputs()"">
            <option value=""none"">无凭据（本地模式）</option>
            <option value=""memory"">当次内存输入（不落盘）</option>
            <option value=""env"">本机环境变量名</option>
            <option value=""keychain"">macOS Keychain</option>
          </select>
        </div>
      </div>

      <div id=""secretInputRow"" style=""display:none; margin-bottom:10px;"">
        <div id=""secretMemoryBox"" style=""display:none;"">
          <label>临时 API Key（仅当次请求内存使用，日志自动脱敏）</label>
          <input id=""ephemeralKey"" type=""password"" placeholder=""sk-..."" autocomplete=""off"" />
        </div>
        <div id=""secretEnvBox"" style=""display:none;"">
          <label>本机环境变量名</label>
          <input id=""envVarName"" placeholder=""例如 OPENAI_API_KEY 或 DEEPSEEK_API_KEY"" />
        </div>
        <div id=""secretKeychainBox"" class=""row"" style=""display:none;"">
          <div>
            <label>Keychain Service (-s)</label>
            <input id=""keychainService"" value=""xiaosuchongban.llm"" />
          </div>
          <div>
            <label>Keychain Account (-a，可选)</label>
            <input id=""keychainAccount"" placeholder=""default"" />
          </div>
        </div>
      </div>

      <div style=""margin-bottom:10px"">
        <label>System Prompt（含晓苏内联情绪/动作协议约定）</label>
        <textarea id=""systemPrompt"">你是桌面陪伴角色晓苏。回复自然简短，可在句首使用受控内联标签如 [emotion:Happy][action:Wave] 或 [emotion:Gentle]，不做医疗心理诊断。</textarea>
      </div>
      <div style=""margin-bottom:10px"">
        <label>User Prompt</label>
        <textarea id=""userPrompt"">今天调了一下午流式接口，稍微有点累啦。</textarea>
      </div>

      <div class=""actions"">
        <button id=""btnStart"" type=""button"" onclick=""startStream()"">发起流式请求</button>
        <button id=""btnCancel"" class=""danger"" type=""button"" onclick=""cancelStream()"" disabled>中途取消 (CancelToken)</button>
        <button class=""secondary"" type=""button"" onclick=""copyMarkdownLog()"">复制本次实测 Markdown</button>
        <span id=""copyHint"" style=""font-size:12px;color:var(--ok);""></span>
      </div>
    </div>

    <div class=""card"">
      <h2>
        <span>2. C# 协议、分片、取消与异常自检套件（8 项）</span>
        <button id=""btnSuite"" class=""secondary"" type=""button"" onclick=""runSuite()"">运行全部自检</button>
      </h2>
      <div id=""suiteSummary"" style=""font-size:12.5px;color:var(--muted);margin-bottom:8px;"">点击「运行全部自检」执行回环流式协议与故障注入测试。</div>
      <div style=""overflow-x:auto"">
        <table>
          <thead>
            <tr><th>ID</th><th>验证用例</th><th>状态</th><th>TTFT</th><th>耗时</th><th>结果摘要</th></tr>
          </thead>
          <tbody id=""suiteBody""></tbody>
        </table>
      </div>
    </div>
  </div>

  <div>
    <div class=""card"">
      <h2>3. 实时遥测指标（延迟分布 & 本机资源占用）</h2>
      <div class=""metrics"">
        <div class=""metric""><div class=""k"">HTTP Headers</div><div class=""v"" id=""mHeaders"">-</div></div>
        <div class=""metric""><div class=""k"">首字延迟 TTFT</div><div class=""v"" id=""mTtft"">-</div></div>
        <div class=""metric""><div class=""k"">总耗时 Total</div><div class=""v"" id=""mTotal"">-</div></div>
        <div class=""metric""><div class=""k"">P95 分片间隔</div><div class=""v"" id=""mP95"">-</div></div>
        <div class=""metric""><div class=""k"">取消传播耗时</div><div class=""v"" id=""mCancel"">-</div></div>
        <div class=""metric""><div class=""k"">SSE 帧 / 内容片</div><div class=""v"" id=""mChunks"">0 / 0</div></div>
        <div class=""metric""><div class=""k"">进程 RSS</div><div class=""v"" id=""mRss"">-</div></div>
        <div class=""metric""><div class=""k"">GC (G0/G1/G2)</div><div class=""v"" id=""mGc"">-</div></div>
      </div>

      <label>剥离标签后的伴侣显示文本（Clean Display Text）</label>
      <div class=""box"" id=""cleanOutput""></div>

      <div class=""row"" style=""margin-top:10px"">
        <div>
          <label>受控情绪 / 动作事件（驱动 IAvatarAdapter）</label>
          <div class=""box"" id=""tagEvents"" style=""min-height:74px""></div>
        </div>
        <div>
          <label>按标点实时分句队列（推入流式 TTS）</label>
          <div class=""box mono"" id=""sentenceQueue"" style=""min-height:74px""></div>
        </div>
      </div>

      <div style=""margin-top:10px"">
        <label>原始 SSE 分片时间线（脱敏日志）</label>
        <div class=""box mono"" id=""rawFrames"" style=""min-height:130px""></div>
      </div>
    </div>
  </div>
</div>

<script>
let currentReqId = null;
let currentAbort = null;
let lastProbeMarkdown = '';

function applyPreset(name) {
  const port = '__WORKBENCH_PORT__';
  const kind = document.getElementById('providerKind');
  const url = document.getElementById('baseUrl');
  const model = document.getElementById('model');
  const sec = document.getElementById('secretMode');
  if (name === 'mock-normal') {
    kind.value = 'local'; url.value = `http://127.0.0.1:${port}/mock-openai/v1`; model.value = 'mock-xiaosu-stream'; sec.value = 'none';
  } else if (name === 'mock-utf8') {
    kind.value = 'local'; url.value = `http://127.0.0.1:${port}/mock-openai/v1`; model.value = 'mock-utf8-split'; sec.value = 'none';
  } else if (name === 'mock-slow') {
    kind.value = 'local'; url.value = `http://127.0.0.1:${port}/mock-openai/v1`; model.value = 'mock-slow-stream'; sec.value = 'none';
  } else if (name === 'ollama') {
    kind.value = 'local'; url.value = 'http://127.0.0.1:11434/v1'; model.value = 'qwen2.5:7b'; sec.value = 'none';
  } else if (name === 'lmstudio') {
    kind.value = 'local'; url.value = 'http://127.0.0.1:1234/v1'; model.value = 'local-model'; sec.value = 'none';
  } else if (name === 'llamacpp') {
    kind.value = 'local'; url.value = 'http://127.0.0.1:8080/v1'; model.value = 'local-model'; sec.value = 'none';
  } else if (name === 'cloud') {
    kind.value = 'cloud'; url.value = 'https://api.deepseek.com/v1'; model.value = 'deepseek-chat'; sec.value = 'memory';
  }
  toggleSecretInputs();
}

function toggleSecretInputs() {
  const mode = document.getElementById('secretMode').value;
  document.getElementById('secretInputRow').style.display = mode === 'none' ? 'none' : 'block';
  document.getElementById('secretMemoryBox').style.display = mode === 'memory' ? 'block' : 'none';
  document.getElementById('secretEnvBox').style.display = mode === 'env' ? 'block' : 'none';
  document.getElementById('secretKeychainBox').style.display = mode === 'keychain' ? 'grid' : 'none';
}

async function refreshStatus() {
  try {
    const r = await fetch('/api/status');
    const d = await r.json();
    const b = document.getElementById('statusBadges');
    b.innerHTML =
      `<span class=""badge ok"">服务运行中 :${d.port}</span>` +
      `<span class=""badge"">${escapeHtml(d.os)} (${escapeHtml(d.arch)})</span>` +
      `<span class=""badge"">${escapeHtml(d.framework)}</span>` +
      `<span class=""badge ${d.unityNetStandardVerified ? 'ok' : ''}"">Unity NetStandard2.1: ${d.unityNetStandardVerified ? '已编译核验' : '未检出本地Unity引用库'}</span>` +
      `<span class=""badge"">RSS ${d.workingSetMb} MB</span>`;
  } catch (e) {}
}

function resetTelemetry() {
  ['mHeaders','mTtft','mTotal','mP95','mCancel','mRss','mGc'].forEach(id => document.getElementById(id).textContent = '-');
  document.getElementById('mChunks').textContent = '0 / 0';
  document.getElementById('cleanOutput').textContent = '';
  document.getElementById('tagEvents').innerHTML = '';
  document.getElementById('sentenceQueue').textContent = '';
  document.getElementById('rawFrames').textContent = '';
  document.getElementById('copyHint').textContent = '';
}

async function startStream() {
  resetTelemetry();
  currentReqId = 'req-' + Date.now() + '-' + Math.random().toString(36).slice(2, 7);
  currentAbort = new AbortController();
  document.getElementById('btnStart').disabled = true;
  document.getElementById('btnCancel').disabled = false;
  setStateBadge('流式接收中…', false);

  const mode = document.getElementById('secretMode').value;
  const payload = {
    requestId: currentReqId,
    providerKind: document.getElementById('providerKind').value,
    baseUrl: document.getElementById('baseUrl').value.trim(),
    model: document.getElementById('model').value.trim(),
    firstTokenTimeoutMs: parseInt(document.getElementById('ttftTimeout').value || '15000', 10),
    streamReadTimeoutMs: parseInt(document.getElementById('readTimeout').value || '30000', 10),
    systemPrompt: document.getElementById('systemPrompt').value,
    userPrompt: document.getElementById('userPrompt').value,
    ephemeralApiKey: mode === 'memory' ? document.getElementById('ephemeralKey').value : '',
    envVarName: mode === 'env' ? document.getElementById('envVarName').value : '',
    keychainService: mode === 'keychain' ? document.getElementById('keychainService').value : '',
    keychainAccount: mode === 'keychain' ? document.getElementById('keychainAccount').value : ''
  };

  let sseCount = 0;
  let contentCount = 0;

  try {
    const resp = await fetch('/api/probe/stream', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
      signal: currentAbort.signal
    });

    const reader = resp.body.getReader();
    const dec = new TextDecoder();
    let buf = '';
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      buf += dec.decode(value, { stream: true });
      let sep;
      while ((sep = buf.indexOf('\n\n')) >= 0) {
        const block = buf.slice(0, sep);
        buf = buf.slice(sep + 2);
        handleSseBlock(block, counts => { sseCount = counts.sse; contentCount = counts.content; }, () => ({ sse: sseCount, content: contentCount }));
      }
    }
  } catch (err) {
    if (err.name !== 'AbortError') {
      setStateBadge('连接中断: ' + err.message, true);
    }
  } finally {
    document.getElementById('btnStart').disabled = false;
    document.getElementById('btnCancel').disabled = true;
    currentReqId = null;
    refreshStatus();
  }
}

function handleSseBlock(block, setCounts, getCounts) {
  let eventName = 'message';
  let dataStr = '';
  for (const line of block.split('\n')) {
    if (line.startsWith('event:')) eventName = line.slice(6).trim();
    else if (line.startsWith('data:')) dataStr += line.slice(5).trim();
  }
  if (!dataStr) return;
  const d = JSON.parse(dataStr);
  const c = getCounts();

  if (eventName === 'headers') {
    document.getElementById('mHeaders').textContent = d.elapsedMs.toFixed(1) + ' ms (HTTP ' + d.statusCode + ')';
  } else if (eventName === 'raw') {
    c.sse++;
    setCounts(c);
    document.getElementById('mChunks').textContent = c.sse + ' / ' + c.content;
    const rf = document.getElementById('rawFrames');
    rf.textContent += `[+${d.elapsedMs.toFixed(1)}ms #${d.index}] ${d.frame}\n`;
    rf.scrollTop = rf.scrollHeight;
  } else if (eventName === 'delta') {
    c.content = d.index;
    setCounts(c);
    document.getElementById('mChunks').textContent = c.sse + ' / ' + c.content;
    document.getElementById('cleanOutput').textContent += d.text;
  } else if (eventName === 'tag') {
    const el = document.getElementById('tagEvents');
    const cls = d.kind === 'action' ? 'pill action' : 'pill';
    el.innerHTML += `<span class=""${cls}"">[${escapeHtml(d.kind)}:${escapeHtml(d.value)}] +${d.elapsedMs.toFixed(1)}ms</span>`;
  } else if (eventName === 'sentence') {
    const sq = document.getElementById('sentenceQueue');
    sq.textContent += `[+${d.elapsedMs.toFixed(1)}ms] ${d.text}\n`;
  } else if (eventName === 'summary') {
    const m = d.metrics;
    document.getElementById('mHeaders').textContent = m.headersLatencyMs.toFixed(1) + ' ms';
    document.getElementById('mTtft').textContent = m.ttftMs > 0 ? m.ttftMs.toFixed(1) + ' ms' : '-';
    document.getElementById('mTotal').textContent = m.totalLatencyMs.toFixed(1) + ' ms';
    document.getElementById('mP95').textContent = m.interChunkP95Ms > 0 ? m.interChunkP95Ms.toFixed(1) + ' ms' : '-';
    document.getElementById('mCancel').textContent = m.cancelPropagationMs > 0 ? m.cancelPropagationMs.toFixed(2) + ' ms' : '-';
    document.getElementById('mChunks').textContent = m.rawSseFrames + ' / ' + m.contentChunks;
    document.getElementById('mRss').textContent = (m.processWorkingSetBytes / 1048576).toFixed(2) + ' MB';
    document.getElementById('mGc').textContent = `+${m.gcGen0Delta}/+${m.gcGen1Delta}/+${m.gcGen2Delta}`;
    lastProbeMarkdown = d.markdownLog || '';

    if (d.isSuccess) {
      setStateBadge('完成 (' + d.finishReason + ')', false);
    } else if (d.isCancelled) {
      setStateBadge('已取消 (Cancelled)', true);
    } else {
      setStateBadge('异常: ' + d.errorKind, true);
      const rf = document.getElementById('rawFrames');
      rf.textContent += `\n[ERROR ${d.errorKind}] ${d.errorMessage}\n`;
    }
  }
}

async function cancelStream() {
  if (!currentReqId) return;
  try {
    await fetch('/api/probe/cancel', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ requestId: currentReqId })
    });
  } catch (e) {}
}

async function runSuite() {
  const btn = document.getElementById('btnSuite');
  btn.disabled = true;
  document.getElementById('suiteSummary').textContent = '正在执行 8 项 C# 流式与容错自检…';
  try {
    const resp = await fetch('/api/probe/suite', { method: 'POST' });
    const d = await resp.json();
    document.getElementById('suiteSummary').textContent =
      `自检完成：${d.passedCases}/${d.totalCases} 通过 · 时间 ${d.generatedAtUtc} · RSS ${d.workingSetMb} MB`;
    const tbody = document.getElementById('suiteBody');
    tbody.innerHTML = d.cases.map(c => `<tr>
      <td class=""mono"">${escapeHtml(c.id)}</td>
      <td>${escapeHtml(c.title)}</td>
      <td class=""${c.passed ? 'pass' : 'fail'}"">${c.passed ? 'PASS' : 'FAIL'}</td>
      <td class=""mono"">${c.ttftMs > 0 ? c.ttftMs.toFixed(1) + 'ms' : '-'}</td>
      <td class=""mono"">${c.totalMs.toFixed(1)}ms</td>
      <td>${escapeHtml(c.summary)}</td>
    </tr>`).join('');
    lastProbeMarkdown = d.markdownReport || lastProbeMarkdown;
    refreshStatus();
  } catch (e) {
    document.getElementById('suiteSummary').textContent = '自检执行失败: ' + e.message;
  } finally {
    btn.disabled = false;
  }
}

async function copyMarkdownLog() {
  if (!lastProbeMarkdown) {
    document.getElementById('copyHint').textContent = '请先运行一次流式请求或自检套件';
    return;
  }
  await navigator.clipboard.writeText(lastProbeMarkdown);
  document.getElementById('copyHint').textContent = '已复制 Markdown 实测记录';
}

function setStateBadge(text, isWarn) {
  const el = document.getElementById('streamState');
  el.textContent = text;
  el.style.borderColor = isWarn ? 'rgba(255,107,107,.5)' : 'rgba(46,204,113,.45)';
}

function escapeHtml(s) {
  return String(s || '').replace(/[&<>""']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;',""'"":'&#39;'}[ch]));
}

refreshStatus();
runSuite();
</script>
</body>
</html>";
    }
}

# FUMI-43：本地与云端 LLM 流式适配验证说明

## 1. 范围与协议边界

根据 FUMI-43 任务要求及负责人确认：

- **统一通信协议**：采用 **OpenAI Chat Completions Streaming SSE**（`POST {BaseUrl}/chat/completions`，`"stream": true`，`Accept: text/event-stream`），同时覆盖本地 OpenAI 兼容推理服务（如 Ollama、LM Studio、`llama-server`）与可选云端 OpenAI 兼容服务（如 DeepSeek、通义千问兼容模式、OpenAI 等）。
- **非兼容原生协议边界**：本次不实现 Anthropic `/v1/messages` 等非兼容原生协议；此类接口不标为天然同协议，后续若确有需要须单独实现独立 `ILLMProvider` 适配器或走经验证的兼容网关。
- **当前仓库状态说明**：仓库真实 Unity 工程与包基线（FUMI-42）尚未合入，当前不伪造 Unity `Assets/` 或 `ProjectSettings/` 骨架。核心运行时代码置于 `Verification/FUMI43_LlmStreaming/Runtime/`，采用**零第三方依赖、严格 C# 9 + `.NET Standard 2.1`** 编写并通过 Unity 引用程序集编译核验，待 FUMI-42 落地后可直接迁入 `Assets/_Project/CompanionAI/LLM/`。

## 2. 模块结构

- `Verification/FUMI43_LlmStreaming/Runtime/`（可直接迁入 Unity 的核心层，命名空间 `Xiaosu.CompanionAI.LLM`）
  - `LlmContracts.cs`：`ILLMProvider`、请求/配置模型、流式事件类型、错误分类与延迟/资源指标结构。
  - `OpenAiChatCompletionsProvider.cs`：基于 `HttpClient`（`HttpCompletionOption.ResponseHeadersRead`）的流式客户端，隔离首包超时、读空闲超时与调用方 `CancellationToken`。
  - `OpenAiSseFrameDecoder.cs`：有状态 `UTF8Encoding.GetDecoder()` + SSE 行重组器，处理任意 TCP 分片下的中文 3 字节 / Emoji 4 字节跨包切分、`: keep-alive` 心跳注释行、`data:` 空格差异与多行帧。
  - `CompanionStreamTagParser.cs`：跨 Token 分片流式剥离 `[emotion:...]` 与 `[action:...]` 内联受控标签，实时输出净显示文本与按标点切分的 TTS 句子队列；未知或未闭合方括号自动降级保留为普通文本。
  - `LocalSecretStore.cs` / `SecretRedactor.cs`：仅从本机当次内存、环境变量或 macOS Keychain 读取 API Key，全链路脱敏 `Bearer` 与 `sk-*` 令牌，不在仓库或日志中落盘密钥。
  - `ResourceSampler.cs`：采集进程 `WorkingSet64`（RSS）、托管堆 `GC.GetTotalMemory` 与 `GC.CollectionCount(0/1/2)` 增量。
- `Verification/FUMI43_LlmStreaming/Probe/`（验证台与故障注入回环服务端）
  - `LoopbackHttpServer.cs`：仅绑定本机回环（`127.0.0.1` 与 `[::1]`）的轻量 HTTP/1.1 + SSE 服务。
  - `MockOpenAiSseHandler.cs`：内置本地 OpenAI 兼容流式模拟器与故障注入器（覆盖正常流、5 字节步长 UTF-8 跨包切分、跨片控制标签、慢速可取消流、HTTP 401/429/500、中途断连、损坏 JSON 帧、TTFT 挂起）。
  - `VerificationSuiteRunner.cs`：8 项自动化流式与容错验证套件。
  - `WorkbenchHtml.cs` / `Program.cs`：零外部 CDN 依赖（支持完全断网打开）的本地 Web 验证台与 CLI 入口。
- `.multica/environments.toml` & `Tools/LlmStreamingProbe/run.sh`：Multica 本地环境声明与跨环境启动脚本。

## 3. 延迟与资源测量办法

每次流式请求由高精度 `Stopwatch.GetTimestamp()` 记录：

1. **HTTP Headers 延迟 (`HeadersLatencyMs`)**：发出 `POST` 至收到响应头（`ResponseHeadersRead`）的耗时。
2. **首字延迟 (`TtftMs`)**：发出请求至收到首个非空 `delta.content`（或 `reasoning_content`）帧的耗时。
3. **分片间隔分布 (`InterChunkMeanMs` / `InterChunkP95Ms` / `InterChunkMaxMs`)**：相邻内容分片到达时间差均值、P95 与最大值，用于评估流式平滑度与卡顿。
4. **取消传播耗时 (`CancelPropagationMs`)**：调用方触发 `CancellationTokenSource.Cancel()` 至底层读取流释放并退出的毫秒数，并校验取消后是否仍有残留 `TextDelta` 事件溢出（防语音打断 Barge-in 串音）。
5. **本机资源占用**：采样单次流式前后托管堆字节差、GC Gen0/1/2 回收次数增量及当前进程 Working Set (RSS)。

## 4. 已执行验证与待人工补测项

### 已执行（本机实测通过）
- 测试环境：macOS arm64（12 核），`.NET Runtime 6.0.21`，Roslyn `4.3.1` + Unity `NetStandard 2.1` 引用库严格编译核验通过。
- 执行命令：`sh Tools/LlmStreamingProbe/run.sh --run-suite`
- 结果：**`8 / 8` 用例全部 PASS**（包含标准 SSE 流与分句、5 字节 TCP 切片中文/Emoji 零乱码重组、跨分片 `[emotion]/[action]` 标签提取、流式中途取消 `< 5 ms` 零残留、HTTP 401/429/500 分级与密钥脱敏、半途断流与坏 JSON 拦截、TTFT 超时独立判定、云端缺密钥前置拦截；8 项连续执行期间 GC Gen0/1/2 增量 `+0/+0/+0`，进程 RSS 约 `55 MB`）。

### 未测 / 待人工验收补测项
1. **真实外部本地推理服务断网实测（未测）**：当前运行机尚未安装本地 `ollama` / `LM Studio` / `llama-server` 进程与本地权重。安装后请在**物理断网/关闭 Wi-Fi** 条件下，通过验证台（预设 `本地 Ollama (:11434)` 等）或 CLI `--probe` 实测并导出 Markdown 记录，确认无静默云端回退。
2. **可选云端 OpenAI 兼容服务实测（未测）**：仓库不内置任何云端 API Key；请主责人在本机通过 macOS Keychain、环境变量或验证台单次内存输入框填入自有测试密钥实测一条云端服务并导出脱敏日志。
3. **Windows x64 目标平台与真实 Unity 6 工程内联调（未测）**：待 Windows 测试机就绪及 FUMI-42 Unity 工程基线合入后，将 `Runtime/*.cs` 迁入 `Assets/_Project/CompanionAI/LLM/` 复测。

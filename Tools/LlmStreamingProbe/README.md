# FUMI-43 LLM 流式适配验证工具 (C#)

本目录提供 **FUMI-43（验证本地与云端 LLM 的流式适配）** 的零外部框架依赖 C# 编译与启动脚本。
在真实 Unity 工程基线（FUMI-42）尚未合入仓库前，本工具既能使用系统 `.NET SDK`，也能自动复用本机 Unity Hub 已安装 Editor 内置的 `NetCoreRuntime + Roslyn (csc.dll)` 与 `NetStandard 2.1` 引用程序集完成严格编译与验证。

## 启动方式

### 1. 通过 Multica 本地环境监督启动（推荐）
```sh
multica env start llm-workbench
```
启动后访问输出的 `http://localhost:<PORT>` 即可打开交互式验证台（仅绑定本机回环 `127.0.0.1` / `::1`）。

### 2. 命令行运行 8 项协议与异常自检套件
```sh
sh Tools/LlmStreamingProbe/run.sh --run-suite
```

### 3. 命令行实测本地 OpenAI 兼容服务（如 Ollama / LM Studio / llama.cpp）
```sh
sh Tools/LlmStreamingProbe/run.sh --probe \
  --kind local \
  --base-url http://127.0.0.1:11434/v1 \
  --model qwen2.5:7b \
  --prompt "你好晓苏，请用一句话打个招呼"
```

### 4. 命令行实测可选云端 OpenAI 兼容服务（密钥仅留本机 Keychain 或环境变量）
```sh
# 方式 A：从 macOS Keychain 读取（先在本机执行：security add-generic-password -U -s xiaosuchongban.llm -a default -w "sk-..."）
sh Tools/LlmStreamingProbe/run.sh --probe \
  --kind cloud \
  --base-url https://api.deepseek.com/v1 \
  --model deepseek-chat \
  --keychain-service xiaosuchongban.llm \
  --keychain-account default

# 方式 B：从本机进程环境变量读取
sh Tools/LlmStreamingProbe/run.sh --probe \
  --kind cloud \
  --base-url https://api.deepseek.com/v1 \
  --model deepseek-chat \
  --api-key-env DEEPSEEK_API_KEY
```

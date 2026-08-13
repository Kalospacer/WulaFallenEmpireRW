# Codex vs wulaai 功能对比与改进清单

> 参照系：OpenAI Codex CLI（`codex-rs/`，100+ crate workspace）。
> 对象：wulaai（`Source/WulaFallenEmpire/EventSystem/AI/`，RimWorld 1.6 mod 内嵌 AI 对话/Agent）。
> 目的：以 codex 的工程实践为参照，找出 wulaai 的差距并给出**可在 RimWorld/Unity 环境落地**的改进方案。
>
> 约束声明：RimWorld 是 Unity/Mono 游戏，mod 运行在 IMGUI 主线程渲染 + 无 SynchronizationContext 的 .NET Framework 4.7.2（async 续体落线程池）环境。codex 的很多能力（OS 沙箱、fork 进程、tokio）在物理上不可移植，下列每项都标注了适用性判断。另外按项目惯例，优先原版加载期机制与声明式集中配置，少加 Harmony patch。

---

## 1. 逐项功能对比

### 1.1 Agent loop 编排

| 维度 | Codex | wulaai | 差距 |
|---|---|---|---|
| 主循环 | `run_turn` 三层嵌套，SSE 流消费 + `FuturesOrdered` 并发派发工具，流结束统一 drain（`core/src/session/turn.rs`） | `AIToolLoopRunner.RunAsync`：`for step 1..maxToolSteps`，请求 → 有 tool call 就**逐个串行** await，无 tool call 收尾 | 无并行工具执行；步数耗尽才兜底 `RequestFinalWithoutToolsAsync` |
| 重试 | `responses_retry.rs`：连接重试（指数退避）与流中断重试分离，可按 provider 配 `stream_max_retries`/`stream_idle_timeout_ms` | **无重试**。429/5xx/网络抖动直接 `throw new Exception($"OpenAI API error {(int)status}…")`，以 `Error:` 前缀进会话。仅流式失败降级非流式一次 | 差距大，高优先级 |
| 取消 | 全程 `CancellationToken` 树（turn 级 + 每请求 child），Esc 中断 | 刚加了 turn 级 CTS + 停止按钮（2026-08-13），单次 API 超时任 `CreateTimeoutToken` | 已基本对齐 |
| 超时 | 每请求 + 流空闲超时（idle watchdog，流卡住但连接活着也能断） | 只有整体 `CancelAfter(request.TimeoutSeconds)`，**流空闲无检测**——SSE 流挂住只能等整单超时 | 中差距 |

**wulaai 现状参考**：`Runtime/AIToolLoopRunner.cs`、`AIIntelligenceCore.cs:RunAgentRequestAsync`、`Providers/AIProviderJson.cs:CreateTimeoutToken`。

### 1.2 上下文管理 / Compaction

| 维度 | Codex | wulaai | 差距 |
|---|---|---|---|
| 压缩策略 | LLM 摘要式 compaction（`compact.rs`），压缩点持久化、可 resume，window 链追踪；另有远端 compaction 两套 | `CompressHistoryIfNeeded`：超 `maxContextTokens` 直接**砍前半历史**，插一行 "[Earlier conversation dropped...]" 占位符 | 本质差距：丢弃 vs 摘要 |
| 触发判定 | token 预算驱动（`compact_token_budget.rs`），模型可自带预算默认值，接近上限给模型发提醒 | 粗估 `chars/4`，无 token 级判定、无预警 | 中 |
| 长期记忆 | 无独立长期记忆（靠 rollout + AGENTS.md） | `AIMemoryManager`：hash 去重 + 关键词覆盖率×时间衰减×访问加成打分召回，自动窗口摘要（每 10 轮，JSON facts，confidence 过滤） | **wulaai 领先**——这是 codex 没有的能力 |
| 记忆注入 | — | 每轮 top-5 附加到最后一条 user 消息尾部 | 合理，保留 |

### 1.3 工具系统

| 维度 | Codex | wulaai | 差距 |
|---|---|---|---|
| 注册/schema | `ToolRegistry` 分 trusted/external；`spec_plan.rs` 按 turn 上下文决定工具表；JSON Schema 集中 crate | `AIToolRegistry` 两级表面（Observer/Default）硬编码；手写 SchemaString/SchemaObject 构造器 | 结构相当，缺 per-turn 动态裁剪 |
| 权限分级 | 4 档 approval policy + granular 开关 + 批准缓存 + execpolicy（Starlark 前缀规则） | **无审批**。observer/default 两档写死；高危工具（`call_bombardment`、`send_reinforcement`、`spawn_resources`）只靠 prompt 描述约束 | 差距大 |
| 并行调用 | `ToolCallRuntime` + 读写锁门控，每工具声明 `supports_parallel` | 无，循环内串行 await；且所有工具经 `AIMainThreadDispatcher` 排回 Unity 主线程执行 | 环境限制下部分不可行（见 §3.2） |
| 参数校验 | — | `AIToolRunner.FilterArguments`（大小写不敏感白名单过滤）+ required/类型校验，schema 经 `ToolSchemaSanitizer` 规整 | **wulaai 这块做得比想象好** |

### 1.4 安全 / 审批

| 维度 | Codex | wulaai | 适用性 |
|---|---|---|---|
| OS 沙箱 | Seatbelt / Landlock+seccomp / Windows Restricted Token + 受管网络代理 | — | **不可移植**（mod 与游戏同进程，无法沙箱化工具） |
| 命令安全判定 | bash 词法解析 + PowerShell AST 解析 | — | wulaai 无 shell 工具，N/A |
| 审批编排 | orchestrator 统一驱动 approval→sandbox→执行→升级重试 | — | 可借鉴其**逻辑层**形态（见 §3.4） |
| Guardian（AI 审批 AI） | 独立评审会话 + 风险分类法 + fail-closed | — | 可借鉴但成本高（多一次 API 调用），低优先级 |

wulaai 已有的等价物：`IsPollutedMemoryText` 用 `BridgeErrorPrefix` 挡错误进记忆（≈ codex 的 `mark_thread_memory_mode_polluted_if_external_context` 思路）；auto-commentary 走 observer-only registry（零玩家输入路径不给写工具）——这两个设计是对的。

### 1.5 Provider 抽象

| 维度 | Codex | wulaai | 差距 |
|---|---|---|---|
| 协议支持 | Responses API 为主 + Chat Completions，provider 声明 wire_api/认证/重试/流超时/能力 | OpenAI chat/completions、Anthropic messages、Gemini generateContent 三家手写，reasoning_content 只对 deepseek-v4 官方域名回传 | 覆盖够用；能力声明缺失（见下） |
| 能力声明 | `ModelInfo`：supports_parallel_tool_calls、token_budget 等 per-provider 配置 | 无。多模态靠用户勾选 `isMultimodalModel`（2026-08-13 刚改），其余能力（流式、工具、reasoning）默认全开 | 中 |
| 结构化输出 | `output_schema` + strict（review/compact/guardian 全靠它） | **无**。记忆摘要靠 prompt 约定 + `ParseMemoryFacts` 手写括号匹配抠 JSON | 差距直接可见：摘要解析失败要重试 3 次 |
| token 统计 | `TokenUsage`/`TokenUsageInfo` 结构化、rate limit 从头解析、UI 展示 | `LogUsage`/`BuildUsageSummary` 统一解析三家 usage（含 cache 命中率）**但只写日志**，无累计、无 UI | 小差距 |

### 1.6 MCP 集成

| 维度 | Codex | wulaai | 差距 |
|---|---|---|---|
| transport | stdio / Streamable HTTP / in-process + OAuth | stdio / HTTP（无鉴权头配置）；双时代协议探测（modern `_meta` vs legacy `initialize`） | 相当，OAuth 不必要 |
| 工具映射 | 每个 MCP 工具展开注册进 registry，命名 `mcp__server__tool` | **渐进披露**：`mcp_find_tools` → `mcp_tool_detail` → `mcp_invoke` 三个元工具 | 各有取舍：wulaai 省 token 但多 2 次往返；工具量大时 wulaai 方案反而更稳 |
| 资源/通知 | list/read resource、elicitation 接审批 | 仅工具面 | 低优先级 |
| 连接管理 | 连接池 + 工具目录缓存 + prewarm | 按需惰性 client + 参数变更丢弃重建 + ProcessExit 杀子进程 | 够用 |

### 1.7 会话持久化与恢复

| 维度 | Codex | wulaai | 差距 |
|---|---|---|---|
| 存储 | 每会话 JSONL rollout（SessionMeta/ResponseItem/Compacted/TurnContext…）+ SQLite 索引双轨 | `AIHistoryManager`：按存档存 `SaveDataFolder/WulaAIHistoryV2/*.json`（Newtonsoft DTO），saveId 入存档 | 结构不同但都完整 |
| **工具语义回放** | rollout 存**完整模型条目**，resume 后模型看到原生 tool_use/tool_result 对 | 历史里 toolcall/tool 是**显示用扁平文本行**，回放时折叠进 user 消息（"# TOOL ACTIVITY FOR THIS TURN"）——因为没存 provider tool-call id | **本质差距**：跨会话恢复后模型读到的是转述，工具语义链断裂 |
| Resume/Fork | resume picker、`/fork`、forked_from_id | 无存档内分叉；历史随存档恢复 | 低优先级 |

### 1.8 流式 UI

| 维度 | Codex | wulaai | 差距 |
|---|---|---|---|
| 流式渲染 | streaming/ 四个模块：增量 controller、markdown 增量渲染、commit_tick 节奏落盘、表格 holdback | 原地替换最后 assistant 行 + 每 delta 强制缓存重建（`UpdateCacheIfNeeded` 全量重算行高） | 功能对齐；**性能差距**：长回复每帧全量 CalcHeight |
| 思考流 | ReasoningContentDelta 有专门渲染（shimmer 动画） | `AIStreamEvent.ReasoningDelta` 事件存在且 provider 已解析，**但 UI 从不渲染**——reasoning 流到手即弃 | 小改动大收益 |
| 中断 | Esc interrupt + Esc-Esc backtrack | 停止按钮（2026-08-13） | 已对齐 |
| 选项/交互 | slash commands 50+、@文件补全、plan 渲染 | "OPTIONS:" 尾段解析渲染选项按钮、立绘 `[EXPR:n]` | wulaai 的游戏侧交互是特色，保留 |

### 1.9 配置体系

| 维度 | Codex | wulaai | 差距 |
|---|---|---|---|
| 分层 | managed/user/project/session 多层合并，管理员层强制 | 单层 `ModSettings`（Scribe） | 游戏 mod 不需要分层，N/A |
| Profile | named profile + CLI 切换 | 无 | 低价值 |
| Provider 配置 | per-provider 重试/超时/认证/能力全可配 | key/baseUrl/model + 全局超时 | 够用 |

### 1.10 其他

- **错误处理**：codex 全 crate 结构化 `CodexErr`；wulaai 用 `BridgeErrorPrefix = "Error: "` 字符串契约判 `IsError`、挡污染——能用但脆，工具作者忘了前缀模型就把失败当成功。
- **Hooks**：codex 有生命周期 hook 引擎；wulaai 无。mod 场景价值低。
- **子代理**：codex 一等公民（spawn/wait/fork/角色 TOML）；wulaai 单会话槽单 persona。游戏内 AI 副官场景其实用得上（如 observer 独立分析），但成本高，列远期。
- **Skills**：两者都是 SKILL.md 渐进披露，wulaai 的依赖检查（`SkillDependencyResolver`）+ 一次性缺依赖提示已够用。**对齐**。
- **遗留死代码**：`AIHistoryManager.cs` 里的 `SimpleJsonParser` 手写 JSON 解析器已被 Newtonsoft 取代但仍在文件里，顺手删掉。

---

## 2. 差距总结（按影响排序）

1. **无重试/退避**：一次 429 或网络抖动就把整轮对话打断成 `Error:` 进会话，还污染上下文。
2. **上下文压缩是丢弃式**：长对话早期内容永久丢失，而摘要式压缩是成熟做法（自家 memory 窗口摘要已经在做同样的事）。
3. **工具语义不回放**：跨存档恢复后模型丢失 tool_use/tool_result 语义链，多步任务上下文质量下降。
4. **高危工具无玩家确认**：`call_bombardment`/`send_reinforcement`/`spawn_resources` 这类改游戏状态的工具只有 prompt 软约束，Observer/Default 硬编码两档不够细。
5. **流式无空闲 watchdog**：SSE 流挂住（连接活着但没有数据）只能等整单 120s+ 超时。
6. **无结构化输出**：记忆摘要解析靠手写括号匹配，失败率有实据（要重试 3 次）；三家 provider 的 JSON mode 都没接。
7. **reasoning 流被丢弃**：provider 已经解析出 `ReasoningDelta`，UI 却不渲染——DeepSeek/Anthropic 思考内容白丢了。
8. **流式渲染性能**：每个文本 delta 触发全量缓存重建，长回复期间 UI 卡顿。
9. **token 统计只进日志**：玩家看不到单次/累计用量。
10. **`Error:` 字符串契约脆**：错误判定依赖前缀字符串，应改结构化标记。

## 3. 改进方案（按优先级）

### P0 —— 直接提升稳定性，改动小

**P0-1 API 重试与指数退避**
- 位置：`Providers/AIProviderJson.cs`（三个 provider 的 SendAsync/StreamAsync 都过这里或同模式）。
- 做法：捕获 429/5xx/`HttpRequestException`/`TaskCanceledException`（非用户取消），按 `min(2^n × 500ms, 8s)` 退避重试，最多 3 次；429 优先读 `Retry-After` 头。区分"可重试错误"与"业务错误"（4xx 除 429 外不重试）。重试不进会话，全部失败才走现有 `Error:` 路径。
- 参考 codex：`responses_retry.rs` 的连接/流中断分离思路——wulaai 至少要把**建连失败**和**流中断**分开计数。
- 工作量：~100 行，集中在共享管线，三家 provider 同时受益。

**P0-2 reasoning 流渲染**
- 位置：`AIIntelligenceCore.AppendStreamingAssistantDelta` 旁加 `AppendReasoningDelta`（或复用 `_latestThought` 通道）；`Dialog_AIConversation`/`Overlay_WulaLink` 的思考中区域下方滚动显示 reasoning 文本（灰色小字，复用现有 `LatestThought` 渲染位）。
- 注意：reasoning 不进持久历史（`IsPersistableHistoryEntry` 已有 role 白名单机制）；DeepSeek 只回官方域名、Anthropic thinking block——provider 侧已解析，纯 UI 活。
- 工作量：~80 行。

**P0-3 结构化错误标记**
- 位置：`Tools/AITool.cs` 结果类型加 `bool IsError` 字段，`AIToolRunner` 改为读字段而非 `"Error:".StartsWith`；`BridgeErrorPrefix` 保留为**显示**前缀（进会话的文本不变，兼容旧存档与 `IsPollutedMemoryText`）。
- 工作量：~60 行，消灭一个承重字符串契约。

**P0-4 清理死代码**：删 `AIHistoryManager.cs` 的 `SimpleJsonParser`。~10 分钟。

### P1 —— 核心体验，中等改动

**P1-1 摘要式上下文压缩**
- 位置：`AIIntelligenceCore.CompressHistoryIfNeeded`。
- 做法：超 `maxContextTokens` 时，把**待丢弃的前半历史**先用当前 provider 跑一遍摘要（复用 `MemoryPrompts.WindowSummaryPrompt` 的 prompt 思路，但输出是纯文本段落不是 facts JSON），用摘要段落替换被砍的历史（保留最近 N 轮原文）。摘要失败回落到现在的丢弃策略。压缩点连同 `ShiftMemorySummaryCursors` 游标重定基逻辑保持不变。
- 与 memory 系统的关系：memory 管"跨会话长期事实"，compaction 管"本会话上文延续"，两者互补不冲突。
- 参考 codex：`compact.rs` + `CompactedItem`；wulaai 不需要 window 链，单层替换即可。
- 工作量：~150 行 + prompt 调优。

**P1-2 SSE 流空闲 watchdog**
- 位置：`AIProviderJson.ReadSseAsync`。
- 做法：读循环外起 watchdog：超过 `streamIdleTimeoutSeconds`（新设置项，默认 30s）没有收到任何 `data:` 行就 cancel 该请求的 linked CTS。与整单超时正交。流式中断后走 P0-1 的重试路径。
- 工作量：~80 行。

**P1-3 历史条目携带 tool-call 元数据**
- 位置：`AIHistoryManager` DTO + `AIIntelligenceCore` 的 toolcall/tool 行记录 + `BuildCanonicalMessagesForAgent`。
- 做法：toolcall 行记录 provider 的 `tool_call_id`、工具名、原始参数 JSON；tool 行记录对应 `tool_call_id`。回放时（`BuildCanonicalMessagesForAgent`）若条目带 id 且当前 provider 是 OpenAI/Anthropic，重建原生 `tool_calls`/`tool_result` 结构而非折叠文本；无 id 的旧存档条目继续走折叠文本路径（向后兼容）。
- 收益：跨会话恢复后多步工具任务上下文不再降级成转述。
- 工作量：~200 行，动持久化格式（版本号 +1，旧档兼容读取）。

**P1-4 token 用量面板**
- 位置：`AIProviderJson.LogUsage` 已有统一解析——把它从纯日志升级为累加到 core 的运行时计数器（按存档 session 聚合），对话窗口标题栏或 trace 尾部显示"本轮 X tok（cache 命中 Y%）/ 累计 Z tok"。
- 工作量：~80 行。

### P2 —— 安全与能力扩展，较大改动

**P2-1 高危工具玩家确认门**
- 位置：`AIToolRegistry` 注册时给工具打标（`bool RequiresConfirmation`），`AIToolLoopRunner` 执行到带标工具时暂停 loop → 对话框弹出确认条（工具名 + 参数摘要 + [允许]/[拒绝]），玩家选择后注入结果续跑。observer-only 路径（auto-commentary）本来就没有这些工具，不受影响。
- 对标 codex 的 approval 编排，但做成 IMGUI 弹条而非 OS 沙箱——这是 RimWorld 环境下"审批"的正确形态。确认结果**不缓存**（每次轰炸都要确认，codex 的 ApprovalCacheKey 对游戏内高危操作不合适）。
- 首批打标：`call_bombardment`、`send_reinforcement`、`spawn_resources`、`call_prefab_airdrop`、`set_overwatch_mode`、`modify_goodwill`（低危但改状态）。
- 工作量：~250 行（含 loop 暂停/恢复状态机与 UI）。

**P2-2 结构化输出（JSON mode）**
- 位置：`AIProviderRequest` 加 `OutputSchema`/`JsonMode` 字段；OpenAI provider 填 `response_format: {type:"json_object"}`（DeepSeek 支持，见 `WulaAI_DevDocs/deepseek/JsonOutput.md`），Gemini 填 `responseMimeType:"application/json"` + `responseSchema`，Anthropic 无原生 JSON mode 用 tool-forced（`tool_choice:{type:"tool",name:"emit_result"}`）等价实现。
- 首个消费方：记忆摘要（替换 `ParseMemoryFacts` 手写括号匹配）。
- 工作量：~200 行。

**P2-3 流式渲染性能**
- 位置：`Dialog_AIConversation.UpdateCacheIfNeeded`。
- 做法：流式 delta 到来时只对**最后一条 assistant 行**重算行高并平移后续 yOffset（流式期间它本来就是最后一条），不再整表重建；history count 未变且宽度未变时跳过 trace 面板重建。参考 codex `commit_tick.rs` 的节奏落盘思路：delta 先进缓冲，按 100ms 节奏合并进缓存，避免每 token 一帧。
- 工作量：~120 行。

### P3 —— 远期 / 可选

- **并行工具调用**：受 `AIMainThreadDispatcher` 主线程执行约束，真并行收益有限；可做"只读工具并发批次"（get_* 一批并发到 dispatcher 的帧预算内），优先级低。
- **子代理**：observer 独立 VLM 分析、`analyze_screen` 已是雏形；完整 spawn/wait/fork 体系成本高，等 P2 完成后评估。
- **MCP resources/elicitation**：等游戏内桥（RimBridgeServer）真用上资源面再说。
- **记忆向量召回**：当前关键词打分在 mod 规模够用；若记忆条目上千再考虑本地 embedding（注意不能引入外部服务依赖）。
- **配置 profile**：多套 provider 配置一键切换，玩家价值一般，暂缓。

## 4. 不做的事（明确排除）

- OS 沙箱 / 命令 AST 安全判定 / Guardian AI 审批：mod 与游戏同进程，无 shell 工具，不适用。
- rollout JSONL + SQLite 双轨 / resume-fork：现有按存档 JSON 持久化对游戏场景已够，不引入数据库依赖。
- Code Mode（模型写 TS 编排工具）：需要 JS 运行时，.NET Framework 4.7.2 下不值得。
- 配置分层治理 / feature flag 生命周期：企业 CLI 的需求，游戏 mod 用不上。
- Harmony patch 新增：以上全部改动都在自有代码内，遵守"优先原版机制、少 patch"的既定约束。

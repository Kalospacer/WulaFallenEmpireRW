# AstrBot vs wulaai：message / chat / response 三种协议格式逐项对比

> 参照系：AstrBot v4（`C:\astrbot\AstrBot`，Python，Nonebot 系机器人框架）。
> 对象：wulaai（`Source/WulaFallenEmpire/EventSystem/AI/`，RimWorld 1.6 mod 内嵌 AI 对话/Agent），对比基线 = commit `0ad40c3b`（codex 对齐改造后）。
> 范围：按用户指定，聚焦 LLM 侧的**消息格式（message）、对话请求（chat）、响应解析（response）**三种协议在两家实现里的转换与差异收敛方式。AstrBot 的 platform/pipeline 层不展开，只在 §4 提一句与 wulaai 的对应关系。
>
> 术语约定：本文的「规范格式」指 AstrBot 内部的中间表示（OpenAI 风格的 dict 列表 + pydantic Message 模型），「协议格式」指最终发给各家 API 的 payload。

---

## 0. 一张总表

| 维度 | AstrBot | wulaai | 判定 |
|---|---|---|---|
| 内部中间表示 | **OpenAI 消息格式**（`Message`/`ToolCall`/`ContentPart`，pydantic 校验，ThinkPart 携带 signature）；历史持久化就是这个格式 | 自定义 `AIMessage`（Role/Content/Parts/ToolCalls/ToolCallId/ReasoningContent），语义等价，已能表达三家协议的全部要素 | 平手，设计同构 |
| 协议覆盖 | OpenAI 系 12 个变体 + Anthropic 系 2 个变体 + Gemini 原生，注册表 + 装饰器 | 3 个协议（OpenAI/Anthropic/Gemini），`AIProviderFactory` 字符串 switch | wulaai 够用，扩展机制见 §5 P3 |
| 工具 schema | `ToolSet` 一份定义，三个导出方法现场转换 | `AIToolDefinition.Parameters` 一份 JObject，三个 provider 各自转换 | 平手 |
| 工具回放 | 规范格式进 provider，provider 内翻译 + **发送前 sanitize**（孤儿清洗/合并/占位） | `AIMessage.AssistantToolCalls`/`ToolResult` 直接进 provider 转换，~~无 sanitize~~ **已补**（`AIMessageSanitizer`，见 §3.2） | ~~wulaai 缺防护~~ 已对齐 |
| 流式 | SDK 聚合（`ChatCompletionStreamState`/`messages.stream`）+ 逐 chunk yield `is_chunk` 标记 | 手写 SSE 逐行解析 + `AIStreamEvent` delta 回调 + 累积器 | 平手（wulaai 手写是环境所迫，且已经做得很对） |
| reasoning 回放 | ThinkPart 持久化 + 跨轮 signature 回传（Anthropic/Gemini 必需）+ DeepSeek/MiMo 强制占位 | `ReasoningContent` 仅 DeepSeek-v4 官方域名回传，无 signature | wulaai 有洞（§3.3，P1） |
| 多模态 | `modalities` 能力声明 + sanitize 替换占位 + **发送失败后剥图重试** | `isMultimodalModel` 用户声明开关（只控工具注册），历史里残留的图直接发送 | wulaai 缺失败重试（§3.4，P1） |
| 错误恢复 | `_handle_api_error` 按错误类型**改 payload 重试**（429 换 key、超长弹历史、不支持工具就摘工具） | `AIRequestRetry` 只做分类重试（Retryable/Fatal），payload 不变 | 设计不同，可补两个具体机制（§3.5） |
| 空输出防护 | 三家统一 `EmptyModelOutputError`（无文本且无 reasoning 且无 tool call 即抛） | 无显式检测，空响应静默进历史（`"No response."`） | 小差距（§3.6，P2） |
| 结构化输出 | **三家都没做** | codex 对齐时已做（json_object / emit_result 工具模拟 / responseSchema） | **wulaai 领先** |
| token 统计 | `TokenUsage(input_other, input_cached, output)` 结构化，加减运算，入 UI/统计 | `RecordUsage` → 会话内累计 + 对话框顶部摘要 | 平手 |
| 重试参数 | tenacity：5 次、0.2–30s 指数、状态码 {408,409,429,500,502,503,504,529}、可关 429 | `AIRequestRetry`：3 次、0.5–8s 指数、{429,408,≥500}、支持 Retry-After 头 | 平手（wulaai 多了 Retry-After 解析，AstrBot 多了 409/529 和 429 开关） |
| prompt caching | Anthropic system 块自动打 `cache_control: ephemeral` 断点 | 只统计 cache 命中，不打断点 | wulaai 可补（§3.7，P2） |

---

## 1. 中间表示（message 格式）的对比

### 1.1 AstrBot 的做法

规范格式 = OpenAI chat messages 的 dict 形态，由 `astrbot/core/agent/message.py` 的 pydantic 模型生成与校验（该文件注释标明灵感来自 MoonshotAI/kosong）：

- `Message{role, content: str|list[ContentPart], tool_calls, tool_call_id}` —— assistant + tool_calls 时允许 content 为 None（`message.py:218-237`）。
- `ContentPart` 子类型注册表：`TextPart` / **`ThinkPart(think, encrypted)`** / `ImageURLPart` / `AudioURLPart`（`message.py:19-141`）。`ThinkPart.encrypted` 存的就是 Anthropic 的 thinking `signature` 和 Gemini 的 `thought_signature`（base64）。
- `ToolCall{id, function{name, arguments: str}, extra_content}` —— `extra_content` 是个逃逸舱：Gemini 的 per-tool-call `thought_signature` 挂在 `extra_content.google.thought_signature` 里（`entities.py:422-439` 的 `to_openai_to_calls_model`）。
- 持久化：历史直接以这个 dict 格式存 DB；`_no_save` 私标（`message.py:215`）标记「只进请求、不落盘」的内容（如临时记忆召回）；`_checkpoint` 伪角色（`message.py:273-307`）把 LLM 轮次锚定到平台消息，provider 发送前由 `strip_checkpoint_messages` 剥掉。

关键性质：**历史里存的是规范格式，不是协议格式**——切换 provider 不需要迁移历史。

### 1.2 wulaai 的做法

- `AIMessage`（`Protocol/AIProtocolModels.cs`）：`Role/Content/Parts/ToolCalls/ToolCallId/ToolName/ReasoningContent`，静态工厂 `User/UserParts/Assistant/AssistantToolCalls/ToolResult/ToolResultParts`。
- 持久化：`AIHistoryManager.SavedHistoryEntry`（role/message + ToolCallId/ToolName/ArgsJson/IsError/HasToolSemantics），UI 展示文本与工具语义元数据分离（`_history` + 平行 `_historyMeta`），重建请求时由 `BuildCanonicalMessagesForAgent`（`AIIntelligenceCore.cs:1451`）把 meta 行还原成 `AssistantToolCalls`/`ToolResult`、无 meta 旧行折叠进 user 文本。

**对比结论**：两者同构——都是「规范格式 + 协议转换在 provider 内」。wulaai 的拆分（展示文本 vs 工具语义）多一层间接，但换来了旧存档兼容；AstrBot 的 `ThinkPart.encrypted`/`ToolCall.extra_content` 两个逃逸舱是 wulaai 没有的（见 §3.3）。

## 2. chat 请求构造的对比

### 2.1 共同骨架

两边都是：system prompt 独立字段 → 历史逐条转换 → 工具定义现场转 schema → tool_choice 映射 → 按 provider 特性补丁。

### 2.2 system prompt

| | AstrBot | wulaai |
|---|---|---|
| OpenAI | 插到 messages 头部 `{"role":"system"}`（`openai_source.py:976`） | 同（`OpenAIChatProvider.BuildPayload:172`） |
| Anthropic | 抽出为顶层 `system`，包装成 `[{"type":"text",...}]` block 列表（`anthropic_source.py:801-806`）——**为了打 cache_control 断点** | 抽出为顶层字符串 `system`（`AnthropicMessagesProvider.BuildPayload:160`），多条 system 历史合并进同一字符串 |
| Gemini | `GenerateContentConfig.system_instruction`（`gemini_source.py:267`） | `system_instruction: {parts:[{text}]}`（`GeminiProvider.BuildPayload:138`） |

平手。AstrBot 的 block 化是为 §3.7 的缓存断点服务。

### 2.3 工具定义（schema 导出）

| | AstrBot（`agent/tool.py`） | wulaai |
|---|---|---|
| OpenAI | `openai_schema()`：`{type:"function", function:{name, description?, parameters?}}`；`omit_empty_parameter_field` 选项专门伺候「空 properties 会被某些 Gemini 代理拒绝」的情况 | `BuildPayload` 内联组装，parameters 直接 `CloneObject` |
| Anthropic | `anthropic_schema()`：只取 `properties`/`required` 组成 `input_schema`，**丢弃顶层其他字段** | 整个 Parameters JObject 原样作为 `input_schema` |
| Gemini | `google_schema().convert_schema()` 白名单递归转换：type 白名单（不支持→"null"）、`type:["string","null"]` 数组取首个非 null、format 白名单（string:enum/date-time 等）、字段白名单（title/description/enum/minimum/maximum/maxItems/minItems/nullable/required）、**递归删 `default` 和 `additionalProperties`**（#5217）、**array 强制补 `items`（缺省补 `{type:"string"}`）**、支持 `anyOf` | `NormalizeSchema`（`GeminiProvider.cs:316`）：type 数组取首个非 null、递归 properties/items、删 `additionalProperties`/`strict` |

> 另一个方向的对比也值得记下：AstrBot 的 `anthropic_schema()` 只做浅拷贝（`properties`/`required` 两个键，其余透传），wulaai 是整个 Parameters 原样透传——两者都信任输入是合法 JSON Schema，实际都没出过问题；Gemini 才是需要认真裁剪的那个。

**wulaai 差距**：Gemini 转换少了三件 AstrBot 实践证明必要的事——array 缺 `items` 的兜底、字段白名单（`default`/`$schema`/`exclusiveMinimum` 这类 MCP 工具常见字段 Gemini 会 400）、anyOf 透传。wulaai 的 MCP 工具（`Mcp/McpClient.cs`）已经能接外部 server，外部 schema 比手写工具脏得多，这个差距会实打实咬人。→ P1。

### 2.4 tool_choice 映射

| | AstrBot | wulaai |
|---|---|---|
| OpenAI | auto/required 字符串透传 | auto/none/required 字符串（`ToolChoiceToOpenAI`） |
| Anthropic | `auto/any/none` + **required→any 的 OpenAI 命名兼容** + dict 透传 + 非法值降级 auto 并警告（`_normalize_tool_choice`，`anthropic_source.py:454-482`） | Auto→`{type:auto}`、Required→`{type:any}`、None→`{type:none}`（`ToolChoiceToAnthropic`） |
| Gemini | required→ANY，其余 AUTO；且**只有存在 function_declarations 时才设 toolConfig**（`gemini_source.py:211-222`） | Auto→AUTO、Required→ANY（有工具时才有 toolConfig，逻辑等价） |

平手。

### 2.5 图片/音频注入

- AstrBot：`assemble_context`（`entities.py:186-259` 及各 provider 重写）按「文本 → 额外块 → 图 → 音」组块；媒体引用统一走 `MediaResolver`（url/base64/本地路径/file_token → data url）；纯文本单块时**降级为简单字符串格式**保持兼容。Anthropic 侧还从二进制 magic bytes 实测 MIME（`_detect_image_mime_type`，`anthropic_source.py:887-897`）；audio 对 Anthropic 降级为 `[Audio Attachment]` 文本占位。
- wulaai：`AIContentPart.ImagePart(mimeType, base64)` 内嵌 data url（OpenAI）/ base64 source block（Anthropic）/ inline_data（Gemini）。截图工具固定 jpeg/png，MIME 来源可信。

平手（wulaai 没有「任意媒体引用」需求——图片来源只有自己截屏，所以 MediaResolver 那套不需要；MIME 实测也不需要）。

## 3. response 解析与回放（tool-call 语义）的对比

### 3.1 流式聚合

| | AstrBot | wulaai |
|---|---|---|
| OpenAI | 官方 SDK `ChatCompletionStreamState.handle_chunk` 聚合成完整 ChatCompletion；**逐 chunk 修兼容**：siliconflow 漏 `type:"function"` 补上、Gemini/代理漏 `index` 用枚举序号补（`openai_source.py:645-653`）；delta=None 的 chunk 跳过但 usage chunk 保留；`stream_options:{include_usage:true}` 主动要 usage；**Moonshot 把 usage 放在 `choices[].usage` 的 workaround**（#6614） | 手写 SSE + `OpenAIStreamAccumulator`（按 index 累积 id/name/arguments 字符串）。也写了「缺 index 时按到达顺序分配」（`NextToolIndex`）——和 AstrBot 的 workaround 等价；usage 从 chunk 根 `usage` 提取 |
| Anthropic | SDK `messages.stream` 事件流：`content_block_start` 建 tool_use 缓冲 → `input_json_delta` 累积 partial_json 字符串 → `content_block_stop` 时 `json.loads` 出 input（解析失败告警跳过该工具）；thinking_delta 累积 + **signature_delta 单独捕获**；usage 从 message_start + message_delta 两段更新 | 手写 SSE：同样的 index→ToolAccumulator 缓冲 + InputJson StringBuilder 拼接；thinking_delta → ReasoningDelta。**signature_delta 未处理**（signature 丢弃） |
| Gemini | SDK `generate_content_stream` 逐 chunk；**发现 function_call part 立即 yield 完整响应并 return**（Gemini 流里 tool call 是整块出现的）；thought part 的文本不进正文（防重复/泄漏）；`thought_signature`（bytes）→ base64 存进 `reasoning_signature` 和 per-tool `extra_content` | 手写 SSE：part 级解析，functionCall 整块收集（等价）；**thought 标记未处理**（thinking 文本会混进正文 Content！见下）；**thought_signature 丢弃** |

**wulaai 的两个洞**（都确认过代码，均已修复——见 §5 的已修标记）：
1. `GeminiProvider.AppendCandidate`（`GeminiProvider.cs:376`）对每个 part 只看 `text` 和 `functionCall`，~~不检查 `thought` 标志~~ → **已修**：thought part 进 `ReasoningDelta`/`Reasoning`，不再混入正文。AstrBot 在 `_process_content_parts` 里显式 `if part.text and not part.thought`（`gemini_source.py:539`）+ 单独 `_extract_reasoning_content`。
2. 跨轮 thinking signature 整体缺失（见 §3.3，仍开放）。

另外记录 AstrBot 流式的一个可选设计：`ChatCompletionStreamState` 之外它还给**每个 content_block_start/thinking_delta 也 yield chunk**（包括空文本的块开始事件），UI 可以更早显示「开始思考/开始输出」状态；wulaai 的 `AIStreamEvent` 只有 delta 回调，思考状态由请求开始即置位，体验等价，无需跟进。

### 3.2 回放清洗（发送前 sanitize）——本次对比 wulaai 最大的架构差距

AstrBot 在**每个请求发出前**对 messages 做防护性清洗，三家各一套：

- **OpenAI**（`_sanitize_assistant_messages`，`openai_source.py:452-529`）：
  1. assistant 无 content 且无 tool_calls 且无 reasoning_content → **整条丢弃**；
  2. 有 reasoning_content 但无 content/tool_calls → `content=""` 占位（推理模型要 reasoning 历史，但 API 要 content/tool_calls 至少其一）；
  3. 有 tool_calls 但 content 为空 → `content=null`（OpenAI 规范形态；Moonshot/DeepSeek Reasoner 对 `""` 会 400）；
  4. **孤儿 tool 消息清洗**：跟踪 `pending_tool_call_ids`，tool 消息的 `tool_call_id` 不在上一条 assistant 的 tool_calls 里 → 丢弃（历史被截断/压缩砍掉头时的必然产物）。
- **Anthropic**（`_sanitize_assistant_messages` + `_merge_consecutive_anthropic_messages`，`anthropic_source.py:311-433`）：连续同角色消息**合并**（API 要求交替；合并 user 时 tool_result 块提前）；孤儿 tool_result（tool_use_id 无对应 tool_use）→ 从块列表剔除，剔空则整条删。
- **Gemini**：`_prepare_conversation` 里 `append_or_extend` 天然合并连续同角色（`gemini_source.py:313-322`）；**`gemini_contents[0]` 若是 ModelContent 则 pop**（`gemini_source.py:430`——意图是「历史被截断后首条可能是 model，Gemini 要求 user 先」；但代码写的是无参 `pop()`，删的是**末尾**而非开头，疑似 AstrBot 自身的 bug，意图本身是对的）。另外 tool 角色回放的 name 取 `message["name"]`、缺失才回退 tool_call_id——OpenAI 格式的 tool 消息常常没有 name 字段，会把 id 当 name 发给 Gemini，也是个粗糙点。

**wulaai 现状**：~~三个 provider 的 `ConvertMessage` 都是「忠实转换、原样发送」~~ **已修**：新增 `Providers/AIMessageSanitizer.cs`，三个 provider 的 `BuildPayload` 在转换前先过 sanitize——OpenAI（空 assistant 规整 + 孤儿 tool 按 id 清洗）、Anthropic（孤儿 tool_result 清洗 + tool 行折进**后随** user 行 + 连续同角色合并）、Gemini（首条非 user 弹出 + 孤儿 functionResponse 按 name 清洗）。压缩砍出的残缺回合降级为丢行，不再触发 400。

> 注：wulaai 在压缩点插了 "[Earlier conversation summary]" 占位 user 行，Anthropic 的交替约束碰巧被缓解，但孤儿配对问题不受影响。

### 3.3 reasoning 的持久化与跨轮回传

| | AstrBot | wulaai |
|---|---|---|
| 存储 | ThinkPart 作为 content part **进持久化历史**，`encrypted` 带 signature | `ReasoningContent` 在 `AIMessage` 上，但 `SavedHistoryEntry` 不存它——**存档/读档后 reasoning 丢失** |
| OpenAI 系回传 | `_finally_convert_payload`（`openai_source.py:1001-1069`）：assistant 的 think part 拆出为 `reasoning_content` 字段；**DeepSeek-v4（按模型名或 base_url 含 api.deepseek.com 判定）和 MiMo 全系列（5 个模型枚举）强制补 `reasoning_content:""`**——这两家对缺失字段直接 400。**反向例子：Groq 子类会删掉历史里的 reasoning 字段**（Groq 拒绝） | `ShouldIncludeReasoningContent`（`OpenAIChatProvider.cs:228`）：仅 `deepseek-v4*` 模型名 + 官方域名才回传。无 MiMo 处理，无 Groq 反向处理 |
| Anthropic 回传 | thinking block **带 signature 回传**（`anthropic_source.py:190-198`）——Anthropic 对开启 thinking 的模型强制校验签名 | 不回传（`ReasoningContent` 只读不回放；Anthropic provider 转换时完全忽略） |
| Gemini 回传 | `thought_signature` 挂回 text part 和 functionCall part（`gemini_source.py:360-410`）——Gemini 3 对 tool call 的 thought_signature 有校验 | 不回传 |

**影响面**：wulaai 目前默认模型是 deepseek-chat，Anthropic/Gemini 用户大概率没开 thinking，所以这个洞**现在不咬人**；但只要玩家用 Claude extended thinking 或 Gemini 3 + 工具，跨轮对话就会被协议拒绝。→ P1（功能缺口，按「用户声明」哲学可以加一个 `enableThinkingSignatures` 开关，或者干脆默认做——无害）。

### 3.4 多模态失败处理

- AstrBot 三层：
  1. **声明**：provider_config 的 `modalities: [image, audio, tool_use]` 列表（runner 层读，`tool_loop_agent_runner.py:338/590/605`）；未配置 = 全开（向后兼容）。
  2. **发送前 sanitize**（`provider/modalities.py`）：不支持 image → 图片块换成 `[Image]` 文本；不支持 audio → `[Audio]`；不支持 tool_use → tool 角色整条降级为 user 占位文本 + assistant 的 tool_calls 剥离。
  3. **失败后重试**（`_handle_api_error`，`openai_source.py:1120-1156`）：识别「The model is not a VLM」（siliconcloud）、图片内容审核（可配置 pattern 列表）、`invalid_attachment` 三类错误 → **剥掉所有图片、换 `[图片]` 占位、原样重试一次**。
- wulaai：`isMultimodalModel` 用户勾选（2026-08-13 改的，语义 = AstrBot 的 modalities 声明，但只控制**工具注册表**不含 take_screenshot/analyze_screen）。若用户勾错了，或历史里残留旧截图（`ToolResultParts` 的 Parts 会进持久化吗？——不会，`AIToolResult.ContentParts` 注释写明 transient 不进历史；但**当轮 loop 内** tool result 的图会进 messages），没有兜底：图片 400 直接终止该轮。

→ P1：给 OpenAI provider 加 AstrBot 的「识别 VLM 不支持错误 → 剥图重试一次」。实现位置：`OpenAIChatProvider` 的 `PostAsync`/`StreamOnceAsync` 错误分支 + `BuildPayload` 的降级参数。`isMultimodalModel=false` 时顺便在 `ConvertMessage` 层把 image part 替换为 `[Image]` 文本占位（= AstrBot 的 sanitize），双保险。

### 3.5 错误恢复策略

| 错误类型 | AstrBot | wulaai |
|---|---|---|
| 429 | 换 API key（多 key 池随机重选）+ 睡 1s；key 用尽抛出 | 分类 Retryable → 指数退避重试同 key（支持 Retry-After） |
| 408/5xx/连接错误 | tenacity 重试（{408,409,429,500,502,503,504,529} + 5xx 区间 + 连接/超时异常类型名匹配） | 同语义（IsRetryableStatus + HttpRequestException）；**AstrBot 多 409/529，且 `retry_rate_limits` 可关 429** |
| 上下文超长 | 识别 "context length" 错误 → **弹出最早一条历史原地重试**（`openai_source.py:1105-1119`） | 无（靠压缩预判；预估模型漂了就直接 Fatal） |
| 不支持 function calling | 识别错误字符串 → `payloads.pop("tools")` 摘工具重试，提示用户去设置里关（`openai_source.py:1158-1176`；Gemini 同逻辑 `gemini_source.py:652-656`） | 无 |
| 不支持 system prompt | Gemini: "Developer instruction is not enabled" → 摘掉 system_instruction 重试 | 无 |
| 不支持多模态输出 | Gemini: 降级 modalities=["TEXT"] 重试 | N/A |
| Recitation | Gemini: finish_reason=RECITATION → temperature+0.2 重试 | 无 |
| 内容过滤 | OpenAI finish_reason=content_filter → 抛专用异常 | 无特殊处理 |

**评价**：AstrBot 的「改 payload 重试」是用字符串匹配错误消息实现的，脆（它自己的注释也承认「错误提示与 code 不统一，只能通过字符串匹配」）。wulaai 不需要全抄——**值得抄的只有两个**：上下文超长弹历史重试（和已有压缩逻辑天然衔接，压缩失败/预估失准时的最后一道防线）、不支持工具时摘工具重试（对玩家接杂牌 OpenAI 兼容端点很友好）。→ P2。

### 3.6 空输出防护

AstrBot 三家统一：响应无文本、无 reasoning、无 tool call → `EmptyModelOutputError`（带 response_id/finish_reason 上下文，`openai_source.py:927-938`、`anthropic_source.py:40-55`、`gemini_source.py:455-470`）。OpenAI 侧还处理 `choices` 为空但 `data` 字段有内容的代理怪癖（#9374）、refusal 字段、`<think>` 标签抠出 reasoning、孤儿 `</think>` 清理（`openai_source.py:855-878`）。

wulaai：无显式检测；空 Content 走 `FinalizeVisibleResponse` → "No response." 进历史。`<think>` 标签不抠（DeepSeek 非官方代理经 `<think>` 输出推理时，思考文本会进正文）。

→ P2：空输出抛 `WulaAiException(Retryable)`（空响应大概率是代理抖动，重试合理）+ `<think>` 抠取（可选，优先级低——官方 DeepSeek 走 reasoning_content 字段，不受影响）。

### 3.7 prompt caching

- AstrBot：Anthropic 自动给 system 最后一个 block 打 `cache_control: {type:"ephemeral"}`（`_apply_explicit_prompt_cache_breakpoints`，`anthropic_source.py:484-492`）。OpenAI/Gemini 靠服务端自动缓存，只统计命中。注意 AstrBot 的 Anthropic usage 统计把 `cache_creation_input_tokens` **丢弃不计**（`anthropic_source.py:435-443`）——写入缓存的 token 在它的统计里会丢，wulaai 的 `BuildUsageSummary` 反而把 creation 算作 miss 侧，口径更完整。
- wulaai：`AIProviderJson.LogUsage`/`ExtractCacheHitTokens` 已统一统计四家命中（DeepSeek 的 hit/miss、OpenAI cached_tokens、Anthropic cache_read、Gemini cachedContentTokenCount），UI 显示 cache %。**但不打断点**——Anthropic 不标 cache_control 就是全不缓存。

→ P2：Anthropic `BuildPayload` 把 system 从字符串改成 block 数组并给末块打 ephemeral 断点（5 行改动，cache 命中率和账单立竿见影）。

### 3.8 usage 与 reasoning 提取的边边角角

- AstrBot 的 `TokenUsage` 是强类型三元组（input_other/input_cached/output），支持加减；wulaai 的 `JObject Usage` 原始但够用（`ExtractPromptTokens/ExtractCompletionTokens/ExtractCacheHitTokens` 三家通吃 + EMA 校准 chars/token 喂给压缩触发——这是 AstrBot 没有的，**wulaai 领先**）。
- AstrBot 流式主动传 `stream_options:{include_usage:true}`；wulaai 不传，靠各家默认（DeepSeek/OpenAI 官方默认流式不给 usage！——**这是一个实际差距**：`RecordUsage` 在流式下大概率拿不到 OpenAI 系 usage，校准和用量显示会失灵）。→ P1，一行修复：`BuildPayload` 流式时加 `stream_options.include_usage=true`。
- AstrBot 还有 Moonshot `choices[].usage` workaround；wulaai 无。低优先级（Moonshot 用户少）。

## 4. 范围外说明

AstrBot 的 platform 层（17 个聊天平台适配器、MessageChain、洋葱 pipeline、UMO 路由）解决的是「一套 AI 接 N 个 IM 平台」的问题；wulaai 只有一个对话窗口和一个游戏内 overlay，**没有对应物也不需要**。它有价值的思路（消息链中间表示、事件传播控制）已经体现在 wulaai 的 `AIMessage` 中间表示 + `AIToolLoopRunner` 回调管线里。若未来想把 wulaai 的 AI 暴露到游戏外（如 Discord 机器人复读游戏状态），再回头看 `astrbot/core/platform/`。

## 5. 优先级清单

| 优先级 | 项 | 出处 | 工作量 |
|---|---|---|---|
| ~~**P0**~~ ✅ | Gemini 流式/非流式过滤 `thought` part：思考文本进 `ReasoningDelta`，不进正文 | §3.1-1 | **已修**（AppendCandidate 检查 `thought`，累积进 Reasoning/ReasoningContent） |
| ~~**P0**~~ ✅ | 三个 provider 发送前 sanitize：孤儿 tool/toolcall 配对清洗、空 assistant 规整、Gemini 首条 model 弹出、Anthropic 连续同角色合并 | §3.2 | **已修**（`Providers/AIMessageSanitizer.cs`，三 provider BuildPayload 接入） |
| **P1** | OpenAI 系流式 `stream_options.include_usage=true` | §3.8 | 极小 |
| **P1** | Gemini schema 转换补齐：array 缺 items 兜底、字段白名单（删 default/$schema 等）、anyOf 透传 | §2.3 | 小（扩 `NormalizeSchema`） |
| **P1** | 多模态失败剥图重试一次（OpenAI 系：识别 not-a-VLM/invalid_attachment 错误）；`isMultimodalModel=false` 时 image part 转 `[Image]` 文本占位 | §3.4 | 中 |
| **P1** | reasoning 跨轮回传：Anthropic thinking block 带 signature、Gemini thought_signature 挂回 part；需要 `SavedHistoryEntry` 增加 reasoning/signature 字段 | §3.3 | 中大（动持久化格式，向后兼容方案：缺字段 = 不回传） |
| **P1** | OpenAI 历史回放：MiMo 系模型强制 `reasoning_content:""` 占位（DeepSeek 已有等价物） | §3.3 | 小（扩 `ShouldIncludeReasoningContent` 的判定表） |
| **P2** | 上下文超长错误 → 弹最早历史原地重试（压缩预估失准的兜底） | §3.5 | 中 |
| **P2** | 模型不支持工具错误 → 摘 tools 重试 + 一次性提示玩家 | §3.5 | 小 |
| **P2** | Anthropic system 块化 + ephemeral cache 断点 | §3.7 | 极小 |
| **P2** | 空输出检测 → `WulaAiException(Retryable)`；`<think>` 标签抠取 | §3.6 | 小 |
| **P2** | 重试状态码补 409/529；`retry_rate_limits` 类开关（429 可禁重试） | §3.5 | 极小 |
| **P3** | provider 变体机制：wulaai 若以后要支持「OpenAI 兼容但 quirks 不同」的端点（硅基流动、Ollama…），把 `OpenAIChatProvider` 的 quirks 抽成「provider profile」声明式配置（模型名/base_url 匹配 → 补丁集），而不是堆 if。参考 AstrBot 的 12 个变体子类（多数是 20 行以内的轻量子类：Groq/OpenRouter 只改 `reasoning_key="reasoning"`，LongCat 只规范化 base_url，Kimi Code 继承 **Anthropic** 协议加 UA 头，MiniMax Token Plan 继承 Anthropic 协议改 Bearer 头）+ `_apply_provider_specific_request_overrides`（NVIDIA MiniMax 强制 max_tokens、Ollama 关 thinking 映射 reasoning_effort=none） | §0/§3.5 | 中（等有第二个变体需求时再做） |
| **不做** | AstrBot 的多 key 池轮换（wulaai 单 key 配置，无需求）、`get_models` 模型列表拉取（玩家手填模型名，够用）、audio 模态（游戏内无音频输入） | — | — |

## 6. wulaai 领先/独有项（保持）

1. **结构化输出**（OutputSchema → json_object / emit_result 强制工具 / responseSchema）：AstrBot 三家都没做，wulaai 的记忆摘要已迁移上去。
2. **SSE 空闲 watchdog**（`ReadSseWithIdleWatchdogAsync`）：AstrBot 只有 SDK 整体超时，流挂住要等满 timeout；wulaai 能 30s 无数据即断并归类 Retryable。
3. **chars-per-token EMA 校准**驱动压缩触发（`CalibrateCharsPerToken`）：AstrBot 的 `token_counter.py` 是静态估算。
4. **压缩 = LLM 摘要 + 占位插入 + 游标平移**，失败降级丢弃；AstrBot 的 `LLMSummaryCompressor` 等价但 wulaai 的降级链更完整。
5. **工具参数白名单过滤**（`AIToolRunner.FilterArguments`，大小写不敏感）：AstrBot 直接把 args JSON 交给 handler。
6. **用量显示进 UI**（思考指示器右上角）+ 会话累计：AstrBot 的 TokenUsage 只进统计模块。

---

## 附：本文核对过的关键文件

- AstrBot 侧（相对 `C:\astrbot\AstrBot\astrbot\core\`）：`provider/entities.py`、`agent/message.py`、`agent/tool.py`、`provider/provider.py`、`provider/sources/{openai_source,anthropic_source,gemini_source,request_retry}.py`、`provider/sources/{zhipu,groq,xai,openrouter,kimi_code,longcat,xiaomi,minimax_token_plan,oai_aihubmix}_source.py`（变体扫读）、`provider/modalities.py`、`agent/runners/tool_loop_agent_runner.py`（modalities 接线处）
- wulaai 侧（相对 `Source/WulaFallenEmpire/EventSystem/AI/`）：`Protocol/AIProtocolModels.cs`、`Providers/{IAIProvider,AIProviderFactory,AIProviderJson,OpenAIChatProvider,AnthropicMessagesProvider,GeminiProvider,WulaAiException}.cs`、`Runtime/AIToolLoopRunner.cs`、`AIIntelligenceCore.cs`（BuildCanonicalMessagesForAgent / RecordUsage / DescribeErrorForPlayer）、`Tools/Tool_AnalyzeScreen.cs`

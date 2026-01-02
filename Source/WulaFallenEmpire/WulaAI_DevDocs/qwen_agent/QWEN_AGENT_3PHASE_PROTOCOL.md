Qwen-Agent 3-Phase Tool Calling Protocol (Design + Implementation Plan)

Goal
- Preserve the existing three-phase workflow: Query -> Action -> Reply.
- Provide a strict, parseable tool-call format compatible with Qwen-Agent style.
- Allow explicit switching between native tool calling and template parsing.

Scope
- Query phase: only query tools (get_*/search_*/analyze_*/recall_memories)
- Action phase: only action tools (spawn_resources, send_reinforcement, call_bombardment, modify_goodwill, call_prefab_airdrop, set_overwatch_mode, remember_fact)
- Reply phase: natural language only, no tool calls

Tool Calling Formats
Native Mode (useNativeToolApi = true)
- Use provider-native tool calls (OpenAI/DeepSeek tool_calls, Gemini functionCall)
- No text template parsing, only validation + execution

Template Mode (useNativeToolApi = false)
- Use Qwen-Agent style tool-call tags in text:
  <tool_call>
  {"name":"tool_name","arguments":{...}}
  </tool_call>
- Tool results are injected as tool-role messages (not visible to the user)
- No natural language is allowed in tool phases
- If no tool is needed: output exactly {"tool_calls":[]}

Three-Phase Flow (Template Mode)
Phase 1: Query
- Prompt includes tool list and Qwen tool-call template
- Model returns one or more <tool_call> blocks
- Parser extracts tool calls -> validator -> tool execution
- Tool results stored and appended to history
- One retry max if tool parsing/validation fails

Phase 2: Action
- Same as Query, but only action tools allowed
- One retry max if tool parsing/validation fails

Phase 3: Reply
- Natural language only
- If tool-call tags or JSON are detected, one retry is triggered

Validation and Guardrails
- ToolCallValidator ensures:
  - Tool exists
  - Required parameters present
  - Tool is valid for current phase
- Failures are returned to the model as tool results for self-correction
- No parameter guessing or defName guessing

Implementation Checklist (Files)
1) WulaFallenEmpireSettings.cs
   - Add useNativeToolApi flag (default true for backward compatibility)
2) WulaFallenEmpireMod.cs
   - Expose a UI checkbox to toggle useNativeToolApi
3) AIIntelligenceCore.cs
   - Select native loop vs template loop based on useNativeToolApi
   - Template loop uses Qwen tool-call instructions in Query/Action
   - Reply phase unchanged
4) JsonToolCallParser.cs
   - Add parsing for <tool_call>...</tool_call>
   - Convert parsed calls to tool_calls list for execution
5) ToolCallValidator.cs
   - Enforce phase/tool rules and required args

Rollout Plan
1) Implement parser + settings toggle
2) Switch flow selection by useNativeToolApi
3) Add template tool-call instructions
4) Validate in-game with:
   - Item request (Query -> search_thing_def)
   - Action call (spawn_resources)
   - Reply text only

Testing Checklist
- Query phase outputs <tool_call> blocks
- Action phase outputs <tool_call> blocks
- Reply phase never outputs tool calls
- Tool results are linked to tool_call_id (native) or injected safely (template)
- One retry only per phase when parsing fails

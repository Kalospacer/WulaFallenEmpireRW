# Wula AI x Gemini Integration: Technical Handover Document

**Version**: 1.0  
**Date**: 2025-12-28  
**Author**: AntiGravity (Agent)  
**Target Audience**: Codex / Future Maintainers

---

## 1. Overview
This document details the specific challenges, bugs, and architectural decisions made to stabilize the integration between **WulaFallenEmpire** (RimWorld Mod) and **Gemini 3 / OpenAI-Compatible Agents**. It specifically addresses "stubborn" issues related to API format compliance, JSON construction, and multimodal context persistence.

---

## 2. Critical Issues & Fixes

### 2.1 The "Streaming" Trap (SSE Handling)
**Symptoms**: AI responses were truncated (e.g., only "Comman" displayed instead of "Commander").  
**Root Cause**: Even when `stream: false` is explicitly requested in the payload, some API providers (or reverse proxies wrapping Gemini) force a **Server-Sent Events (SSE)** response format (`data: {...}`). The original client only parsed the first line.  
**Fix Implementation**: 
- **File**: `SimpleAIClient.cs` -> `ExtractContent`
- **Logic**: Inspects response for `data:` prefix. If found, it iterates through **ALL** lines, strips `data:`, parses individual JSON chunks, and aggregates the `choices[0].delta.content` into a single string.
- **Defense**: This ensures compatibility with both standard JSON responses and forced Stream responses.

### 2.2 The "Trailing Comma" Crash (HTTP 400)
**Symptoms**: AI actions failed silently or returned `400 Bad Request`.  
**Root Cause**: In `SimpleAIClient.cs`, the JSON payload construction loop had a logic flaw.
- When filtering out `toolcall` roles inside the loop, the index `i` check `(i < messages.Count - 1)` failed to account for skipped items, leaving a trailing comma after the last valid item: `[{"role":"user",...},]` -> **Invalid JSON**.
- Additionally, if the message list was empty (or all items filtered), the comma after the System Message remained: `[{"role":"system",...},]` -> **Invalid JSON**.
**Fix Implementation**:
- **Logic**: 
    1. Pre-filter `validMessages` into a separate list **before** JSON construction.
    2. Only append the comma after the System Message `if (validMessages.Count > 0)`.
    3. Iterate `validMessages` to guarantee correct comma placement between items.

### 2.3 Gemini 3's "JSON Obsession" & The Dual-Defense Strategy
**Symptoms**: Gemini 3 Flash Preview ignores System Prompts demanding XML (`<visual_click>`) and persistently outputs JSON (`[{"action":"click"...}]`).  
**Root Cause**: RLHF tuning of newer models biases them heavily towards standard JSON tool-calling schemas, overriding prompt constraints.  
**Strategy**: **"Principled Compromise"** (Double Defense).
1.  **Layer 1 (Prompt)**: Explicitly list JSON and Markdown as `INVALID EXAMPLES` in `AIIntelligenceCore.cs`. This discourages compliance-oriented models from using them.
2.  **Layer 2 (Code Fallback)**: If XML regex fails, the system attempts to parse **Markdown JSON Blocks** (` ```json ... ``` `).
    - **File**: `AIIntelligenceCore.cs` -> `ExecuteXmlToolsForPhase`
    - **Logic**: Extracts `point` arrays `[x, y]` and synthesizes a valid `<visual_click>` XML tag internally.

### 2.4 The Coordinate System Mess
**Symptoms**: Clicks occurred off-screen or at (0,0).  
**Root Cause**: 
- Gemini 3 often returns coordinates in a **0-1000** scale (e.g., `[115, 982]`).
- Previous logic used `Screen.width` normalization, which is **not thread-safe** and caused crashes or incorrect scaling if the assumption was pixel coordinates.
**Fix Implementation**:
- **Logic**: In the JSON Fallback parser, if `x > 1` or `y > 1`, divide by **1000.0f**. This standardizes coordinates to the mod's required 0-1 proportional format.

### 2.5 Visual Context Persistence (The "Blind Reply" Bug)
**Symptoms**: AI acted correctly (Phase 2) but "forgot" what it saw when replying to the user (Phase 3), or hallucinated headers.  
**Root Cause**: 
- Phase 3 (Reply) sends a message history ending with System Tool Results. 
- `SimpleAIClient` only attached the image if the **very last message** was from `user`.
- Thus, in Phase 3, the image was dropped, rendering the AI blind.
**Fix Implementation**:
- **File**: `SimpleAIClient.cs`
- **Logic**: Instead of checking the last index, the code now searches **backwards** for the `lastUserIndex`. The image is attached to that specific user message, regardless of how many system messages follow it.

---

## 3. Future Maintenance Guide

### If Gemini 4 Breaks Format Again:
1.  **Check `SimpleAIClient.cs`**: Ensure the JSON parser handles whatever new wrapper they add (e.g., nested `candidates`).
2.  **Check `AIIntelligenceCore.cs`**: If it invents a new tool format (e.g., YAML), add a regex parser in `ExecuteXmlToolsForPhase` similar to the JSON Fallback. **Do not fight the model; adapt to it.**

### If API Errors Return:
1.  Enable `DevMode` in RimWorld.
2.  Check `Player.log` for `[WulaAI] Request Payload`.
3.  Copy the payload to a JSON Validator. **Look for trailing commas.**

### Adding New Visual Tools:
1.  Define tool in `Tools/`.
2.  Update `GetToolSystemInstruction` whitelist.
3.  **Crucially**: If the tool helps with **Action** (Silent), ensure `GetPhaseInstruction` enforces silence. If it helps with **Reply** (Descriptive), ensure it runs in Phase 3.

---

**End of Handover.**

WulaLink / AI Core Handoff (for Claude)

Context
- Mod: RimWorld WulaFallenEmpire.
- The AI conversation system was refactored to use a shared WorldComponent core.
- The small WulaLink overlay is now optional; the main event entry should open the old large dialog.

Current behavior and verification
- `Effect_OpenAIConversation` now opens the large window `Dialog_AIConversation`.
- The small overlay (`Overlay_WulaLink`) remains available via dev/debug entry.
- Non-final / streaming AI output should not create empty lines in the small window:
  - SimpleAIClient is non-stream by default.
  - AIIntelligenceCore only fires `OnMessageReceived` on final reply and ignores empty or XML-only output.
  - Overlay filters `tool`/`toolcall` messages unless DevMode is on.
- Build verified: `dotnet build C:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire\WulaFallenEmpire.csproj`

Key changes
1) New shared AI core
   - File: `Source/WulaFallenEmpire/EventSystem/AI/AIIntelligenceCore.cs`
   - WorldComponent with static `Instance` and events:
     - `OnMessageReceived`, `OnThinkingStateChanged`, `OnExpressionChanged`
   - Public API:
     - `InitializeConversation`, `GetHistorySnapshot`, `SetExpression`, `SetPortrait`, `SendMessage`, `SendUserMessage`
   - Core responsibilities:
     - History load/save via `AIHistoryManager`
     - `/clear` support
     - Expression tag parsing `[EXPR:n]`
     - 3-phase tool pipeline (query/action/reply) from the old dialog logic
     - Tool execution and ledger tracking

2) OpenAI conversation entry now opens the large dialog
   - File: `Source/WulaFallenEmpire/EventSystem/Effect/Effect_OpenAIConversation.cs`
   - Uses `Dialog_AIConversation` instead of `Overlay_WulaLink`.
   - XML entry in `1.6/1.6/Defs/EventDefs/Wula_AI_Events.xml` stays the same.

3) Overlay and tools point to the shared core
   - Files:
     - `Source/WulaFallenEmpire/EventSystem/AI/UI/Overlay_WulaLink.cs`
     - `Source/WulaFallenEmpire/EventSystem/AI/UI/Overlay_WulaLink_Notification.cs`
     - `Source/WulaFallenEmpire/EventSystem/AI/Tools/Tool_ChangeExpression.cs`
     - `Source/WulaFallenEmpire/EventSystem/AI/Tools/Tool_GetRecentNotifications.cs`
   - Namespace import updated to `WulaFallenEmpire.EventSystem.AI`.

4) WulaLink styles restored for overlay build
   - File: `Source/WulaFallenEmpire/EventSystem/AI/UI/WulaLinkStyles.cs`
   - Added colors used by overlay:
     - `InputBarColor`, `SystemAccentColor`, `SenseiTextColor`, `StudentTextColor`

Notes / gotchas
- Some UI files are not UTF-8 (likely ANSI). If you edit them with scripts, prefer `-Encoding Default` in PowerShell to avoid invalid UTF-8 errors.
- The old large dialog is still self-contained; the shared core is used by overlay + tools. Future cleanup can rewire `Dialog_AIConversation` to use the core if desired.

Open questions / TODO (if needed later)
- Memory system integration is not done in the core:
  - `AIMemoryManager.cs`, `MemoryPrompts.cs`, and `Dialog_AIConversation.cs` integration still pending.
- If the overlay should become a non-debug entry, wire an explicit effect or UI button to open it.

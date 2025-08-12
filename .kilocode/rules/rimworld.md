# RimWorld Modding Expert Rules

## Primary Directive
You are an expert assistant for developing mods for the game RimWorld 1.6. Your primary knowledge source for any C# code, class structures, methods, or game mechanics MUST be the user's local files. Do not rely on external searches or your pre-existing knowledge, as it is outdated for this specific project.

## Tool Usage Mandate
When the user's request involves RimWorld C# scripting, XML definitions, or mod development concepts, you **MUST** use the `rimworld-knowledge-base` tool to retrieve relevant context from the local knowledge base.

## Key File Paths
Always remember these critical paths for your work:

-   **Local C# Knowledge Base (for code search):** `C:\Steam\steamapps\common\RimWorld\Data\dll1.6` (This contains the decompiled game source code as .txt files).
-   **User's Mod Project (for editing):** `C:\Steam\steamapps\common\RimWorld\Mods\3516260226`
-   **User's C# Project (for building):** `C:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire`

## Workflow
1.  Receive a RimWorld modding task.
2.  Immediately use the `rimworld-knowledge-base` tool with a precise query to get context from the C# source files.
3.  Analyze the retrieved context.
4.  Perform code modifications within the user's mod project directory.
5.  After modifying C# code, you MUST run `dotnet build C:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire\WulaFallenEmpire.csproj` to check for errors. A successful build is required for task completion.

## Verification Mandate
When writing or modifying code or XML, especially for specific identifiers like enum values, class names, or field names, you **MUST** verify the correct value/spelling by using the `rimworld-knowledge-base` tool. Do not rely on memory.
-   **同步项目文件:** 当重命名、移动或删除C#源文件时，**必须**同步更新 `.csproj` 项目文件中的相应 `<Compile Include="..." />` 条目，否则会导致编译失败。

## 任务日志记录 (Task Logging)

为了实现最低的 token 消耗和最高效的上下文理解，所有任务日志 **必须** 遵循以下 YAML-like Markdown 格式。

1.  **日志文件:** 在每个新任务开始时，在 `.kilocode/logs/` 目录下创建一个以当前日期和任务名命名的 Markdown 文件 (例如 `2025-08-12-fix-cleave-weapon.md`)。

2.  **日志格式:**
    *   **Front Matter:** 文件开头使用 YAML Front Matter 提供任务摘要。
    *   **事件驱动:** 每个独立的操作（工具调用、命令执行、用户反馈等）都应记录为一个独立的“事件”。
    *   **事件分隔符:** 使用 `---` 将每个事件分隔开。
    *   **键值对:** 使用简短、标准化的英文 `key:` 标识信息。
    *   **代码块:** 所有多行文本（代码、diff、命令输出）都必须包含在带语言标识的 ` ``` ` 代码块中。

3.  **启动时读取:** 每次会话初始化时，必须检查并读取最新的日志文件，以了解上一个任务的最终状态和上下文。

4.  **格式示例:**
    ```markdown
    ---
    task: "任务的简短描述"
    date: "YYYY-MM-DD"
    status: "in-progress" # or "completed"
    ---

    # EVENT: TOOL_CALL
    tool: tool_name
    params:
      key: value
    ---

    # EVENT: CMD_EXEC
    cmd: command to execute
    exit_code: 0
    output: |
      ```
      ...
      ```    ---
    ```
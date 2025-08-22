---
trigger: always_on
---

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
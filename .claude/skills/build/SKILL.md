---
name: build
description: Compile RimWorld mod projects using VS2022 MSBuild
license: MIT
compatibility: opencode
---

# Build Skill - RimWorld Mods

When the user invokes `/build`, compile RimWorld mod projects using VS2022 MSBuild.

## Available Projects

| Project | Path |
|---------|------|
| **WulaFallenEmpire** | `3516260226\Source\WulaFallenEmpire\WulaFallenEmpire.csproj` |
| **ArachnaeSwarm** | `ArachnaeSwarm\Source\ArachnaeSwarm\ArachnaeSwarm.csproj` |
| **DivineDiurganate** | `DivineDiurganate\Source\DivineDiurganate\DivineDiurganate.csproj` |
| **DragonianMix** | `2961683592\Source\dragonianmix-csharp-lib\DragonianMix\DragonianMix.csproj` |

## Build Command Template

```bash
cd "<PROJECT_DIR>" && "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" <PROJECT_NAME>.csproj -p:Configuration=Release -verbosity:minimal
```

## Instructions

1. If user specifies a project name, build that project
2. If no project specified, build **WulaFallenEmpire** (default)
3. Report success or failure with DLL output path
4. If compilation errors, show error messages clearly

## Quick Commands

Build WulaFallenEmpire:
```bash
cd "C:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire" && "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" WulaFallenEmpire.csproj -p:Configuration=Release -verbosity:minimal
```

Build ArachnaeSwarm:
```bash
cd "C:\Steam\steamapps\common\RimWorld\Mods\ArachnaeSwarm\Source\ArachnaeSwarm" && "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" ArachnaeSwarm.csproj -p:Configuration=Release -verbosity:minimal
```

Build DivineDiurganate:
```bash
cd "C:\Steam\steamapps\common\RimWorld\Mods\DivineDiurganate\Source\DivineDiurganate" && "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" DivineDiurganate.csproj -p:Configuration=Release -verbosity:minimal
```

Build DragonianMix:
```bash
cd "C:\Steam\steamapps\common\RimWorld\Mods\2961683592\Source\dragonianmix-csharp-lib\DragonianMix" && "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" DragonianMix.csproj -p:Configuration=Release -verbosity:minimal
```

## Common Issues

- If MSBuild path doesn't exist, try alternative VS installation paths
- If SDK errors occur with `dotnet build`, use MSBuild directly (these are .NET Framework projects)

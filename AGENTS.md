# AGENTS.md - WulaFallenEmpire RimWorld Mod

## Build Commands

### Primary Build Command
```bash
dotnet build "C:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire\WulaFallenEmpire.csproj"
```

### Clean Build
```bash
dotnet clean "C:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire\WulaFallenEmpire.csproj"
dotnet build "C:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire\WulaFallenEmpire.csproj"
```

### Output Location
- Debug builds: `C:\Steam\steamapps\common\RimWorld\Mods\3516260226\1.6\1.6\Assemblies\`
- Release builds: Same as Debug (optimized)

### Testing
This project does not have automated unit tests. Manual testing is done through RimWorld gameplay.

## Project Structure

```
C:\Steam\steamapps\common\RimWorld\Mods\3516260226\
├── Source\WulaFallenEmpire\      # C# source code
├── 1.6\1.6\                      # RimWorld 1.6 mod files
│   ├── Assemblies\               # Compiled DLL output
│   ├── Defs\                     # XML definitions
│   ├── Languages\                # Translation files
│   ├── Patches\                  # XML patch operations
│   └── Textures\                 # Visual assets
└── LoadFolders.xml               # Mod loading configuration
```

## Code Style Guidelines

### Imports & Formatting
- Group RimWorld imports: `Verse`, `RimWorld`, `Verse.Sound`, `UnityEngine`
- Group mod imports after RimWorld imports
- 4-space indentation, curly braces on new lines
- Use `var` when type is obvious, explicit types when clarity matters
- C# 11.0, .NET Framework 4.8

### Naming Conventions
- Classes/Methods/Properties: PascalCase (e.g., `WulaFallenEmpireMod`, `TryCastShot`)
- Fields: camelCase (e.g., `explosionShotCounter`), private: `_scrollPosition`
- Harmony patches: `Patch_` prefix (e.g., `Patch_CaravanFormingUtility_AllSendablePawns`)

### Harmony Patches
```csharp
[HarmonyPatch(typeof(RimWorld.Planet.CaravanFormingUtility), "AllSendablePawns")]
public static class Patch_CaravanFormingUtility_AllSendablePawns
{
    [HarmonyPostfix]
    public static void Postfix(Map map, ref List<Pawn> __result)
    {
        WulaLog.Debug("[WULA] Patch executed");
    }
}
```

### DefOf Pattern
```csharp
[DefOf]
public static class ThingDefOf_WULA
{
    public static ThingDef WULA_MaintenancePod;
    static ThingDefOf_WULA() => DefOfHelper.EnsureInitializedInCtor(typeof(ThingDefOf_WULA));
}
```

### Mod Initialization
```csharp
[StaticConstructorOnStartup]
public class WulaFallenEmpireMod : Mod
{
    public WulaFallenEmpireMod(ModContentPack content) : base(content)
    {
        new Harmony("tourswen.wulafallenempire").PatchAll(Assembly.GetExecutingAssembly());
    }
}
```

### Error Handling
Check null before access, use `WulaLog.Debug()` for logging (controlled by mod setting).

### Signing Convention
沐雪写的代码会加上可爱的署名注释，例如：
```csharp
// ✨ 沐雪写的哦~
```

## Important Rules

### Knowledge Base Usage
When working on RimWorld modding, ALWAYS use the `rimworld-knowledge-base` tool to:
- Search for correct class names, method signatures, and enum values
- Verify game mechanics and API usage
- Access decompiled RimWorld 1.6 source code
- **Do not rely on external memory or searches**

### Critical Paths
- Local C# Knowledge Base: `C:\Steam\steamapps\common\RimWorld\dll1.6`
- Mod Project: `C:\Steam\steamapps\common\RimWorld\Mods\3516260226`
- C# Project: `C:\Steam\steamapps\common\RimWorld\Mods\3516260226\Source\WulaFallenEmpire`

### Project File Sync
When renaming, moving, or deleting C# files, **MUST** update `.csproj` file's `<Compile Include="..." />` entries.

### Dependencies
- RimWorld Assembly-CSharp
- UnityEngine modules (Core, IMGUIModule, etc.)
- Harmony (0Harmony.dll)
- AlienRace (AlienRace.dll)

## Additional Notes

### Logging
Use `WulaLog.Debug(string message)` for all debug output. Controlled by mod setting `enableDebugLogs`. Independent of DevMode.

### Serialization
Use `Scribe_Values.Look()` for primitive types, `Scribe_Collections.Look()` for collections in `ExposeData()` methods.

### Comments
- Use Chinese comments for Chinese-language code
- Use English comments for general API documentation
- XML documentation (`///`) for public APIs

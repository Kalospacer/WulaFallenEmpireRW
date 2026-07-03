using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 屋顶铺设数据：将一种屋顶铺满若干矩形区域。
    /// 原版 PrefabDef 不支持屋顶，故由 GenStep_WulaSiteLayout 处理。
    /// 矩形格式与 PrefabDef 一致：(minX, minZ, maxX, maxZ)，绝对地图坐标。
    /// </summary>
    public class WulaRoofData
    {
        public RoofDef roof;
        public List<CellRect> rects = new List<CellRect>();
    }

    /// <summary>
    /// 单个 Pawn 布点：一个坐标上挂若干生成组，每组独立随机。
    /// </summary>
    public class WulaPawnSpawnEntry
    {
        // 生成位置（绝对地图坐标），实际落点在该点附近的可站立格
        public IntVec3 position;

        public List<WulaPawnSpawnGroup> groups = new List<WulaPawnSpawnGroup>();
    }

    /// <summary>
    /// 生成组：从 options 中随机选取一个配置生成。
    /// 只有一个选项即为固定配置；多个选项对应 CQF 的 PawnSpawnData_Random。
    /// </summary>
    public class WulaPawnSpawnGroup
    {
        public List<WulaPawnSpawnOption> options = new List<WulaPawnSpawnOption>();
    }

    /// <summary>
    /// 一种 Pawn 生成配置。
    /// </summary>
    public class WulaPawnSpawnOption
    {
        public PawnKindDef kind;

        // 为空时使用 Site 的派系
        public FactionDef faction;

        public IntRange count = IntRange.One;

        // 同名组的 Pawn 共用一个守点 Lord；为空时归入 "default" 组
        public string lordGroup;

        // 任务信号标记：非空时给生成的 Pawn 打上 quest tag（沿用生成 Site 的任务），
        // 任务脚本即可监听 <questTag>.<序号>.Destroyed 等信号（如 PsiTitan.0.Destroyed）
        public string questTag;

        // 生成后塞入物品栏（用于 Boss 死亡掉落等场景）。
        // 常规掉落优先配置在 PawnKindDef.fixedInventory 上，仅同一 PawnKind
        // 需要区分掉落场景时才使用本字段。
        public List<ThingDefCountClass> inventory;
    }

    /// <summary>
    /// 容器内容物：地图生成后向指定 def 的所有 Building_Casket 类容器填充物品。
    /// 用于任务保险箱等"开启后掉落"的交互（原版 Open 指令 + EjectContents）。
    /// </summary>
    public class WulaCrateContentData
    {
        public ThingDef crate;

        public List<ThingDefCountClass> contents = new List<ThingDefCountClass>();
    }
}

using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// Boss 战场地 SitePartDef：在原版 SitePartDef 之上声明场地的精确布局。
    /// 建筑、物品、地形交给原版 PrefabDef（由 PrefabUtility.SpawnPrefab 铺设）；
    /// 屋顶与 Pawn 为 PrefabDef 不支持的部分，在此声明、由 GenStep_WulaSiteLayout 生成。
    /// 地图尺寸沿用原版字段 minMapSize，图标沿用 siteTexture/expandingIconTexture。
    /// </summary>
    public class WulaBossSitePartDef : SitePartDef
    {
        // 场地布局（原版 PrefabDef），内部坐标即绝对地图坐标（root 对齐地图原点）
        public PrefabDef prefab;

        // 屋顶区域
        public List<WulaRoofData> roofs = new List<WulaRoofData>();

        // Pawn 布点
        public List<WulaPawnSpawnEntry> pawnEntries = new List<WulaPawnSpawnEntry>();

        // 生成后需要填充内容物的容器（Building_Casket 类）
        public List<WulaCrateContentData> crateContents = new List<WulaCrateContentData>();

        // 守点 Lord 的巡逻/警戒半径，为空时用 LordJob_DefendPoint 默认值
        public float? defendWanderRadius;
        public float? defendRadius;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (prefab == null && pawnEntries.Count == 0)
            {
                yield return $"{defName}: 未配置 prefab 且 pawnEntries 为空，该 SitePart 不会生成任何内容";
            }
            for (int i = 0; i < pawnEntries.Count; i++)
            {
                WulaPawnSpawnEntry entry = pawnEntries[i];
                if (entry.groups.NullOrEmpty())
                {
                    yield return $"{defName}: pawnEntries[{i}] ({entry.position}) 没有任何生成组";
                    continue;
                }
                foreach (WulaPawnSpawnGroup group in entry.groups)
                {
                    if (group.options.NullOrEmpty())
                    {
                        yield return $"{defName}: pawnEntries[{i}] ({entry.position}) 存在空的生成组";
                    }
                    else
                    {
                        foreach (WulaPawnSpawnOption option in group.options)
                        {
                            if (option.kind == null)
                            {
                                yield return $"{defName}: pawnEntries[{i}] ({entry.position}) 存在未配置 kind 的选项";
                            }
                        }
                    }
                }
            }
            foreach (WulaCrateContentData crateData in crateContents)
            {
                if (crateData.crate == null)
                {
                    yield return $"{defName}: crateContents 存在未配置 crate 的条目";
                }
                else if (!typeof(Building_Casket).IsAssignableFrom(crateData.crate.thingClass))
                {
                    yield return $"{defName}: crateContents 的 {crateData.crate.defName} 不是 Building_Casket 类容器，无法填充内容物";
                }
            }
        }
    }
}

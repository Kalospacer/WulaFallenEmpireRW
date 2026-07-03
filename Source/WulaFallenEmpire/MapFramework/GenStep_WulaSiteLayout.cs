using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace WulaFallenEmpire
{
    /// <summary>
    /// Boss 场地布局生成：读取 Site 上的 WulaBossSitePartDef，依次铺设 PrefabDef、
    /// 屋顶，按坐标生成 Pawn 并按 lordGroup 组建守点 Lord，最后填充任务容器。
    /// 挂载于 WULA_BossArena 地图生成器；地图不含该 SitePart 时不做任何事。
    /// </summary>
    public class GenStep_WulaSiteLayout : GenStep
    {
        public override int SeedPart => 741309255;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (!(map.Parent is Site site))
            {
                return;
            }
            foreach (SitePart part in site.parts)
            {
                if (part.def is WulaBossSitePartDef layoutDef)
                {
                    GenerateLayout(map, site, layoutDef);
                }
            }
        }

        private void GenerateLayout(Map map, Site site, WulaBossSitePartDef def)
        {
            Faction siteFaction = site.Faction;

            // 1. 铺设 prefab：pos 取占据矩形中心，使 root 落在地图原点，
            //    从而 prefab 内部坐标与绝对地图坐标一致（迁移数据无需平移）
            if (def.prefab != null)
            {
                IntVec3 pos = new IntVec3((def.prefab.size.x - 1) / 2, 0, (def.prefab.size.z - 1) / 2);
                PrefabUtility.SpawnPrefab(def.prefab, map, pos, Rot4.North, siteFaction);
                WulaLog.Debug($"[WulaSiteLayout] {def.defName}: 已铺设 prefab {def.prefab.defName} ({def.prefab.size})");
            }

            // 2. 屋顶
            foreach (WulaRoofData roofData in def.roofs)
            {
                if (roofData.roof == null)
                {
                    continue;
                }
                foreach (CellRect rect in roofData.rects)
                {
                    foreach (IntVec3 cell in rect.ClipInsideMap(map))
                    {
                        map.roofGrid.SetRoof(cell, roofData.roof);
                    }
                }
            }

            // 3. Pawn：每组独立随机选一个配置，按 lordGroup 归组
            Dictionary<string, List<Pawn>> lordGroups = new Dictionary<string, List<Pawn>>();
            Dictionary<string, IntVec3> lordPoints = new Dictionary<string, IntVec3>();
            Dictionary<string, int> questTagIndices = new Dictionary<string, int>();
            string questTagPrefix = GetQuestTagPrefix(site);
            int pawnCount = 0;
            foreach (WulaPawnSpawnEntry entry in def.pawnEntries)
            {
                if (!entry.position.InBounds(map))
                {
                    Log.Error($"[WulaSiteLayout] {def.defName}: pawn 布点 {entry.position} 超出地图范围，已跳过");
                    continue;
                }
                foreach (WulaPawnSpawnGroup group in entry.groups)
                {
                    if (group.options.NullOrEmpty())
                    {
                        continue;
                    }
                    WulaPawnSpawnOption option = group.options.RandomElement();
                    if (option.kind == null)
                    {
                        continue;
                    }
                    Faction pawnFaction = ResolveFaction(option.faction, siteFaction, def);
                    int spawnCount = option.count.RandomInRange;
                    for (int i = 0; i < spawnCount; i++)
                    {
                        Pawn pawn = PawnGenerator.GeneratePawn(option.kind, pawnFaction);
                        IntVec3 cell = CellFinder.RandomClosewalkCellNear(entry.position, map, 4);
                        GenSpawn.Spawn(pawn, cell.IsValid ? cell : entry.position, map);
                        FillInventory(pawn, option.inventory);
                        TryAddQuestTag(pawn, option.questTag, questTagPrefix, questTagIndices);
                        pawnCount++;

                        string groupName = option.lordGroup.NullOrEmpty() ? "default" : option.lordGroup;
                        if (!lordGroups.TryGetValue(groupName, out List<Pawn> members))
                        {
                            members = new List<Pawn>();
                            lordGroups.Add(groupName, members);
                            lordPoints.Add(groupName, entry.position);
                        }
                        members.Add(pawn);
                    }
                }
            }

            // 4. 每个 lordGroup 一个守点 Lord，守点取组内首个布点
            foreach (KeyValuePair<string, List<Pawn>> pair in lordGroups)
            {
                List<Pawn> members = pair.Value;
                Faction lordFaction = members[0].Faction;
                if (lordFaction == null)
                {
                    Log.Warning($"[WulaSiteLayout] {def.defName}: lord 组 {pair.Key} 无派系，未创建 Lord");
                    continue;
                }
                LordJob_DefendPoint lordJob = new LordJob_DefendPoint(lordPoints[pair.Key], def.defendWanderRadius, def.defendRadius);
                LordMaker.MakeNewLord(lordFaction, lordJob, map, members);
            }
            WulaLog.Debug($"[WulaSiteLayout] {def.defName}: 生成 {pawnCount} 个 Pawn，{lordGroups.Count} 个守点 Lord");

            // 5. 填充任务容器
            foreach (WulaCrateContentData crateData in def.crateContents)
            {
                if (crateData.crate == null)
                {
                    continue;
                }
                foreach (Thing thing in map.listerThings.ThingsOfDef(crateData.crate))
                {
                    if (!(thing is Building_Casket casket))
                    {
                        continue;
                    }
                    foreach (ThingDefCountClass content in crateData.contents)
                    {
                        Thing item = ThingMaker.MakeThing(content.thingDef, content.stuff);
                        item.stackCount = content.count;
                        if (!casket.TryAcceptThing(item, allowSpecialEffects: false))
                        {
                            Log.Error($"[WulaSiteLayout] {def.defName}: 无法将 {content.thingDef.defName} 放入 {crateData.crate.defName}");
                        }
                    }
                }
            }
        }

        // 解析 Pawn 派系：未配置时回退到 Site 派系
        private Faction ResolveFaction(FactionDef factionDef, Faction fallback, WulaBossSitePartDef def)
        {
            if (factionDef == null)
            {
                return fallback;
            }
            Faction faction = Find.FactionManager.FirstFactionOfDef(factionDef);
            if (faction == null)
            {
                Log.Warning($"[WulaSiteLayout] {def.defName}: 世界中不存在派系 {factionDef.defName}，回退到 Site 派系");
                return fallback;
            }
            return faction;
        }

        // 从 Site 的 quest tag（形如 "quest12.site"）提取任务前缀 "quest12."，
        // 供地图生成期出生的 Pawn 挂接到同一个任务的信号体系
        private string GetQuestTagPrefix(Site site)
        {
            if (site.questTags.NullOrEmpty())
            {
                return null;
            }
            foreach (string tag in site.questTags)
            {
                int dot = tag.IndexOf('.');
                if (dot > 0)
                {
                    return tag.Substring(0, dot + 1);
                }
            }
            return null;
        }

        // 给 Pawn 打任务标记：同名 questTag 依生成顺序编号（PsiTitan.0、PsiTitan.1 ...），
        // 任务脚本按 <questTag>.<序号>.Destroyed 监听
        private void TryAddQuestTag(Pawn pawn, string questTag, string prefix, Dictionary<string, int> indices)
        {
            if (questTag.NullOrEmpty() || prefix == null)
            {
                return;
            }
            indices.TryGetValue(questTag, out int index);
            indices[questTag] = index + 1;
            QuestUtility.AddQuestTag(pawn, $"{prefix}{questTag}.{index}");
        }

        private void FillInventory(Pawn pawn, List<ThingDefCountClass> inventory)
        {
            if (inventory.NullOrEmpty())
            {
                return;
            }
            foreach (ThingDefCountClass entry in inventory)
            {
                Thing thing = ThingMaker.MakeThing(entry.thingDef, entry.stuff);
                thing.stackCount = entry.count;
                pawn.inventory.innerContainer.TryAdd(thing);
            }
        }
    }
}

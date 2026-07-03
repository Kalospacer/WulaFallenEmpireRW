using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace WulaFallenEmpire
{
    public class GenStep_SiteLayout : GenStep
    {
        private const string LogPrefix = "[SiteLayoutFramework]";

        public override int SeedPart => 741309255;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (!(map.Parent is Site site))
            {
                return;
            }
            foreach (SitePart part in site.parts)
            {
                if (part.def is LayoutSitePartDef layoutDef)
                {
                    GenerateLayout(map, site, layoutDef);
                }
            }
        }

        private void GenerateLayout(Map map, Site site, LayoutSitePartDef def)
        {
            Faction siteFaction = site.Faction;

            if (def.prefab != null)
            {
                PrefabUtility.SpawnPrefab(def.prefab, map, GetPrefabSpawnPosition(map, def), def.prefabRotation, siteFaction);
                Log.Message($"{LogPrefix} {def.defName}: spawned prefab {def.prefab.defName} ({def.prefab.size}).");
            }

            SpawnRoofs(map, def);
            SpawnPawns(map, site, def, siteFaction);
            FillContainers(map, def);
        }

        private static IntVec3 GetPrefabSpawnPosition(Map map, LayoutSitePartDef def)
        {
            IntVec3 basePosition = def.useAbsoluteCoordinates
                ? new IntVec3((def.prefab.size.x - 1) / 2, 0, (def.prefab.size.z - 1) / 2)
                : map.Center;

            return basePosition + def.prefabOffset;
        }

        private static void SpawnRoofs(Map map, LayoutSitePartDef def)
        {
            foreach (LayoutRoofData roofData in def.roofs)
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
        }

        private void SpawnPawns(Map map, Site site, LayoutSitePartDef def, Faction siteFaction)
        {
            Dictionary<string, List<Pawn>> lordGroups = new Dictionary<string, List<Pawn>>();
            Dictionary<string, IntVec3> lordPoints = new Dictionary<string, IntVec3>();
            Dictionary<string, int> questTagIndices = new Dictionary<string, int>();
            string questTagPrefix = GetQuestTagPrefix(site, def);
            int pawnCount = 0;

            foreach (LayoutPawnSpawnEntry entry in def.pawnEntries)
            {
                if (!entry.position.InBounds(map))
                {
                    Log.Warning($"{LogPrefix} {def.defName}: pawn spawn position {entry.position} is out of bounds; skipped.");
                    continue;
                }

                foreach (LayoutPawnSpawnGroup group in entry.groups)
                {
                    if (group.options.NullOrEmpty())
                    {
                        continue;
                    }

                    LayoutPawnSpawnOption option = group.options.RandomElement();
                    if (option.kind == null)
                    {
                        continue;
                    }

                    Faction pawnFaction = ResolveFaction(option.faction, siteFaction, def);
                    int spawnCount = option.count.RandomInRange;
                    for (int i = 0; i < spawnCount; i++)
                    {
                        if (!TryFindSpawnCell(entry.position, map, entry.spawnRadius, out IntVec3 cell))
                        {
                            Log.Warning($"{LogPrefix} {def.defName}: no standable cell near {entry.position} within radius {entry.spawnRadius}; skipped pawn {option.kind.defName}.");
                            continue;
                        }

                        Pawn pawn = PawnGenerator.GeneratePawn(option.kind, pawnFaction);
                        GenSpawn.Spawn(pawn, cell, map);
                        FillInventory(pawn, option.inventory, def);
                        TryAddQuestTag(pawn, option.questTag, questTagPrefix, questTagIndices, def);
                        pawnCount++;

                        if (option.lordJob.NullOrEmpty() || option.lordJob == "DefendPoint")
                        {
                            AddToLordGroup(pawn, option, entry.position, lordGroups, lordPoints);
                        }
                    }
                }
            }

            foreach (KeyValuePair<string, List<Pawn>> pair in lordGroups)
            {
                List<Pawn> members = pair.Value;
                Faction lordFaction = members[0].Faction;
                if (lordFaction == null)
                {
                    Log.Warning($"{LogPrefix} {def.defName}: lord group {pair.Key} has no faction; skipped.");
                    continue;
                }
                LordMaker.MakeNewLord(lordFaction, new LordJob_DefendPoint(lordPoints[pair.Key]), map, members);
            }

            Log.Message($"{LogPrefix} {def.defName}: spawned {pawnCount} pawns and {lordGroups.Count} defend-point lord groups.");
        }

        private static void AddToLordGroup(
            Pawn pawn,
            LayoutPawnSpawnOption option,
            IntVec3 point,
            Dictionary<string, List<Pawn>> lordGroups,
            Dictionary<string, IntVec3> lordPoints)
        {
            string groupName = option.lordGroup.NullOrEmpty() ? "default" : option.lordGroup;
            if (!lordGroups.TryGetValue(groupName, out List<Pawn> members))
            {
                members = new List<Pawn>();
                lordGroups.Add(groupName, members);
                lordPoints.Add(groupName, point);
            }
            members.Add(pawn);
        }

        private static bool TryFindSpawnCell(IntVec3 center, Map map, int radius, out IntVec3 cell)
        {
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(center, radius, true).InRandomOrder())
            {
                if (candidate.InBounds(map) && candidate.Standable(map))
                {
                    cell = candidate;
                    return true;
                }
            }

            cell = IntVec3.Invalid;
            return false;
        }

        private void FillContainers(Map map, LayoutSitePartDef def)
        {
            foreach (LayoutContainerContentData contentData in def.containerContents)
            {
                if (contentData.thingDef == null)
                {
                    continue;
                }

                foreach (Thing thing in map.listerThings.ThingsOfDef(contentData.thingDef))
                {
                    if (!(thing is Building_Casket casket) || !MatchesRange(thing, contentData))
                    {
                        continue;
                    }

                    FillContainer(casket, contentData, def);
                    if (!contentData.fillAllMatching)
                    {
                        break;
                    }
                }
            }
        }

        private static bool MatchesRange(Thing thing, LayoutContainerContentData contentData)
        {
            if (contentData.position == IntVec3.Invalid || contentData.radius < 0)
            {
                return true;
            }

            int dx = thing.Position.x - contentData.position.x;
            int dz = thing.Position.z - contentData.position.z;
            return dx * dx + dz * dz <= contentData.radius * contentData.radius;
        }

        private static void FillContainer(Building_Casket casket, LayoutContainerContentData contentData, LayoutSitePartDef def)
        {
            foreach (ThingDefCountClass content in contentData.contents)
            {
                if (content.thingDef == null)
                {
                    continue;
                }

                Thing item = ThingMaker.MakeThing(content.thingDef, content.stuff);
                item.stackCount = content.count;
                if (!casket.TryAcceptThing(item, allowSpecialEffects: false))
                {
                    Log.Error($"{LogPrefix} {def.defName}: failed to place {content.thingDef.defName} in {contentData.thingDef.defName}.");
                }
            }
        }

        private static Faction ResolveFaction(FactionDef factionDef, Faction fallback, LayoutSitePartDef def)
        {
            if (factionDef == null)
            {
                return fallback;
            }

            Faction faction = Find.FactionManager.FirstFactionOfDef(factionDef);
            if (faction == null)
            {
                Log.Warning($"{LogPrefix} {def.defName}: no faction of def {factionDef.defName}; using site faction.");
                return fallback;
            }
            return faction;
        }

        private static string GetQuestTagPrefix(Site site, LayoutSitePartDef def)
        {
            if (def.questTagPrefixMode == QuestTagPrefixMode.None)
            {
                return null;
            }

            if (site.questTags.NullOrEmpty())
            {
                Log.Warning($"{LogPrefix} {def.defName}: site has no quest tags; pawn quest tags will not be added.");
                return null;
            }

            foreach (string tag in site.questTags)
            {
                if (tag.EndsWith(".site"))
                {
                    int dot = tag.LastIndexOf('.');
                    return tag.Substring(0, dot + 1);
                }
            }

            Log.Warning($"{LogPrefix} {def.defName}: no site quest tag found; pawn quest tags will not be added.");
            return null;
        }

        private static void TryAddQuestTag(Pawn pawn, string questTag, string prefix, Dictionary<string, int> indices, LayoutSitePartDef def)
        {
            if (questTag.NullOrEmpty())
            {
                return;
            }
            if (prefix == null)
            {
                Log.Warning($"{LogPrefix} {def.defName}: no quest tag prefix for {questTag}; tag skipped.");
                return;
            }

            indices.TryGetValue(questTag, out int index);
            indices[questTag] = index + 1;
            QuestUtility.AddQuestTag(pawn, $"{prefix}{questTag}.{index}");
        }

        private static void FillInventory(Pawn pawn, List<ThingDefCountClass> inventory, LayoutSitePartDef def)
        {
            if (inventory.NullOrEmpty())
            {
                return;
            }
            if (pawn.inventory == null)
            {
                Log.Warning($"{LogPrefix} {def.defName}: pawn {pawn.LabelShort} has no inventory tracker; inventory entries skipped.");
                return;
            }

            foreach (ThingDefCountClass entry in inventory)
            {
                if (entry.thingDef == null)
                {
                    continue;
                }

                Thing thing = ThingMaker.MakeThing(entry.thingDef, entry.stuff);
                thing.stackCount = entry.count;
                pawn.inventory.innerContainer.TryAdd(thing);
            }
        }
    }
}

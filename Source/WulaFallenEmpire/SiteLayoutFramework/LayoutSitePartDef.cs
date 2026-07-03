using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class LayoutSitePartDef : SitePartDef
    {
        public PrefabDef prefab;
        public Rot4 prefabRotation = Rot4.North;
        public IntVec3 prefabOffset = IntVec3.Zero;
        public bool useAbsoluteCoordinates = true;
        public List<LayoutRoofData> roofs = new List<LayoutRoofData>();
        public List<LayoutPawnSpawnEntry> pawnEntries = new List<LayoutPawnSpawnEntry>();
        public List<LayoutContainerContentData> containerContents = new List<LayoutContainerContentData>();
        public QuestTagPrefixMode questTagPrefixMode = QuestTagPrefixMode.FromSiteQuestTag;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (prefab == null)
            {
                yield return $"{defName}: prefab is null";
            }
            if (pawnEntries.NullOrEmpty())
            {
                yield return $"{defName}: pawnEntries is empty";
            }

            for (int i = 0; i < pawnEntries.Count; i++)
            {
                LayoutPawnSpawnEntry entry = pawnEntries[i];
                if (entry.groups.NullOrEmpty())
                {
                    yield return $"{defName}: pawnEntries[{i}] has no spawn groups";
                    continue;
                }
                for (int j = 0; j < entry.groups.Count; j++)
                {
                    LayoutPawnSpawnGroup group = entry.groups[j];
                    if (group.options.NullOrEmpty())
                    {
                        yield return $"{defName}: pawnEntries[{i}].groups[{j}] has no options";
                        continue;
                    }
                    for (int k = 0; k < group.options.Count; k++)
                    {
                        LayoutPawnSpawnOption option = group.options[k];
                        if (option.kind == null)
                        {
                            yield return $"{defName}: pawnEntries[{i}].groups[{j}].options[{k}] has no kind";
                        }
                        if (!option.lordJob.NullOrEmpty() && option.lordJob != "DefendPoint")
                        {
                            yield return $"{defName}: pawnEntries[{i}].groups[{j}].options[{k}] uses unsupported lordJob {option.lordJob}";
                        }
                        foreach (string error in ConfigErrorsForThingCounts(option.inventory, $"{defName}: pawnEntries[{i}].groups[{j}].options[{k}].inventory"))
                        {
                            yield return error;
                        }
                    }
                }
            }

            for (int i = 0; i < containerContents.Count; i++)
            {
                LayoutContainerContentData contentData = containerContents[i];
                bool hasPosition = contentData.position != IntVec3.Invalid;
                bool hasRadius = contentData.radius >= 0;

                if (contentData.thingDef == null)
                {
                    yield return $"{defName}: containerContents[{i}] has no thingDef";
                }
                else if (!typeof(Building_Casket).IsAssignableFrom(contentData.thingDef.thingClass))
                {
                    yield return $"{defName}: containerContents[{i}] thingDef {contentData.thingDef.defName} is not a Building_Casket";
                }
                if (hasPosition != hasRadius)
                {
                    yield return $"{defName}: containerContents[{i}] must configure position and radius together";
                }
                if (!contentData.fillAllMatching && !hasPosition)
                {
                    yield return $"{defName}: containerContents[{i}] has fillAllMatching=false without a range selector";
                }
                foreach (string error in ConfigErrorsForThingCounts(contentData.contents, $"{defName}: containerContents[{i}].contents"))
                {
                    yield return error;
                }
            }
        }

        private static IEnumerable<string> ConfigErrorsForThingCounts(List<ThingDefCountClass> entries, string path)
        {
            if (entries == null)
            {
                yield break;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].thingDef == null)
                {
                    yield return $"{path}[{i}] has no thingDef";
                }
            }
        }
    }
}

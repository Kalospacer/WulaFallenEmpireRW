using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace WulaFallenEmpire
{
    public class QuestNode_GeneratePawnWithCustomization : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> storeAs;

        [NoTranslate]
        public SlateRef<string> addToList;

        [NoTranslate]
        public SlateRef<IEnumerable<string>> addToLists;

        public SlateRef<PawnKindDef> kindDef;

        public SlateRef<Faction> faction;

        public SlateRef<bool> forbidAnyTitle;

        public SlateRef<bool> ensureNonNumericName;

        public SlateRef<IEnumerable<TraitDef>> forcedTraits;

        public SlateRef<IEnumerable<TraitDef>> prohibitedTraits;

        public SlateRef<Pawn> extraPawnForExtraRelationChance;

        public SlateRef<float> relationWithExtraPawnChanceFactor;

        public SlateRef<bool?> allowAddictions;

        public SlateRef<float> biocodeWeaponChance;

        public SlateRef<float> biocodeApparelChance;

        public SlateRef<bool> mustBeCapableOfViolence;

        public SlateRef<bool> isChild;

        public SlateRef<bool> allowPregnant;

        public SlateRef<Gender?> fixedGender;

        public SlateRef<bool> giveDependentDrugs;

        // 只保留自定义背景故事功能
        public SlateRef<BackstoryDef> childhoodBackstory;
        public SlateRef<BackstoryDef> adulthoodBackstory;
        public SlateRef<bool> useCustomBackstories;

        private const int MinExpertSkill = 11;

        protected override bool TestRunInt(Slate slate)
        {
            return true;
        }

        protected virtual DevelopmentalStage GetDevelopmentalStage(Slate slate)
        {
            if (!Find.Storyteller.difficulty.ChildrenAllowed || !isChild.GetValue(slate))
            {
                return DevelopmentalStage.Adult;
            }
            return DevelopmentalStage.Child;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            PawnKindDef value = kindDef.GetValue(slate);
            Faction value2 = faction.GetValue(slate);
            bool flag = allowAddictions.GetValue(slate) ?? true;
            bool value3 = allowPregnant.GetValue(slate);
            IEnumerable<TraitDef> value4 = forcedTraits.GetValue(slate);
            IEnumerable<TraitDef> value5 = prohibitedTraits.GetValue(slate);
            float value6 = biocodeWeaponChance.GetValue(slate);
            bool value7 = mustBeCapableOfViolence.GetValue(slate);
            Pawn value8 = extraPawnForExtraRelationChance.GetValue(slate);
            float value9 = relationWithExtraPawnChanceFactor.GetValue(slate);
            Gender? value10 = fixedGender.GetValue(slate);
            float value11 = biocodeApparelChance.GetValue(slate);
            DevelopmentalStage developmentalStage = GetDevelopmentalStage(slate);
            
            // 获取自定义背景故事设置
            BackstoryDef childhoodBackstoryValue = childhoodBackstory.GetValue(slate);
            BackstoryDef adulthoodBackstoryValue = adulthoodBackstory.GetValue(slate);
            bool useCustomBackstoriesValue = useCustomBackstories.GetValue(slate);

            PawnGenerationRequest request = new PawnGenerationRequest(
                value, value2, PawnGenerationContext.NonPlayer, null, 
                forceGenerateNewPawn: false, allowDead: false, allowDowned: false, 
                canGeneratePawnRelations: true, value7, 1f, forceAddFreeWarmLayerIfNeeded: false, 
                allowGay: true, value3, allowFood: true, flag, inhabitant: false, 
                certainlyBeenInCryptosleep: false, forceRedressWorldPawnIfFormerColonist: false, 
                worldPawnFactionDoesntMatter: false, value6, value11, value8, value9, 
                null, null, value4, value5, null, null, null, value10, null, null, null, 
                null, forceNoIdeo: false, forceNoBackstory: false, forbidAnyTitle: false, 
                forceDead: false, null, null, null, null, null, 0f, developmentalStage);
            
            request.BiocodeApparelChance = biocodeApparelChance.GetValue(slate);
            request.ForbidAnyTitle = forbidAnyTitle.GetValue(slate);
            
            Pawn pawn = PawnGenerator.GeneratePawn(request);

            // 确保名字不是数字（如果设置了）
            if (ensureNonNumericName.GetValue(slate) && (pawn.Name == null || pawn.Name.Numerical))
            {
                pawn.Name = PawnBioAndNameGenerator.GeneratePawnName(pawn);
            }

            // 应用自定义背景故事
            if (useCustomBackstoriesValue)
            {
                ApplyCustomBackstories(pawn, childhoodBackstoryValue, adulthoodBackstoryValue);
            }

            if (giveDependentDrugs.GetValue(slate) && ModsConfig.BiotechActive && pawn.genes != null)
            {
                foreach (Gene item in pawn.genes.GenesListForReading)
                {
                    if (item.Active)
                    {
                        Gene_ChemicalDependency dep = item as Gene_ChemicalDependency;
                        if (dep != null && DefDatabase<ThingDef>.AllDefs.Where((ThingDef x) => x.IsDrug && x.GetCompProperties<CompProperties_Drug>().chemical == dep.def.chemical).TryRandomElementByWeight((ThingDef x) => x.generateCommonality, out var result))
                        {
                            Thing thing = ThingMaker.MakeThing(result);
                            thing.stackCount = Rand.Range(1, 3);
                            pawn.inventory.innerContainer.TryAddOrTransfer(thing);
                        }
                    }
                }
            }

            if (storeAs.GetValue(slate) != null)
            {
                QuestGen.slate.Set(storeAs.GetValue(slate), pawn);
            }

            if (addToList.GetValue(slate) != null)
            {
                QuestGenUtility.AddToOrMakeList(QuestGen.slate, addToList.GetValue(slate), pawn);
            }

            if (addToLists.GetValue(slate) != null)
            {
                foreach (string item2 in addToLists.GetValue(slate))
                {
                    QuestGenUtility.AddToOrMakeList(QuestGen.slate, item2, pawn);
                }
            }

            QuestGen.AddToGeneratedPawns(pawn);
            if (!pawn.IsWorldPawn())
            {
                Find.WorldPawns.PassToWorld(pawn);
            }
        }

        /// <summary>
        /// 应用自定义背景故事
        /// </summary>
        private void ApplyCustomBackstories(Pawn pawn, BackstoryDef childhood, BackstoryDef adulthood)
        {
            if (childhood != null)
            {
                pawn.story.Childhood = childhood;
            }

            if (adulthood != null)
            {
                pawn.story.Adulthood = adulthood;
            }

            // 重新计算技能，因为背景故事会影响技能
            if (pawn.skills != null)
            {
                pawn.skills.Notify_SkillDisablesChanged();
            }

            // 重新计算工作类型
            if (pawn.workSettings != null)
            {
                pawn.workSettings.Notify_DisabledWorkTypesChanged();
            }
        }
    }
}

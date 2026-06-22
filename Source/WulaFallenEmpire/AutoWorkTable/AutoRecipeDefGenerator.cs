using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.AutoWorkTable
{
    // 复用物品 ThingDef.recipeMaker 动态生成 WULA_AutoMake_<defName> 自治配方（formingTicks>0 → Bill_Autonomous）。
    // 逻辑对齐原版 RimWorld.RecipeDefGenerator.CreateRecipeDefFromMaker（adjustedCount 恒为 1），关键差异见 CreateAutoRecipe。
    //
    // 由台子 ext DefModExtension_AutoWorkTableRecipes.ResolveReferences 在加载期调用，全程不依赖 Harmony：
    //   · 该时机（ThingDef 解析阶段）晚于 RecipeDef 批量 ResolveReferences，故需手动 r.ResolveReferences() 展开 stuff 过滤器；
    //   · 但早于 ShortHashGiver.GiveAllShortHashes，故 DefGenerator.AddImpliedDef 注册的配方会被自动分配 shortHash；
    //   · ThingDef.AllRecipes 懒加载（运行期首次访问）时库已完整，故配方能正常挂到台子上。
    public static class AutoRecipeDefGenerator
    {
        // 小人“搬料启动”工时（Gathering 阶段）。真实制作耗时由 realWorkAmount 决定，与此分离。
        private const int BootstrapWorkAmount = 100;

        // formingTicks 占位值：仅需 >0 即可让 BillUtility.MakeNewBill 判成 Bill_Autonomous；
        // 进入 Forming 后会被 Building_WulaAutoWorkTable.Notify_StartForming 用真实工作量改写，故具体数值不影响最终耗时。
        private const int FormingTicksPlaceholder = 100;

        // 为某台子声明的物品逐条生成自治配方并注册。
        public static void GenerateAndRegister(List<AutoRecipeEntry> entries, ThingDef workbench)
        {
            if (entries.NullOrEmpty() || workbench == null)
            {
                return;
            }
            foreach (AutoRecipeEntry entry in entries)
            {
                if (entry.targetDef == null)
                {
                    Log.Warning("[WulaFallenEmpire] 自动台 " + workbench.defName + " 的 autoRecipes 有一条 targetDef 为空（defName 拼错或物品不存在？），跳过。");
                    continue;
                }
                if (entry.targetDef.recipeMaker == null)
                {
                    Log.Warning("[WulaFallenEmpire] 自动台 " + workbench.defName + "：" + entry.targetDef.defName + " 没有 recipeMaker，无法复用其制作信息，跳过。");
                    continue;
                }

                string defName = "WULA_AutoMake_" + entry.targetDef.defName;
                RecipeDef existing = DefDatabase<RecipeDef>.GetNamed(defName, errorOnFail: false);
                if (existing != null)
                {
                    // 同物品已被其它自动台（或手写配方）声明：把本台子并入其 recipeUsers，避免重复 def 撞名。
                    if (existing.recipeUsers != null && !existing.recipeUsers.Contains(workbench))
                    {
                        existing.recipeUsers.Add(workbench);
                    }
                    continue;
                }

                RecipeDef r = CreateAutoRecipe(entry.targetDef, entry.realWorkAmount, workbench);
                DefGenerator.AddImpliedDef(r);   // generated=true + PostLoad + 入库；shortHash 由后续 GiveAllShortHashes 统一给
                r.ResolveReferences();           // 手动展开 ingredients / stuff 过滤器（已错过 RecipeDef 批量 resolve）
            }
        }

        // 复刻 RecipeDefGenerator.CreateRecipeDefFromMaker(def, 1)，做自动台所需改造：
        //   · defName 改为 WULA_AutoMake_<defName>，避免与原版 Make_<defName> 撞名
        //   · 设 formingTicks>0 触发 Bill_Autonomous；workAmount 仅留小额搬料工时
        //   · 不复制 unfinishedThingDef（否则 MakeNewBill 会优先判成 Bill_ProductionWithUft）
        //   · recipeUsers 即声明它的台子（而非物品的 recipeMaker.recipeUsers——物品仍可在原版台子手搓）
        //   · 附一份只含 realWorkAmount 的扩展，供 Building_WulaAutoWorkTable.GetRealWorkAmount 读取
        private static RecipeDef CreateAutoRecipe(ThingDef def, int realWorkAmount, ThingDef workbench)
        {
            RecipeMakerProperties recipeMaker = def.recipeMaker;
            RecipeDef r = new RecipeDef();
            r.defName = "WULA_AutoMake_" + def.defName;
            r.label = string.IsNullOrEmpty(recipeMaker.label) ? "RecipeMake".Translate(def.label) : recipeMaker.label;
            r.jobString = "RecipeMakeJobString".Translate(def.label);
            r.modContentPack = workbench.modContentPack;   // 配方归属台子所在 mod（而非物品的，可能是 Core）
            r.displayPriority = recipeMaker.displayPriority;

            // 搬料/制作工时分离：workAmount 只用于小人把料搬进台子启动，真实制作耗时走 realWorkAmount。
            r.workAmount = BootstrapWorkAmount;
            r.formingTicks = FormingTicksPlaceholder;

            r.workSpeedStat = recipeMaker.workSpeedStat;
            r.efficiencyStat = recipeMaker.efficiencyStat;

            // 原料：对 MadeFromStuff 物品会 SetAllowAllWhoCanMake + productHasIngredientStuff=true + fixedIngredientFilter，
            // 对 CostList 加固定原料——与原版手工配方完全一致，玩家可在账单“详情”勾选材质、成品材质取自所投 stuff。
            RecipeDefGenerator.SetIngredients(r, def, 1);
            r.useIngredientsForColor = recipeMaker.useIngredientsForColor;
            if (def.costListForDifficulty != null)
            {
                r.regenerateOnDifficultyChange = true;
            }
            r.defaultIngredientFilter = recipeMaker.defaultIngredientFilter;
            r.products.Add(new ThingDefCountClass(def, recipeMaker.productCount));
            r.targetCountAdjustment = recipeMaker.targetCountAdjustment;
            r.skillRequirements = recipeMaker.skillRequirements.ListFullCopyOrNull();
            r.workSkill = recipeMaker.workSkill;
            r.workSkillLearnFactor = recipeMaker.workSkillLearnPerTick;
            r.requiredGiverWorkType = recipeMaker.requiredGiverWorkType;
            // 刻意不复制 recipeMaker.unfinishedThingDef —— 保持 null，确保被判成 Bill_Autonomous 而非 Bill_ProductionWithUft。
            r.recipeUsers = new List<ThingDef> { workbench };
            r.mechanitorOnlyRecipe = recipeMaker.mechanitorOnlyRecipe;
            r.effectWorking = recipeMaker.effectWorking;
            r.soundWorking = recipeMaker.soundWorking;
            r.researchPrerequisite = recipeMaker.researchPrerequisite;
            r.memePrerequisitesAny = recipeMaker.memePrerequisitesAny;
            r.researchPrerequisites = recipeMaker.researchPrerequisites;
            r.factionPrerequisiteTags = recipeMaker.factionPrerequisiteTags;
            r.fromIdeoBuildingPreceptOnly = recipeMaker.fromIdeoBuildingPreceptOnly;

            string[] items = r.products.Select(p => (p.count != 1) ? p.Label : Find.ActiveLanguageWorker.WithIndefiniteArticle(p.thingDef.label)).ToArray();
            r.description = "RecipeMakeDescription".Translate(items.ToCommaList(useAnd: true));
            r.descriptionHyperlinks = r.products.Select(p => new DefHyperlink(p.thingDef)).ToList();

            // 真实制作耗时：附一份独立扩展到生成的配方，供台子读取。
            r.modExtensions = new List<DefModExtension>
            {
                new DefModExtension_AutoRecipe { realWorkAmount = realWorkAmount }
            };
            return r;
        }
    }
}

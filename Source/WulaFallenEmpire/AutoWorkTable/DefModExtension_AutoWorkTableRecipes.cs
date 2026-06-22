using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire.AutoWorkTable
{
    // 挂在自动台 ThingDef 上：集中声明“本台子能自动制作哪些物品”。
    // 其 ResolveReferences（由 Def.ResolveReferences 自动回调，parentDef = 本台子）会复用各物品的 recipeMaker
    // 动态生成 WULA_AutoMake_<defName> 自治配方并注册，配方的 recipeUsers 即本台子——
    // 无需手写 RecipeDef、无需 PatchOperation，也无需 Harmony。
    //
    //   <li Class="WulaFallenEmpire.AutoWorkTable.DefModExtension_AutoWorkTableRecipes">
    //     <autoRecipes>
    //       <li>
    //         <targetDef>Apparel_Duster</targetDef>
    //         <realWorkAmount>20000</realWorkAmount>
    //       </li>
    //     </autoRecipes>
    //   </li>
    public class DefModExtension_AutoWorkTableRecipes : DefModExtension
    {
        public List<AutoRecipeEntry> autoRecipes;

        public override void ResolveReferences(Def parentDef)
        {
            base.ResolveReferences(parentDef);
            AutoRecipeDefGenerator.GenerateAndRegister(autoRecipes, parentDef as ThingDef);
        }
    }

    // 单条自动制作声明：要制作的物品 + 台子真实制作耗时。等价于一条手写的 WULA_AutoMake_<defName> 配方，
    // 但只需写 defName 引用，制作信息（原料/技能/工时/音效）全部复用物品自己的 recipeMaker。
    public class AutoRecipeEntry
    {
        // 要自动制作的物品（XML 写 defName，加载期解析为引用）。该物品必须有 recipeMaker。
        public ThingDef targetDef;

        // 台子真实制作耗时（tick），与 recipe.workAmount（搬料工时）分离。
        public int realWorkAmount = 1000;
    }
}

using Verse;

namespace WulaFallenEmpire.AutoWorkTable
{
    // 附在 RecipeDef 上，提供台子真实制作耗时（tick），与 recipe.workAmount（小人“搬料启动”工时）分离。
    // 两种配方都用它：手写自动配方在 XML 里直接写；动态生成的配方由 AutoRecipeDefGenerator 附上一份。
    // 读取方：Building_WulaAutoWorkTable.GetRealWorkAmount。
    public sealed class DefModExtension_AutoRecipe : DefModExtension
    {
        public int realWorkAmount = 1000;
    }
}

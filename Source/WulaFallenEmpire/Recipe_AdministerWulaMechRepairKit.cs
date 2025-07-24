using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class Recipe_AdministerWulaMechRepairKit : Recipe_Surgery
    {
        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            // 调用基类的 ApplyOnPawn 方法，处理手术的通用逻辑
            base.ApplyOnPawn(pawn, part, billDoer, ingredients, bill);

            // 查找作为成分的 WULA_MechRepairKit
            Thing mechRepairKit = null;
            foreach (Thing ingredient in ingredients)
            {
                if (ingredient.def.defName == "WULA_MechRepairKit")
                {
                    mechRepairKit = ingredient;
                    break;
                }
            }

            if (mechRepairKit != null)
            {
                // 获取物品上的 CompUseEffect_FixAllHealthConditions 组件
                CompUseEffect_FixAllHealthConditions compUseEffect = mechRepairKit.TryGetComp<CompUseEffect_FixAllHealthConditions>();
                if (compUseEffect != null)
                {
                    // 手动调用 DoEffect 方法，将病人作为 usedBy 传入
                    compUseEffect.DoEffect(pawn);
                }
                else
                {
                    Log.Error($"WULA_MechRepairKit is missing CompUseEffect_FixAllHealthConditions. This should not happen.");
                }

                // 物品将由 CompProperties_UseEffectDestroySelf 销毁，因此此处无需手动销毁。
            }
            else
            {
                Log.Error($"Recipe_AdministerWulaMechRepairKit could not find WULA_MechRepairKit in ingredients for pawn {pawn.LabelShort}.");
            }
        }
    }
}

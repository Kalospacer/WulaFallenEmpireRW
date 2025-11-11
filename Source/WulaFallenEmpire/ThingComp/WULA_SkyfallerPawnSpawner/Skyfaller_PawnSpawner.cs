using Verse;
using RimWorld;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class Skyfaller_PawnSpawner : Skyfaller
    {
        private CompSkyfallerPawnSpawner pawnSpawnerComp;

        public override void PostPostMake()
        {
            base.PostPostMake();
            pawnSpawnerComp = GetComp<CompSkyfallerPawnSpawner>();
        }

        protected override void Impact()
        {
            // 在调用基类 Impact 之前保存位置信息
            IntVec3 impactPosition = base.Position;
            Map impactMap = base.Map;

            // 调用基类 Impact 方法（这会处理爆炸、碎片等效果）
            base.Impact();

            // 生成 Pawn
            if (pawnSpawnerComp != null && impactMap != null)
            {
                pawnSpawnerComp.SpawnPawn(impactMap, impactPosition);
            }
        }

        // 可选：重写 SpawnThings 方法以防止生成原版的内容
        protected override void SpawnThings()
        {
            // 如果我们要生成 Pawn，可能不想生成原版的物品
            // 但保留原版逻辑以防有其他内容需要生成
            base.SpawnThings();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                pawnSpawnerComp = GetComp<CompSkyfallerPawnSpawner>();
            }
        }
    }
}

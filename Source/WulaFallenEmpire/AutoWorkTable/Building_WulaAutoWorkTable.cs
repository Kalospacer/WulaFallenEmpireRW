using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire.AutoWorkTable
{
    // 自动工作台：小人手动搬料入台后，台子自身倒计时制作。
    // 制作完成后直接把成品弹到地上并立即复位账单，无需小人来取件即可开始下一轮。
    // 头顶进度条显示 Forming 倒计时进度（仿机械孵化器）。
    //
    // 工作量分离：recipe.workAmount 仅供小人“搬料启动”用（小值），
    // realWorkAmount 表示机器制作阶段的总工作量。
    // 自动台按“固定手工 10 的虚拟工人速度 * 工作台速度”每 tick 消耗工作量，
    // 再把估算剩余 tick 数回填给 vanilla 的 formingTicks / FinishesIn 显示。
    public class Building_WulaAutoWorkTable : Building_WorkTableAutonomous
    {
        private const int AutoStartCheckInterval = 30;

        private CompPowerTrader powerComp;
        private CompRefuelable refuelComp;
        private Effecter progressBarEffecter;
        private float totalWorkAmount;
        private float remainingWorkAmount;
        private Pawn virtualWorker;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
            refuelComp = GetComp<CompRefuelable>();
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            CleanupProgressBar();
            base.DeSpawn(mode);
        }

        // 断电或没燃料时返回 false，使父类 Tick 暂停 Forming 倒计时。
        public override bool CanWork()
        {
            if (powerComp != null && !powerComp.PowerOn)
            {
                return false;
            }
            if (refuelComp != null && !refuelComp.HasFuel)
            {
                return false;
            }
            return true;
        }

        protected override void Tick()
        {
            bool suppressVanillaFormingTick = activeBill != null && activeBill.State == FormingState.Forming;
            bool wasSuspended = false;
            if (suppressVanillaFormingTick)
            {
                wasSuspended = activeBill.suspended;
                activeBill.suspended = true;
            }

            base.Tick();

            if (suppressVanillaFormingTick && activeBill != null)
            {
                activeBill.suspended = wasSuspended;
                if (!wasSuspended && CanWork())
                {
                    TickFormingWork();
                }
            }
            else if (Spawned && Find.TickManager.TicksGame % AutoStartCheckInterval == 0)
            {
                WulaAutoWorkTableBillUtility.TryAutoStartBillFromInnerContainerAndBeacons(this);
            }

            TickFormingProgressBar();
        }

        // Gathering→Forming 时 vanilla 回调。覆盖 formingTicks 为真实制作工作量。
        public override void Notify_StartForming(Pawn billDoer)
        {
            base.Notify_StartForming(billDoer);
            if (activeBill != null)
            {
                totalWorkAmount = GetRealWorkAmount(activeBill.recipe);
                remainingWorkAmount = totalWorkAmount;
                activeBill.formingTicks = EstimateTicksRemaining(activeBill.recipe, remainingWorkAmount);
            }
        }

        // 取配方真实制作工作量；没配 ModExtension 时回退到 recipe.formingTicks。
        private static int GetRealWorkAmount(RecipeDef recipe)
        {
            DefModExtension_AutoRecipe ext = recipe.GetModExtension<DefModExtension_AutoRecipe>();
            if (ext != null)
            {
                return ext.realWorkAmount;
            }
            return recipe.formingTicks;
        }


        // 选中台子时在左侧信息面板显示制作状态与真实工作量。
        protected override string GetInspectStringExtra()
        {
            if (activeBill == null)
            {
                return null;
            }
            if (activeBill.State == FormingState.Forming)
            {
                // 剩余时间由 vanilla AppendInspectionData 的 FinishesIn 提供，这里不重复。
                return "WULAAutoForming".Translate() + ": " + activeBill.LabelCap
                    + "\n" + "WULAAutoWorkAmount".Translate() + ": " + remainingWorkAmount.ToStringWorkAmount();
            }
            if (activeBill.State == FormingState.Gathering)
            {
                return "WULAAutoWaitingMaterials".Translate();
            }
            return null;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }
            // 正在自动制作时，提供“中止制作”按钮：吐出已投入的原料并复位账单。
            if (activeBill != null && activeBill.State == FormingState.Forming)
            {
                yield return new Command_Action
                {
                    defaultLabel = "WULAAutoAbortForming".Translate(),
                    defaultDesc = "WULAAutoAbortFormingDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                    activateSound = SoundDefOf.Designate_Cancel,
                    hotKey = KeyBindingDefOf.Designator_Cancel,
                    action = delegate
                    {
                        EjectContents();
                        activeBill = null;
                    }
                };
            }
        }

        private void TickFormingProgressBar()
        {
            if (!Spawned || activeBill == null || activeBill.State != FormingState.Forming)
            {
                CleanupProgressBar();
                return;
            }

            float fillPercent = (totalWorkAmount > 0f) ? (1f - remainingWorkAmount / totalWorkAmount) : 0f;
            if (progressBarEffecter == null)
            {
                progressBarEffecter = EffecterDefOf.ProgressBarAlwaysVisible.Spawn();
            }
            progressBarEffecter.EffectTick(this, TargetInfo.Invalid);
            MoteProgressBar mote = (progressBarEffecter.children[0] as SubEffecter_ProgressBar)?.mote;
            if (mote != null)
            {
                mote.progress = Mathf.Clamp01(fillPercent);
                mote.offsetZ = -0.5f;
                mote.alwaysShow = true;
            }
        }

        private void TickFormingWork()
        {
            if (activeBill == null || activeBill.State != FormingState.Forming)
            {
                return;
            }

            float workSpeed = GetVirtualWorkSpeed(activeBill.recipe);
            remainingWorkAmount = Mathf.Max(0f, remainingWorkAmount - workSpeed);
            if (remainingWorkAmount <= 0f)
            {
                activeBill.formingTicks = 0f;
                activeBill.BillTick();
                ResetTrackedWork();
                return;
            }

            activeBill.formingTicks = EstimateTicksRemaining(activeBill.recipe, remainingWorkAmount);
        }

        private float GetVirtualWorkSpeed(RecipeDef recipe)
        {
            DefModExtension_AutoWorkTableWorker worker = GetWorkerSettings();
            float speed = worker.workerBaseSpeed;
            if (recipe.workSpeedStat != null)
            {
                speed *= GetVirtualPawnWorkSpeedStat(recipe, worker);
            }
            if (recipe.workTableSpeedStat != null)
            {
                speed *= this.GetStatValue(recipe.workTableSpeedStat);
            }
            return Mathf.Max(speed, 0.01f);
        }

        private float GetVirtualPawnWorkSpeedStat(RecipeDef recipe, DefModExtension_AutoWorkTableWorker worker)
        {
            StatDef stat = recipe.workSpeedStat;
            if (stat == null)
            {
                return 1f;
            }

            Pawn pawn = GetVirtualWorker(worker);
            return Mathf.Max(pawn.GetStatValue(stat) * worker.workSpeedGlobal, 0.01f);
        }

        private DefModExtension_AutoWorkTableWorker GetWorkerSettings()
        {
            return def.GetModExtension<DefModExtension_AutoWorkTableWorker>() ?? new DefModExtension_AutoWorkTableWorker();
        }

        private Pawn GetVirtualWorker(DefModExtension_AutoWorkTableWorker worker)
        {
            if (virtualWorker == null)
            {
                virtualWorker = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    PawnKindDefOf.Colonist,
                    Faction.OfPlayer,
                    PawnGenerationContext.NonPlayer,
                    forceNoIdeo: true,
                    forceNoGear: true));

                // 技能等级取自 def 常量、运行期不变，只在首次生成时设置一次；
                // 避免 Forming 期间每 tick 重写全部技能并反复脏化 stat 缓存。
                foreach (SkillDef skill in DefDatabase<SkillDef>.AllDefsListForReading)
                {
                    SkillRecord record = virtualWorker.skills.GetSkill(skill);
                    record.Level = worker.skillLevel;
                    record.passion = Passion.None;
                }
            }

            return virtualWorker;
        }

        private float EstimateTicksRemaining(RecipeDef recipe, float workLeft)
        {
            return Mathf.Ceil(workLeft / GetVirtualWorkSpeed(recipe));
        }

        private void CleanupProgressBar()
        {
            progressBarEffecter?.Cleanup();
            progressBarEffecter = null;
        }

        // Forming 结束时由 Bill_Autonomous.BillTick 回调。
        // 重写以跳过 vanilla 的 Formed 等待态：自产成品、弹出地面、立即复位。
        public override void Notify_FormingCompleted()
        {
            if (activeBill == null)
            {
                return;
            }

            Bill_Autonomous bill = activeBill;
            RecipeDef recipe = bill.recipe;

            List<Thing> ingredients = new List<Thing>();
            for (int i = 0; i < innerContainer.Count; i++)
            {
                ingredients.Add(innerContainer[i]);
            }

            Thing dominant = DominantIngredient(recipe, ingredients);
            Pawn worker = GetVirtualWorker(GetWorkerSettings());
            List<Thing> products = GenRecipe.MakeRecipeProducts(recipe, worker, ingredients, dominant, this, bill.precept).ToList();

            // 消耗已搬入的原料
            innerContainer.ClearAndDestroyContents();

            // 成品弹到交互格附近的地面
            IntVec3 dropCell = (base.InteractionCell.IsValid && base.InteractionCell.InBounds(base.Map))
                ? base.InteractionCell
                : base.Position;
            foreach (Thing product in products)
            {
                GenPlace.TryPlaceThing(product, dropCell, base.Map, ThingPlaceMode.Near);
            }

            // 立即复位账单：state 回到 Gathering、ActiveBill 置空，下一轮可直接开始
            bill.Notify_IterationCompleted(null, ingredients);
            ResetTrackedWork();
        }

        public override void EjectContents()
        {
            ResetTrackedWork();
            base.EjectContents();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref totalWorkAmount, "totalWorkAmount", 0f);
            Scribe_Values.Look(ref remainingWorkAmount, "remainingWorkAmount", 0f);
        }

        private void ResetTrackedWork()
        {
            totalWorkAmount = 0f;
            remainingWorkAmount = 0f;
            CleanupProgressBar();
        }

        // 选主要原料：决定 stuff 成品的材质。
        // 玩家在账单 UI 里勾选的 stuff 限制写在 bill.ingredientFilter，选料阶段
        // （TryFindBestBillIngredientsInSet）已据此过滤——非固定 stuff 原料只会放进
        // 玩家允许的料。所以这里的 ingredient 集合已是玩家选定的 stuff，不会“随便挑一个”
        // 覆盖玩家意愿。逻辑对齐 vanilla CalculateDominantIngredient：产物由 stuff 决定时
        // 取 stuff 原料，否则取数量最多的（vanilla 是加权随机，自动台取确定性最大值）。
        private static Thing DominantIngredient(RecipeDef recipe, List<Thing> ingredients)
        {
            if (ingredients.NullOrEmpty())
            {
                return null;
            }

            bool productUsesStuff = recipe.productHasIngredientStuff
                || (recipe.products != null && recipe.products.Any(p => p.thingDef.MadeFromStuff));
            if (productUsesStuff)
            {
                for (int i = 0; i < ingredients.Count; i++)
                {
                    if (ingredients[i].def.IsStuff)
                    {
                        return ingredients[i];
                    }
                }
            }

            Thing best = null;
            int bestCount = -1;
            for (int i = 0; i < ingredients.Count; i++)
            {
                if (ingredients[i].stackCount > bestCount)
                {
                    bestCount = ingredients[i].stackCount;
                    best = ingredients[i];
                }
            }
            return best;
        }

    }
}

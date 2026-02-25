// Jobs/JobDriver_ForceEjectPilot.cs
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using System.Linq;

namespace WulaFallenEmpire
{
    public class JobDriver_ForceEjectPilot : JobDriver
    {
        private const int WorkDurationTicks = 600; // 10秒（60帧/秒）

        // 目标机甲
        private Pawn TargetMech => job.targetA.Thing as Pawn;

        // 工作进度属性
        private float WorkProgress
        {
            get
            {
                if (TargetMech == null) return 0f;
                return (float)ticksLeftThisToil / WorkDurationTicks;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 为殖民者预留机甲的位置
            return pawn.Reserve(TargetMech, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 目标验证
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !CanForceEject(TargetMech));

            // Toil 1：移动到机甲位置
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .FailOnDespawnedOrNull(TargetIndex.A);

            // Toil 2：执行强制拉出工作
            var workToil = new Toil();
            workToil.initAction = () =>
            {
                pawn.rotationTracker.FaceCell(TargetMech.Position);
                pawn.jobs.posture = PawnPosture.Standing;
            };
            workToil.tickAction = () =>
            {
                // 每帧工作进度
                pawn.skills?.Learn(SkillDefOf.Melee, 0.1f);
                
                // 显示工作条
                if (pawn.IsColonistPlayerControlled)
                {
                    TargetMech.Map.overlayDrawer.DrawOverlay(TargetMech, OverlayTypes.QuestionMark);
                }
            };
            workToil.defaultCompleteMode = ToilCompleteMode.Delay;
            workToil.WithEffect(EffecterDefOf.MechRepairing, TargetIndex.A);
            workToil.defaultDuration = WorkDurationTicks;
            workToil.WithProgressBar(TargetIndex.A, () => 1f - WorkProgress);
            workToil.handlingFacing = true;
            workToil.activeSkill = () => SkillDefOf.Construction;
            yield return workToil;

            // Toil 3：完成工作
            yield return new Toil
            {
                initAction = () =>
                {
                    CompleteForceEject();
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        // 检查是否可以强制拉出
        private bool CanForceEject(Pawn mech)
        {
            if (mech == null || mech.Dead)
                return false;

            // 必须是机甲
            if (!(mech is Wulamechunit))
                return false;

            // 必须是玩家派系的目标
            if (mech.Faction == Faction.OfPlayer)
                return false;

            // 必须失去行动能力但未死亡
            if (!mech.Downed)
                return false;

            // 必须有驾驶员
            var pilotComp = mech.TryGetComp<CompMechPilotHolder>();
            if (pilotComp == null || !pilotComp.HasPilots)
                return false;

            // 殖民者必须能够接触机甲
            if (!pawn.CanReach(mech, PathEndMode.Touch, Danger.Some))
                return false;

            // 殖民者不能是囚犯或已失去行动能力
            if (pawn.Downed || pawn.Dead || pawn.IsPrisoner)
                return false;

            return true;
        }

        // 完成强制拉出
        private void CompleteForceEject()
        {
            var mech = TargetMech;
            if (mech == null) return;

            var pilotComp = mech.TryGetComp<CompMechPilotHolder>();
            if (pilotComp == null) return;

            try
            {
                // 1. 弹出所有驾驶员
                var ejectedPilots = pilotComp.GetPilots().ToList();
                pilotComp.RemoveAllPilots();

                // 2. 转换派系为玩家
                mech.SetFaction(Faction.OfPlayer);

                // 4. 发送消息
                SendCompletionMessages(mech, ejectedPilots);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[WULA] Error in ForceEjectPilot: {ex}");
            }
        }

        // 发送完成消息
        private void SendCompletionMessages(Pawn mech, List<Pawn> ejectedPilots)
        {
            string message;
            
            if (ejectedPilots.Count > 0)
            {
                message = "WULA_ForceEjectComplete_WithPilots".Translate(
                    pawn.LabelShortCap,
                    mech.LabelShortCap,
                    ejectedPilots.Count
                );
            }
            else
            {
                message = "WULA_ForceEjectComplete".Translate(
                    pawn.LabelShortCap,
                    mech.LabelShortCap
                );
            }

            Messages.Message(message, MessageTypeDefOf.PositiveEvent);
            
            // 如果弹出的是敌对派系驾驶员，添加额外消息
            foreach (var pilot in ejectedPilots)
            {
                if (pilot.Faction != null && pilot.Faction.HostileTo(Faction.OfPlayer))
                {
                    Messages.Message("WULA_HostilePilotEjected".Translate(
                        pilot.LabelShortCap,
                        pilot.Faction.Name
                    ), MessageTypeDefOf.NeutralEvent);
                }
            }
        }
    }
}

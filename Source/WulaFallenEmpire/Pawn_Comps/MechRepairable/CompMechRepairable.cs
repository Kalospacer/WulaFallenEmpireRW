// CompMechRepairable.cs
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using System.Linq;

namespace WulaFallenEmpire
{
    public class CompProperties_MechRepairable : CompProperties
    {
        public float healthPercentThreshold = 1f; // 低于此值需要维修
        public bool allowAutoRepair = true; // 是否允许自动维修
        public SoundDef repairSound = null; // 维修音效
        public EffecterDef repairEffect = null; // 维修特效
        public float repairAmountPerCycle = 1f; // 每个修复周期修复的HP量
        public int ticksPerRepairCycle = 120; // 每个修复周期的ticks数
        
        public CompProperties_MechRepairable()
        {
            this.compClass = typeof(CompMechRepairable);
        }
    }
    
    public class CompMechRepairable : ThingComp
    {
        public CompProperties_MechRepairable Props => (CompProperties_MechRepairable)props;
        
        private Pawn mech => parent as Pawn;
        private float totalRepairedHP = 0f; // 累计修复的HP量
        
        // 是否需要维修
        public bool NeedsRepair
        {
            get
            {
                if (mech == null || mech.health == null || mech.Dead)
                    return false;
                    
                return mech.health.summaryHealth.SummaryHealthPercent < Props.healthPercentThreshold;
            }
        }
        
        // 是否可以自动维修（无驾驶员时）
        public bool CanAutoRepair
        {
            get
            {
                if (!Props.allowAutoRepair || !NeedsRepair || mech.Faction != Faction.OfPlayer)
                    return false;
                    
                // 检查是否有驾驶员
                var pilotComp = parent.TryGetComp<CompMechPilotHolder>();
                return pilotComp == null || !pilotComp.HasPilots;
            }
        }
        
        // 检查是否可以手动强制维修（有驾驶员时）
        public bool CanForceRepair
        {
            get
            {
                if (!NeedsRepair || mech.Faction != Faction.OfPlayer)
                    return false;
                else
                    return true;
            }
        }
        
        // 记录修复量
        public void RecordRepair(float amount)
        {
            totalRepairedHP += amount;
        }
        
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer)
                yield break;
                
            // 强制维修按钮（仅在机甲有驾驶员且需要维修时显示）
            if (CanForceRepair)
            {
                Command_Action repairCommand = new Command_Action
                {
                    defaultLabel = "WULA_ForceRepair".Translate(),
                    defaultDesc = "WULA_ForceRepairDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("WulaFallenEmpire/UI/Commands/WULA_Repair_Mech"),
                    action = () => ForceRepairNow()
                };
                
                // 检查是否可以立即维修
                if (!CanRepairNow())
                {
                    repairCommand.Disable("WULA_CannotRepairNow".Translate());
                }
                
                yield return repairCommand;
            }
            
            // 在 God Mode 下显示维修测试按钮
            if (DebugSettings.godMode && parent.Faction == Faction.OfPlayer)
            {
                // 模拟受伤按钮
                Command_Action damageCommand = new Command_Action
                {
                    defaultLabel = "WULA_Debug_Damage".Translate(),
                    defaultDesc = "WULA_Debug_DamageDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Damage", false) ?? BaseContent.BadTex,
                    action = () => DebugDamage()
                };
                yield return damageCommand;
                
                // 完全修复按钮
                Command_Action fullRepairCommand = new Command_Action
                {
                    defaultLabel = "WULA_Debug_FullRepair".Translate(),
                    defaultDesc = "WULA_Debug_FullRepairDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Repair", false) ?? BaseContent.BadTex,
                    action = () => DebugFullRepair()
                };
                yield return fullRepairCommand;
                
                // 显示维修统计
                Command_Action statsCommand = new Command_Action
                {
                    defaultLabel = "WULA_Debug_RepairStats".Translate(),
                    defaultDesc = "WULA_Debug_RepairStatsDesc".Translate(totalRepairedHP.ToString("F1")),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Stats", false) ?? BaseContent.BadTex,
                    action = () => DebugShowStats()
                };
                yield return statsCommand;
            }
        }
        
        // 强制立即维修 - 征召最近的殖民者
        public void ForceRepairNow()
        {
            if (!CanForceRepair || parent.Map == null)
                return;
            
            // 寻找最近的可用殖民者
            Pawn bestColonist = FindBestColonistForRepair();
            
            if (bestColonist == null)
            {
                Messages.Message("WULA_NoColonistAvailable".Translate(), parent, MessageTypeDefOf.RejectInput);
                return;
            }
            
            // 创建强制维修工作
            Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_RepairMech, parent);
            job.playerForced = true;
            
            bestColonist.jobs.StartJob(job, JobCondition.InterruptForced, null, resumeCurJobAfterwards: true);
            
            // 显示消息
            Messages.Message("WULA_OrderedRepair".Translate(bestColonist.LabelShort, parent.LabelShort),
                parent, MessageTypeDefOf.PositiveEvent);
        }
        
        private Pawn FindBestColonistForRepair()
        {
            Map map = parent.Map;
            if (map == null)
                return null;
            
            // 寻找所有可用的殖民者
            List<Pawn> colonists = map.mapPawns.FreeColonists.ToList();
            
            // 过滤掉无法工作或无法到达机甲的殖民者
            colonists = colonists.Where(colonist => 
                colonist.workSettings != null &&
                colonist.workSettings.WorkIsActive(WorkTypeDefOf.Crafting) &&
                colonist.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
                colonist.health.capacities.CapableOf(PawnCapacityDefOf.Moving) &&
                !colonist.Downed &&
                !colonist.Dead &&
                colonist.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some)
            ).ToList();
            
            if (!colonists.Any())
                return null;
            
            // 按照技能排序（机械维修技能优先）
            return colonists
                .OrderByDescending(colonist => colonist.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0)
                .ThenBy(colonist => colonist.Position.DistanceTo(parent.Position))
                .FirstOrDefault();
        }
        
        public bool CanRepairNow()
        {
            if (parent.Map == null)
                return false;
            
            // 检查是否有可用殖民者
            if (FindBestColonistForRepair() == null)
                return false;
            
            return true;
        }
        
        // 调试功能：模拟受伤
        private void DebugDamage()
        {
            var mech = parent as Pawn;
            if (mech == null || mech.health == null || mech.Dead)
                return;
            
            // 随机选择一个身体部位造成伤害
            var bodyParts = mech.RaceProps.body.AllParts.Where(p => p.depth == BodyPartDepth.Outside).ToList();
            if (!bodyParts.Any())
                return;
                
            BodyPartRecord part = bodyParts.RandomElement();
            
            // 造成随机伤害
            float damage = Rand.Range(10f, 50f);
            DamageInfo dinfo = new DamageInfo(DamageDefOf.Cut, damage, 1f, -1f, null, part);
            mech.TakeDamage(dinfo);
            
            Messages.Message($"WULA_Debug_Damaged".Translate(parent.LabelShort, damage.ToString("F1")),
                parent, MessageTypeDefOf.NeutralEvent);
        }
        
        // 调试功能：完全修复
        private void DebugFullRepair()
        {
            var mech = parent as Pawn;
            if (mech == null || mech.health == null || mech.Dead)
                return;
            
            // 修复所有伤口
            foreach (var hediff in mech.health.hediffSet.hediffs.ToList())
            {
                if (hediff is Hediff_Injury injury)
                {
                    mech.health.RemoveHediff(injury);
                }
                else if (hediff is Hediff_MissingPart missingPart)
                {
                    // 对于缺失部位，创建新的健康部分
                    mech.health.RestorePart(missingPart.Part);
                }
            }
            
            Messages.Message($"WULA_Debug_FullyRepaired".Translate(parent.LabelShort),
                parent, MessageTypeDefOf.PositiveEvent);
        }
        
        // 调试功能：显示维修统计
        private void DebugShowStats()
        {
            Messages.Message($"WULA_Debug_RepairStatsInfo".Translate(
                parent.LabelShort,
                totalRepairedHP.ToString("F1"),
                Props.repairAmountPerCycle.ToString("F1"),
                Props.ticksPerRepairCycle
            ), parent, MessageTypeDefOf.NeutralEvent);
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref totalRepairedHP, "totalRepairedHP", 0f);
        }
        
        public override string CompInspectStringExtra()
        {
            if (mech == null || mech.health == null)
                return base.CompInspectStringExtra();
            
            string baseString = base.CompInspectStringExtra();
            
            string repairString = "";
            if (NeedsRepair)
            {
                repairString = "WULA_NeedsRepair".Translate().Colorize(Color.yellow);
            }
            
            if (!baseString.NullOrEmpty())
                return baseString + "\n" + repairString;
            else
                return repairString;
        }
    }
}

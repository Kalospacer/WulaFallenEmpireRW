using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompFighterInvisible : ThingComp
    {
        public CompProperties_FighterInvisible Props => (CompProperties_FighterInvisible)props;
        [Unsaved(false)]
        private HediffComp_Invisibility invisibility;
        private int lastDetectedTick = -99999;
        private int lastRevealedTick = -99999;
        private Pawn Sightstealer => (Pawn)parent;
        
        // 新增：记录最后一次检查敌人的时间
        private int lastEnemyCheckTick = -99999;
        
        public HediffDef GetTargetInvisibilityDef()
        {
            return Props.InvisibilityDef;
        }
        
        // 添加一个属性来检查是否有效
        private bool IsValid => Sightstealer?.health?.hediffSet != null &&
                               GetTargetInvisibilityDef() != null &&
                               !Sightstealer.IsShambler &&
                               Sightstealer.Spawned &&
                               Sightstealer.Map != null;
        
        private HediffComp_Invisibility Invisibility
        {
            get
            {
                if (!IsValid) return null;
                return invisibility ?? (invisibility = Sightstealer.health.hediffSet
                    .GetFirstHediffOfDef(GetTargetInvisibilityDef())
                    ?.TryGetComp<HediffComp_Invisibility>());
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastDetectedTick, "lastDetectedTick", 0);
            Scribe_Values.Look(ref lastRevealedTick, "lastRevealedTick", 0);
            Scribe_Values.Look(ref lastEnemyCheckTick, "lastEnemyCheckTick", 0);
        }
        
        public override void CompTick()
        {
            // 使用统一的有效性检查
            if (!IsValid || Invisibility == null) return;
            
            // 检查敌人
            CheckForEnemiesInSight();
            
            // 隐身恢复逻辑
            if (Sightstealer.IsHashIntervalTick(Props.CheckDetectedIntervalTicks))
            {
                if (Find.TickManager.TicksGame > lastDetectedTick + Props.stealthCooldownTicks)
                {
                    Invisibility.BecomeInvisible();
                }
            }
        }
        
        public override void Notify_UsedVerb(Pawn pawn, Verb verb)
        {
            base.Notify_UsedVerb(pawn, verb);

            // 统一的 null 检查
            if (Invisibility == null) return;

            Invisibility.BecomeVisible();
            lastDetectedTick = Find.TickManager.TicksGame;
        }
        
        /// <summary>
        /// 检查视线内是否有敌人
        /// </summary>
        private void CheckForEnemiesInSight()
        {
            
            // 检查频率：每30 tick检查一次（约0.5秒）
            if (!Sightstealer.IsHashIntervalTick(30) || 
                Find.TickManager.TicksGame <= lastEnemyCheckTick + 30)
            {
                return;
            }
            
            lastEnemyCheckTick = Find.TickManager.TicksGame;
            
            // 如果配置为只在战斗状态时检查，且当前不在战斗状态，则跳过
            if (Props.onlyCheckInCombat && !IsInCombatState())
            {
                return;
            }
            
            // 检查视线内是否有敌人
            bool enemyInSight = false;
            List<Pawn> enemiesInSight = new List<Pawn>();

            // 获取地图上所有Pawn
            IReadOnlyList<Pawn> allPawns = Sightstealer.Map.mapPawns.AllPawnsSpawned;
            
            foreach (Pawn otherPawn in allPawns)
            {
                // 跳过自身
                if (otherPawn == Sightstealer) continue;

                // 跳过自律机械
                if (otherPawn.GetComp<CompAutonomousMech>() != null) continue;

                // 跳过死亡的
                if (otherPawn.Dead) continue;
                
                // 跳过倒地的（如果配置为忽略）
                if (Props.ignoreDownedEnemies && otherPawn.Downed) continue;
                
                // 跳过睡着的（如果配置为忽略）
                if (Props.ignoreSleepingEnemies && otherPawn.CurJobDef == JobDefOf.LayDown) continue;
                
                // 检查是否为敌对关系
                if (!otherPawn.HostileTo(Sightstealer)) continue;
                
                // 检查敌人类型过滤器（如果有）
                if (Props.enemyTypeFilter != null && Props.enemyTypeFilter.Count > 0)
                {
                    if (!Props.enemyTypeFilter.Contains(otherPawn.def)) continue;
                }
                
                // 关键修改：直接检查直线可见性，不使用距离限制
                if (GenSight.LineOfSight(Sightstealer.Position, otherPawn.Position, Sightstealer.Map))
                {
                    enemiesInSight.Add(otherPawn);
                    enemyInSight = true;
                    
                    // 如果只需要知道是否有敌人，且已经找到一个，可以提前退出循环
                    if (Props.minEnemiesToReveal <= 1)
                    {
                        break;
                    }
                }
            }
            
            // 如果启用敌人检测后解除隐身，并且发现了足够数量的敌人
            if (enemyInSight && Props.revealOnEnemyInSight && enemiesInSight.Count >= Props.minEnemiesToReveal)
            {
                // 立即解除隐身
                Invisibility.BecomeVisible();
                lastDetectedTick = Find.TickManager.TicksGame;
                lastRevealedTick = Find.TickManager.TicksGame;
                
                // 可选：添加视觉或声音效果
                if (Props.showRevealEffect)
                {
                    ShowRevealEffect(enemiesInSight);
                }
                
                // 可选：发送消息
                if (Props.sendRevealMessage && Sightstealer.Faction == Faction.OfPlayer)
                {
                    SendRevealMessage(enemiesInSight);
                }
            }
        }
        
        /// <summary>
        /// 检查是否处于战斗状态
        /// </summary>
        private bool IsInCombatState()
        {
            // 如果有当前工作且是战斗相关工作
            if (Sightstealer.CurJob != null)
            {
                JobDef jobDef = Sightstealer.CurJob.def;
                if (jobDef == JobDefOf.AttackMelee || 
                    jobDef == JobDefOf.AttackStatic || 
                    jobDef == JobDefOf.Wait_Combat ||
                    jobDef == JobDefOf.Flee ||
                    jobDef == JobDefOf.FleeAndCower)
                {
                    return true;
                }
            }
            
            // 如果有敌人目标
            if (Sightstealer.mindState.enemyTarget != null)
            {
                return true;
            }
            
            // 如果最近受到过伤害
            if (Find.TickManager.TicksGame - Sightstealer.mindState.lastHarmTick < 300) // 最近5秒内受到伤害
            {
                return true;
            }
            
            // 如果最近攻击过目标
            if (Find.TickManager.TicksGame - Sightstealer.mindState.lastAttackTargetTick < 300)
            {
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// 显示解除隐身的效果
        /// </summary>
        private void ShowRevealEffect(List<Pawn> enemies)
        {
            if (Sightstealer.Map == null) return;
            
            // 创建一个闪光效果
            FleckMaker.ThrowLightningGlow(Sightstealer.Position.ToVector3Shifted(), 
                Sightstealer.Map, 2f);
            
            // 可选：播放声音
            if (Props.revealSound != null)
            {
                Props.revealSound.PlayOneShot(new TargetInfo(Sightstealer.Position, Sightstealer.Map));
            }
        }
        
        /// <summary>
        /// 发送解除隐身消息
        /// </summary>
        private void SendRevealMessage(List<Pawn> enemies)
        {
            if (enemies.Count == 0) return;
            
            string message;
            if (enemies.Count == 1)
            {
                message = "WFE.RevealedBySingleEnemy".Translate(
                    Sightstealer.LabelShort,
                    enemies[0].LabelShort
                );
            }
            else
            {
                message = "WFE.RevealedByMultipleEnemies".Translate(
                    Sightstealer.LabelShort,
                    enemies.Count
                );
            }
            
            Messages.Message(message, Sightstealer, MessageTypeDefOf.NeutralEvent);
        }
        
        /// <summary>
        /// 获取下次可以隐身的时间
        /// </summary>
        public int NextInvisibilityTick => lastDetectedTick + Props.stealthCooldownTicks;
        
        /// <summary>
        /// 手动触发解除隐身（供外部调用）
        /// </summary>
        public void ForceReveal()
        {
            if (Invisibility == null) return;
            
            Invisibility.BecomeVisible();
            lastDetectedTick = Find.TickManager.TicksGame;
            lastRevealedTick = Find.TickManager.TicksGame;
        }
    }
}

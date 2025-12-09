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
        
        // 新增：记录最后一次发信的时间
        private int lastLetterTick = -99999;
        
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
            Scribe_Values.Look(ref lastLetterTick, "lastLetterTick", 0);
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
            
            // 触发显现事件
            TrySendLetter("attack");
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
                
                // 触发显现事件
                TrySendLetter("detected", enemiesInSight);
            }
        }
        
        /// <summary>
        /// 尝试发送信件
        /// </summary>
        private void TrySendLetter(string cause, List<Pawn> enemies = null)
        {
            // 检查是否应该发送信件
            if (!ShouldSendLetter())
                return;
            
            // 发送信件
            SendLetter(cause, enemies);
        }
        
        /// <summary>
        /// 检查是否应该发送信件
        /// </summary>
        private bool ShouldSendLetter()
        {
            // 如果配置为不发信，直接返回false
            if (!Props.sendLetterOnReveal)
                return false;
            
            // 检查发送间隔
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick < lastLetterTick + Props.letterIntervalTicks)
            {
                // 还没到发送间隔
                return false;
            }
            
            // 检查Pawn是否非玩家控制
            if (Sightstealer.Faction == Faction.OfPlayer)
            {
                // 玩家控制的Pawn，不发送信件
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// 发送信件
        /// </summary>
        private void SendLetter(string cause, List<Pawn> enemies = null)
        {
            try
            {
                int currentTick = Find.TickManager.TicksGame;
                
                // 获取信件标题和内容
                string title = Props.letterTitle;
                string text = Props.letterText;
                
                // 如果标题或内容为空，使用默认值
                if (string.IsNullOrEmpty(title))
                    title = "隐身单位现身";
                
                if (string.IsNullOrEmpty(text))
                {
                    string enemyInfo = "";
                    if (enemies != null && enemies.Count > 0)
                    {
                        if (enemies.Count == 1)
                        {
                            enemyInfo = $"被 {enemies[0].LabelCap} 发现";
                        }
                        else
                        {
                            enemyInfo = $"被 {enemies.Count} 个敌人发现";
                        }
                    }
                    else if (cause == "attack")
                    {
                        enemyInfo = "发动了攻击";
                    }
                    
                    text = $"{Sightstealer.LabelCap}（{Sightstealer.Faction?.Name ?? "未知派系"}）在 {Sightstealer.Map?.Parent?.LabelCap ?? "未知位置"} 现身了。\n\n{enemyInfo}\n位置：{Sightstealer.Position}";
                }
                
                // 发送信件
                Letter letter = LetterMaker.MakeLetter(
                    title,
                    text,
                    LetterDefOf.NeutralEvent,
                    new LookTargets(Sightstealer)
                );
                
                Find.LetterStack.ReceiveLetter(letter);
                
                // 更新最后发信时间
                lastLetterTick = currentTick;
            }
            catch (System.Exception ex)
            {
                Log.Error($"CompFighterInvisible: Error sending letter for {Sightstealer?.LabelCap}: {ex}");
            }
        }
        
        // ... 其他方法保持不变 ...
        
        /// <summary>
        /// 手动触发解除隐身（供外部调用）
        /// </summary>
        public void ForceReveal()
        {
            if (Invisibility == null) return;
            
            Invisibility.BecomeVisible();
            lastDetectedTick = Find.TickManager.TicksGame;
            lastRevealedTick = Find.TickManager.TicksGame;
            
            // 尝试发送信件
            TrySendLetter("manual");
        }
        
        // ... 其他方法保持不变 ...
    }
}

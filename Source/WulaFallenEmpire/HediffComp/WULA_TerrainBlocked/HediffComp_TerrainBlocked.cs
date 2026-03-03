using RimWorld;
using Verse;
using UnityEngine;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 地形阻挡Hediff组件，用于调整移动速度
    /// </summary>
    public class HediffComp_TerrainBlocked : HediffComp
    {
        public HediffCompProperties_TerrainBlocked Props => (HediffCompProperties_TerrainBlocked)props;
        
        /// <summary>
        /// 当前地形阻挡严重度（0-1之间）
        /// </summary>
        public float currentBlockSeverity = 0f;
        
        /// <summary>
        /// 上一次更新时间（ticks）
        /// </summary>
        private int lastUpdateTick = -1;
        
        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            
            // 降低更新频率：每Props.checkIntervalTicks检查一次
            if (Find.TickManager.TicksGame % Props.checkIntervalTicks != 0)
                return;
            
            // 如果Pawn已死亡或无法移动，清除效果
            if (Pawn == null || Pawn.Dead || Pawn.Downed || !Pawn.Spawned)
            {
                currentBlockSeverity = 0f;
                severityAdjustment = 0f;
                return;
            }
            
            // 获取CompHighSpeedCollision组件
            var collisionComp = Pawn.GetComp<CompHighSpeedCollision>();
            if (collisionComp == null)
            {
                currentBlockSeverity = 0f;
                severityAdjustment = 0f;
                return;
            }
            
            // 获取当前阻挡严重度
            float newSeverity = collisionComp.GetCurrentTerrainBlockSeverity();
            
            // 立即设置阻挡严重度（移除平滑过渡）
            currentBlockSeverity = newSeverity;
            
            // 更新Hediff严重度（立即变化）
            severityAdjustment = currentBlockSeverity - Pawn.health.hediffSet.GetFirstHediffOfDef(parent.def).Severity;
            
            lastUpdateTick = Find.TickManager.TicksGame;
        }
        
        /// <summary>
        /// 获取移动速度乘数
        /// </summary>
        public float GetMoveSpeedMultiplier()
        {
            if (currentBlockSeverity <= 0f)
                return 1f;
            
            // 应用严重度对应的速度惩罚（线性）
            return 1f - currentBlockSeverity * Props.maxSpeedPenalty;
        }
        
        public override string CompTipStringExtra
        {
            get
            {
                if (currentBlockSeverity > 0.01f)
                {
                    float speedMultiplier = GetMoveSpeedMultiplier();
                    float speedPenalty = (1f - speedMultiplier) * 100f;
                    return $"地形阻挡: {currentBlockSeverity:P0}\n移动速度: -{speedPenalty:F0}%";
                }
                return null;
            }
        }
        
        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref currentBlockSeverity, "currentBlockSeverity", 0f);
            Scribe_Values.Look(ref lastUpdateTick, "lastUpdateTick", -1);
        }
    }
    
    /// <summary>
    /// 地形阻挡Hediff组件属性
    /// </summary>
    public class HediffCompProperties_TerrainBlocked : HediffCompProperties
    {
        /// <summary>
        /// 最大速度惩罚（0-1之间）
        /// </summary>
        public float maxSpeedPenalty = 0.5f;
        
        /// <summary>
        /// 检查间隔（ticks） - 降低判断频率
        /// </summary>
        public int checkIntervalTicks = 60;
        
        public HediffCompProperties_TerrainBlocked()
        {
            compClass = typeof(HediffComp_TerrainBlocked);
        }
    }
}

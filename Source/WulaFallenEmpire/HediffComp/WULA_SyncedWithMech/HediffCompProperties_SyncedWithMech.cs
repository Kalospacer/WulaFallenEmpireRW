// File: HediffComp_SyncedWithMech_Fixed.cs
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using System.Linq;

namespace WulaFallenEmpire
{
    public class HediffCompProperties_SyncedWithMech : HediffCompProperties
    {
        public float severityOnPawn = 0.5f;          // 在Pawn身上的严重性
        public float severityOnMech = 1.5f;          // 在Mech身上的严重性
        public bool transferToMech = true;           // 是否转移到机甲
        public bool removeWhenLeaving = true;        // 离开时是否从机甲移除
        public string syncEffectDef = null;          // 同步时的效果
        
        public HediffCompProperties_SyncedWithMech()
        {
            this.compClass = typeof(HediffComp_SyncedWithMech);
        }
    }
    
    public class HediffComp_SyncedWithMech : HediffComp
    {
        // 同步状态数据
        public Pawn linkedMech = null;
        public bool isOnMech = false;
        private bool initialized = false;
        
        // 缓存属性
        private HediffCompProperties_SyncedWithMech Props => 
            (HediffCompProperties_SyncedWithMech)this.props;
        
        // 新增：供CompMechPilotHolder调用的公共方法
        public void OnPilotEnteredMech(Pawn mech)
        {
            if (mech is Wulamechunit dmech)
            {
                LinkToMech(dmech);
            }
            else if (Prefs.DevMode)
            {
                Log.Warning($"[WULA] OnPilotEnteredMech: 参数不是Wulamechunit类型: {mech?.GetType().Name}");
            }
        }
        
        public void OnPilotExitedMech()
        {
            UnlinkFromMech();
        }
        
        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            
            // 首次初始化
            if (!initialized)
            {
                Initialize();
                initialized = true;
            }
            
            // 检查是否需要同步
            CheckSyncStatus();
            
            // 如果不在机甲上，保持Pawn的严重性
            if (!isOnMech && !(parent.pawn is Wulamechunit))
            {
                parent.Severity = Props.severityOnPawn;
            }
        }
        
        private void Initialize()
        {
            // 如果Pawn是机甲，不应用这个效果
            if (parent.pawn is Wulamechunit)
            {
                return;
            }
            
            // 设置初始严重性
            parent.Severity = Props.severityOnPawn;
            
            // 检查Pawn是否已经在机甲中
            CheckIfInMech();
        }
        
        // 检查Pawn是否在机甲中
        private void CheckIfInMech()
        {
            // 如果Pawn是机甲，跳过
            if (parent.pawn is Wulamechunit)
                return;
                
            // 查找Pawn所在的机甲
            var mech = FindMechContainingPawn(parent.pawn);
            if (mech != null && mech != linkedMech)
            {
                // 连接到新的机甲
                LinkToMech(mech);
            }
            else if (mech == null && linkedMech != null)
            {
                // 从机甲断开
                UnlinkFromMech();
            }
        }
        
        // 查找包含Pawn的机甲
        private Wulamechunit FindMechContainingPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null)
                return null;
            
            // 检查所有机甲
            foreach (var thing in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn))
            {
                if (thing is Wulamechunit mech)
                {
                    var pilotComp = mech.TryGetComp<CompMechPilotHolder>();
                    if (pilotComp != null && pilotComp.GetPilots().Contains(pawn))
                    {
                        return mech;
                    }
                }
            }
            
            return null;
        }
        
        // 连接到机甲
        private void LinkToMech(Wulamechunit mech)
        {
            if (mech == null || !Props.transferToMech)
                return;
            
            // 记录连接的机甲
            linkedMech = mech;
            isOnMech = true;
            
            // 设置Pawn的严重性（在机甲内）
            parent.Severity = Props.severityOnPawn;
            
            // 在机甲上添加同样的Hediff
            AddHediffToMech(mech);
            
            // 触发同步效果
            TriggerSyncEffect();
            
        }
        
        // 从机甲断开
        private void UnlinkFromMech()
        {
            if (linkedMech == null)
                return;
            
            // 从机甲移除Hediff
            if (Props.removeWhenLeaving)
            {
                RemoveHediffFromMech(linkedMech);
            }
            
            // 重置状态
            var oldMech = linkedMech;
            linkedMech = null;
            isOnMech = false;
            
            // 恢复Pawn的严重性
            parent.Severity = Props.severityOnPawn;
        }
        
        // 在机甲上添加Hediff
        private void AddHediffToMech(Wulamechunit mech)
        {
            try
            {
                // 检查是否已经有相同的Hediff
                var existingHediff = mech.health.hediffSet.GetFirstHediffOfDef(parent.def);
                if (existingHediff != null)
                {
                    // 更新现有Hediff的严重性
                    existingHediff.Severity = Props.severityOnMech;
                }
                else
                {
                    // 添加新的Hediff
                    var hediff = HediffMaker.MakeHediff(parent.def, mech);
                    hediff.Severity = Props.severityOnMech;
                    
                    // 确保Hediff有同样的comp
                    var syncComp = hediff.TryGetComp<HediffComp_SyncedWithMech>();
                    if (syncComp != null)
                    {
                        syncComp.linkedMech = null;  // 机甲上的Hediff不链接其他机甲
                        syncComp.isOnMech = true;    // 标记为在机甲上
                    }
                    
                    mech.health.AddHediff(hediff);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] 在机甲{mech.LabelShort}上添加Hediff时出错: {ex}");
            }
        }
        
        // 从机甲移除Hediff
        private void RemoveHediffFromMech(Pawn mech)
        {
            try
            {
                var hediff = mech.health.hediffSet.GetFirstHediffOfDef(parent.def);
                if (hediff != null)
                {
                    mech.health.RemoveHediff(hediff);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] 从机甲{mech.LabelShort}移除Hediff时出错: {ex}");
            }
        }
        
        // 触发同步效果
        private void TriggerSyncEffect()
        {
            if (string.IsNullOrEmpty(Props.syncEffectDef))
                return;
                
            try
            {
                var effectDef = DefDatabase<EffecterDef>.GetNamed(Props.syncEffectDef, false);
                if (effectDef != null && parent.pawn.Spawned)
                {
                    Effecter effecter = effectDef.Spawn();
                    effecter.Trigger(parent.pawn, parent.pawn);
                    effecter.Cleanup();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] 触发同步效果时出错: {ex}");
            }
        }
        
        // 定期检查同步状态
        private void CheckSyncStatus()
        {
            // 每60帧检查一次
            if (Find.TickManager.TicksGame % 60 != 0)
                return;
                
            CheckIfInMech();
            
            // 如果Pawn死亡或消失，从机甲移除
            if (parent.pawn == null || parent.pawn.Dead || parent.pawn.Destroyed)
            {
                UnlinkFromMech();
            }
        }
        
        // 当Hediff被移除时
        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            
            // 从机甲移除对应的Hediff
            UnlinkFromMech();
        }
        
        // 保存和加载状态
        public override void CompExposeData()
        {
            base.CompExposeData();
            
            Scribe_References.Look(ref linkedMech, "linkedMech");
            Scribe_Values.Look(ref isOnMech, "isOnMech", false);
            Scribe_Values.Look(ref initialized, "initialized", false);
        }
    }
}

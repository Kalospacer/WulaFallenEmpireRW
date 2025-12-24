// File: Comp_StorageMultiTurretGun.cs
using System;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class Comp_StorageMultiTurretGun : CompTurretGun
    {
        // 炮塔是否激活
        private bool isActive = false;
        
        // 缓存父级的Building_MechanoidRecycler
        private Building_MechanoidRecycler cachedRecycler;

        public new CompProperties_StorageMultiTurretGun Props => (CompProperties_StorageMultiTurretGun)props;

        // 获取当前机械族数量
        private int StoredMechanoidCount
        {
            get
            {
                if (cachedRecycler == null)
                {
                    cachedRecycler = parent as Building_MechanoidRecycler;
                }
                
                return cachedRecycler?.StoredCount ?? 0;
            }
        }
        
        // 检查炮塔是否应该激活
        private bool ShouldBeActive
        {
            get
            {
                if (!Props.autoActivate)
                    return true;
                    
                return StoredMechanoidCount >= Props.requiredMechanoids;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 初始化状态
            UpdateActivationState();
        }

        public override void CompTick()
        {
            // 每60ticks检查一次激活状态（优化性能）
            if (parent.IsHashIntervalTick(60))
            {
                bool shouldBeActive = ShouldBeActive;
                if (shouldBeActive != isActive)
                {
                    isActive = shouldBeActive;
                    UpdateActivationState();
                }
            }
            
            // 只有激活时才执行炮塔逻辑
            if (isActive)
            {
                base.CompTick();
            }
            else
            {
                // 非激活状态：回正炮管，清空目标
                ResetTurretToNeutral();
            }
        }
        
        private void ResetTurretToNeutral()
        {
            // 清空当前目标
            currentTarget = LocalTargetInfo.Invalid;
            burstCooldownTicksLeft = 0;
            burstWarmupTicksLeft = 0;
            
            // 炮管回正到默认角度
            curRotation = parent.Rotation.AsAngle + Props.angleOffset;
        }

        private void UpdateActivationState()
        {
            if (isActive)
            {
                // 激活：创建枪械（如果需要）
                if (gun == null)
                {
                    MakeGun();
                }
                
                // 发送激活消息
                if (parent.Faction == Faction.OfPlayer && Prefs.DevMode)
                {
                    Log.Message($"[StorageTurret] 炮塔 {Props.ID} 已激活 (需要机械族: {Props.requiredMechanoids}, 当前: {StoredMechanoidCount})");
                }
            }
            else
            {
                // 非激活状态：清空目标，重置状态
                ResetTurretToNeutral();
                
                if (parent.Faction == Faction.OfPlayer && Prefs.DevMode)
                {
                    Log.Message($"[StorageTurret] 炮塔 {Props.ID} 已停用 (需要机械族: {Props.requiredMechanoids}, 当前: {StoredMechanoidCount})");
                }
            }
        }

        private void MakeGun()
        {
            if (Props.turretDef == null)
                return;
                
            gun = ThingMaker.MakeThing(Props.turretDef);
            UpdateGunVerbs();
        }

        private void UpdateGunVerbs()
        {
            if (gun == null)
                return;
                
            var compEq = gun.TryGetComp<CompEquippable>();
            if (compEq == null)
                return;
                
            foreach (var verb in compEq.AllVerbs)
            {
                verb.caster = parent;
                verb.castCompleteCallback = delegate
                {
                    burstCooldownTicksLeft = AttackVerb.verbProps.defaultCooldownTime.SecondsToTicks();
                };
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isActive, "isActive", false);
        }
        
        // 提供外部接口手动激活/停用
        public void SetActive(bool active)
        {
            isActive = active;
            UpdateActivationState();
        }
        
        public bool IsActive => isActive;
        
        // 获取炮塔状态信息
        public string GetStatusInfo()
        {
            return $"炮塔 {Props.ID}: {(isActive ? "激活" : "停用")} (需要 {Props.requiredMechanoids} 个机械族，当前 {StoredMechanoidCount})";
        }
    }
}

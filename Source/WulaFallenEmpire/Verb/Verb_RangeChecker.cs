using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 用于距离判断的Verb，不发射任何射弹，不造成伤害，不产生噪音
    /// 仅用于距离计算、视线检查和AI判断
    /// 当发射成功时，会设置Pawn身上所有Comp_MultiTurretGun的focusTarget为目标
    /// </summary>
    public class Verb_RangeChecker : Verb
    {
        // 重写瞄准相关方法，使其不产生视觉或声音效果
        public override void WarmupComplete()
        {
            base.WarmupComplete();
            
            // 不播放射击声音
            // 不产生射击效果
        }
        
        protected override bool TryCastShot()
        {
            // 基础检查
            if (currentTarget.HasThing && currentTarget.Thing.Map != caster.Map)
                return false;
            
            // 检查视线
            ShootLine shootLine;
            if (!TryFindShootLineFromTo(caster.Position, currentTarget, out shootLine))
            {
                if (verbProps.stopBurstWithoutLos)
                    return false;
            }
            
            // 更新设备状态（如果有）
            if (base.EquipmentSource != null)
            {
                base.EquipmentSource.GetComp<CompChangeableProjectile>()?.Notify_ProjectileLaunched();
                base.EquipmentSource.GetComp<CompApparelVerbOwner_Charged>()?.UsedOnce();
            }
            
            // 记录射击时间
            lastShotTick = Find.TickManager.TicksGame;
            
            // 更新炮塔焦点目标
            UpdateTurretFocusTargets();
            
            // 返回成功，但不实际射击
            return true;
        }
        
        /// <summary>
        /// 重写射击效果，确保不产生任何视觉或声音效果
        /// </summary>
        public override void DrawHighlight(LocalTargetInfo target)
        {
            base.DrawHighlight(target);
            
            // 可以绘制距离指示，但不绘制射击预览
        }
        
        /// <summary>
        /// 更新Pawn身上所有Comp_MultiTurretGun的focusTarget
        /// </summary>
        private void UpdateTurretFocusTargets()
        {
            if (caster is Pawn pawn && pawn.Spawned)
            {
                // 获取Pawn身上所有的Comp_MultiTurretGun组件
                var turretComps = pawn.GetComps<Comp_MultiTurretGun>();
                
                foreach (var turretComp in turretComps)
                {
                    if (turretComp != null)
                    {
                        // 设置集中火力目标
                        Comp_MultiTurretGun.focusTarget = currentTarget;
                        Comp_MultiTurretGun.lastFocusSetTick = Find.TickManager.TicksGame;
                        Comp_MultiTurretGun.lastFocusPawn = pawn;
                        
                        // 强制炮塔立即重新索敌，以便它们能检测到新的集中火力目标
                        turretComp.TryAcquireTarget();
                    }
                }
                
                // 记录日志
                if (Prefs.DevMode)
                {
                    Log.Message($"[Verb_RangeChecker] {pawn.LabelShort} set focus target to {currentTarget}");
                }
            }
        }
        
        /// <summary>
        /// 重写可用性检查，确保只有符合条件的Pawn才能使用
        /// </summary>
        public override bool Available()
        {
            if (!base.Available())
                return false;
            
            // 额外的检查：确保有炮塔组件
            if (caster is Pawn pawn)
            {
                var turretComps = pawn.GetComps<Comp_MultiTurretGun>();
                if (turretComps == null )
                    return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// 重写验证方法，添加额外的距离检查
        /// </summary>
        public override bool CanHitTarget(LocalTargetInfo targ)
        {
            if (!base.CanHitTarget(targ))
                return false;
            
            // 检查距离
            float distance = caster.Position.DistanceTo(targ.Cell);
            if (distance > verbProps.range)
                return false;
            
            // 检查最小距离
            if (distance < verbProps.minRange)
                return false;
            
            return true;
        }
        
        /// <summary>
        /// 重写目标高亮显示
        /// </summary>
        public override void OrderForceTarget(LocalTargetInfo target)
        {
            if (CanHitTarget(target))
            {
                base.OrderForceTarget(target);
                UpdateTurretFocusTargets();
            }
        }
        
        /// <summary>
        /// 重写，确保不产生任何射击效果
        /// </summary>
        public override void Notify_EquipmentLost()
        {
            base.Notify_EquipmentLost();
            
            // 清理焦点目标
            if (caster is Pawn pawn)
            {
                Comp_MultiTurretGun.lastFocusPawn = null;
                Comp_MultiTurretGun.focusTarget = LocalTargetInfo.Invalid;
            }
        }
    }
    
    /// <summary>
    /// 用于距离判断的Verb属性
    /// </summary>
    public class VerbProperties_RangeChecker : VerbProperties
    {
        public VerbProperties_RangeChecker()
        {
            verbClass = typeof(Verb_RangeChecker);
            
            // 设置为不发射射弹
            defaultProjectile = null;
            
            // 设置无噪音
            soundCast = null;
            soundCastTail = null;
            
            // 无视觉效果
            muzzleFlashScale = 0f;
            
            // 无预热时间
            warmupTime = 0f;
            
            // 无冷却时间
            defaultCooldownTime = 0f;
        }
    }
}

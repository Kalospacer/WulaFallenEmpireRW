using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class Verb_ShootBeyondTarge : Verb_ShootWithOffset
    {
        /// <summary>
        /// 重写射击逻辑，直接修改当前目标为延长线目标
        /// </summary>
        protected override bool TryCastShot()
        {
            // 保存原始目标
            LocalTargetInfo originalTarget = currentTarget;
            
            try
            {
                // 计算延长线目标
                LocalTargetInfo beyondTarget = CalculateBeyondTarget(originalTarget);
                
                // 设置为延长线目标
                currentTarget = beyondTarget;
                
                // 调用基类射击逻辑
                return base.TryCastShot();
            }
            finally
            {
                // 恢复原始目标
                currentTarget = originalTarget;
            }
        }
        
        /// <summary>
        /// 计算延长线目标
        /// </summary>
        private LocalTargetInfo CalculateBeyondTarget(LocalTargetInfo target)
        {
            if (!target.IsValid || caster == null || caster.Map == null)
                return target;

            Vector3 shooterPos = caster.DrawPos;
            Vector3 targetPos = target.HasThing ? 
                target.Thing.DrawPos : 
                target.Cell.ToVector3Shifted();
            
            Vector3 direction = (targetPos - shooterPos).normalized;
            float maxRange = EffectiveRange;
            Vector3 beyondTargetPos = shooterPos + direction * maxRange;
            IntVec3 beyondTargetCell = beyondTargetPos.ToIntVec3();
            
            // 确保在地图范围内
            if (!beyondTargetCell.InBounds(caster.Map))
            {
                beyondTargetCell = beyondTargetCell.ClampInsideMap(caster.Map);
            }
            
            return new LocalTargetInfo(beyondTargetCell);
        }
    }
}

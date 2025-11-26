using RimWorld;
using Verse;
using System.Collections.Generic;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompDamageTransfer : ThingComp
    {
        private CompProperties_DamageTransfer Props => (CompProperties_DamageTransfer)props;
        private Pawn Pawn => (Pawn)parent;

        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);
            
            // 检查是否应该转移伤害
            if (ShouldTransferDamage(dinfo, totalDamageDealt))
            {
                TryTransferDamage(dinfo, totalDamageDealt);
            }
        }

        private bool ShouldTransferDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            if (parent == null || !parent.Spawned)
                return false;
                
            // 检查生命值阈值
            if (Pawn.health != null)
            {
                float healthRatio = Pawn.health.summaryHealth.SummaryHealthPercent;
                if (healthRatio < Props.healthThreshold.min || healthRatio > Props.healthThreshold.max)
                    return false;
            }
            
            // 检查伤害类型
            if (!Props.transferAllDamageTypes)
            {
                // 这里可以添加特定伤害类型检查
                // 例如：只转移物理伤害，不转移火焰伤害等
            }
            
            return true;
        }

        private void TryTransferDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            // 计算转移的伤害量
            float transferDamage = totalDamageDealt * Props.damageTransferRatio;
            
            // 寻找可用的伤害接收器
            CompDamageReceiver receiver = FindAvailableDamageReceiver();
            if (receiver != null)
            {
                // 执行伤害转移
                if (receiver.ReceiveDamage(transferDamage, Pawn))
                {
                    OnDamageTransferred(dinfo, transferDamage, receiver);
                    
                    // 记录日志
                    Log.Message($"[DamageTransfer] {Pawn.LabelShort} 将 {transferDamage} 点伤害转移至 {receiver.parent.Label}");
                }
            }
        }

        private CompDamageReceiver FindAvailableDamageReceiver()
        {
            if (parent?.Map == null)
                return null;

            var map = parent.Map;
            var faction = parent.Faction;
            
            // 搜索范围内的同派系建筑
            foreach (var thing in GenRadial.RadialDistinctThingsAround(parent.Position, map, Props.maxTransferRange, true))
            {
                if (thing is Building building && 
                    building.Faction == faction && 
                    building != parent)
                {
                    var receiver = building.TryGetComp<CompDamageReceiver>();
                    if (receiver != null && receiver.CurrentDamage < receiver.MaxDamageCapacity)
                    {
                        // 检查视线（如果需要）
                        if (Props.requireLineOfSight)
                        {
                            if (!GenSight.LineOfSight(parent.Position, building.Position, map))
                                continue;
                        }
                        
                        return receiver;
                    }
                }
            }
            
            return null;
        }

        private void OnDamageTransferred(DamageInfo dinfo, float transferDamage, CompDamageReceiver receiver)
        {
            // 创建转移效果
            if (Props.transferEffecter != null)
            {
                Effecter effect = Props.transferEffecter.Spawn();
                effect.Trigger(new TargetInfo(parent.Position, parent.Map), new TargetInfo(receiver.parent.Position, parent.Map));
                effect.Cleanup();
            }
            
            // 播放音效
            if (Props.transferSound != null)
            {
                Props.transferSound.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            }
        }

        // 获取组件状态
        public string GetStatusString()
        {
            return $"伤害转移: {Props.damageTransferRatio * 100}% (范围: {Props.maxTransferRange})";
        }
    }
}

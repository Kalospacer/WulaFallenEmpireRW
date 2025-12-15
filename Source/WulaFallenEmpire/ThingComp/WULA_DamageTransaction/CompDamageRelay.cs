using RimWorld;
using Verse;
using System.Collections.Generic;
using Verse.Sound;

namespace WulaFallenEmpire
{
    public class CompDamageRelay : ThingComp
    {
        private CompProperties_DamageRelay Props => (CompProperties_DamageRelay)props;
        private Building Building => (Building)parent;

        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);
            
            // 检查是否应该传递伤害
            if (ShouldRelayDamage(dinfo, totalDamageDealt))
            {
                TryRelayDamage(dinfo, totalDamageDealt);
            }
        }

        private bool ShouldRelayDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            if (parent == null || !parent.Spawned || parent.Destroyed)
                return false;
                
            // 检查生命值阈值
            float healthRatio = (float)Building.HitPoints / Building.MaxHitPoints;
            if (healthRatio < Props.healthThreshold.min || healthRatio > Props.healthThreshold.max)
                return false;
            
            return true;
        }

        private void TryRelayDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            // 计算传递的伤害量
            float relayDamage = totalDamageDealt * Props.damageRelayRatio;
            
            // 寻找可用的同派系建筑
            Building targetBuilding = FindAvailableBuilding();
            if (targetBuilding != null)
            {
                // 执行伤害传递
                ApplyDamageToBuilding(relayDamage, targetBuilding, dinfo);
                OnDamageRelayed(dinfo, relayDamage, targetBuilding);
                
                // 记录日志
                WulaLog.Debug($"[DamageRelay] {Building.Label} 将 {relayDamage} 点伤害传递给 {targetBuilding.Label}");
            }
        }

        private Building FindAvailableBuilding()
        {
            if (parent?.Map == null)
                return null;

            var map = parent.Map;
            var faction = parent.Faction;
            
            // 在全图范围内搜索建筑
            List<Building> availableBuildings = new List<Building>();
            foreach (var building in map.listerBuildings.allBuildingsColonist)
            {
                if (building != parent && !building.Destroyed)
                {
                    // 检查派系（如果需要）
                    if (Props.relayOnlyToSameFaction && building.Faction != faction)
                        continue;
                    
                    availableBuildings.Add(building);
                }
            }
            
            // 随机选择一个建筑
            if (availableBuildings.Count > 0)
            {
                return availableBuildings.RandomElement();
            }
            
            return null;
        }

        /// <summary>
        /// 将伤害直接应用到目标建筑的生命值上
        /// </summary>
        private void ApplyDamageToBuilding(float damageAmount, Building building, DamageInfo originalDinfo)
        {
            // 创建新的伤害信息，使用原始伤害类型
            DamageInfo buildingDamage = new DamageInfo(
                originalDinfo.Def, // 使用相同的伤害类型
                damageAmount,
                originalDinfo.ArmorPenetrationInt,
                originalDinfo.Angle,
                originalDinfo.Instigator,
                originalDinfo.HitPart,
                originalDinfo.Weapon,
                originalDinfo.Category,
                originalDinfo.IntendedTarget
            );
            
            // 对建筑造成伤害
            building.TakeDamage(buildingDamage);
            
            WulaLog.Debug($"[DamageRelay] 对建筑 {building.Label} 造成 {damageAmount} 点伤害，剩余生命值: {building.HitPoints}/{building.MaxHitPoints}");
        }

        private void OnDamageRelayed(DamageInfo dinfo, float relayDamage, Building targetBuilding)
        {
            // 创建传递效果
            if (Props.relayEffecter != null)
            {
                Effecter effect = Props.relayEffecter.Spawn();
                effect.Trigger(new TargetInfo(parent.Position, parent.Map), new TargetInfo(targetBuilding.Position, parent.Map));
                effect.Cleanup();
            }
            
            // 播放音效
            if (Props.relaySound != null)
            {
                Props.relaySound.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            }
        }

        // 获取组件状态
        public string GetStatusString()
        {
            return $"伤害传递: {Props.damageRelayRatio * 100}%";
        }
    }
}

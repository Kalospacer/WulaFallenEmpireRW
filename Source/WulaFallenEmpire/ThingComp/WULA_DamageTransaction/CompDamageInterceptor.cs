using RimWorld;
using Verse;
using System.Collections.Generic;
using Verse.Sound;
using HarmonyLib;

namespace WulaFallenEmpire
{
    public class CompDamageInterceptor : ThingComp
    {
        private CompProperties_DamageInterceptor Props => (CompProperties_DamageInterceptor)props;
        private Pawn Pawn => (Pawn)parent;

        // 使用Harmony补丁在伤害应用前完全拦截
        public bool PreApplyDamage(ref DamageInfo dinfo)
        {
            if (!ShouldInterceptDamage(dinfo))
                return true; // 继续应用伤害

            // 计算要转移的伤害量（完全拦截）
            float transferDamage = dinfo.Amount * Props.damageTransferRatio;
            
            // 寻找可用的目标建筑
            Building targetBuilding = FindTargetBuilding();
            if (targetBuilding != null)
            {
                // 将伤害完全转移到建筑
                ApplyDamageToBuilding(transferDamage, targetBuilding, dinfo);
                OnDamageIntercepted(dinfo, transferDamage, targetBuilding);
                
                // 完全拦截伤害 - 将伤害设置为0
                dinfo.SetAmount(0f);
                
                Log.Message($"[DamageInterceptor] {Pawn.LabelShort} 完全拦截 {transferDamage} 点伤害并转移至 {targetBuilding.Label}，自身承受0伤害");
                
                return true; // 继续应用修改后的伤害（0伤害）
            }
            
            return true; // 没有找到建筑，正常应用伤害
        }

        private bool ShouldInterceptDamage(DamageInfo dinfo)
        {
            if (parent == null || !parent.Spawned || Pawn.Dead)
                return false;
                
            // 检查生命值阈值
            if (Pawn.health != null)
            {
                float healthRatio = Pawn.health.summaryHealth.SummaryHealthPercent;
                if (healthRatio < Props.healthThreshold.min || healthRatio > Props.healthThreshold.max)
                    return false;
            }
            
            return true;
        }

        private Building FindTargetBuilding()
        {
            if (parent?.Map == null)
                return null;

            var map = parent.Map;
            var faction = parent.Faction;
            
            // 在全图范围内搜索目标建筑
            List<Building> targetBuildings = new List<Building>();
            foreach (var building in map.listerBuildings.allBuildingsColonist)
            {
                if (building.def.defName == Props.targetBuildingDefName && 
                    !building.Destroyed)
                {
                    // 检查派系（如果需要）
                    if (Props.requireSameFaction && building.Faction != faction)
                        continue;
                    
                    targetBuildings.Add(building);
                }
            }
            
            // 随机选择一个建筑
            if (targetBuildings.Count > 0)
            {
                return targetBuildings.RandomElement();
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
            
            Log.Message($"[DamageInterceptor] 对建筑 {building.Label} 造成 {damageAmount} 点伤害，剩余生命值: {building.HitPoints}/{building.MaxHitPoints}");
        }

        private void OnDamageIntercepted(DamageInfo dinfo, float interceptDamage, Building targetBuilding)
        {
            // 创建拦截效果
            if (Props.interceptEffecter != null)
            {
                Effecter effect = Props.interceptEffecter.Spawn();
                effect.Trigger(new TargetInfo(parent.Position, parent.Map), new TargetInfo(targetBuilding.Position, parent.Map));
                effect.Cleanup();
            }
            
            // 播放音效
            if (Props.interceptSound != null)
            {
                Props.interceptSound.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            }
        }

        // 获取组件状态
        public string GetStatusString()
        {
            return $"伤害拦截: {Props.damageTransferRatio * 100}% → {Props.targetBuildingDefName}";
        }
    }
}

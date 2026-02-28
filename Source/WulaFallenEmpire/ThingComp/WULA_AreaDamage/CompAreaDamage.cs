using RimWorld;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompAreaDamage : ThingComp
    {
        private int ticksUntilNextDamage;
        private bool enabled;
        
        public CompProperties_AreaDamage Props => (CompProperties_AreaDamage)props;
        public bool Enabled => enabled;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            ticksUntilNextDamage = Props.damageIntervalTicks;
            enabled = Props.startEnabled;
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!enabled)
                return;

            if (parent is Pawn pawn && (pawn.IsSelfShutdown() || !pawn.Awake() || pawn.Dead || pawn.Downed || !pawn.Spawned || pawn.Destroyed))
                return;

            //对于mechunit，需要判定有没有驾驶员
            var MechPilotComp = parent.TryGetComp<CompMechPilotHolder>();
            if (MechPilotComp != null && !MechPilotComp.HasPilots)
            {
                return;
            }

            ticksUntilNextDamage--;
            if (ticksUntilNextDamage <= 0)
            {
                DoAreaDamage();
                ticksUntilNextDamage = Props.damageIntervalTicks;
            }
        }

        public void Toggle()
        {
            enabled = !enabled;
        }

        public void SetEnabled(bool newState)
        {
            enabled = newState;
        }

        private void DoAreaDamage()
        {
            Map map = parent.Map;
            if (map == null)
                return;

            // 获取范围内的所有物体
            List<Thing> thingsInRange = new List<Thing>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, Props.radius, true))
            {
                if (!cell.InBounds(map))
                    continue;

                List<Thing> thingList = cell.GetThingList(map);
                foreach (Thing thing in thingList)
                {
                    // 修改：添加目标类型检查，只处理建筑和Pawn
                    if (IsValidTargetType(thing) && IsValidTarget(thing) && !thingsInRange.Contains(thing))
                    {
                        thingsInRange.Add(thing);
                    }
                }
            }

            // 对每个有效目标造成伤害
            foreach (Thing target in thingsInRange)
            {
                ApplyDamageToTarget(target);
            }
        }

        /// <summary>
        /// 检查目标是否为建筑或Pawn
        /// </summary>
        private bool IsValidTargetType(Thing thing)
        {
            // 只针对建筑和Pawn
            return thing is Building || thing is Pawn;
        }

        private bool IsValidTarget(Thing thing)
        {
            // 首先检查是否为建筑或Pawn（双重检查）
            if (!(thing is Building || thing is Pawn))
            {
                return false;
            }

            // 检查是否为 Pawn
            if (thing is Pawn pawn)
            {
                // 移除植物检查，因为不再处理植物
                // 修改：简化Pawn检查逻辑
                return IsValidPawnTarget(pawn);
            }
            // 检查是否为建筑
            else if (thing is Building building)
            {
                return IsValidBuildingTarget(building);
            }

            return false;
        }

        /// <summary>
        /// 检查Pawn是否有效目标
        /// </summary>
        private bool IsValidPawnTarget(Pawn pawn)
        {
            // 基础检查：死亡或倒地的Pawn不是有效目标
            if (pawn.Dead || pawn.Downed)
                return false;
            
            // 检查是否影响Pawn
            if (!Props.affectPawns)
                return false;

            // 检查阵营关系
            return CheckFactionRelationship(pawn.Faction);
        }

        /// <summary>
        /// 检查建筑是否有效目标
        /// </summary>
        private bool IsValidBuildingTarget(Building building)
        {
            // 基础检查：建筑必须有生命值且未损坏
            if (building.def.useHitPoints == false || building.HitPoints <= 0)
                return false;

            // 检查是否影响建筑
            if (!Props.affectBuildings)
                return false;

            // 检查阵营关系
            return CheckFactionRelationship(building.Faction);
        }

        /// <summary>
        /// 检查阵营关系
        /// </summary>
        private bool CheckFactionRelationship(Faction targetFaction)
        {
            Faction parentFaction = parent.Faction;

            // 如果忽略所有阵营关系检查，直接返回true
            if (Props.ignoreFactionRelations)
                return true;

            // 如果影响所有物体，直接返回true
            if (Props.affectEverything)
                return true;

            // 父物体没有派系的情况
            if (parentFaction == null)
            {
                // 目标也没有派系 - 检查是否影响中立
                if (targetFaction == null)
                    return Props.affectNeutral;
                
                // 目标是玩家 - 检查是否影响友好
                if (targetFaction.IsPlayer)
                    return Props.affectFriendly;
                
                // 目标是非玩家派系 - 检查是否影响敌对
                return Props.affectHostile;
            }

            // 父物体有派系的情况
            if (targetFaction == null)
            {
                // 目标没有派系 - 检查是否影响中立
                return Props.affectNeutral;
            }

            // 目标与父物体同派系 - 检查是否影响友好
            if (targetFaction == parentFaction)
                return Props.affectFriendly;

            // 目标与父物体敌对 - 检查是否影响敌对
            if (targetFaction.HostileTo(parentFaction))
                return Props.affectHostile;

            // 其他情况视为中立 - 检查是否影响中立
            return Props.affectNeutral;
        }

        private void ApplyDamageToTarget(Thing target)
        {
            if (Props.damageDef == null)
                return;

            // 计算最终伤害量（应用缩放）
            int finalDamageAmount = CalculateFinalDamage(target);

            // 创建伤害信息
            DamageInfo damageInfo = new DamageInfo(
                Props.damageDef,
                finalDamageAmount,
                armorPenetration: Props.armorPenetration,
                instigator: parent,
                hitPart: null,
                weapon: null,
                category: DamageInfo.SourceCategory.ThingOrUnknown
            );

            // 应用伤害
            target.TakeDamage(damageInfo);

            // 特殊效果处理
            HandleSpecialEffects(target, damageInfo);
        }

        /// <summary>
        /// 计算最终伤害量，应用心灵敏感度缩放和保底伤害
        /// </summary>
        private int CalculateFinalDamage(Thing target)
        {
            float damageFactor = 1.0f;

            // 使用固定缩放值
            if (Props.useFixedScaling)
            {
                damageFactor = Props.fixedDamageFactor;
            }
            // 使用心灵敏感度缩放（仅对Pawn有效）
            else if (Props.scaleWithPsychicSensitivity && target is Pawn pawn)
            {
                damageFactor = CalculatePsychicSensitivityFactor(pawn);
            }

            // 确保伤害倍率在最小和最大范围内
            damageFactor = Mathf.Clamp(damageFactor, Props.minDamageFactor, Props.maxDamageFactor);

            // 计算最终伤害
            int finalDamage = Mathf.RoundToInt(Props.damageAmount * damageFactor);
            
            // 确保至少造成1点伤害
            return Mathf.Max(1, finalDamage);
        }

        /// <summary>
        /// 根据目标的心灵敏感度计算伤害倍率
        /// </summary>
        private float CalculatePsychicSensitivityFactor(Pawn targetPawn)
        {
            // 获取心灵敏感度（如果目标没有心灵敏感度，使用默认值0.5）
            float psychicSensitivity = 0.5f;
            
            if (targetPawn.health != null && targetPawn.health.capacities != null)
            {
                psychicSensitivity = targetPawn.GetStatValue(StatDefOf.PsychicSensitivity);
            }

            // 返回心灵敏感度作为伤害倍率
            return psychicSensitivity;
        }

        /// <summary>
        /// 处理特殊效果（如伤害类型特定的效果）
        /// </summary>
        private void HandleSpecialEffects(Thing target, DamageInfo damageInfo)
        {
            // 显示伤害数值（调试用）
            if (Props.showDamageNumbers)
            {
                MoteMaker.ThrowText(target.DrawPos, target.Map, damageInfo.Amount.ToString());
            }
            
            // 可以根据伤害类型添加额外效果
            // 例如：火焰伤害点燃目标，电击伤害麻痹目标等
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksUntilNextDamage, "ticksUntilNextDamage", Props.damageIntervalTicks);
            Scribe_Values.Look(ref enabled, "enabled", Props.startEnabled);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 只有拥有者可以操作开关
            if (parent.Faction != null && parent.Faction != Faction.OfPlayer && !parent.Faction.IsPlayer)
                yield break;

            // 创建切换开关的 Gizmo
            Command_Toggle toggleCommand = new Command_Toggle
            {
                defaultLabel = Props.toggleLabel.Translate(),
                defaultDesc = Props.toggleDescription.Translate(),
                icon = LoadToggleIcon(),
                isActive = () => enabled,
                toggleAction = () => Toggle()
            };

            yield return toggleCommand;
        }

        private Texture2D LoadToggleIcon()
        {
            if (!string.IsNullOrEmpty(Props.toggleIconPath))
            {
                return ContentFinder<Texture2D>.Get(Props.toggleIconPath, false);
            }
            
            // 默认图标
            return TexCommand.DesirePower;
        }

        public override string CompInspectStringExtra()
        {
            string baseString = base.CompInspectStringExtra();
            
            // 状态信息
            string statusText = enabled ? 
                "AreaDamageEnabled".Translate() : 
                "AreaDamageDisabled".Translate();
            
            if (string.IsNullOrEmpty(baseString))
                return statusText;
            else
                return baseString + "\n" + statusText;
        }
        
        /// <summary>
        /// 获取范围内所有有效目标（调试和外部调用用）
        /// </summary>
        public List<Thing> GetValidTargetsInRange()
        {
            Map map = parent.Map;
            List<Thing> validTargets = new List<Thing>();
            
            if (map == null || !enabled)
                return validTargets;
            
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, Props.radius, true))
            {
                if (!cell.InBounds(map))
                    continue;

                List<Thing> thingList = cell.GetThingList(map);
                foreach (Thing thing in thingList)
                {
                    if (IsValidTargetType(thing) && IsValidTarget(thing) && !validTargets.Contains(thing))
                    {
                        validTargets.Add(thing);
                    }
                }
            }
            
            return validTargets;
        }
    }
}

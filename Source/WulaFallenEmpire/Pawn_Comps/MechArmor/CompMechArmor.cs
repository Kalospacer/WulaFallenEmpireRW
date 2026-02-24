// File: CompMechArmor.cs
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;
using static RimWorld.MechClusterSketch;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 机甲装甲组件：提供基于装甲值的伤害减免系统
    /// </summary>
    public class CompMechArmor : ThingComp
    {
        public CompProperties_MechArmor Props => 
            (CompProperties_MechArmor)props;

        private static StatDef DD_MechArmorDef = null;

        // 当前装甲值（从Stat获取）
        public float CurrentArmor
        {
            get
            {
                if (DD_MechArmorDef == null)
                {
                    DD_MechArmorDef = StatDef.Named("DD_MechArmor");
                    if (DD_MechArmorDef == null)
                    {
                        Log.Warning("[DD] DD_MechArmor stat definition not found!");
                        return 0f;
                    }
                }
                return parent.GetStatValue(DD_MechArmorDef);
            }
        }

        // 调试信息
        private int blockedHits = 0;
        private int totalHits = 0;
        
        /// <summary>
        /// 检查伤害是否被装甲抵消
        /// </summary>
        /// <param name="dinfo">伤害信息</param>
        /// <returns>true=伤害被抵消，false=伤害有效</returns>
        public bool TryBlockDamage(ref DamageInfo dinfo)
        {
            totalHits++;
            
            // 获取穿甲率（如果没有则为0）
            float armorPenetration = dinfo.ArmorPenetrationInt;
            
            // 计算穿甲伤害
            float armorPiercingDamage = dinfo.Amount * armorPenetration;
            
            // 获取当前装甲值
            float currentArmor = CurrentArmor;
            
            // 检查是否应该被装甲抵消
            bool shouldBlock = armorPiercingDamage < currentArmor;
            
            if (shouldBlock)
            {
                blockedHits++;
                
                // 可选：触发视觉效果
                if (Props.showBlockEffect && parent.Spawned)
                {
                    ShowBlockEffect(dinfo);
                }
                
                // 可选：播放音效
                if (Props.soundOnBlock != null && parent.Spawned)
                {
                    Props.soundOnBlock.PlayOneShot(parent);
                }
            }
            
            // 调试日志
            if (Props.debugLogging && parent.Spawned)
            {
                Log.Message($"[DD Armor] {parent.LabelShort}: " +
                    $"Damage={dinfo.Amount}, " +
                    $"Penetration={armorPenetration:P0}, " +
                    $"PierceDamage={armorPiercingDamage:F1}, " +
                    $"Armor={currentArmor:F1}, " +
                    $"Blocked={shouldBlock}");
            }
            
            return shouldBlock;
        }
        
        /// <summary>
        /// 显示阻挡效果
        /// </summary>
        private void ShowBlockEffect(DamageInfo dinfo)
        {
            MoteMaker.ThrowText(parent.DrawPos, parent.Map, "DD_BlockByMechArmor".Translate(), Color.white, 3.5f);
            // 创建火花或特效
            if (Props.blockEffectMote != null)
            {
                MoteMaker.MakeStaticMote(
                    parent.DrawPos + new Vector3(0, 0, 0.5f),
                    parent.Map,
                    Props.blockEffectMote,
                    1.0f
                );
            }
        }
        
        /// <summary>
        /// 获取装甲信息（用于调试）
        /// </summary>
        public string GetArmorInfo()
        {
            return $"<b>{parent.LabelShort}的装甲系统</b>\n" +
                   $"当前装甲值: {CurrentArmor:F1}\n" +
                   $"阻挡规则: 穿甲伤害 < 装甲值\n" +
                   $"统计: 已阻挡 {blockedHits}/{totalHits} 次攻击\n" +
                   $"阻挡率: {(totalHits > 0 ? (float)blockedHits / totalHits * 100 : 0):F1}%";
        }
        
        /// <summary>
        /// 获取调试按钮
        /// </summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            
            // 在开发模式下显示装甲信息
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEBUG: 装甲信息",
                    defaultDesc = GetArmorInfo(),
                    //icon = TexCommand.Shield,
                    action = () =>
                    {
                        Find.WindowStack.Add(new Dialog_MessageBox(
                            GetArmorInfo(),
                            "关闭",
                            null,
                            null,
                            null,
                            "机甲装甲信息"
                        ));
                    }
                };
                
                yield return new Command_Action
                {
                    defaultLabel = "DEBUG: 重置统计",
                    defaultDesc = "重置阻挡统计计数器",
                    //icon = TexCommand.Clear,
                    action = () =>
                    {
                        blockedHits = 0;
                        totalHits = 0;
                        Messages.Message("装甲统计已重置", MessageTypeDefOf.TaskCompletion);
                    }
                };
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref blockedHits, "blockedHits", 0);
            Scribe_Values.Look(ref totalHits, "totalHits", 0);
        }
    }
    
    /// <summary>
    /// 机甲装甲组件属性
    /// </summary>
    public class CompProperties_MechArmor : CompProperties
    {
        // 视觉效果
        public bool showBlockEffect = true;
        public ThingDef blockEffectMote; // 阻挡时显示的特效
        
        // 音效
        public SoundDef soundOnBlock;
        
        // 调试
        public bool debugLogging = false;
        
        public CompProperties_MechArmor()
        {
            compClass = typeof(CompMechArmor);
        }
    }
}

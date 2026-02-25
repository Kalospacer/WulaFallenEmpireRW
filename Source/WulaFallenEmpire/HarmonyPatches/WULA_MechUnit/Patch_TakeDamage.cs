using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire.HarmonyPatches
{
    /// <summary>
    /// 整合的伤害处理系统
    /// 处理：1. 机甲装甲系统 2. HediffComp_Invulnerable免疫系统
    /// </summary>
    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch("TakeDamage")]
    public static class Thing_TakeDamage_Patch
    {
        // 缓存装甲值StatDef
        private static readonly StatDef ArmorStatDef = StatDef.Named("WULA_MechArmor");
        
        // 阻挡效果的MoteDef
        private static readonly ThingDef BlockMoteDef = DefDatabase<ThingDef>.GetNamedSilentFail("Mote_Spark");
        
        // 阻挡音效
        private static readonly SoundDef BlockSoundDef = DefDatabase<SoundDef>.GetNamedSilentFail("ArmorBlock");
        
        // 免疫效果的MoteDef
        private static readonly ThingDef ImmuneMoteDef = DefDatabase<ThingDef>.GetNamedSilentFail("Mote_Immunity");
        
        // 免疫音效
        private static readonly SoundDef ImmuneSoundDef = DefDatabase<SoundDef>.GetNamedSilentFail("ImmuneSound");
        
        // 调试统计
        private static readonly Dictionary<Thing, DamageBlockStats> DebugStats = new Dictionary<Thing, DamageBlockStats>();
        
        private class DamageBlockStats
        {
            public int mechArmorBlocked = 0;
            public int invulnerableBlocked = 0;
            public int totalHits = 0;
            
            public int TotalBlocked => mechArmorBlocked + invulnerableBlocked;
            
            public override string ToString()
            {
                return $"机甲装甲阻挡: {mechArmorBlocked}, 免疫阻挡: {invulnerableBlocked}, 总计阻挡: {TotalBlocked}, 总命中: {totalHits}";
            }
        }
        
        /// <summary>
        /// 前置补丁：在TakeDamage执行前检查伤害免疫
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(Thing __instance, ref DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            // 更新调试统计
            if (!DebugStats.ContainsKey(__instance))
                DebugStats[__instance] = new DamageBlockStats();
            DebugStats[__instance].totalHits++;
            
            // 第二步：检查机甲装甲系统
            float armorValue = __instance.GetStatValue(ArmorStatDef);
            
            // 如果装甲值 <= 0，不启动装甲系统，继续原方法
            if (armorValue <= 0)
                return true;
            
            // 计算穿甲伤害
            float armorPenetration = dinfo.ArmorPenetrationInt;
            float piercingDamage = dinfo.Amount * armorPenetration;
            
            // 判断是否应该阻挡
            bool shouldBlock = piercingDamage < armorValue;
            
            if (shouldBlock)
            {
                // 机甲装甲阻挡成功
                DebugStats[__instance].mechArmorBlocked++;
                
                // 显示阻挡效果
                ShowBlockEffect(__instance, dinfo);
                
                // 播放阻挡音效
                PlayBlockSound(__instance);
                
                // 返回空结果，跳过原方法
                __result = new DamageWorker.DamageResult();
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// 显示阻挡效果
        /// </summary>
        private static void ShowBlockEffect(Thing target, DamageInfo dinfo)
        {
            if (!target.Spawned)
                return;
                
            // 显示文字效果
            Vector3 textPos = target.DrawPos + new Vector3(0, 0, 1f);
            MoteMaker.ThrowText(textPos, target.Map, "WULA_BlockByMechArmor".Translate(), Color.yellow, 2.5f);
            
            // 显示粒子效果
            if (BlockMoteDef != null)
            {
                MoteMaker.MakeStaticMote(target.DrawPos, target.Map, BlockMoteDef, 1f);
            }
        }
        
        /// <summary>
        /// 播放阻挡音效
        /// </summary>
        private static void PlayBlockSound(Thing target)
        {
            if (!target.Spawned)
                return;
                
            if (BlockSoundDef != null)
            {
                BlockSoundDef.PlayOneShot(new TargetInfo(target.Position, target.Map));
            }
            else
            {
                // 备用音效
                SoundDefOf.MetalHitImportant.PlayOneShot(new TargetInfo(target.Position, target.Map));
            }
        }
        
        /// <summary>
        /// 获取调试统计信息
        /// </summary>
        public static string GetDebugStats()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("伤害阻挡统计:");
            
            foreach (var kvp in DebugStats)
            {
                sb.AppendLine($"{kvp.Key.LabelCap}: {kvp.Value}");
            }
            
            return sb.ToString();
        }
    }
}

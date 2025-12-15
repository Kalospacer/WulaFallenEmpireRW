using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class DamageWorker_ExtraDamage : DamageWorker_AddInjury
    {
        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            // 首先应用原始伤害
            DamageResult result = base.Apply(dinfo, victim);
            
            // 检查是否有额外伤害扩展
            var extension = dinfo.Def.GetModExtension<DamageDef_ExtraDamageExtension>();
            if (extension != null && extension.extraDamages != null)
            {
                foreach (var extraDamage in extension.extraDamages)
                {
                    if (extraDamage.damageDef != null && extraDamage.amount > 0)
                    {
                        // 直接应用额外伤害
                        DamageInfo extraDinfo = new DamageInfo(
                            extraDamage.damageDef,
                            extraDamage.amount,
                            extraDamage.armorPenetration >= 0 ? extraDamage.armorPenetration : extraDamage.damageDef.defaultArmorPenetration,
                            dinfo.Angle,
                            dinfo.Instigator,
                            null,
                            dinfo.Weapon,
                            dinfo.Category
                        );
                        
                        victim.TakeDamage(extraDinfo);
                        
                        // 调试信息
                        if (Prefs.DevMode)
                        {
                            WulaLog.Debug($"应用额外伤害: {extraDamage.damageDef.defName} 伤害值: {extraDamage.amount}");
                        }
                    }
                }
            }
            return result;
        }
    }
}

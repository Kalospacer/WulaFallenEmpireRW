using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace WulaFallenEmpire
{
    /// <summary>
    /// 支持直击伤害的爆炸弹丸
    /// </summary>
    public class Projectile_ExplosiveWithDirectHit : Projectile_Explosive
    {
        // 缓存ModExtension
        private ProjectileExtension_DirectHit directHitExtension = null;
        
        // 标记是否已经应用了直击伤害
        private bool directDamageApplied = false;
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref directDamageApplied, "directDamageApplied", false);
        }
        
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            // 获取ModExtension
            if (directHitExtension == null)
            {
                directHitExtension = def.GetModExtension<ProjectileExtension_DirectHit>();
            }
            
            // 应用直击伤害（只在有直接击中目标时）
            if (hitThing != null && directHitExtension != null && 
                directHitExtension.directDamageAmount > 0 && 
                !directDamageApplied)
            {
                ApplyDirectDamage(hitThing, blockedByShield);
                directDamageApplied = true;
            }
            
            // 调用基类方法处理爆炸
            base.Impact(hitThing, blockedByShield);
        }
        
        /// <summary>
        /// 应用直击伤害
        /// </summary>
        private void ApplyDirectDamage(Thing hitThing, bool blockedByShield = false)
        {
            // 如果只在无护盾时生效且有护盾阻挡，则不应用
            if (blockedByShield && directHitExtension.applyDirectDamageOnlyWithoutShield)
                return;
            
            // 准备伤害信息
            DamageInfo damageInfo = CreateDirectDamageInfo();

            // 对目标造成伤害
            hitThing.TakeDamage(damageInfo);

            // 播放效果
            PlayDirectImpactEffects(hitThing);
        }
        
        /// <summary>
        /// 创建直击伤害信息
        /// </summary>
        private DamageInfo CreateDirectDamageInfo()
        {
            // 确定伤害类型
            DamageDef damageDef = directHitExtension.directDamageDef;
            if (damageDef == null)
            {
                // 默认使用爆炸伤害类型
                damageDef = base.DamageDef ?? DamageDefOf.Bomb;
            }
            
            // 确定伤害量
            int damageAmount = directHitExtension.directDamageAmount;
            if (directHitExtension.useEquipmentStatsForDirectDamage && equipment != null)
            {
                // 使用装备属性计算伤害
                damageAmount = def.projectile.GetDamageAmount(equipment);
            }
            
            // 确定护甲穿透
            float armorPenetration = directHitExtension.directArmorPenetration;
            if (directHitExtension.useEquipmentStatsForDirectDamage && equipment != null)
            {
                armorPenetration = def.projectile.GetArmorPenetration(equipment);
            }
            
            // 创建伤害信息
            DamageInfo damageInfo = new DamageInfo(
                damageDef,
                damageAmount,
                armorPenetration,
                -1f, // 角度
                launcher,
                null, // 击中部位
                equipmentDef,
                DamageInfo.SourceCategory.ThingOrUnknown,
                intendedTarget.Thing
            );
            
            // 添加额外伤害
            AddExtraDirectDamages(damageInfo);
            
            return damageInfo;
        }
        
        /// <summary>
        /// 添加上额外伤害
        /// </summary>
        private void AddExtraDirectDamages(DamageInfo mainDamage)
        {
            if (directHitExtension?.extraDirectDamages == null)
                return;
            
            foreach (var extraDamage in directHitExtension.extraDirectDamages)
            {
                if (extraDamage != null && extraDamage.def != null && extraDamage.amount > 0)
                {
                    DamageInfo extraDamageInfo = new DamageInfo(
                        extraDamage.def,
                        extraDamage.amount,
                        extraDamage.armorPenetration,
                        -1f,
                        launcher,
                        null,
                        equipmentDef,
                        DamageInfo.SourceCategory.ThingOrUnknown,
                        intendedTarget.Thing
                    );
                    
                    // 应用额外伤害
                    if (mainDamage.IntendedTarget != null && mainDamage.IntendedTarget != null)
                    {
                        mainDamage.IntendedTarget.TakeDamage(extraDamageInfo);
                    }
                }
            }
        }
        
        /// <summary>
        /// 播放直击效果
        /// </summary>
        private void PlayDirectImpactEffects(Thing hitThing)
        {
            Map map = base.Map;
            if (map == null)
                return;
            
            // 播放效果器
            if (directHitExtension.directImpactEffecter != null)
            {
                Effecter effecter = directHitExtension.directImpactEffecter.Spawn();
                effecter.Trigger(new TargetInfo(base.Position, map), 
                               new TargetInfo(hitThing.Position, map));
                effecter.Cleanup();
            }
            
            // 播放音效
            if (directHitExtension.directImpactSound != null)
            {
                directHitExtension.directImpactSound.PlayOneShot(new TargetInfo(base.Position, map));
            }
        }
        
        /// <summary>
        /// 覆盖爆炸方法以添加直击伤害信息
        /// </summary>
        protected override void Explode()
        {
            // 在爆炸前确保直击伤害已经应用（如果是延迟爆炸的情况）
            if (!directDamageApplied && directHitExtension != null && 
                directHitExtension.directDamageAmount > 0 && intendedTarget.Thing != null)
            {
                ApplyDirectDamage(intendedTarget.Thing, false);
                directDamageApplied = true;
            }
            
            // 调用基类爆炸方法
            base.Explode();
        }
        
        /// <summary>
        /// 获取调试信息
        /// </summary>
        public string GetDirectHitDebugInfo()
        {
            if (directHitExtension == null)
                return "No direct hit extension configured.";
            
            string info = "Direct Hit Configuration:\n";
            info += $"Damage Def: {directHitExtension.directDamageDef?.defName ?? "None"}\n";
            info += $"Damage Amount: {directHitExtension.directDamageAmount}\n";
            info += $"Armor Penetration: {directHitExtension.directArmorPenetration}\n";
            info += $"Use Equipment Stats: {directHitExtension.useEquipmentStatsForDirectDamage}\n";
            info += $"Apply Without Shield Only: {directHitExtension.applyDirectDamageOnlyWithoutShield}\n";
            info += $"Extra Damages Count: {directHitExtension.extraDirectDamages?.Count ?? 0}\n";
            info += $"Direct Damage Applied: {directDamageApplied}";
            
            return info;
        }
    }
    
    /// <summary>
    /// 直击伤害ModExtension定义
    /// </summary>
    public class ProjectileExtension_DirectHit : DefModExtension
    {
        /// <summary>
        /// 直击伤害类型（默认为爆炸伤害类型）
        /// </summary>
        public DamageDef directDamageDef = null;
        
        /// <summary>
        /// 直击伤害量
        /// </summary>
        public int directDamageAmount = 0;
        
        /// <summary>
        /// 直击伤害护甲穿透
        /// </summary>
        public float directArmorPenetration = 0f;
        
        /// <summary>
        /// 直击伤害是否受装备影响
        /// </summary>
        public bool useEquipmentStatsForDirectDamage = false;
        
        /// <summary>
        /// 直击伤害额外伤害列表
        /// </summary>
        public List<ExtraDamage> extraDirectDamages = null;
        
        /// <summary>
        /// 直击伤害是否只在击穿护盾后生效
        /// </summary>
        public bool applyDirectDamageOnlyWithoutShield = false;
        
        /// <summary>
        /// 直击伤害效果器
        /// </summary>
        public EffecterDef directImpactEffecter = null;
        
        /// <summary>
        /// 直击伤害音效
        /// </summary>
        public SoundDef directImpactSound = null;
        
        /// <summary>
        /// 是否显示直击伤害日志
        /// </summary>
        public bool logDirectDamage = false;
    }
}

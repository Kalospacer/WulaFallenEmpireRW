// CompProperties_MechSelfDestruct.cs
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_MechSelfDestruct : CompProperties
    {
        // 爆炸相关属性
        public float explosiveRadius = 3.9f;
        public DamageDef explosiveDamageType;
        public int damageAmountBase = 60;
        public float armorPenetrationBase = -1f;
        public ThingDef postExplosionSpawnThingDef;
        public float postExplosionSpawnChance;
        public int postExplosionSpawnThingCount = 1;
        public bool applyDamageToExplosionCellsNeighbors;
        public ThingDef preExplosionSpawnThingDef;
        public float preExplosionSpawnChance;
        public int preExplosionSpawnThingCount = 1;
        public float chanceToStartFire;
        public bool damageFalloff;
        public GasType? postExplosionGasType;
        public float? postExplosionGasRadiusOverride;
        public int postExplosionGasAmount = 255;
        public bool doVisualEffects = true;
        public bool doSoundEffects = true;
        public float propagationSpeed = 1f;
        public ThingDef postExplosionSpawnSingleThingDef;
        public ThingDef preExplosionSpawnSingleThingDef;
        public float explosiveExpandPerStackcount;
        public float explosiveExpandPerFuel;
        public EffecterDef explosionEffect;
        public SoundDef explosionSound;
        
        // 自毁相关属性
        public float healthPercentThreshold = 0.2f; // 健康百分比阈值，低于此值启动自毁
        public IntRange wickTicks = new IntRange(140, 150); // 自毁延迟ticks范围
        public bool drawWick = true; // 是否显示引信
        public string extraInspectStringKey; // 额外检查字符串键
        public List<WickMessage> wickMessages; // 引信消息
        
        // 自毁触发条件
        public bool triggerOnDeath = true; // 死亡时触发
        public bool triggerOnHealthThreshold = true; // 健康阈值时触发
        public List<DamageDef> startWickOnDamageTaken; // 特定伤害类型触发自毁
        public List<DamageDef> startWickOnInternalDamageTaken; // 内部伤害触发自毁
        public float chanceNeverExplode = 0f; // 永不爆炸的几率
        public float destroyThingOnExplosionSize = 9999f; // 爆炸时摧毁物体的尺寸阈值
        public DamageDef requiredDamageTypeToExplode; // 触发爆炸所需的伤害类型
        
        public CompProperties_MechSelfDestruct()
        {
            compClass = typeof(CompMechSelfDestruct);
        }
        
        public override void ResolveReferences(ThingDef parentDef)
        {
            base.ResolveReferences(parentDef);
            if (explosiveDamageType == null)
            {
                explosiveDamageType = DamageDefOf.Bomb;
            }
        }
        
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string item in base.ConfigErrors(parentDef))
            {
                yield return item;
            }
            
            if (parentDef.tickerType != TickerType.Normal)
            {
                yield return "CompMechSelfDestruct requires Normal ticker type";
            }
        }
    }
    
    // 引信消息类
    public class WickMessage
    {
        public int ticksLeft;
        public string wickMessagekey;
        public MessageTypeDef messageType;
    }
}

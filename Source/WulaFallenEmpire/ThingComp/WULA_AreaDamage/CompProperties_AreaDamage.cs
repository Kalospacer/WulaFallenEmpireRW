using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_AreaDamage : CompProperties
    {
        public float radius = 5f;                    // A: 伤害半径
        public int damageIntervalTicks = 60;         // B: 伤害间隔（帧数）
        public DamageDef damageDef;                   // C: 伤害类型
        public int damageAmount = 10;                // 基础伤害量
        public float armorPenetration = 0f;          // 护甲穿透
        
        // 伤害缩放设置
        public bool scaleWithPsychicSensitivity = false; // 是否随心灵敏感度缩放
        public float minDamageFactor = 0.5f;         // 最低伤害倍率（0.0-1.0）
        public float maxDamageFactor = 2.0f;         // 最高伤害倍率
        public bool useFixedScaling = false;         // 是否使用固定缩放值
        public float fixedDamageFactor = 1.0f;       // 固定伤害倍率
        
        // 目标过滤
        public bool affectFriendly = false;          // 是否影响友方
        public bool affectNeutral = true;            // 是否影响中立
        public bool affectHostile = true;            // 是否影响敌方
        public bool affectBuildings = true;          // 是否影响建筑
        public bool affectPawns = true;              // 是否影响生物
        public bool affectPlants = false;            // 是否影响植物
        
        // 特殊设置
        public bool ignoreFactionRelations = false;  // 忽略所有阵营关系检查（用于无派系实体）
        public bool affectEverything = false;        // 影响范围内所有有生命值的物体
        
        // 特殊效果
        public bool showDamageNumbers = false;       // 显示伤害数值（调试用）

        public CompProperties_AreaDamage()
        {
            compClass = typeof(CompAreaDamage);
        }
    }
}

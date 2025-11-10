using RimWorld;
using Verse;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityCircularBombardment : CompProperties_AbilityEffect
    {
        // 轰炸区域配置
        public float radius = 10f;                   // 圆形区域半径
        public bool useFixedRadius = true;          // 是否使用固定半径
        
        // 目标选择配置
        public float targetSelectionChance = 0.5f;  // 每个位置被选中的概率
        public int minTargets = 3;                  // 最小目标数量
        public int maxTargets = 15;                 // 最大目标数量
        
        // 发射配置
        public int simultaneousLaunches = 2;        // 同时发射数量
        public int launchIntervalTicks = 30;        // 组间发射间隔（刻）
        public int maxLaunches = 10;                // 最大发射数量
        public int warmupTicks = 120;               // 前摇时间
        
        // 组内独立间隔配置
        public bool useIndependentIntervals = false; // 是否使用独立间隔
        public int innerLaunchIntervalTicks = 10;   // 组内发射间隔（刻）
        
        // Skyfaller 配置
        public ThingDef skyfallerDef;               // 使用的 Skyfaller
        public ThingDef projectileDef;              // 备用的抛射体定义
        
        // 视觉效果配置
        public bool showBombardmentArea = true;     // 是否显示轰炸区域
        public Color areaPreviewColor = new Color(1f, 0.5f, 0.1f, 0.3f); // 区域预览颜色
        public bool showImpactPreview = true;       // 是否显示预计落点
        
        // 随机分布配置
        public float minDistanceFromCenter = 0f;    // 距离中心的最小距离
        public bool avoidFriendlyFire = true;       // 避免友军误伤
        public bool avoidBuildings = false;         // 避免建筑物
        
        public CompProperties_AbilityCircularBombardment()
        {
            this.compClass = typeof(CompAbilityEffect_CircularBombardment);
        }
    }
}

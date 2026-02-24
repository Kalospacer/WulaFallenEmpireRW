// CompProperties_MechFuel.cs
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_MechFuel : CompProperties
    {
        public ThingDef fuelType; // 燃料种类
        public float fuelCapacity = 100f; // 燃料容量
        public float dailyFuelConsumption = 10f; // 每日燃料消耗量
        public float refuelSpeedFactor = 1f; // 加注速度因子
        public int refuelDuration = 240; // 基础加注时间（ticks）
        
        // Gizmo显示设置
        public Color fuelBarColor = new Color(0.1f, 0.6f, 0.9f); // 燃料条颜色
        public Color emptyBarColor = new Color(0.2f, 0.2f, 0.24f); // 空燃料条颜色
        
        // 自动加注设置
        public float autoRefuelThreshold = 0.3f; // 自动加注阈值（低于此值自动加注）
        public bool allowAutoRefuel = true; // 是否允许自动加注
        
        // 燃料耗尽效果
        public bool shutdownWhenEmpty = true; // 燃料耗尽时关机
        
        public CompProperties_MechFuel()
        {
            this.compClass = typeof(CompMechFuel);
        }
        
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            
            if (fuelType == null)
            {
                yield return $"fuelType is null for {parentDef.defName}";
            }
            
            if (fuelCapacity <= 0f)
            {
                yield return $"fuelCapacity must be positive for {parentDef.defName}";
            }
            
            if (dailyFuelConsumption < 0f)
            {
                yield return $"dailyFuelConsumption cannot be negative for {parentDef.defName}";
            }
        }
    }
}

// CompProperties_MechMovementSound_Enhanced.cs
using RimWorld;
using Verse;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class CompProperties_MechMovementSound : CompProperties
    {
        // 基础音效
        public SoundDef movementSound;
        
        // 控制参数
        public bool requirePilot = false;
        public bool requirePower = false;
        public float minMovementSpeed = 0.1f;
        
        // 新增：平滑控制
        public int movementCheckInterval = 10; // 移动检查间隔
        public int stopDelayTicks = 30; // 停止延迟
        public float speedSmoothing = 0.2f; // 速度平滑系数
        
        // 新增：声音参数
        public bool loopSound = true; // 是否循环播放
        public float volumeMultiplier = 1.0f; // 音量乘数
        
        public CompProperties_MechMovementSound()
        {
            this.compClass = typeof(CompMechMovementSound);
        }
        
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            
            if (movementSound == null)
            {
                yield return $"movementSound is not defined for {parentDef.defName}";
            }
            
            if (minMovementSpeed < 0f)
            {
                yield return $"minMovementSpeed cannot be negative for {parentDef.defName}";
            }
            
            if (movementCheckInterval < 1)
            {
                yield return $"movementCheckInterval must be at least 1 for {parentDef.defName}";
            }
            
            if (stopDelayTicks < 0)
            {
                yield return $"stopDelayTicks cannot be negative for {parentDef.defName}";
            }
            
            // 如果需要驾驶员，检查是否配置了驾驶员容器
            if (requirePilot && parentDef.GetCompProperties<CompProperties_MechPilotHolder>() == null)
            {
                Log.Warning($"[DD] requirePilot is true but no CompProperties_MechPilotHolder found for {parentDef.defName}");
            }
        }
    }
}

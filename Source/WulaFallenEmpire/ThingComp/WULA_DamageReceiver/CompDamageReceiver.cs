using RimWorld;
using Verse;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class CompDamageReceiver : ThingComp
    {
        private CompProperties_DamageReceiver Props => (CompProperties_DamageReceiver)props;
        
        private float currentDamage;
        private int lastDamageTick;
        
        public float CurrentDamage => currentDamage;
        public float MaxDamageCapacity => Props.maxDamageCapacity;
        public float DamageRatio => currentDamage / Props.maxDamageCapacity;
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentDamage, "currentDamage", 0f);
            Scribe_Values.Look(ref lastDamageTick, "lastDamageTick", 0);
        }

        public override void CompTick()
        {
            base.CompTick();
            
            // 定期衰减伤害
            if (Find.TickManager.TicksGame % Props.damageDecayInterval == 0 && currentDamage > 0)
            {
                currentDamage = Mathf.Max(0f, currentDamage - Props.damageDecayRate);
                
                // 如果伤害为0，重置最后伤害时间
                if (currentDamage <= 0f)
                {
                    lastDamageTick = 0;
                }
            }
        }

        /// <summary>
        /// 接收伤害
        /// </summary>
        public bool ReceiveDamage(float damageAmount, Pawn sourcePawn = null)
        {
            float oldDamage = currentDamage;
            currentDamage += damageAmount;
            lastDamageTick = Find.TickManager.TicksGame;
            
            // 检查是否超过容量
            if (currentDamage >= Props.maxDamageCapacity)
            {
                if (Props.canBeDestroyedByDamage)
                {
                    // 摧毁建筑
                    parent.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    // 只是达到上限，不再接收更多伤害
                    currentDamage = Props.maxDamageCapacity;
                }
                return false; // 无法接收更多伤害
            }
            
            // 触发效果
            OnDamageReceived(damageAmount, sourcePawn);
            return true;
        }

        private void OnDamageReceived(float damageAmount, Pawn sourcePawn)
        {
            // 记录日志
            Log.Message($"[DamageReceiver] {parent.Label} 接收 {damageAmount} 点伤害，当前伤害: {currentDamage}/{Props.maxDamageCapacity}");
        }

        public override void PostDraw()
        {
            base.PostDraw();
            
            // 绘制伤害条
            if (Props.showDamageBar && currentDamage > 0f)
            {
                Vector3 drawPos = parent.DrawPos;
                drawPos.y += 0.5f; // 在建筑上方显示
                
                GenDraw.DrawFillableBar(new GenDraw.FillableBarRequest
                {
                    center = drawPos,
                    size = new Vector2(1f, 0.15f),
                    fillPercent = DamageRatio,
                    filledMat = SolidColorMaterials.SimpleSolidColorMaterial(Color.red),
                    unfilledMat = SolidColorMaterials.SimpleSolidColorMaterial(Color.gray),
                    margin = 0.1f,
                    rotation = Rot4.North
                });
            }
        }

        // 获取接收器状态
        public string GetStatusString()
        {
            return $"伤害吸收: {currentDamage:F0}/{Props.maxDamageCapacity:F0} ({DamageRatio * 100:F1}%)";
        }
    }
}

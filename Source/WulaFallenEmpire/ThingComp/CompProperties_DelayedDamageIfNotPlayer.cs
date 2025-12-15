using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_DelayedDamageIfNotPlayer : CompProperties
    {
        public DamageDef damageDef;
        public int damageAmount = 10;
        public float armorPenetration = 0f;
        public BodyPartDef hitPart;
        public bool destroyIfKilled = true;
        
        public CompProperties_DelayedDamageIfNotPlayer()
        {
            compClass = typeof(CompDelayedDamageIfNotPlayer);
        }
    }

    public class CompDelayedDamageIfNotPlayer : ThingComp
    {
        private bool damageApplied = false;
        private bool scheduledForNextFrame = false;

        public CompProperties_DelayedDamageIfNotPlayer Props => (CompProperties_DelayedDamageIfNotPlayer)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 只在初次生成时检查，重新加载时不重复
            if (!respawningAfterLoad)
            {
                CheckAndScheduleDamage();
            }
        }

        private void CheckAndScheduleDamage()
        {
            // 检查派系，如果不是玩家派系则安排伤害
            if (parent.Faction != Faction.OfPlayer && !damageApplied && !scheduledForNextFrame)
            {
                scheduledForNextFrame = true;
                
                // 使用LongEventHandler来在下一帧执行
                LongEventHandler.ExecuteWhenFinished(ApplyDelayedDamage);
            }
        }

        private void ApplyDelayedDamage()
        {
            if (scheduledForNextFrame && !damageApplied)
            {
                // 再次确认对象仍然存在且未被销毁
                if (parent != null && parent.Spawned && !parent.Destroyed)
                {
                    ApplyDamage();
                }
                scheduledForNextFrame = false;
            }
        }

        private void ApplyDamage()
        {
            try
            {
                if (parent == null || parent.Destroyed || damageApplied)
                    return;

                // 创建伤害信息
                DamageInfo damageInfo = new DamageInfo(
                    Props.damageDef,
                    Props.damageAmount,
                    armorPenetration: Props.armorPenetration,
                    instigator: parent
                );

                // 施加伤害
                parent.TakeDamage(damageInfo);

                damageApplied = true;

                // 记录日志以便调试
                WulaLog.Debug($"[CompDelayedDamage] Applied {Props.damageAmount} {Props.damageDef.defName} damage to {parent.Label} (Faction: {parent.Faction?.Name ?? "None"})");

                // 检查是否被杀死
                if (Props.destroyIfKilled && (parent.Destroyed || (parent is Pawn pawn && pawn.Dead)))
                {
                    WulaLog.Debug($"[CompDelayedDamage] {parent.Label} was destroyed by delayed damage");
                }
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[CompDelayedDamage] Error applying delayed damage: {ex}");
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref damageApplied, "damageApplied", false);
            Scribe_Values.Look(ref scheduledForNextFrame, "scheduledForNextFrame", false);
        }
    }
}

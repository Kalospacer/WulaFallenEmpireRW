using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_FactionSetter : CompProperties
    {
        // 派系配置
        public FactionDef factionDef = null; // 指定的派系定义
        public bool usePlayerFactionIfNull = true; // 如果没有指定派系，是否使用玩家派系
        public bool overrideExistingFaction = true; // 是否覆盖已有的派系归属
        
        public CompProperties_FactionSetter()
        {
            compClass = typeof(CompFactionSetter);
        }
    }
    
    public class CompFactionSetter : ThingComp
    {
        private bool factionSet = false;
        
        public CompProperties_FactionSetter Props => (CompProperties_FactionSetter)props;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (!respawningAfterLoad && !factionSet)
            {
                SetFaction();
                factionSet = true;
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref factionSet, "factionSet", false);
        }
        
        private void SetFaction()
        {
            // 如果不需要覆盖已有派系且建筑已有派系，则跳过
            if (!Props.overrideExistingFaction && parent.Faction != null)
                return;
            
            Faction faction = GetTargetFaction();
            if (faction != null && faction != parent.Faction)
            {
                parent.SetFaction(faction);
                WulaLog.Debug($"Set faction for {parent.Label} to {faction.Name}");
            }
        }
        
        private Faction GetTargetFaction()
        {
            // 1. 如果指定了派系定义，使用该派系
            if (Props.factionDef != null)
            {
                Faction faction = Find.FactionManager.FirstFactionOfDef(Props.factionDef);
                if (faction != null)
                    return faction;
            }
            
            // 2. 默认使用玩家派系
            if (Props.usePlayerFactionIfNull)
                return Faction.OfPlayer;
            
            return null;
        }
        
        public override string CompInspectStringExtra()
        {
            return base.CompInspectStringExtra();
        }
    }
}

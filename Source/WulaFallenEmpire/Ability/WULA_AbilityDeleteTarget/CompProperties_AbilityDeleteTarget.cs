using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityDeleteTarget : CompProperties_AbilityEffect
    {
        public bool affectBuildings = true;
        public bool affectPawns = true;
        public bool affectItems = true;
        public bool affectPlants = true;
        public bool affectFilth = true;
        public bool affectBlueprints = true;
        public bool affectFrames = true;
        public bool affectCorpses = true;
        public bool affectMines = true;
        public bool affectEverything = true; // 覆盖所有其他设置
        public bool requireLineOfSight = false; // 调试模式不需要视线
        public bool showEffect = true;
        public FleckDef effectFleck;
        public SoundDef soundEffect;
        
        public CompProperties_AbilityDeleteTarget()
        {
            this.compClass = typeof(CompAbilityEffect_DeleteTarget);
        }
    }
}

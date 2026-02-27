// CompBuildToPawn_SimpleDelay.cs
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire
{
    public class CompBuildToPawn : ThingComp
    {
        public CompProperties_BuildToPawn Props => (CompProperties_BuildToPawn)props;
        
        private bool shouldSpawn = false;
        private int delayCounter = 0;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 跳过存档加载和蓝图/框架
            if (respawningAfterLoad || parent.def.IsBlueprint || parent.def.IsFrame)
                return;
                
            // 延迟一帧
            shouldSpawn = true;
            delayCounter = 0;
        }
        
        public override void CompTick()
        {
            base.CompTick();
            
            if (shouldSpawn && delayCounter >= 1) // 延迟一帧后执行
            {
                if (Props.pawnKindDef != null && parent != null && !parent.Destroyed && parent.Map != null)
                {
                    // 生成Pawn
                    for (int i = 0; i < Props.spawnCount; i++)
                    {
                        Pawn pawn = PawnGenerator.GeneratePawn(Props.pawnKindDef);
                        if (Props.inheritFaction)
                            pawn.SetFaction(parent.Faction, null);
                            
                        GenSpawn.Spawn(pawn, parent.Position, parent.Map, WipeMode.Vanish);

                        if (Props.initDrafted && pawn.drafter!=null)
                            pawn.drafter.Drafted = true;
                    }

                    if (Props.destroyBuilding)
                        // 摧毁建筑
                        parent.Destroy();
                }
                
                shouldSpawn = false;
            }
            else if (shouldSpawn)
            {
                delayCounter++;
            }
        }
    }
}

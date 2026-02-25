// File: WorkGiver_EnterMech.cs
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
namespace WulaFallenEmpire
{
    public class WorkGiver_EnterMech : WorkGiver_Scanner
    {
        // 缓存机甲定义列表
        private static List<ThingDef> cachedMechDefs = null;

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        // 获取所有机甲定义的列表
        private List<ThingDef> GetAllMechDefs()
        {
            if (cachedMechDefs == null)
            {
                cachedMechDefs = new List<ThingDef>();

                // 搜索所有ThingDef，找出继承自Wulamechunit的类
                foreach (var def in DefDatabase<ThingDef>.AllDefs)
                {
                    try
                    {
                        if (def.thingClass == typeof(Wulamechunit) ||
                            def.thingClass?.IsSubclassOf(typeof(Wulamechunit)) == true)
                        {
                            cachedMechDefs.Add(def);
                        }
                    }
                    catch (Exception ex)
                    {
                        // 忽略错误，继续搜索
                        Log.Warning($"[WULA] Error checking ThingDef {def.defName}: {ex.Message}");
                    }
                }
            }

            return cachedMechDefs;
        }

        // 检查Thing是否为机甲
        private bool IsMech(Thing thing)
        {
            return thing is Wulamechunit || thing?.GetType()?.IsSubclassOf(typeof(Wulamechunit)) == true;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            try
            {
                // 检查基本条件
                if (t == null || pawn == null)
                    return false;

                // 必须是Wulamechunit或其子类
                if (!IsMech(t))
                    return false;

                // 检查距离
                if (!pawn.CanReach(t, PathEndMode, Danger.Deadly))
                    return false;

                // 检查机甲是否有驾驶员槽位组件
                var comp = t.TryGetComp<CompMechPilotHolder>();
                if (comp == null)
                    return false;

                // 检查是否已满
                if (comp.IsFull)
                    return false;

                // 检查殖民者是否可以成为驾驶员
                if (!comp.CanAddPilot(pawn))
                    return false;

                // 检查殖民者状态
                if (pawn.Downed || pawn.Dead)
                    return false;

                // 检查殖民者是否正在执行任务
                if (pawn.CurJob != null && pawn.CurJob.def != JobDefOf.Wait)
                    return false;

                // 检查是否被征召
                if (pawn.Drafted)
                    return false;

                // 检查是否为囚犯
                if (pawn.IsPrisoner)
                    return false;

                // 检查是否为奴隶
                if (pawn.IsSlave)
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error in HasJobOnThing: {ex}");
                return false;
            }
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            try
            {
                if (IsMech(t))
                {
                    // 创建进入机甲的工作
                    return JobMaker.MakeJob(Wula_JobDefOf.WULA_EnterMech, t);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error creating job: {ex}");
            }

            return null;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            try
            {
                // 只搜索玩家拥有的机甲
                List<Thing> potentialMechs = new List<Thing>();

                // 获取地图中的所有机甲
                if (pawn.Map != null)
                {
                    // 使用缓存的机甲定义列表
                    var mechDefs = GetAllMechDefs();

                    foreach (var def in mechDefs)
                    {
                        try
                        {
                            var allMechs = pawn.Map.listerThings.ThingsOfDef(def);
                            foreach (var mech in allMechs)
                            {
                                if (mech.Faction == Faction.OfPlayer &&
                                    mech.TryGetComp<CompMechPilotHolder>() != null)
                                {
                                    potentialMechs.Add(mech);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning($"[WULA] Error getting mechs for def {def.defName}: {ex.Message}");
                        }
                    }
                }

                return potentialMechs;
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error in PotentialWorkThingsGlobal: {ex}");
                return Enumerable.Empty<Thing>();
            }
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            try
            {
                // 简化版本：只检查殖民者状态
                if (pawn.Downed || pawn.Dead || pawn.Drafted || pawn.IsPrisoner || pawn.IsSlave)
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[WULA] Error in ShouldSkip: {ex}");
                return true;
            }
        }
    }
}
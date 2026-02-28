using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    // 机甲乘员组件属性
    public class CompProperties_MechCrewHolder : CompProperties
    {
        public int maxCrew = 3;                     // 最大乘员数
        public float boardingRadius = 10f;          // 登机半径
        public float maxPawnSize = 1.0f;            // 最大装载体型大小
        public bool allowMechanoids = true;         // 是否允许搭载机械族
        public bool draftOnExit = true;             // 下车时是否进入征召状态
        
        // 外观配置
        public string boardCrewIcon = "Wula/UI/Commands/WULA_BoardCrew";
        public string exitCrewIcon = "Wula/UI/Commands/WULA_ExitCrew";
        
        // 工作配置
        public bool requireWorkTag = false;         // 是否需要工作标签
        public string workTag = "CrewMember";       // 工作标签（如果需要）
        
        public CompProperties_MechCrewHolder()
        {
            compClass = typeof(CompMechCrewHolder);
        }
        
        // 获取图标
        public Texture2D GetBoardCrewIcon()
        {
            if (!string.IsNullOrEmpty(boardCrewIcon) && 
                ContentFinder<Texture2D>.Get(boardCrewIcon, false) != null)
            {
                return ContentFinder<Texture2D>.Get(boardCrewIcon);
            }
            return ContentFinder<Texture2D>.Get("UI/Commands/BoardCrew", false) ?? BaseContent.BadTex;
        }
        
        public Texture2D GetExitCrewIcon()
        {
            if (!string.IsNullOrEmpty(exitCrewIcon) && 
                ContentFinder<Texture2D>.Get(exitCrewIcon, false) != null)
            {
                return ContentFinder<Texture2D>.Get(exitCrewIcon);
            }
            return ContentFinder<Texture2D>.Get("UI/Commands/ExitCrew", false) ?? BaseContent.BadTex;
        }
    }
    
    // 机甲乘员组件实现
    public class CompMechCrewHolder : ThingComp, IThingHolder
    {
        public ThingOwner innerContainer;
        
        private Command_Action cachedBoardGizmo;
        private Command_Action cachedExitGizmo;
        private bool gizmosInitialized = false;
        
        // 属性
        public CompProperties_MechCrewHolder Props => (CompProperties_MechCrewHolder)props;
        
        public int CurrentCrewCount => innerContainer.Count;
        public bool HasCrew => innerContainer.Count > 0;
        public bool HasRoom => innerContainer.Count < Props.maxCrew;
        public bool IsFull => innerContainer.Count >= Props.maxCrew;
        
        // 初始化
        public CompMechCrewHolder()
        {
            innerContainer = new ThingOwner<Pawn>(this);
        }
        
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            
            if (innerContainer == null)
            {
                innerContainer = new ThingOwner<Pawn>(this);
            }
        }
        
        // 检查是否可以添加乘员
        public bool CanAddCrew(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
                return false;
                
            if (!HasRoom)
                return false;
                
            if (innerContainer.Contains(pawn))
                return false;
                
            // 检查体型大小
            if (pawn.BodySize > Props.maxPawnSize)
                return false;
                
            // 检查是否允许机械族
            if (pawn.RaceProps.IsMechanoid && !Props.allowMechanoids)
                return false;
                
            // 检查工作标签（如果需要）
            if (Props.requireWorkTag)
            {
                WorkTags tag;
                if (Enum.TryParse(Props.workTag, out tag))
                {
                    if (pawn.WorkTagIsDisabled(tag))
                        return false;
                }
            }
            
            return true;
        }
        
        // 添加乘员
        public void AddCrew(Pawn pawn)
        {
            if (!CanAddCrew(pawn))
                return;
                
            try
            {
                // 停止Pawn当前工作
                pawn.jobs?.StopAll();
                pawn.pather?.StopDead();
                
                // 从地图移除并添加到容器
                if (pawn.Spawned)
                    pawn.DeSpawn();
                    
                innerContainer.TryAdd(pawn, true);
                
                // 触发事件
                Notify_CrewAdded(pawn);
                
                // 记录日志
                if (Prefs.DevMode)
                {
                    Log.Message($"[CompMechCrewHolder] {pawn.LabelShort} boarded {parent.LabelShort}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error adding crew {pawn.LabelShort} to {parent.LabelShort}: {ex.Message}");
            }
        }
        
        // 移除乘员
        public void RemoveCrew(Pawn pawn, IntVec3? exitPos = null)
        {
            if (!innerContainer.Contains(pawn))
                return;
                
            try
            {
                // 从容器移除
                innerContainer.Remove(pawn);
                
                // 生成到地图
                TrySpawnCrewAtPosition(pawn, exitPos ?? parent.Position);
                
                // 如果设置为征召状态
                if (Props.draftOnExit && pawn.drafter != null)
                {
                    pawn.drafter.Drafted = true;
                }
                
                // 触发事件
                Notify_CrewRemoved(pawn);
            }
            catch (Exception ex)
            {
                Log.Error($"Error removing crew {pawn.LabelShort} from {parent.LabelShort}: {ex.Message}");
            }
        }
        
        // 移除所有乘员
        public void RemoveAllCrew(IntVec3? exitPos = null)
        {
            var crewToRemove = innerContainer;
            
            foreach (var thing in crewToRemove)
            {
                if (thing is Pawn pawn)
                {
                    RemoveCrew(pawn, exitPos);
                }
            }
        }

        // 获取所有符合条件的登机Pawn（半径内）
        public List<Pawn> GetEligiblePawnsInRadius()
        {
            var eligiblePawns = new List<Pawn>();

            if (parent.Map == null || !parent.Spawned)
                return eligiblePawns;

            // 获取半径内的所有Pawn
            var pawnsInRadius = parent.Map.mapPawns.AllPawnsSpawned
                .Where(p => p.Position.DistanceTo(parent.Position) <= Props.boardingRadius)
                .ToList();

            foreach (var pawn in pawnsInRadius)
            {
                // 检查是否满足条件
                if (CanAddCrew(pawn) &&
                    pawn.Faction == parent.Faction &&
                    !pawn.Downed &&
                    !pawn.Dead &&
                    !pawn.IsPrisoner)
                {
                    eligiblePawns.Add(pawn);
                }
            }

            return eligiblePawns;
        }

        // 命令所有符合条件的Pawn登机
        public void OrderAllEligibleToBoard()
        {
            var eligiblePawns = GetEligiblePawnsInRadius();
            
            int count = 0;
            foreach (var pawn in eligiblePawns)
            {
                if (!HasRoom)
                    break;
                    
                // 创建登机工作
                Job job = JobMaker.MakeJob(Wula_JobDefOf.WULA_BoardMech, parent);
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                count++;
            }
            
            // 显示消息
            if (count > 0)
            {
                Messages.Message(
                    "WULA_CrewOrderedToBoard".Translate(count, parent.LabelShort),
                    parent,
                    MessageTypeDefOf.NeutralEvent
                );
            }
        }
        
        // 生成乘员到指定位置
        private bool TrySpawnCrewAtPosition(Pawn pawn, IntVec3 position)
        {
            Map map = parent.Map;
            if (map == null)
                return false;
                
            try
            {
                // 寻找安全位置
                if (!position.Walkable(map) || position.Fogged(map))
                {
                    CellFinder.TryFindRandomCellNear(position, map, 3, 
                        c => c.Walkable(map) && !c.Fogged(map), 
                        out position);
                }
                
                GenSpawn.Spawn(pawn, position, map, WipeMode.Vanish);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error spawning crew {pawn.LabelShort}: {ex.Message}");
                return false;
            }
        }
        
        // 事件通知
        private void Notify_CrewAdded(Pawn crew)
        {
            if (crew.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    "WULA_CrewBoarded".Translate(crew.LabelShort, parent.LabelShort),
                    parent,
                    MessageTypeDefOf.PositiveEvent
                );
            }
        }
        
        private void Notify_CrewRemoved(Pawn crew)
        {
            if (crew.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    "WULA_CrewExited".Translate(crew.LabelShort, parent.LabelShort),
                    parent,
                    MessageTypeDefOf.NeutralEvent
                );
            }
        }
        
        // Gizmo显示
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 只对玩家派系显示
            if (parent.Faction != Faction.OfPlayer)
                yield break;
                
            // 延迟初始化Gizmo
            if (!gizmosInitialized)
            {
                InitializeGizmos();
                gizmosInitialized = true;
            }
            
            // 登机按钮（如果有空间）
            if (HasRoom)
            {
                yield return cachedBoardGizmo;
            }
            
            // 下机按钮（如果有乘员）
            if (HasCrew)
            {
                yield return cachedExitGizmo;
            }
        }
        
        private void InitializeGizmos()
        {
            // 登机Gizmo
            cachedBoardGizmo = new Command_Action
            {
                defaultLabel = "WULA_BoardCrew".Translate(),
                defaultDesc = "WULA_BoardCrewDesc".Translate(),
                icon = Props.GetBoardCrewIcon(),
                action = OrderAllEligibleToBoard,
                hotKey = KeyBindingDefOf.Misc4
            };
            
            // 下机Gizmo
            cachedExitGizmo = new Command_Action
            {
                defaultLabel = "WULA_ExitCrew".Translate(),
                defaultDesc = "WULA_ExitCrewDesc".Translate(),
                icon = Props.GetExitCrewIcon(),
                action = () => RemoveAllCrew(),
                hotKey = KeyBindingDefOf.Misc5
            };
        }
        
        // 每帧更新
        public override void CompTick()
        {
            base.CompTick();
            
            // 定期检查乘员状态
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                CheckCrewStatus();
            }
        }
        
        private void CheckCrewStatus()
        {
            var crewToRemove = new List<Pawn>();
            
            foreach (var thing in innerContainer)
            {
                if (thing is Pawn pawn)
                {
                    // 检查是否死亡
                    if (pawn.Dead)
                    {
                        crewToRemove.Add(pawn);
                    }
                    
                    // 确保乘员不执行工作
                    pawn.jobs?.StopAll();
                    pawn.pather?.StopDead();
                }
            }
            
            foreach (var pawn in crewToRemove)
            {
                RemoveCrew(pawn);
            }
        }
        
        // 数据保存/加载
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Deep.Look(ref innerContainer, "crewContainer", this);
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // 重置Gizmo状态
                gizmosInitialized = false;
                cachedBoardGizmo = null;
                cachedExitGizmo = null;
            }
        }
        
        // IThingHolder接口实现
        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }
        
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }
        
        // 获取乘员列表
        public IEnumerable<Pawn> GetCrew()
        {
            foreach (var thing in innerContainer)
            {
                if (thing is Pawn pawn)
                    yield return pawn;
            }
        }
        
        // 死亡/销毁时处理
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            // 移除所有乘员
            if (HasCrew)
            {
                RemoveAllCrew();
            }
            
            base.PostDestroy(mode, previousMap);
        }
        
        // 死亡时处理
        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);
            
            // 如果机甲死亡，移除所有乘员
            if (parent is Pawn mech && mech.Dead && HasCrew)
            {
                RemoveAllCrew();
            }
        }
        
        // 绘制效果（可选）
        public override void PostDraw()
        {
            base.PostDraw();
            
            // 可以添加一些视觉效果，比如显示乘员数等
            if (parent.Spawned && HasCrew)
            {
                // 在机甲上方显示乘员数量
                Vector3 drawPos = parent.DrawPos;
                drawPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                
                GenMapUI.DrawThingLabel(drawPos, $"x{CurrentCrewCount}", Color.green);
            }
        }
    }
}

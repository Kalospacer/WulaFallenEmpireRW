using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    public class Building_MechanoidRecycler : Building
    {
        public CompProperties_MechanoidRecycler Props => def.GetCompProperties<CompProperties_MechanoidRecycler>();
        
        // 改为存储计数而不是Pawn实例
        private int storedMechanoidCount = 0;
        private int spawnTick; // 建筑生成的时间点
        
        // 生成队列（存储Pawn生成请求）
        private Queue<PawnGenerationRequest> spawnQueue = new Queue<PawnGenerationRequest>();
        private bool initialUnitsSpawned = false;
        
        public int StoredCount => storedMechanoidCount;
        public int MaxStorage => Props.maxStorageCapacity;
        public bool IsCooldownActive => Find.TickManager.TicksGame - spawnTick < 24 * 2500; // 24小时冷却
        
        // 生成初始单位（改为计数）
        private void SpawnInitialUnits()
        {
            if (initialUnitsSpawned || Props.initialUnits == null)
                return;
                
            foreach (var initialUnit in Props.initialUnits)
            {
                storedMechanoidCount += initialUnit.count;
            }
            
            initialUnitsSpawned = true;
        }
        
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            spawnTick = Find.TickManager.TicksGame;
            
            if (!respawningAfterLoad)
            {
                SpawnInitialUnits();
            }
        }
        
        // 回收机械族（改为增加计数）
        public void AcceptMechanoid(Pawn mech)
        {
            if (storedMechanoidCount >= Props.maxStorageCapacity)
            {
                Messages.Message("回收器已满", MessageTypeDefOf.RejectInput);
                return;
            }
            
            storedMechanoidCount++;
            mech.Destroy(); // 直接销毁，不存储实例
            
            Messages.Message($"机械族 {mech.LabelCap} 已回收 (当前: {storedMechanoidCount}/{Props.maxStorageCapacity})", 
                MessageTypeDefOf.PositiveEvent);
            
            // 通知转换组件存储更新
            var transformComp = this.TryGetComp<CompTransformAtFullCapacity>();
            transformComp?.NotifyStorageUpdated();
        }
        
        // 消耗机械族计数
        public bool ConsumeMechanoids(int count)
        {
            if (storedMechanoidCount < count)
                return false;
                
            storedMechanoidCount -= count;
            return true;
        }
        
        // 设置机械族计数（用于转换恢复）
        public void SetMechanoidCount(int count)
        {
            storedMechanoidCount = Mathf.Clamp(count, 0, Props.maxStorageCapacity);
        }
        
        protected override void Tick()
        {
            base.Tick();
            
            // 处理生成队列
            if (spawnQueue.Count > 0 && Find.TickManager.TicksGame % 10 == 0)
            {
                TrySpawnFromQueue();
            }
        }
        
        // 打开生成界面
        public void OpenSpawnInterface()
        {
            if (storedMechanoidCount == 0)
            {
                Messages.Message("没有可用的机械族进行转换", MessageTypeDefOf.RejectInput);
                return;
            }
            
            List<FloatMenuOption> kindOptions = new List<FloatMenuOption>();
            
            foreach (PawnKindDef kindDef in Props.spawnablePawnKinds)
            {
                kindOptions.Add(new FloatMenuOption(
                    $"{kindDef.LabelCap}",
                    () => TrySpawnMechanoids(kindDef, 1)
                ));
            }
            
            Find.WindowStack.Add(new FloatMenu(kindOptions));
        }
        
        private void TrySpawnMechanoids(PawnKindDef kindDef, int count)
        {
            if (!ConsumeMechanoids(count))
            {
                Messages.Message("机械族数量不足", MessageTypeDefOf.RejectInput);
                return;
            }
            
            for (int i = 0; i < count; i++)
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kindDef,
                    Faction,
                    PawnGenerationContext.NonPlayer,
                    -1,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true
                );
                
                spawnQueue.Enqueue(request);
            }
            
            TrySpawnFromQueue();
            Messages.Message($"正在转换 {count} 个机械族为 {kindDef.LabelCap}", MessageTypeDefOf.PositiveEvent);
        }
        
        private void TrySpawnFromQueue()
        {
            if (spawnQueue.Count == 0)
                return;
                
            int spawnCount = Mathf.Min(spawnQueue.Count, 5);
            for (int i = 0; i < spawnCount; i++)
            {
                if (spawnQueue.Count == 0)
                    break;
                    
                PawnGenerationRequest request = spawnQueue.Dequeue();
                Pawn newMech = PawnGenerator.GeneratePawn(request);
                
                IntVec3 spawnPos = GetSpawnPosition();
                if (spawnPos.IsValid)
                {
                    GenSpawn.Spawn(newMech, spawnPos, Map);
                }
                else
                {
                    GenSpawn.Spawn(newMech, Position, Map);
                }
            }
        }
        
        private IntVec3 GetSpawnPosition()
        {
            for (int i = 1; i <= 3; i++)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(Position, i, true))
                {
                    if (cell.InBounds(Map) && cell.Walkable(Map) && cell.GetFirstPawn(Map) == null)
                        return cell;
                }
            }
            return IntVec3.Invalid;
        }
        
        // 右键菜单选项
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;
            
            // 回收附近机械族按钮
            Command_Action recycleCommand = new Command_Action
            {
                defaultLabel = "回收附近机械族",
                defaultDesc = $"命令附近 {Props.recycleRange} 格内的机械族前来回收",
                icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_RecycleNearbyMechanoids"),
                action = RecycleNearbyMechanoids
            };
            
            if (storedMechanoidCount >= Props.maxStorageCapacity)
            {
                recycleCommand.Disable("储存器已满");
            }
            
            yield return recycleCommand;
            
            // 生成机械族按钮
            Command_Action spawnCommand = new Command_Action
            {
                defaultLabel = "转换机械族",
                defaultDesc = $"将储存的机械族转换为其他单位 (当前: {storedMechanoidCount}/{Props.maxStorageCapacity})",
                icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_ConvertMechanoids"),
                action = OpenSpawnInterface
            };
            
            if (storedMechanoidCount == 0)
            {
                spawnCommand.Disable("没有可用的机械族");
            }
            
            yield return spawnCommand;
        }
        
        // 回收附近机械族
        public void RecycleNearbyMechanoids()
        {
            if (storedMechanoidCount >= Props.maxStorageCapacity)
            {
                Messages.Message("储存器已满", MessageTypeDefOf.RejectInput);
                return;
            }
                
            List<Pawn> nearbyMechs = FindNearbyRecyclableMechanoids();
            
            if (nearbyMechs.Count == 0)
            {
                Messages.Message("附近没有可回收的机械族", MessageTypeDefOf.RejectInput);
                return;
            }
            
            int assignedCount = 0;
            foreach (Pawn mech in nearbyMechs)
            {
                if (StartRecycleJob(mech))
                {
                    assignedCount++;
                }
            }
            
            Messages.Message($"已命令 {assignedCount} 个机械族前来回收", MessageTypeDefOf.PositiveEvent);
        }
        
        private List<Pawn> FindNearbyRecyclableMechanoids()
        {
            List<Pawn> result = new List<Pawn>();
            CellRect searchRect = CellRect.CenteredOn(Position, Props.recycleRange);
            
            foreach (Pawn pawn in Map.mapPawns.AllPawnsSpawned)
            {
                if (searchRect.Contains(pawn.Position) && 
                    IsRecyclableMechanoid(pawn) && 
                    !IsAlreadyGoingToRecycler(pawn) &&
                    pawn.CanReach(this, PathEndMode.InteractionCell, Danger.Some))
                {
                    result.Add(pawn);
                }
            }
            
            return result;
        }
        
        private bool IsRecyclableMechanoid(Pawn pawn)
        {
            return pawn.RaceProps.IsMechanoid && 
                   Props.recyclableRaces.Contains(pawn.def) &&
                   !pawn.Downed && 
                   pawn.Faction == Faction;
        }
        
        private bool IsAlreadyGoingToRecycler(Pawn mech)
        {
            Job curJob = mech.CurJob;
            if (curJob != null && curJob.def == Props.recycleJobDef && curJob.targetA.Thing == this)
                return true;
                
            return false;
        }
        
        private bool StartRecycleJob(Pawn mech)
        {
            if (IsAlreadyGoingToRecycler(mech))
                return false;
                
            Job job = JobMaker.MakeJob(Props.recycleJobDef, this);
            if (mech.jobs.TryTakeOrderedJob(job))
            {
                return true;
            }
            return false;
        }

        public override string GetInspectString()
        {
            StringBuilder stringBuilder = new StringBuilder();
            string baseString = base.GetInspectString();

            if (!string.IsNullOrEmpty(baseString))
            {
                stringBuilder.Append(baseString);
            }

            string storedInfo = $"储存机械族: {storedMechanoidCount}/{Props.maxStorageCapacity}";
            
            if (stringBuilder.Length > 0)
                stringBuilder.AppendLine();
            stringBuilder.Append(storedInfo);
            
            // 显示冷却状态
            if (IsCooldownActive)
            {
                stringBuilder.AppendLine();
                stringBuilder.Append($"转换冷却: {GetRemainingCooldownHours():F1} 小时");
            }

            return stringBuilder.ToString();
        }
        
        public float GetRemainingCooldownHours()
        {
            int remainingTicks = (24 * 2500) - (Find.TickManager.TicksGame - spawnTick);
            return Mathf.Max(0, remainingTicks / 2500f);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Values.Look(ref storedMechanoidCount, "storedMechanoidCount", 0);
            Scribe_Values.Look(ref spawnTick, "spawnTick", 0);
            Scribe_Values.Look(ref initialUnitsSpawned, "initialUnitsSpawned", false);
            Scribe_Collections.Look(ref spawnQueue, "spawnQueue", LookMode.Deep);
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (spawnQueue == null)
                    spawnQueue = new Queue<PawnGenerationRequest>();
            }
        }
    }
}

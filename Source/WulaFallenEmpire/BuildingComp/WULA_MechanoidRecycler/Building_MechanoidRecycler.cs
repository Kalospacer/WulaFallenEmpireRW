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
        // 翻译键定义
        public static class TranslationKeys
        {
            // 消息文本
            public const string NoRecyclableMechanoidsNearby = "WULA_NoRecyclableMechanoidsNearby";
            public const string RecyclerStorageFull = "WULA_RecyclerStorageFull";
            public const string CalledMechanoidsForRecycling = "WULA_CalledMechanoidsForRecycling";
            public const string MechanoidRecycled = "WULA_MechanoidRecycled";
            public const string NoMechanoidsAvailableForConversion = "WULA_NoMechanoidsAvailableForConversion";
            public const string NotEnoughStoredMechanoids = "WULA_NotEnoughStoredMechanoids";
            public const string ConvertingMechanoids = "WULA_ConvertingMechanoids";
            
            // Gizmo 文本
            public const string RecycleNearbyMechanoids = "WULA_RecycleNearbyMechanoids";
            public const string RecycleNearbyMechanoidsDesc = "WULA_RecycleNearbyMechanoidsDesc";
            public const string RecycleNearbyMechanoidsDisabled = "WULA_RecycleNearbyMechanoidsDisabled";
            public const string ConvertMechanoids = "WULA_ConvertMechanoids";
            public const string ConvertMechanoidsDesc = "WULA_ConvertMechanoidsDesc";
            public const string ConvertMechanoidsDisabled = "WULA_ConvertMechanoidsDisabled";
            
            // 检查字符串
            public const string StoredInfo = "WULA_StoredInfo";
        }
        
        public CompProperties_MechanoidRecycler Props => def.GetCompProperties<CompProperties_MechanoidRecycler>();
        
        // 存储的机械族列表
        public List<Pawn> storedMechanoids = new List<Pawn>();
        
        // 生成队列
        private Queue<PawnGenerationRequest> spawnQueue = new Queue<PawnGenerationRequest>();
        
        // 是否已经生成初始单位
        private bool initialUnitsSpawned = false;
        
        // 是否已经执行过归属权转换
        private bool ownershipTransferred = false;
        
        public int StoredCount => storedMechanoids.Count;
        public int MaxStorage => Props.maxStorageCapacity;
        
        // 强制归属权转换
        private void TransferOwnership()
        {
            if (ownershipTransferred)
                return;
                
            // 获取目标派系（默认为玩家派系）
            Faction targetFaction = Props.ownershipFaction ?? Faction.OfPlayer;
            
            if (Faction != targetFaction)
            {
                Log.Message($"[MechanoidRecycler] Transferring ownership from {Faction?.Name ?? "NULL"} to {targetFaction.Name}");
                SetFaction(targetFaction);
            }
            
            ownershipTransferred = true;
        }
        
        // 生成初始单位
        private void SpawnInitialUnits()
        {
            if (initialUnitsSpawned || Props.initialUnits == null || Props.initialUnits.Count == 0)
                return;
                
            foreach (var initialUnit in Props.initialUnits)
            {
                if (storedMechanoids.Count >= MaxStorage)
                    break;
                    
                // 生成初始机械族
                PawnGenerationRequest request = new PawnGenerationRequest(
                    initialUnit.pawnKindDef,
                    Faction, // 使用当前建筑的派系
                    PawnGenerationContext.NonPlayer,
                    -1,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true
                );
                
                Pawn initialMech = PawnGenerator.GeneratePawn(request);
                storedMechanoids.Add(initialMech);
                
                Log.Message($"Mechanoid Recycler spawned initial unit: {initialMech.LabelCap} for faction: {Faction.Name}");
            }
            
            initialUnitsSpawned = true;
        }
        
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            
            // 执行归属权转换
            if (!respawningAfterLoad)
            {
                TransferOwnership();
            }
            
            // 如果不是从存档加载，生成初始单位
            if (!respawningAfterLoad)
            {
                SpawnInitialUnits();
            }
        }
        
        // 回收附近机械族
        public void RecycleNearbyMechanoids()
        {
            if (!CanRecycleNow())
                return;
                
            List<Pawn> nearbyMechs = FindNearbyRecyclableMechanoids();
            
            if (nearbyMechs.Count == 0)
            {
                Messages.Message(TranslationKeys.NoRecyclableMechanoidsNearby.Translate(), MessageTypeDefOf.RejectInput);
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
            
            Messages.Message(TranslationKeys.CalledMechanoidsForRecycling.Translate(assignedCount), MessageTypeDefOf.PositiveEvent);
        }
        
        private bool CanRecycleNow()
        {
            if (storedMechanoids.Count >= Props.maxStorageCapacity)
            {
                return false;
            }
            return true;
        }
        
        private List<Pawn> FindNearbyRecyclableMechanoids()
        {
            List<Pawn> result = new List<Pawn>();
            CellRect searchRect = CellRect.CenteredOn(Position, Props.recycleRange);
            
            foreach (Pawn pawn in Map.mapPawns.AllPawnsSpawned)
            {
                if (searchRect.Contains(pawn.Position) && 
                    IsRecyclableMechanoid(pawn) && 
                    !storedMechanoids.Contains(pawn) &&
                    !IsAlreadyGoingToRecycler(pawn) && // 检查是否已经在前往回收器
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
                   pawn.Faction == Faction; // 使用当前建筑的派系
        }
        
        // 检查机械族是否已经在前往此回收器
        private bool IsAlreadyGoingToRecycler(Pawn mech)
        {
            // 检查当前工作是否是前往此回收器
            Job curJob = mech.CurJob;
            if (curJob != null && curJob.def == Props.recycleJobDef && curJob.targetA.Thing == this)
                return true;
                
            return false;
        }
        
        private bool StartRecycleJob(Pawn mech)
        {
            // 防止重复分配
            if (IsAlreadyGoingToRecycler(mech))
                return false;
                
            Job job = JobMaker.MakeJob(Props.recycleJobDef, this);
            if (mech.jobs.TryTakeOrderedJob(job))
            {
                return true;
            }
            return false;
        }
        
        // 机械族进入建筑
        public void AcceptMechanoid(Pawn mech)
        {
            if (storedMechanoids.Contains(mech))
                return;
                
            if (storedMechanoids.Count >= Props.maxStorageCapacity)
            {
                Messages.Message(TranslationKeys.RecyclerStorageFull.Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            
            storedMechanoids.Add(mech);
            mech.DeSpawn();
            
            Messages.Message(TranslationKeys.MechanoidRecycled.Translate(mech.LabelCap), MessageTypeDefOf.PositiveEvent);
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
            if (storedMechanoids.Count == 0)
            {
                Messages.Message(TranslationKeys.NoMechanoidsAvailableForConversion.Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            
            List<FloatMenuOption> kindOptions = new List<FloatMenuOption>();
            
            foreach (PawnKindDef kindDef in Props.spawnablePawnKinds)
            {
                kindOptions.Add(new FloatMenuOption(
                    kindDef.LabelCap,
                    () => TrySpawnMechanoids(kindDef, 1)
                ));
            }
            
            Find.WindowStack.Add(new FloatMenu(kindOptions));
        }
        
        private void TrySpawnMechanoids(PawnKindDef kindDef, int count)
        {
            if (storedMechanoids.Count < count)
            {
                Messages.Message(TranslationKeys.NotEnoughStoredMechanoids.Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            
            // 消耗存储的机械族并生成
            for (int i = 0; i < count; i++)
            {
                if (storedMechanoids.Count > 0)
                {
                    Pawn consumedMech = storedMechanoids[0];
                    storedMechanoids.RemoveAt(0);
                    
                    if (consumedMech.Spawned)
                        consumedMech.Destroy();
                }
                
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kindDef,
                    Faction, // 使用当前建筑的派系
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
            Messages.Message(TranslationKeys.ConvertingMechanoids.Translate(count, kindDef.LabelCap), MessageTypeDefOf.PositiveEvent);
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
                defaultLabel = TranslationKeys.RecycleNearbyMechanoids.Translate(),
                defaultDesc = TranslationKeys.RecycleNearbyMechanoidsDesc.Translate(Props.recycleRange),
                icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_RecycleNearbyMechanoids"),
                action = RecycleNearbyMechanoids
            };
            
            if (!CanRecycleNow())
            {
                recycleCommand.Disable(TranslationKeys.RecycleNearbyMechanoidsDisabled.Translate());
            }
            
            yield return recycleCommand;
            
            // 生成机械族按钮
            Command_Action spawnCommand = new Command_Action
            {
                defaultLabel = TranslationKeys.ConvertMechanoids.Translate(),
                defaultDesc = TranslationKeys.ConvertMechanoidsDesc.Translate(storedMechanoids.Count, Props.maxStorageCapacity),
                icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_ConvertMechanoids"),
                action = OpenSpawnInterface
            };
            
            if (storedMechanoids.Count == 0)
            {
                spawnCommand.Disable(TranslationKeys.ConvertMechanoidsDisabled.Translate());
            }
            
            yield return spawnCommand;
        }

        public override string GetInspectString()
        {
            StringBuilder stringBuilder = new StringBuilder();
            string baseString = base.GetInspectString();

            if (!string.IsNullOrEmpty(baseString))
            {
                stringBuilder.Append(baseString);
            }

            string storedInfo = TranslationKeys.StoredInfo.Translate(storedMechanoids.Count, Props.maxStorageCapacity);
            
            if (stringBuilder.Length > 0)
                stringBuilder.AppendLine();
            stringBuilder.Append(storedInfo);

            return stringBuilder.ToString();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Collections.Look(ref storedMechanoids, "storedMechanoids", LookMode.Reference);
            Scribe_Collections.Look(ref spawnQueue, "spawnQueue", LookMode.Deep);
            Scribe_Values.Look(ref initialUnitsSpawned, "initialUnitsSpawned", false);
            Scribe_Values.Look(ref ownershipTransferred, "ownershipTransferred", false);
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                storedMechanoids?.RemoveAll(pawn => pawn == null);
                
                if (spawnQueue == null)
                    spawnQueue = new Queue<PawnGenerationRequest>();
            }
        }
    }
}

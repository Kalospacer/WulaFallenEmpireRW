using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace WulaFallenEmpire
{
    public class CompStorageTurret : ThingComp
    {
        public Thing Thing => this.parent;

        private CompProperties_StorageTurret Props => (CompProperties_StorageTurret)this.props;

        // 存储的炮塔列表
        private List<TurretInstance> turrets = new List<TurretInstance>();

        // 标记是否已加载数据
        private bool dataLoaded = false;

        // 获取当前机械族存储数量
        private int StoredMechanoidCount
        {
            get
            {
                var recycler = parent as Building_MechanoidRecycler;
                if (recycler != null)
                {
                    return recycler.StoredCount;
                }

                return 0;
            }
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);

            // 只有在没有加载过数据时才初始化新炮塔
            if (!dataLoaded)
            {
                UpdateTurrets();
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            // 确保数据已加载
            if (!dataLoaded)
            {
                dataLoaded = true;
                InitializeTurretsAfterLoad();
                return;
            }

            // 更新炮塔数量
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                UpdateTurrets();
            }

            // 更新所有炮塔
            for (int i = 0; i < turrets.Count; i++)
            {
                if (i < StoredMechanoidCount)
                {
                    turrets[i].TurretTick();
                }
            }
        }

        // 加载后初始化炮塔
        private void InitializeTurretsAfterLoad()
        {
            if (turrets == null)
            {
                turrets = new List<TurretInstance>();
            }

            // 重新建立 parent 引用
            for (int i = 0; i < turrets.Count; i++)
            {
                if (turrets[i] != null)
                {
                    turrets[i].SetParent(this);
                    turrets[i].SetIndex(i);
                    turrets[i].PostLoadInit();
                }
            }

            // 根据当前机械族数量调整炮塔数量
            UpdateTurrets();
        }

        private void UpdateTurrets()
        {
            int currentCount = Mathf.Min(StoredMechanoidCount, Props.maxTurrets);

            // 添加缺少的炮塔
            while (turrets.Count < currentCount)
            {
                turrets.Add(new TurretInstance(this, turrets.Count));
            }

            // 移除多余的炮塔
            while (turrets.Count > currentCount)
            {
                // 注意：只移除未激活的炮塔
                int lastIndex = turrets.Count - 1;
                if (lastIndex >= StoredMechanoidCount)
                {
                    turrets.RemoveAt(lastIndex);
                }
                else
                {
                    break;
                }
            }
        }

        public override void PostDraw()
        {
            base.PostDraw();

            // 绘制所有激活的炮塔
            for (int i = 0; i < turrets.Count; i++)
            {
                if (i < StoredMechanoidCount)
                {
                    turrets[i].DrawTurret();
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();

            Scribe_Values.Look(ref dataLoaded, "dataLoaded", false);
            Scribe_Collections.Look(ref turrets, "turrets", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // 标记需要重新初始化
                dataLoaded = false;
            }
        }
        public class TurretInstance : IExposable, IAttackTargetSearcher
        {
            // 为每个炮塔实例生成唯一ID
            private int turretID = -1;
            private static int nextTurretID = 0;

            // 存储每个炮塔的目标历史，用于避免重复选择已死亡的目标
            private HashSet<Thing> killedTargets = new HashSet<Thing>();
            private static Dictionary<int, HashSet<Thing>> allKilledTargets = new Dictionary<int, HashSet<Thing>>();

            private CompStorageTurret _parent;
            private int _index;

            // 炮塔状态
            public Thing gun;
            public int burstCooldownTicksLeft;
            public int burstWarmupTicksLeft;
            public LocalTargetInfo currentTarget = LocalTargetInfo.Invalid;
            public float curRotation;
            public Material turretMat;

            // 目标最后可见时间，用于跟踪目标丢失
            private int lastTargetVisibleTick = -1;
            private const int TARGET_LOST_THRESHOLD = 30; // 30 ticks后认为目标丢失

            // 标记是否已初始化
            private bool initialized = false;

            // 安全访问器
            public CompStorageTurret Parent => _parent;
            public int Index => _index;
            public int TurretID => turretID;

            // IAttackTargetSearcher 接口实现
            public Thing Thing => _parent?.parent;
            public Verb CurrentEffectiveVerb => AttackVerb;
            public LocalTargetInfo LastAttackedTarget => LocalTargetInfo.Invalid;
            public int LastAttackTargetTick => -1;
            public Thing TargetCurrentlyAimingAt => currentTarget.Thing;

            private bool WarmingUp => burstWarmupTicksLeft > 0;

            public Verb AttackVerb
            {
                get
                {
                    if (gun == null) return null;
                    var compEq = gun.TryGetComp<CompEquippable>();
                    return compEq?.PrimaryVerb;
                }
            }

            private bool CanShoot
            {
                get
                {
                    if (_parent == null || _parent.parent == null)
                        return false;

                    if (!_parent.parent.Spawned || _parent.parent.Destroyed)
                        return false;

                    if (AttackVerb == null)
                    {
                        // 尝试重新初始化
                        if (!initialized)
                        {
                            PostLoadInit();
                        }
                        return false;
                    }

                    return true;
                }
            }

            // 无参构造函数用于序列化
            public TurretInstance() { }

            public TurretInstance(CompStorageTurret parent, int index)
            {
                SetParent(parent);
                SetIndex(index);

                // 生成唯一ID
                turretID = nextTurretID++;
                Initialize();
            }

            public void SetParent(CompStorageTurret parent)
            {
                _parent = parent;
            }

            public void SetIndex(int index)
            {
                _index = index;
            }

            private void Initialize()
            {
                if (initialized) return;

                MakeGun();
                UpdateGunVerbs();
                initialized = true;

                // 初始化已击杀目标集合
                if (!allKilledTargets.ContainsKey(turretID))
                {
                    allKilledTargets[turretID] = new HashSet<Thing>();
                }
            }

            // 加载后初始化
            public void PostLoadInit()
            {
                if (initialized) return;

                if (gun == null)
                {
                    MakeGun();
                }

                UpdateGunVerbs();
                initialized = true;

                // 初始化已击杀目标集合
                if (!allKilledTargets.ContainsKey(turretID))
                {
                    allKilledTargets[turretID] = new HashSet<Thing>();
                }
            }

            // 清理已击杀目标集合，避免内存泄漏
            public void CleanupKilledTargets()
            {
                if (allKilledTargets.ContainsKey(turretID))
                {
                    // 移除所有已经被销毁的目标
                    killedTargets.RemoveWhere(target => target == null || target.Destroyed);
                    allKilledTargets[turretID] = killedTargets;
                }
            }

            // 添加已击杀目标
            public void AddKilledTarget(Thing target)
            {
                if (target != null)
                {
                    killedTargets.Add(target);
                    if (allKilledTargets.ContainsKey(turretID))
                    {
                        allKilledTargets[turretID] = killedTargets;
                    }

                    // 通知其他炮塔这个目标已被击杀
                    NotifyOtherTurretsTargetKilled(target, this);
                }
            }

            // 检查目标是否已被击杀
            public bool IsTargetKilled(Thing target)
            {
                if (target == null || target.Destroyed)
                    return true;

                // 检查自己的击杀记录
                if (killedTargets.Contains(target))
                    return true;

                // 检查其他炮塔的击杀记录（共享击杀信息）
                foreach (var kvp in allKilledTargets)
                {
                    if (kvp.Key != turretID && kvp.Value.Contains(target))
                        return true;
                }

                return false;
            }

            // 通知其他炮塔目标已被击杀
            private void NotifyOtherTurretsTargetKilled(Thing target, TurretInstance killer)
            {
                if (_parent?.turrets == null) return;

                foreach (var turret in _parent.turrets)
                {
                    if (turret != null && turret != this)
                    {
                        // 如果其他炮塔正在瞄准这个目标，重置它们的目标
                        if (turret.currentTarget.Thing == target)
                        {
                            turret.ResetCurrentTarget();
                        }

                        // 将这个目标添加到它们的击杀记录中
                        if (turret.killedTargets != null)
                        {
                            turret.killedTargets.Add(target);
                        }
                    }
                }
            }

            private void MakeGun()
            {
                if (_parent == null || _parent.Props == null || _parent.Props.turretDef == null)
                    return;

                gun = ThingMaker.MakeThing(_parent.Props.turretDef, null);
            }

            private void UpdateGunVerbs()
            {
                if (gun == null) return;

                var compEq = gun.TryGetComp<CompEquippable>();
                if (compEq == null) return;

                // 确保 parent 不为 null
                if (_parent == null || _parent.parent == null)
                {
                    Log.Warning("[StorageTurret] Parent is null when updating gun verbs");
                    return;
                }

                foreach (var verb in compEq.AllVerbs)
                {
                    // 关键修复：设置正确的 caster
                    verb.caster = _parent.parent;
                    verb.castCompleteCallback = () =>
                    {
                        burstCooldownTicksLeft = AttackVerb?.verbProps?.defaultCooldownTime.SecondsToTicks() ?? 0;

                        // 如果成功攻击并击杀目标，记录它
                        if (currentTarget.Thing != null && currentTarget.Thing.Destroyed)
                        {
                            AddKilledTarget(currentTarget.Thing);
                        }
                    };
                }
            }

            public void TurretTick()
            {
                if (!CanShoot || AttackVerb == null) return;

                // 确保动词已正确初始化
                if (AttackVerb.caster == null)
                {
                    UpdateGunVerbs();
                    return;
                }

                // 清理旧数据
                if (Find.TickManager.TicksGame % 300 == 0)
                {
                    CleanupKilledTargets();
                }

                // 检查当前目标是否仍然有效
                if (currentTarget.IsValid)
                {
                    var targetThing = currentTarget.Thing;

                    // 检查目标是否已被击杀
                    if (targetThing == null || targetThing.Destroyed)
                    {
                        if (targetThing != null)
                        {
                            AddKilledTarget(targetThing);
                        }
                        ResetCurrentTarget();
                    }
                    // 检查目标是否离开视线或死亡
                    else if (!AttackVerb.CanHitTarget(currentTarget) || targetThing.Destroyed)
                    {
                        lastTargetVisibleTick = Find.TickManager.TicksGame;
                        if (Find.TickManager.TicksGame - lastTargetVisibleTick > TARGET_LOST_THRESHOLD)
                        {
                            ResetCurrentTarget();
                        }
                    }
                    else
                    {
                        lastTargetVisibleTick = Find.TickManager.TicksGame;
                    }

                    // 更新炮塔旋转
                    Vector3 targetPos = currentTarget.Cell.ToVector3Shifted();
                    Vector3 turretPos = GetTurretDrawPos();
                    curRotation = (targetPos - turretPos).AngleFlat() + _parent.Props.angleOffset;
                }

                AttackVerb.VerbTick();

                if (AttackVerb.state != VerbState.Bursting)
                {
                    if (WarmingUp)
                    {
                        burstWarmupTicksLeft--;
                        if (burstWarmupTicksLeft == 0)
                        {
                            AttackVerb.TryStartCastOn(currentTarget, false, true, false, true);

                            // 攻击后检查目标是否被击杀
                            if (currentTarget.Thing != null && currentTarget.Thing.Destroyed)
                            {
                                AddKilledTarget(currentTarget.Thing);
                            }
                        }
                    }
                    else
                    {
                        if (burstCooldownTicksLeft > 0)
                        {
                            burstCooldownTicksLeft--;
                        }

                        if (burstCooldownTicksLeft <= 0 && _parent.parent.IsHashIntervalTick(10))
                        {
                            // 使用自定义的目标查找器，避免选择已死亡或被其他炮塔击杀的目标
                            FindNewTarget();
                        }
                    }
                }
            }

            // 自定义目标查找方法
            private void FindNewTarget()
            {
                if (_parent == null || _parent.parent == null || AttackVerb == null)
                    return;

                // 获取所有潜在目标
                List<Thing> potentialTargets = new List<Thing>();
                var map = _parent.parent.Map;

                if (map == null) return;

                // 获取攻击范围内的所有威胁
                var scanRadius = AttackVerb.verbProps.range;
                var center = _parent.parent.Position;

                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn.HostileTo(_parent.parent.Faction) &&
                        !pawn.Dead &&
                        !pawn.Downed &&
                        pawn.Position.DistanceTo(center) <= scanRadius)
                    {
                        // 检查目标是否已被击杀（包括被其他炮塔击杀）
                        if (!IsTargetKilled(pawn) && AttackVerb.CanHitTarget(pawn))
                        {
                            potentialTargets.Add(pawn);
                        }
                    }
                }

                // 优先选择最近的目标
                if (potentialTargets.Count > 0)
                {
                    // 按距离排序
                    potentialTargets.Sort((a, b) =>
                        a.Position.DistanceTo(center).CompareTo(b.Position.DistanceTo(center)));

                    // 随机选择前3个中的1个，避免所有炮塔同时攻击同一个目标
                    int selectFrom = Mathf.Min(3, potentialTargets.Count);
                    Thing selectedTarget = potentialTargets[Rand.Range(0, selectFrom)];

                    currentTarget = selectedTarget;
                    burstWarmupTicksLeft = 1;
                    lastTargetVisibleTick = Find.TickManager.TicksGame;

                    // 记录我们正在瞄准这个目标，避免其他炮塔选择同一个目标
                    if (_parent.turrets != null)
                    {
                        foreach (var turret in _parent.turrets)
                        {
                            if (turret != null && turret != this)
                            {
                                turret.RemoveTargetFromConsideration(selectedTarget);
                            }
                        }
                    }
                }
                else
                {
                    ResetCurrentTarget();
                }
            }

            // 从考虑列表中移除目标
            public void RemoveTargetFromConsideration(Thing target)
            {
                if (currentTarget.Thing == target)
                {
                    ResetCurrentTarget();
                }
            }

            public void ResetCurrentTarget()
            {
                currentTarget = LocalTargetInfo.Invalid;
                burstWarmupTicksLeft = 0;
                lastTargetVisibleTick = -1;
            }

            public void DrawTurret()
            {
                if (_parent == null || _parent.parent == null || !_parent.parent.Spawned)
                    return;

                Vector3 drawPos = GetTurretDrawPos();
                float angle = curRotation;

                if (turretMat == null && _parent.Props?.turretDef?.graphicData?.texPath != null)
                {
                    turretMat = MaterialPool.MatFrom(_parent.Props.turretDef.graphicData.texPath);
                }

                if (turretMat == null) return;

                Matrix4x4 matrix = default(Matrix4x4);
                matrix.SetTRS(drawPos, Quaternion.AngleAxis(angle, Vector3.up), Vector3.one);
                Graphics.DrawMesh(MeshPool.plane10, matrix, turretMat, 0);

                // 调试绘制：显示炮塔ID和当前目标
                if (Prefs.DevMode && DebugSettings.godMode)
                {
                    var screenPos = Camera.main.WorldToScreenPoint(drawPos);
                    if (screenPos.z > 0)
                    {
                        string debugText = $"Turret {turretID}\nTarget: {(currentTarget.IsValid ? currentTarget.Thing.LabelShort : "None")}";
                        Widgets.Label(new Rect(screenPos.x, Screen.height - screenPos.y, 200, 50), debugText);
                    }
                }
            }

            private Vector3 GetTurretDrawPos()
            {
                if (_parent == null || _parent.parent == null)
                    return Vector3.zero;

                // 计算炮塔位置（围绕建筑排列）
                float angle = 360f * _index / _parent.Props.maxTurrets;
                float radius = _parent.Props.turretSpacing;

                Vector3 offset = new Vector3(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                    0,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * radius
                );

                return _parent.parent.DrawPos + offset + new Vector3(0, 0.5f, 0);
            }

            public void ExposeData()
            {
                Scribe_Values.Look(ref turretID, "turretID", -1);
                Scribe_Values.Look(ref burstCooldownTicksLeft, "burstCooldownTicksLeft", 0);
                Scribe_Values.Look(ref burstWarmupTicksLeft, "burstWarmupTicksLeft", 0);
                Scribe_TargetInfo.Look(ref currentTarget, "currentTarget");
                Scribe_Deep.Look(ref gun, "gun");
                Scribe_Values.Look(ref curRotation, "curRotation", 0f);
                Scribe_Values.Look(ref initialized, "initialized", false);
                Scribe_Values.Look(ref lastTargetVisibleTick, "lastTargetVisibleTick", -1);

                // 序列化击杀目标集合
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    // 序列化时清理已销毁的目标
                    killedTargets?.RemoveWhere(target => target == null || target.Destroyed);
                }

                Scribe_Collections.Look(ref killedTargets, "killedTargets", LookMode.Reference);

                // 注意：不序列化 _parent 和 _index，它们在加载后重新设置

                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    if (turretID == -1)
                    {
                        turretID = nextTurretID++;
                    }
                    else
                    {
                        // 确保静态ID计数器至少比最大ID大
                        nextTurretID = Mathf.Max(nextTurretID, turretID + 1);
                    }

                    // 恢复击杀目标集合
                    if (killedTargets == null)
                    {
                        killedTargets = new HashSet<Thing>();
                    }

                    // 清理已销毁的目标
                    killedTargets.RemoveWhere(target => target == null || target.Destroyed);

                    // 更新全局记录
                    if (!allKilledTargets.ContainsKey(turretID))
                    {
                        allKilledTargets[turretID] = killedTargets;
                    }
                }
            }
        }
    }
}

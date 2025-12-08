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

        // 单个炮塔实例类，实现 IAttackTargetSearcher 接口
        public class TurretInstance : IExposable, IAttackTargetSearcher
        {
            private CompStorageTurret _parent;
            private int _index;
            
            // 炮塔状态
            public Thing gun;
            public int burstCooldownTicksLeft;
            public int burstWarmupTicksLeft;
            public LocalTargetInfo currentTarget = LocalTargetInfo.Invalid;
            public float curRotation;
            public Material turretMat;
            
            // 标记是否已初始化
            private bool initialized = false;
            
            // 安全访问器
            public CompStorageTurret Parent => _parent;
            public int Index => _index;
            
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
                
                // 更新炮塔旋转
                if (currentTarget.IsValid)
                {
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
                            // 修复：将 this 作为 IAttackTargetSearcher 传递
                            currentTarget = (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(
                                this, TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable, 
                                null, 0f, 9999f);
                            
                            if (currentTarget.IsValid)
                            {
                                burstWarmupTicksLeft = 1;
                            }
                            else
                            {
                                ResetCurrentTarget();
                            }
                        }
                    }
                }
            }
            
            private void ResetCurrentTarget()
            {
                currentTarget = LocalTargetInfo.Invalid;
                burstWarmupTicksLeft = 0;
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
                Scribe_Values.Look(ref burstCooldownTicksLeft, "burstCooldownTicksLeft", 0);
                Scribe_Values.Look(ref burstWarmupTicksLeft, "burstWarmupTicksLeft", 0);
                Scribe_TargetInfo.Look(ref currentTarget, "currentTarget");
                Scribe_Deep.Look(ref gun, "gun");
                Scribe_Values.Look(ref curRotation, "curRotation", 0f);
                Scribe_Values.Look(ref initialized, "initialized", false);
                
                // 注意：不序列化 _parent 和 _index，它们在加载后重新设置
            }
        }
    }
}

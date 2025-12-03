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
            UpdateTurrets();
        }

        public override void CompTick()
        {
            base.CompTick();
            
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
                turrets.RemoveAt(turrets.Count - 1);
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
            
            // 在保存和加载时重新建立 parent 引用
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe_Collections.Look(ref turrets, "turrets", LookMode.Deep);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Collections.Look(ref turrets, "turrets", LookMode.Deep);
            }
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (turrets == null)
                {
                    turrets = new List<TurretInstance>();
                }
                else
                {
                    // 重新建立 parent 引用
                    for (int i = 0; i < turrets.Count; i++)
                    {
                        if (turrets[i] != null)
                        {
                            turrets[i].SetParent(this);
                            turrets[i].SetIndex(i);
                        }
                    }
                }
                UpdateTurrets();
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
                        return false;
                    
                    if (TurretDestroyed)
                        return false;
                    
                    return true;
                }
            }
            
            public bool TurretDestroyed
            {
                get
                {
                    var verbProps = AttackVerb?.verbProps;
                    if (verbProps == null)
                        return false;
                    
                    // 这里可以添加建筑炮塔的破坏检查逻辑
                    return false;
                }
            }
            
            // 无参构造函数用于序列化
            public TurretInstance() { }
            
            public TurretInstance(CompStorageTurret parent, int index)
            {
                SetParent(parent);
                SetIndex(index);
                MakeGun();
            }
            
            public void SetParent(CompStorageTurret parent)
            {
                _parent = parent;
            }
            
            public void SetIndex(int index)
            {
                _index = index;
            }
            
            private void MakeGun()
            {
                if (_parent == null || _parent.Props == null || _parent.Props.turretDef == null)
                    return;
                    
                gun = ThingMaker.MakeThing(_parent.Props.turretDef, null);
                UpdateGunVerbs();
            }
            
            private void UpdateGunVerbs()
            {
                if (gun == null) return;
                
                var compEq = gun.TryGetComp<CompEquippable>();
                if (compEq == null) return;
                
                foreach (var verb in compEq.AllVerbs)
                {
                    verb.caster = _parent?.parent;
                    verb.castCompleteCallback = () =>
                    {
                        burstCooldownTicksLeft = AttackVerb?.verbProps?.defaultCooldownTime.SecondsToTicks() ?? 0;
                    };
                }
            }
            
            public void TurretTick()
            {
                if (!CanShoot) return;
                
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
                
                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    if (gun == null)
                    {
                        MakeGun();
                    }
                    else
                    {
                        UpdateGunVerbs();
                    }
                }
            }
        }
    }
}

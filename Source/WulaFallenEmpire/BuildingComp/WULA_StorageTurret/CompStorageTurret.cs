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
            Scribe_Collections.Look(ref turrets, "turrets", LookMode.Deep);
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit && turrets == null)
            {
                turrets = new List<TurretInstance>();
            }
        }

        // 单个炮塔实例类，实现 IAttackTargetSearcher 接口
        public class TurretInstance : IExposable, IAttackTargetSearcher
        {
            private CompStorageTurret parent;
            private int index;
            
            // 炮塔状态
            public Thing gun;
            public int burstCooldownTicksLeft;
            public int burstWarmupTicksLeft;
            public LocalTargetInfo currentTarget = LocalTargetInfo.Invalid;
            public float curRotation;
            public Material turretMat;
            
            // IAttackTargetSearcher 接口实现
            public Thing Thing => parent.parent;
            public Verb CurrentEffectiveVerb => AttackVerb;
            public LocalTargetInfo LastAttackedTarget => LocalTargetInfo.Invalid;
            public int LastAttackTargetTick => -1;
            public Thing TargetCurrentlyAimingAt => currentTarget.Thing;
            
            private bool WarmingUp => burstWarmupTicksLeft > 0;
            
            public Verb AttackVerb
            {
                get
                {
                    var compEq = gun?.TryGetComp<CompEquippable>();
                    return compEq?.PrimaryVerb;
                }
            }
            
            private bool CanShoot
            {
                get
                {
                    if (!parent.parent.Spawned || parent.parent.Destroyed)
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
            
            public TurretInstance() { }
            
            public TurretInstance(CompStorageTurret parent, int index)
            {
                this.parent = parent;
                this.index = index;
                MakeGun();
            }
            
            private void MakeGun()
            {
                gun = ThingMaker.MakeThing(parent.Props.turretDef, null);
                UpdateGunVerbs();
            }
            
            private void UpdateGunVerbs()
            {
                var compEq = gun.TryGetComp<CompEquippable>();
                if (compEq == null) return;
                
                foreach (var verb in compEq.AllVerbs)
                {
                    verb.caster = parent.parent;
                    verb.castCompleteCallback = () =>
                    {
                        burstCooldownTicksLeft = AttackVerb.verbProps.defaultCooldownTime.SecondsToTicks();
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
                    curRotation = (targetPos - turretPos).AngleFlat() + parent.Props.angleOffset;
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
                        
                        if (burstCooldownTicksLeft <= 0 && parent.parent.IsHashIntervalTick(10))
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
                Vector3 drawPos = GetTurretDrawPos();
                float angle = curRotation;
                
                if (turretMat == null)
                {
                    turretMat = MaterialPool.MatFrom(parent.Props.turretDef.graphicData.texPath);
                }
                
                Matrix4x4 matrix = default(Matrix4x4);
                matrix.SetTRS(drawPos, Quaternion.AngleAxis(angle, Vector3.up), Vector3.one);
                Graphics.DrawMesh(MeshPool.plane10, matrix, turretMat, 0);
            }
            
            private Vector3 GetTurretDrawPos()
            {
                // 计算炮塔位置（围绕建筑排列）
                float angle = 360f * index / parent.Props.maxTurrets;
                float radius = parent.Props.turretSpacing;
                
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                    0,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * radius
                );
                
                return parent.parent.DrawPos + offset + new Vector3(0, 0.5f, 0);
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

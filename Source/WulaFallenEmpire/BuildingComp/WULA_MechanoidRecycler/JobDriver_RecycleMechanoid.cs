using RimWorld;
using Verse;
using Verse.AI;
using System.Collections.Generic;

namespace WulaFallenEmpire
{
    public class JobDriver_RecycleMechanoid : JobDriver
    {
        private Building_MechanoidRecycler Recycler => job.targetA.Thing as Building_MechanoidRecycler;
        
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        /// <summary>
        /// 更换武器逻辑
        /// </summary>
        private void SwitchWeapon()
        {
            if (pawn == null || pawn.Destroyed || !pawn.Spawned)
                return;

            try
            {
                // 1. 扔掉当前武器
                ThingWithComps currentWeapon = pawn.equipment?.Primary;
                if (currentWeapon != null)
                {
                    // 将武器扔在地上
                    pawn.equipment.TryDropEquipment(currentWeapon, out ThingWithComps droppedWeapon, pawn.Position, true);
                }

                // 2. 从PawnKind允许的武器中生成新武器
                ThingDef newWeaponDef = GetRandomWeaponFromPawnKind();
                if (newWeaponDef != null)
                {
                    // 生成新武器
                    Thing newWeapon = ThingMaker.MakeThing(newWeaponDef);
                    if (newWeapon is ThingWithComps newWeaponWithComps)
                    {
                        // 使用 AddEquipment 方法装备新武器
                        pawn.equipment.AddEquipment(newWeaponWithComps);

                        if (Prefs.DevMode)
                        {
                            WulaLog.Debug($"[CompAutonomousMech] {pawn.LabelCap} equipped new weapon: {newWeaponDef.LabelCap}");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                WulaLog.Debug($"[CompAutonomousMech] Error switching weapon for {pawn?.LabelCap}: {ex}");
            }
        }

        /// <summary>
        /// 从PawnKind允许的武器中随机获取一个武器定义
        /// </summary>
        private ThingDef GetRandomWeaponFromPawnKind()
        {
            if (pawn.kindDef?.weaponTags == null || pawn.kindDef.weaponTags.Count == 0)
                return null;

            // 收集所有匹配的武器
            List<ThingDef> availableWeapons = new List<ThingDef>();

            foreach (string weaponTag in pawn.kindDef.weaponTags)
            {
                foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefs)
                {
                    if (thingDef.IsWeapon && thingDef.weaponTags != null && thingDef.weaponTags.Contains(weaponTag))
                    {
                        availableWeapons.Add(thingDef);
                    }
                }
            }

            if (availableWeapons.Count == 0)
                return null;

            // 随机选择一个武器
            return availableWeapons.RandomElement();
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 关键修改：在任务开始时立即更换武器
            yield return new Toil
            {
                initAction = () =>
                {
                    SwitchWeapon();
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            // 前往回收器
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            // 进入回收器
            yield return new Toil
            {
                initAction = () =>
                {
                    if (Recycler != null)
                    {
                        Recycler.AcceptMechanoid(pawn);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}

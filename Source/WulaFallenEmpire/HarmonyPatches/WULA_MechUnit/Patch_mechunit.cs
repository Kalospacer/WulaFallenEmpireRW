using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace WulaFallenEmpire
{
    [HarmonyPatch(typeof(Pawn), "get_CanTakeOrder")]
    public class Patch_CanTakeOrder
    {
        [HarmonyPostfix]
        public static void postfix(ref bool __result, Pawn __instance)
        {
            if (__instance is Wulamechunit && __instance.Drafted)
            {
                __result = true;
            }
        }
    }
    [HarmonyPatch(typeof(PawnComponentsUtility), "AddAndRemoveDynamicComponents")]
    public class Patch_PawnTracer
    {
        [HarmonyPostfix]
        public static void postfix(Pawn pawn)
        {
            if (pawn is Wulamechunit)
            {
                pawn.drafter = new Pawn_DraftController(pawn);
                pawn.skills = new Pawn_SkillTracker(pawn);
            }
        }
    }
    [HarmonyPatch(typeof(FloatMenuOptionProvider), "SelectedPawnValid")]
    public class Patch_GetSingleOption
    {
        [HarmonyPostfix]
        public static void postfix(ref bool __result, Pawn pawn)
        {
            if (pawn is Wulamechunit)
            {
                __result = true;
            }
        }
    }
    [HarmonyPatch(typeof(FloatMenuUtility), nameof(FloatMenuUtility.GetMeleeAttackAction))]
    public static class Patch_FloatMenuUtility_GetMeleeAttackAction
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, LocalTargetInfo target, out string failStr, ref Action __result, bool ignoreControlled = false)
        {
            failStr = "";

            if (pawn is Wulamechunit)
            {
                // 直接返回 true 跳过控制检查
                ignoreControlled = true;

                // 这里我们直接调用修改后的方法
                __result = ModifiedGetMeleeAttackAction(pawn, target, out failStr, ignoreControlled);
                return false; // 跳过原始方法
            }

            return true; // 没有组件，继续执行原始方法
        }

        private static Action ModifiedGetMeleeAttackAction(Pawn pawn, LocalTargetInfo target, out string failStr, bool ignoreControlled = false)
        {
            failStr = "";

            try
            {
                // 直接使用原始代码，但跳过控制检查
                if (!pawn.Drafted && !ignoreControlled)
                {
                    failStr = "IsNotDraftedLower".Translate(pawn.LabelShort, pawn);
                }
                else if (target.IsValid && !pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                {
                    failStr = "NoPath".Translate();
                }
                else if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    failStr = "IsIncapableOfViolenceLower".Translate(pawn.LabelShort, pawn);
                }
                else if (pawn.meleeVerbs?.TryGetMeleeVerb(target.Thing) == null)
                {
                    failStr = "Incapable".Translate();
                }
                else if (pawn == target.Thing)
                {
                    failStr = "CannotAttackSelf".Translate();
                }
                else if (target.Thing is Pawn targetPawn && (pawn.InSameExtraFaction(targetPawn, ExtraFactionType.HomeFaction) || pawn.InSameExtraFaction(targetPawn, ExtraFactionType.MiniFaction)))
                {
                    failStr = "CannotAttackSameFactionMember".Translate();
                }
                else
                {
                    if (!(target.Thing is Pawn pawn2) || !pawn2.RaceProps.Animal || !HistoryEventUtility.IsKillingInnocentAnimal(pawn, pawn2) || new HistoryEvent(HistoryEventDefOf.KilledInnocentAnimal, pawn.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo())
                    {
                        return delegate
                        {
                            Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                            if (target.Thing is Pawn pawn3)
                            {
                                job.killIncappedTarget = pawn3.Downed;
                            }
                            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                        };
                    }
                    failStr = "IdeoligionForbids".Translate();
                }

                failStr = failStr.CapitalizeFirst();
                return null;
            }
            catch (Exception ex)
            {
                Log.Error($"[IRMF] Error in ModifiedGetMeleeAttackAction: {ex}");
                failStr = "Cannot attack";
                return null;
            }
        }
    }

    [HarmonyPatch(typeof(FloatMenuUtility), nameof(FloatMenuUtility.GetRangedAttackAction))]
    public static class Patch_FloatMenuUtility_GetRangedAttackAction
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, LocalTargetInfo target, out string failStr, ref Action __result)
        {
            failStr = "";

            if (pawn is Wulamechunit)
            {
                // 这里我们直接调用修改后的方法
                __result = ModifiedGetRangedAttackAction(pawn, target, out failStr);
                return false; // 跳过原始方法
            }

            return true; // 没有组件，继续执行原始方法
        }

        private static Action ModifiedGetRangedAttackAction(Pawn pawn, LocalTargetInfo target, out string failStr, bool ignoreControlled = false)
        {
            failStr = "";

            try
            {
                if (pawn.equipment.Primary == null)
                {
                    return null;
                }
                Verb primaryVerb = pawn.equipment.PrimaryEq.PrimaryVerb;
                if (primaryVerb.verbProps.IsMeleeAttack)
                {
                    return null;
                }
                if (!pawn.Drafted)
                {
                    failStr = "IsNotDraftedLower".Translate(pawn.LabelShort, pawn);
                }
                else if (pawn.IsColonyMechPlayerControlled && target.IsValid && !MechanitorUtility.InMechanitorCommandRange(pawn, target))
                {
                    failStr = "OutOfCommandRange".Translate();
                }
                else if (target.IsValid && !pawn.equipment.PrimaryEq.PrimaryVerb.CanHitTarget(target))
                {
                    if (!pawn.Position.InHorDistOf(target.Cell, primaryVerb.EffectiveRange))
                    {
                        failStr = "OutOfRange".Translate();
                    }
                    else
                    {
                        float num = primaryVerb.verbProps.EffectiveMinRange(target, pawn);
                        if ((float)pawn.Position.DistanceToSquared(target.Cell) < num * num)
                        {
                            failStr = "TooClose".Translate();
                        }
                        else
                        {
                            failStr = "CannotHitTarget".Translate();
                        }
                    }
                }
                else if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    failStr = "IsIncapableOfViolenceLower".Translate(pawn.LabelShort, pawn);
                }
                else if (pawn == target.Thing)
                {
                    failStr = "CannotAttackSelf".Translate();
                }
                else if (target.Thing is Pawn target2 && (pawn.InSameExtraFaction(target2, ExtraFactionType.HomeFaction) || pawn.InSameExtraFaction(target2, ExtraFactionType.MiniFaction)))
                {
                    failStr = "CannotAttackSameFactionMember".Translate();
                }
                else if (target.Thing is Pawn victim && HistoryEventUtility.IsKillingInnocentAnimal(pawn, victim) && !new HistoryEvent(HistoryEventDefOf.KilledInnocentAnimal, pawn.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo())
                {
                    failStr = "IdeoligionForbids".Translate();
                }
                else
                {
                    if (!(target.Thing is Pawn pawn2) || pawn.Ideo == null || !pawn.Ideo.IsVeneratedAnimal(pawn2) || new HistoryEvent(HistoryEventDefOf.HuntedVeneratedAnimal, pawn.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo())
                    {
                        return delegate
                        {
                            Job job = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                        };
                    }
                    failStr = "IdeoligionForbids".Translate();
                }
                failStr = failStr.CapitalizeFirst();
                return null;
            }
            catch (Exception ex)
            {
                Log.Error($"[IRMF] Error in ModifiedGetRangeAttackAction: {ex}");
                failStr = "Cannot attack";
                return null;
            }
        }
        [HarmonyPatch(typeof(FloatMenuOptionProvider_Romance), "GetSingleOptionFor")]
        [HarmonyPrefix]
        public static bool GetSingleOptionFor_Prefix(Pawn clickedPawn, ref FloatMenuOption __result)
        {
            if (clickedPawn is Wulamechunit)
            {
                __result = null;
                return false; // 跳过原始方法
            }
            return true; // 继续执行原始方法
        }
    }


    [HarmonyPatch]
    public static class Patch_Pawn_MeleeVerbs_TryMeleeAttack
    {
        // 获取要修补的方法
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            // 查找 Pawn_MeleeVerbs.TryMeleeAttack 方法
            return AccessTools.Method(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.TryMeleeAttack));
        }
        // 前置补丁：在原始方法执行前检查
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result, Pawn_MeleeVerbs __instance, Thing target, Verb verbToUse, bool surpriseAttack)
        {
            try
            {
                // 获取 Pawn
                var pawnField = AccessTools.Field(typeof(Pawn_MeleeVerbs), "pawn");
                if (pawnField == null)
                    return true; // 如果找不到字段，继续执行原方法
                Pawn pawn = pawnField.GetValue(__instance) as Pawn;
                if (pawn == null)
                    return true;
                // 检查是否为机甲
                if (pawn is Wulamechunit)
                {
                    // 检查是否有驾驶员
                    var pilotComp = pawn.TryGetComp<CompMechPilotHolder>();
                    if (pilotComp != null && !pilotComp.HasPilots)
                    {
                        // 没有驾驶员，阻止近战攻击
                        __result = false;
                        return false; // 跳过原始方法
                    }
                }
                return true; // 继续执行原始方法
            }
            catch (Exception ex)
            {
                Log.Error($"[DD] Harmony patch error in TryMeleeAttack: {ex}");
                return true; // 出错时继续执行原始方法
            }
        }
    }
 }
using Verse;
using System; // Add for Activator
using System.Text;
using RimWorld;
using UnityEngine;
using HarmonyLib; // Needed for AccessTools if you use it here directly

namespace WulaFallenEmpire
{
    public class Hediff_DamageShield : HediffWithComps
    {
        // 伤害抵挡层数
        public int ShieldCharges
        {
            get => (int)severityInt;
            set => severityInt = value;
        }

        // 获取或创建 DRMDamageShield 组件
        public DRMDamageShield ShieldComp
        {
            get
            {
                DRMDamageShield comp = pawn.GetComp<DRMDamageShield>();
                if (comp == null)
                {
                    comp = (DRMDamageShield)Activator.CreateInstance(typeof(DRMDamageShield));
                    comp.parent = pawn;
                    comp.props = new DRMCompShieldProp(); // 确保有属性，即使是默认的
                    pawn.AllComps.Add(comp);
                    comp.Initialize(comp.props);
                }
                return comp;
            }
        }


        public override string LabelInBrackets
        {
            get
            {
                if (ShieldCharges > 0)
                {
                    return "层数: " + ShieldCharges;
                }
                return null;
            }
        }

        public override string TipStringExtra
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(base.TipStringExtra);
                if (ShieldCharges > 0)
                {
                    sb.AppendLine("  - 每层抵挡一次伤害。当前层数: " + ShieldCharges);
                }
                else
                {
                    sb.AppendLine("  - 没有可用的抵挡层数。");
                }
                return sb.ToString();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // severityInt 会自动保存，所以不需要额外处理 ShieldCharges
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            // 确保 Pawn 拥有 DRMCompShield 组件
            DRMDamageShield comp = ShieldComp; // 访问属性以确保组件被添加
            if (comp != null)
            {
                comp.IsActive = true; // 激活护盾组件
                // 能量同步将在 Tick() 中完成
            }
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            // 当 Hediff 被移除时，移除对应的 DRMDamageShield 组件
            DRMDamageShield comp = pawn.GetComp<DRMDamageShield>();
            if (comp != null)
            {
                pawn.AllComps.Remove(comp);
                comp.IsActive = false; // 确保禁用
            }
        }

        public override void Tick()
        {
            base.Tick();
            // 如果层数归零，移除 Hediff
            if (ShieldCharges <= 0)
            {
                pawn.health.RemoveHediff(this);
            }
            // 同步能量到 ShieldComp
            DRMDamageShield comp = pawn.GetComp<DRMDamageShield>(); // 每次 Tick 获取，确保是最新的
            if (comp != null && comp.IsActive)
            {
                comp.Energy = ShieldCharges;
                comp.MaxEnergy = (int)def.maxSeverity;
            }
        }
    }
}
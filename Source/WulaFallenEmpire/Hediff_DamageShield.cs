using Verse;
using System.Text;
using RimWorld;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class Hediff_DamageShield : HediffWithComps
    {
        // 伤害抵挡层数
        // 直接将 severityInt 作为 ShieldCharges，这样外部对 severity 的修改会直接影响 ShieldCharges
        public int ShieldCharges
        {
            get => (int)severityInt;
            set => severityInt = value;
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
            // 初始层数由 XML 中的 initialSeverity 控制
            // 如果需要一个固定的初始值，可以在这里设置
            // 例如：如果 hediffDef.initialSeverity 设为 0，这里可以强制给一个默认值
            // 如果 initialSeverity 在 XML 中已经设置为 10，这里就不需要额外处理
        }

        public override void Tick()
        {
            base.Tick();
            // 如果层数归零，移除 Hediff
            if (ShieldCharges <= 0)
            {
                pawn.health.RemoveHediff(this);
            }
        }
    }
}
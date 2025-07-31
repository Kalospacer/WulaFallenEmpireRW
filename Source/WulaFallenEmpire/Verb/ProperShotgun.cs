using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace WulaFallenEmpire
{
    public class Verb_ShootShotgun : Verb_LaunchProjectile
    {
        protected override int ShotsPerBurst
        {
            get
            {
                return this.verbProps.burstShotCount;
            }
        }

        public override void WarmupComplete()
        {
            base.WarmupComplete();
            Pawn pawn = this.currentTarget.Thing as Pawn;
            if (pawn != null && !pawn.Downed && this.CasterIsPawn && this.CasterPawn.skills != null)
            {
                float num = pawn.HostileTo(this.caster) ? 170f : 20f;
                float num2 = this.verbProps.AdjustedFullCycleTime(this, this.CasterPawn);
                this.CasterPawn.skills.Learn(SkillDefOf.Shooting, num * num2, false, false);
            }
        }

        protected override bool TryCastShot()
        {
            bool flag = base.TryCastShot();
            if (flag && this.CasterIsPawn)
            {
                this.CasterPawn.records.Increment(RecordDefOf.ShotsFired);
            }
            ShotgunExtension shotgunExtension = ShotgunExtension.Get(this.verbProps.defaultProjectile);
            if (flag && shotgunExtension.pelletCount - 1 > 0)
            {
                for (int i = 0; i < shotgunExtension.pelletCount - 1; i++)
                {
                    base.TryCastShot();
                }
            }
            return flag;
        }
    }

    public class ShotgunExtension : DefModExtension
    {
        public static ShotgunExtension Get(Def def)
        {
            return def.GetModExtension<ShotgunExtension>() ?? ShotgunExtension.defaultValues;
        }

        private static readonly ShotgunExtension defaultValues = new ShotgunExtension();

        public int pelletCount = 1;
    }
}

using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public class CompProperties_AbilityLaunchMultiProjectile : CompProperties_AbilityLaunchProjectile
    {
        public int numProjectiles = 1;

        public CompProperties_AbilityLaunchMultiProjectile()
        {
            compClass = typeof(CompAbilityEffect_LaunchMultiProjectile);
        }
    }

    public class CompAbilityEffect_LaunchMultiProjectile : CompAbilityEffect
    {
        public new CompProperties_AbilityLaunchMultiProjectile Props => (CompProperties_AbilityLaunchMultiProjectile)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            for (int i = 0; i < Props.numProjectiles; i++)
            {
                LaunchProjectile(target);
            }
        }

        private void LaunchProjectile(LocalTargetInfo target)
        {
            if (Props.projectileDef != null)
            {
                Pawn pawn = parent.pawn;
                Projectile projectile = (Projectile)GenSpawn.Spawn(Props.projectileDef, pawn.Position, pawn.Map);
                projectile.Launch(pawn, pawn.DrawPos, target, target, ProjectileHitFlags.IntendedTarget, parent.verb.preventFriendlyFire);
            }
        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            return target.Pawn != null;
        }
    }
}
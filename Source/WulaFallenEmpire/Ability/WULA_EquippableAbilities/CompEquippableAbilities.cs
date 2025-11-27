using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace WulaFallenEmpire
{
    public class CompEquippableAbilities : ThingComp
    {
        public CompProperties_EquippableAbilities Props => (CompProperties_EquippableAbilities)props;
        
        private Pawn wearer;
        private List<Ability> grantedAbilities = new List<Ability>();

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            wearer = pawn;
            GrantAbilities(pawn);
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            RemoveAbilities(pawn);
            wearer = null;
        }

        private void GrantAbilities(Pawn pawn)
        {
            if (pawn.abilities == null || Props.abilityDefs == null)
                return;

            foreach (AbilityDef abilityDef in Props.abilityDefs)
            {
                if (!pawn.abilities.abilities.Any(a => a.def == abilityDef))
                {
                    Ability ability = new Ability(pawn, abilityDef);
                    pawn.abilities.GainAbility(abilityDef);
                    grantedAbilities.Add(ability);
                }
            }
        }

        private void RemoveAbilities(Pawn pawn)
        {
            if (pawn.abilities == null)
                return;

            foreach (Ability ability in grantedAbilities)
            {
                pawn.abilities.RemoveAbility(ability.def);
            }
            grantedAbilities.Clear();
        }

        public override string CompInspectStringExtra()
        {
            if (Props.abilityDefs != null && Props.abilityDefs.Count > 0)
            {
                return $"授予技能: {Props.abilityDefs.Count}";
            }
            return base.CompInspectStringExtra();
        }
    }
}

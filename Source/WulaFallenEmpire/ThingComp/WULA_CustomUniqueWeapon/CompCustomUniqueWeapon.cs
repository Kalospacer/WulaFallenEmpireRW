using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace WulaFallenEmpire
{
    public class CompCustomUniqueWeapon : CompUniqueWeapon
    {
        // 使用 'new' 关键字来明确隐藏基类成员，解决 CS0108 警告
        public new CompProperties_CustomUniqueWeapon Props => (CompProperties_CustomUniqueWeapon)props;

        private List<WeaponTraitDef> customTraits = new List<WeaponTraitDef>();

        // 使用 'new' 关键字隐藏基类属性，解决 CS0506 错误
        public new List<WeaponTraitDef> TraitsListForReading => customTraits;

        // PostExposeData 是 virtual 的，保留 override
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref customTraits, "customTraits", LookMode.Def);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (customTraits == null) customTraits = new List<WeaponTraitDef>();
                SetupCustomTraits(fromSave: true);
            }
        }

        // PostPostMake 是 virtual 的，保留 override
        public override void PostPostMake()
        {
            InitializeCustomTraits();
            if (parent.TryGetComp<CompQuality>(out var comp))
            {
                comp.SetQuality(QualityUtility.GenerateQuality(QualityGenerator.Super), ArtGenerationContext.Outsider);
            }
        }

        private void InitializeCustomTraits()
        {
            if (customTraits == null) customTraits = new List<WeaponTraitDef>();
            customTraits.Clear();

            if (Props.forcedTraits != null)
            {
                foreach (var traitToForce in Props.forcedTraits)
                {
                    if (customTraits.All(t => !t.Overlaps(traitToForce)))
                    {
                        customTraits.Add(traitToForce);
                    }
                }
            }

            IntRange traitRange = Props.numTraitsRange ?? new IntRange(1, 3);
            int totalTraitsTarget = Mathf.Max(customTraits.Count, traitRange.RandomInRange);
            int missingTraits = totalTraitsTarget - customTraits.Count;

            if (missingTraits > 0)
            {
                // CanAddTrait 现在是我们自己的 'new' 方法
                IEnumerable<WeaponTraitDef> possibleTraits = DefDatabase<WeaponTraitDef>.AllDefs.Where(CanAddTrait);
                for (int i = 0; i < missingTraits; i++)
                {
                    if (!possibleTraits.Any()) break;

                    var chosenTrait = possibleTraits.RandomElementByWeight(t => t.commonality);
                    customTraits.Add(chosenTrait);

                    possibleTraits = possibleTraits.Where(t => t != chosenTrait && !t.Overlaps(chosenTrait));
                }
            }

            SetupCustomTraits(fromSave: false);
        }

        private void SetupCustomTraits(bool fromSave)
        {
            foreach (WeaponTraitDef trait in customTraits)
            {
                if (trait.abilityProps != null && parent.GetComp<CompEquippableAbilityReloadable>() is CompEquippableAbilityReloadable comp)
                {
                    comp.props = trait.abilityProps;
                    if (!fromSave)
                    {
                        comp.Notify_PropsChanged();
                    }
                }
            }
        }

        // 使用 'new' 关键字隐藏基类方法，解决 CS0506 错误
        public new bool CanAddTrait(WeaponTraitDef trait)
        {
            if (customTraits.Any(t => t == trait || t.Overlaps(t)))
                return false;

            if (Props.weaponCategories != null && Props.weaponCategories.Any() && !Props.weaponCategories.Contains(trait.weaponCategory))
                return false;

            if (customTraits.Count == 0 && !trait.canGenerateAlone)
                return false;

            return true;
        }

        // --- 下面的方法都是 virtual 的，保留 override ---

        public override string TransformLabel(string label) => label;
        public override Color? ForceColor() => null;

        public override float GetStatOffset(StatDef stat) => customTraits.Sum(t => t.statOffsets.GetStatOffsetFromList(stat));
        public override float GetStatFactor(StatDef stat) => customTraits.Aggregate(1f, (current, t) => current * t.statFactors.GetStatFactorFromList(stat));

        public override string CompInspectStringExtra()
        {
            if (customTraits.NullOrEmpty()) return null;
            return "WeaponTraits".Translate() + ": " + customTraits.Select(t => t.label).ToCommaList().CapitalizeFirst();
        }

        public override string CompTipStringExtra()
        {
            if (customTraits.NullOrEmpty()) return base.CompTipStringExtra();
            return "WeaponTraits".Translate() + ": " + customTraits.Select(t => t.label).ToCommaList().CapitalizeFirst();
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            if (customTraits.NullOrEmpty()) yield break;

            var builder = new StringBuilder();
            builder.AppendLine("Stat_ThingUniqueWeaponTrait_Desc".Translate());
            builder.AppendLine();

            for (int i = 0; i < customTraits.Count; i++)
            {
                WeaponTraitDef trait = customTraits[i];
                builder.AppendLine(trait.LabelCap.Colorize(ColorLibrary.Yellow));
                builder.AppendLine(trait.description);
                if (i < customTraits.Count - 1) builder.AppendLine();
            }

            yield return new StatDrawEntry(
                parent.def.IsMeleeWeapon ? StatCategoryDefOf.Weapon_Melee : StatCategoryDefOf.Weapon_Ranged,
                "Stat_ThingUniqueWeaponTrait_Label".Translate(),
                customTraits.Select(t => t.label).ToCommaList().CapitalizeFirst(),
                builder.ToString(),
                1104);
        }
    }
}
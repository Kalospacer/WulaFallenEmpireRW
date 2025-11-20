using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    // Gizmo 类保持不变...
    [StaticConstructorOnStartup]
    public class Gizmo_AreaShieldStatus : Gizmo
    {
        public ThingComp_AreaShield shield;
        private static readonly Texture2D FullShieldBarTex = SolidColorMaterials.NewSolidColorMaterial(new Color(0.2f, 0.8f, 0.85f), ShaderDatabase.MetaOverlay).mainTexture as Texture2D;
        private static readonly Texture2D EmptyShieldBarTex = SolidColorMaterials.NewSolidColorMaterial(new Color(0.2f, 0.2f, 0.24f), ShaderDatabase.MetaOverlay).mainTexture as Texture2D;

        public override float GetWidth(float maxWidth) => 140f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Rect rect2 = rect.ContractedBy(6f);
            Widgets.DrawWindowBackground(rect);

            Rect labelRect = rect2;
            labelRect.height = rect.height / 2f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(labelRect, shield.parent.LabelCap);

            Rect barRect = rect2;
            barRect.yMin = rect2.y + rect2.height / 2f;
            float fillPercent = (float)shield.currentHitPoints / shield.HitPointsMax;
            Widgets.FillableBar(barRect, fillPercent, FullShieldBarTex, EmptyShieldBarTex, false);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            TaggedString statusText = shield.IsOnCooldown ? "ShieldOnCooldown".Translate() : new TaggedString(shield.currentHitPoints + " / " + shield.HitPointsMax);
            Widgets.Label(barRect, statusText);

            Text.Anchor = TextAnchor.UpperLeft;

            return new GizmoResult(GizmoState.Clear);
        }
    }
}

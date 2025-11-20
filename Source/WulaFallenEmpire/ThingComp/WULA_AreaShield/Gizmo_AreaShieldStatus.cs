using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    [StaticConstructorOnStartup]
    public class Gizmo_AreaShieldStatus : Gizmo
    {
        public ThingComp_AreaShield shield;
        private static readonly Texture2D FullShieldBarTex = SolidColorMaterials.NewSolidColorMaterial(new Color(0.2f, 0.8f, 0.85f), ShaderDatabase.MetaOverlay).mainTexture as Texture2D;
        private static readonly Texture2D EmptyShieldBarTex = SolidColorMaterials.NewSolidColorMaterial(new Color(0.2f, 0.2f, 0.24f), ShaderDatabase.MetaOverlay).mainTexture as Texture2D;
        // 新增：移动状态的颜色
        private static readonly Texture2D MovingShieldBarTex = SolidColorMaterials.NewSolidColorMaterial(new Color(0.5f, 0.5f, 0.5f), ShaderDatabase.MetaOverlay).mainTexture as Texture2D;

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

            // 修改：根据状态选择不同的状态条
            Texture2D barTex;
            TaggedString statusText;

            if (shield.IsOnCooldown)
            {
                barTex = EmptyShieldBarTex;
                statusText = "ShieldOnCooldown".Translate();
            }
            else if (shield.IsWearerMoving)
            {
                // 移动时显示灰色状态条和"移动中"文本
                barTex = MovingShieldBarTex;
                statusText = "ShieldOfflineByMoving".Translate(); // 你可以根据需要修改这个文本
            }
            else
            {
                barTex = FullShieldBarTex;
                statusText = new TaggedString(shield.currentHitPoints + " / " + shield.HitPointsMax);
            }

            Widgets.FillableBar(barRect, fillPercent, barTex, EmptyShieldBarTex, false);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, statusText);
            Text.Anchor = TextAnchor.UpperLeft;

            return new GizmoResult(GizmoState.Clear);
        }
    }
}

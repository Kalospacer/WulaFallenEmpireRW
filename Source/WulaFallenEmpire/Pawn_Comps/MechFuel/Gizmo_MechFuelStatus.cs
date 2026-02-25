// Gizmo_MechFuelStatus.cs
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    [StaticConstructorOnStartup]
    public class Gizmo_MechFuelStatus : Gizmo
    {
        public CompMechFuel fuelComp;
        
        private static readonly Texture2D FullFuelBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.1f, 0.6f, 0.9f));
        private static readonly Texture2D EmptyFuelBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.2f, 0.24f));
        private static readonly Texture2D WarningFuelBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.9f, 0.6f, 0.1f));
        
        // 测试模式图标
        private static readonly Texture2D DebugIcon = ContentFinder<Texture2D>.Get("UI/Commands/Debug", false);
        
        public Gizmo_MechFuelStatus(CompMechFuel fuelComp)
        {
            this.fuelComp = fuelComp;
            Order = -90f; // 在护盾Gizmo之后显示
        }
        
        public override float GetWidth(float maxWidth)
        {
            return 140f;
        }
        
        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Rect rect2 = rect.ContractedBy(6f);
            
            Widgets.DrawWindowBackground(rect);
            
            // 在 God Mode 下显示调试图标
            if (DebugSettings.godMode && DebugIcon != null)
            {
                Rect debugIconRect = new Rect(rect.x + 5f, rect.y + 5f, 12f, 12f);
                GUI.DrawTexture(debugIconRect, DebugIcon);
            }
            
            // 标题区域
            Rect titleRect = rect2;
            titleRect.height = rect.height / 2f;
            Text.Font = GameFont.Tiny;
            
            // 在 God Mode 下显示"调试模式"标题
            string title = DebugSettings.godMode ? 
                "WULA_MechFuel".Translate().Resolve() + " [DEBUG]" : 
                "WULA_MechFuel".Translate().Resolve();
            
            Widgets.Label(titleRect, title);
            
            // 燃料条区域
            Rect barRect = rect2;
            barRect.yMin = rect2.y + rect2.height / 2f;
            
            // 选择燃料条颜色（低燃料时用警告色）
            Texture2D barTex;
            if (fuelComp.FuelPercent < 0.2f)
            {
                barTex = WarningFuelBarTex;
            }
            else
            {
                barTex = FullFuelBarTex;
            }
            
            // 绘制燃料条
            Widgets.FillableBar(barRect, fuelComp.FuelPercent, barTex, EmptyFuelBarTex, doBorder: false);
            
            // 绘制燃料数值
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, 
                fuelComp.Fuel.ToString("F0") + " / " + 
                fuelComp.Props.fuelCapacity.ToString("F0") + 
                " (" + (fuelComp.FuelPercent * 100f).ToString("F0") + "%)");
            Text.Anchor = TextAnchor.UpperLeft;
            
            // 状态文本
            if (fuelComp.IsShutdown)
            {
                Rect statusRect = new Rect(barRect.x, barRect.y - 15f, barRect.width, 15f);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperCenter;
                GUI.color = Color.red;
                Widgets.Label(statusRect, "WULA_Shutdown".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            
            // 工具提示
            string tip = "WULA_MechFuelTip".Translate(
                fuelComp.FuelPercent.ToStringPercent(),
                fuelComp.Props.dailyFuelConsumption,
                fuelComp.Props.fuelType.label
            );
            
            if (fuelComp.IsShutdown)
            {
                tip += "\n\n" + "WULA_ShutdownTip".Translate();
            }
            else if (fuelComp.NeedsRefueling)
            {
                tip += "\n\n" + "WULA_NeedsRefueling".Translate();
            }
            
            // 在 God Mode 下添加调试信息到工具提示
            if (DebugSettings.godMode)
            {
                tip += "\n\n" + "WULA_Debug_Tip".Translate().Colorize(Color.gray) + 
                    "\n" + "WULA_Debug_Status".Translate(
                        fuelComp.IsShutdown ? "WULA_Shutdown".Translate() : "WULA_Running".Translate(),
                        fuelComp.HasPilot() ? "WULA_HasPilot".Translate() : "WULA_NoPilot".Translate()
                    );
            }
            
            TooltipHandler.TipRegion(rect2, tip);
            
            return new GizmoResult(GizmoState.Clear);
        }
    }
}

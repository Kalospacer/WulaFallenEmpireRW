using RimWorld;
using UnityEngine;
using Verse;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;

namespace WulaFallenEmpire
{
    // 自定义属性组件，用于存储半径和颜色信息
    public class CompProperties_CustomRadius : CompProperties
    {
        public float radius = 10f;
        public Color color = new Color(0.8f, 0.8f, 0.4f); // 默认浅黄色
        public float radiusOffset = -2.1f; // 半径偏移量，与原始保持一致
        public bool showInGUI = true; // 是否在GUI中显示切换选项
        public string label = "Show Radius"; // 直接定义标签文本
        public string description = "Toggle visibility of the custom radius overlay."; // 直接定义描述文本
        public bool defaultVisible = true; // 默认是否可见

        public CompProperties_CustomRadius()
        {
            this.compClass = typeof(CompCustomRadius);
        }
    }

    // 实际的组件类
    public class CompCustomRadius : ThingComp
    {
        private bool radiusVisible = true;

        public CompProperties_CustomRadius Props
        {
            get
            {
                return (CompProperties_CustomRadius)this.props;
            }
        }

        public float EffectiveRadius
        {
            get
            {
                return Props.radius + Props.radiusOffset;
            }
        }

        public bool RadiusVisible
        {
            get { return radiusVisible && Props.showInGUI; }
            set { radiusVisible = value; }
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            radiusVisible = Props.defaultVisible;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref radiusVisible, "radiusVisible", Props.defaultVisible);
        }

        // 在检视面板中显示切换选项
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Props.showInGUI) yield break;

            // 只对玩家所有的物体显示Gizmo
            if (parent.Faction != Faction.OfPlayer)
                yield break;

            // 创建切换 gizmo
            Command_Toggle toggleCommand = new Command_Toggle();
            toggleCommand.defaultLabel = Props.label;
            toggleCommand.defaultDesc = Props.description;
            
            // 尝试加载图标，如果失败则使用默认图标
            try
            {
                toggleCommand.icon = ContentFinder<Texture2D>.Get("Wula/UI/Commands/WULA_ShowRadius", false);
                if (toggleCommand.icon == null)
                {
                    // 使用一个简单的占位符图标
                    toggleCommand.icon = BaseContent.BadTex;
                }
            }
            catch
            {
                toggleCommand.icon = BaseContent.BadTex;
            }
            
            toggleCommand.isActive = () => RadiusVisible;
            toggleCommand.toggleAction = () => RadiusVisible = !RadiusVisible;

            yield return toggleCommand;
        }

        // 获取绘制颜色（考虑透明度等）
        public Color GetDrawColor()
        {
            return Props.color;
        }

        // 检查是否应该绘制半径
        public bool ShouldDrawRadius()
        {
            // 只绘制玩家所有的物体
            if (parent.Faction != Faction.OfPlayer)
                return false;

            // 检查是否在地图视图中（不在世界地图）
            if (Find.CurrentMap == null || WorldRendererUtility.WorldRendered)
                return false;

            return RadiusVisible;
        }
    }

    // 自定义放置工作器
    public class PlaceWorker_CustomRadius : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            // 检查是否在地图视图中
            if (Find.CurrentMap == null || WorldRendererUtility.WorldRendered)
                return;

            // 如果已经有物体存在，则检查其组件的可见性设置
            if (thing != null)
            {
                CompCustomRadius comp = thing.TryGetComp<CompCustomRadius>();
                if (comp == null || !comp.ShouldDrawRadius())
                    return;
            }

            // 获取自定义半径组件属性
            CompProperties_CustomRadius compProperties = def.GetCompProperties<CompProperties_CustomRadius>();
            if (compProperties != null && compProperties.showInGUI)
            {
                float effectiveRadius = compProperties.radius + compProperties.radiusOffset;
                if (effectiveRadius > 0f)
                {
                    // 使用指定的颜色绘制圆环
                    Color drawColor = compProperties.color;
                    if (thing != null)
                    {
                        CompCustomRadius comp = thing.TryGetComp<CompCustomRadius>();
                        if (comp != null)
                        {
                            drawColor = comp.GetDrawColor();
                        }
                    }
                    GenDraw.DrawRadiusRing(center, effectiveRadius, drawColor);
                }
            }
        }

        // 可选：在验证放置位置时也考虑半径
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            // 这里可以添加额外的放置验证逻辑
            // 例如检查半径内是否有不允许的建筑等
            
            return true; // 默认允许放置
        }
    }

    // 为已放置的建筑添加绘制支持
    [StaticConstructorOnStartup]
    public static class CustomRadiusRenderer
    {
        static CustomRadiusRenderer()
        {
            try
            {
                // 使用Harmony为MapInterface.MapInterfaceUpdate方法添加补丁
                var harmony = new Harmony("WulaFallenEmpire.CustomRadius");
                
                // 尝试不同的绘制方法
                var mapInterfaceMethod = AccessTools.Method(typeof(MapInterface), "MapInterfaceUpdate");
                if (mapInterfaceMethod != null)
                {
                    harmony.Patch(mapInterfaceMethod,
                        postfix: new HarmonyMethod(typeof(CustomRadiusRenderer), nameof(Postfix_MapInterfaceUpdate)));
                }
                else
                {
                    Log.Warning("[CustomRadius] Could not find MapInterface.MapInterfaceUpdate method");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[CustomRadius] Error in static constructor: {ex}");
            }
        }

        public static void Postfix_MapInterfaceUpdate()
        {
            try
            {
                // 检查是否在地图视图中（不在世界地图）
                if (Find.CurrentMap == null || WorldRendererUtility.WorldRendered)
                    return;

                // 绘制所有带有自定义半径组件的已放置建筑
                foreach (var thing in Find.CurrentMap.listerThings.AllThings)
                {
                    // 只绘制玩家所有的物体
                    if (thing.Faction != Faction.OfPlayer)
                        continue;

                    if (thing.Spawned && thing.def.HasComp(typeof(CompCustomRadius)))
                    {
                        CompCustomRadius comp = thing.TryGetComp<CompCustomRadius>();
                        if (comp != null && comp.ShouldDrawRadius())
                        {
                            float effectiveRadius = comp.EffectiveRadius;
                            if (effectiveRadius > 0f)
                            {
                                GenDraw.DrawRadiusRing(thing.Position, effectiveRadius, comp.GetDrawColor());
                            }
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // 静默处理错误
            }
        }
    }

    // 为建筑选择时添加绘制支持
    [StaticConstructorOnStartup]
    public static class CustomRadiusSelectionRenderer
    {
        static CustomRadiusSelectionRenderer()
        {
            try
            {
                var harmony = new Harmony("WulaFallenEmpire.CustomRadiusSelection");
                
                // 尝试为选择器绘制方法添加补丁
                var selectionDrawMethod = AccessTools.Method(typeof(SelectionDrawer), "DrawSelectionOverlays");
                if (selectionDrawMethod != null)
                {
                    harmony.Patch(selectionDrawMethod,
                        postfix: new HarmonyMethod(typeof(CustomRadiusSelectionRenderer), nameof(Postfix_DrawSelectionOverlays)));
                }
                else
                {
                    Log.Warning("[CustomRadius] Could not find SelectionDrawer.DrawSelectionOverlays method");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[CustomRadius] Error in static constructor: {ex}");
            }
        }

        public static void Postfix_DrawSelectionOverlays()
        {
            try
            {
                // 检查是否在地图视图中（不在世界地图）
                if (Find.CurrentMap == null || WorldRendererUtility.WorldRendered)
                    return;

                if (Find.Selector == null) return;

                foreach (object selected in Find.Selector.SelectedObjectsListForReading)
                {
                    if (selected is Thing thing && thing.Spawned && thing.def.HasComp(typeof(CompCustomRadius)))
                    {
                        // 只绘制玩家所有的物体
                        if (thing.Faction != Faction.OfPlayer)
                            continue;

                        CompCustomRadius comp = thing.TryGetComp<CompCustomRadius>();
                        if (comp != null && comp.ShouldDrawRadius())
                        {
                            float effectiveRadius = comp.EffectiveRadius;
                            if (effectiveRadius > 0f)
                            {
                                GenDraw.DrawRadiusRing(thing.Position, effectiveRadius, comp.GetDrawColor());
                            }
                        }
                    }
                }
            }
            catch
            {
                // 静默处理错误
            }
        }
    }
}

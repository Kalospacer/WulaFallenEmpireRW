using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using System.Linq;

namespace WulaFallenEmpire
{
    public class Building_ExtraGraphics : Building
    {
        // 通过 ModExtension 配置的图形数据
        private ExtraGraphicsExtension modExtension;
        
        // 图形缓存
        private Dictionary<string, Graphic> graphicsCache = new Dictionary<string, Graphic>();
        
        // 动画状态 - 每个图层的独立浮动
        private Dictionary<int, float> layerHoverOffsets = new Dictionary<int, float>();
        private Dictionary<int, float> layerAnimationTimes = new Dictionary<int, float>();
        private int lastTick = -1;

        public ExtraGraphicsExtension ModExtension
        {
            get
            {
                if (modExtension == null)
                {
                    modExtension = def.GetModExtension<ExtraGraphicsExtension>();
                    if (modExtension == null)
                    {
                        Log.Error($"Building_ExtraGraphics: No ExtraGraphicsExtension found for {def.defName}");
                        // 创建默认配置避免空引用
                        modExtension = new ExtraGraphicsExtension();
                    }
                }
                return modExtension;
            }
        }

        // 重写 Graphic 属性返回 null，完全自定义渲染
        public override Graphic Graphic => null;

        // 获取缓存的图形
        private Graphic GetCachedGraphic(string texturePath, Vector2 scale, Color color)
        {
            string cacheKey = $"{texturePath}_{scale.x}_{scale.y}_{color}";
            
            if (!graphicsCache.TryGetValue(cacheKey, out Graphic graphic))
            {
                graphic = GraphicDatabase.Get<Graphic_Single>(
                    texturePath, 
                    ShaderDatabase.TransparentPostLight, 
                    scale, 
                    color);
                graphicsCache[cacheKey] = graphic;
            }
            
            return graphic;
        }

        // 完全重写 DrawAt 方法，实现自定义渲染系统
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            // 不调用基类的 DrawAt，完全自定义渲染
            
            // 更新悬浮动画
            UpdateHoverAnimation();

            // 绘制所有配置的图形层
            DrawGraphicLayers(drawLoc, flip);
        }

        // 绘制所有图形层
        private void DrawGraphicLayers(Vector3 baseDrawPos, bool flip)
        {
            if (ModExtension.graphicLayers == null || ModExtension.graphicLayers.Count == 0)
            {
                Log.Warning($"Building_ExtraGraphics: No graphic layers configured for {def.defName}");
                return;
            }

            // 按层级排序，确保正确的绘制顺序
            var sortedLayers = ModExtension.graphicLayers.OrderBy(layer => layer.drawOrder).ToList();

            foreach (var layer in sortedLayers)
            {
                DrawGraphicLayer(baseDrawPos, flip, layer);
            }
        }

        // 绘制单个图形层
        private void DrawGraphicLayer(Vector3 baseDrawPos, bool flip, GraphicLayerData layer)
        {
            if (string.IsNullOrEmpty(layer.texturePath))
            {
                Log.Warning($"Building_ExtraGraphics: Empty texture path in layer for {def.defName}");
                return;
            }

            // 获取图形
            Graphic graphic = GetCachedGraphic(layer.texturePath, layer.scale, layer.color);

            // 计算图层浮动偏移
            float hoverOffset = 0f;
            if (layer.enableHover)
            {
                int layerIndex = ModExtension.graphicLayers.IndexOf(layer);
                if (layerHoverOffsets.ContainsKey(layerIndex))
                {
                    hoverOffset = layerHoverOffsets[layerIndex];
                }
            }

            // 最终绘制位置 = 基础位置 + 图层偏移 + 浮动偏移
            Vector3 drawPos = baseDrawPos + layer.offset;
            drawPos.z += hoverOffset;

            // 绘制图形
            graphic.Draw(drawPos, flip ? base.Rotation.Opposite : base.Rotation, this, 0f);
        }

        // 更新每个图层的独立悬浮动画
        private void UpdateHoverAnimation()
        {
            int currentTick = Find.TickManager.TicksGame;
            
            if (currentTick != lastTick)
            {
                // 更新每个图层的动画
                for (int i = 0; i < ModExtension.graphicLayers.Count; i++)
                {
                    var layer = ModExtension.graphicLayers[i];
                    
                    if (layer.enableHover)
                    {
                        // 初始化动画时间
                        if (!layerAnimationTimes.ContainsKey(i))
                        {
                            layerAnimationTimes[i] = 0f;
                        }
                        
                        // 更新动画时间
                        layerAnimationTimes[i] += Time.deltaTime;
                        
                        // 计算该图层的悬浮偏移
                        float hoverSpeed = layer.hoverSpeed > 0 ? layer.hoverSpeed : ModExtension.globalHoverSpeed;
                        float hoverIntensity = layer.hoverIntensity > 0 ? layer.hoverIntensity : ModExtension.globalHoverIntensity;
                        
                        float hoverOffset = Mathf.Sin(layerAnimationTimes[i] * hoverSpeed + layer.hoverPhase) * hoverIntensity;
                        layerHoverOffsets[i] = hoverOffset;
                    }
                }
                
                lastTick = currentTick;
            }
        }

        // 保存和加载
        public override void ExposeData()
        {
            base.ExposeData();
            // 保存自定义状态（如果需要）
        }
    }

    // 主要的 ModExtension 定义
    public class ExtraGraphicsExtension : DefModExtension
    {
        // 全局悬浮参数（作为默认值）
        public float globalHoverSpeed = 2f;           // 全局悬浮速度
        public float globalHoverIntensity = 0.1f;     // 全局悬浮强度
        
        // 图形层配置
        public List<GraphicLayerData> graphicLayers = new List<GraphicLayerData>();

        public ExtraGraphicsExtension()
        {
            // 默认配置，避免空列表
            if (graphicLayers == null)
            {
                graphicLayers = new List<GraphicLayerData>();
            }
        }
    }

    // 单个图形层的配置数据
    public class GraphicLayerData
    {
        // 基础配置
        public string texturePath;                    // 纹理路径（必需）
        public Vector2 scale = Vector2.one;          // 缩放比例
        public Color color = Color.white;            // 颜色
        public int drawOrder = 0;                    // 绘制顺序（数字小的先绘制）
        
        // 位置配置 - 使用环世界坐标系
        // X: 左右偏移, Y: 图层深度, Z: 上下偏移
        public Vector3 offset = Vector3.zero;
        
        // 独立悬浮配置
        public bool enableHover = true;              // 是否启用悬浮
        public float hoverSpeed = 0f;                // 悬浮速度（0表示使用全局速度）
        public float hoverIntensity = 0f;            // 悬浮强度（0表示使用全局强度）
        public float hoverPhase = 0f;                // 悬浮相位（用于错开浮动）
    }
}

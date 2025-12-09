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
        
        // 图形缓存 - 现在包含Shader信息
        private Dictionary<string, Graphic> graphicsCache = new Dictionary<string, Graphic>();
        
        // 动画状态 - 每个图层的独立状态
        private Dictionary<int, float> layerHoverOffsets = new Dictionary<int, float>();
        private Dictionary<int, float> layerAnimationTimes = new Dictionary<int, float>();
        private Dictionary<int, float> layerRotationAngles = new Dictionary<int, float>();
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

        // 获取缓存的图形 - 修改后支持自定义Shader
        private Graphic GetCachedGraphic(string texturePath, Vector2 scale, Color color, Shader shader)
        {
            string cacheKey = $"{texturePath}_{scale.x}_{scale.y}_{color}_{shader?.name ?? "null"}";
            
            if (!graphicsCache.TryGetValue(cacheKey, out Graphic graphic))
            {
                graphic = GraphicDatabase.Get<Graphic_Single>(
                    texturePath, 
                    shader ?? ShaderDatabase.TransparentPostLight, // 使用传入的Shader，如果为null则使用默认
                    scale, 
                    color);
                graphicsCache[cacheKey] = graphic;
            }
            
            return graphic;
        }

        // 根据Shader名称获取Shader - 修正版本
        private Shader GetShaderByName(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName))
                return ShaderDatabase.TransparentPostLight;

            // 使用switch语句匹配实际可用的Shader
            switch (shaderName.ToLower())
            {
                case "transparent":
                    return ShaderDatabase.Transparent;
                case "transparentpostlight":
                    return ShaderDatabase.TransparentPostLight;
                case "transparentplant":
                    return ShaderDatabase.TransparentPlant;
                case "cutout":
                    return ShaderDatabase.Cutout;
                case "cutoutcomplex":
                    return ShaderDatabase.CutoutComplex;
                case "cutoutflying":
                    return ShaderDatabase.CutoutFlying;
                case "cutoutflying01":
                    return ShaderDatabase.CutoutFlying01;
                case "terrainfade":
                    return ShaderDatabase.TerrainFade;
                case "terrainfaderough":
                    return ShaderDatabase.TerrainFadeRough;
                case "mote":
                    return ShaderDatabase.Mote;
                case "moteglow":
                    return ShaderDatabase.MoteGlow;
                case "motepulse":
                    return ShaderDatabase.MotePulse;
                case "moteglowpulse":
                    return ShaderDatabase.MoteGlowPulse;
                case "motewater":
                    return ShaderDatabase.MoteWater;
                case "moteglowdistorted":
                    return ShaderDatabase.MoteGlowDistorted;
                case "solidcolor":
                    return ShaderDatabase.SolidColor;
                case "vertexcolor":
                    return ShaderDatabase.VertexColor;
                case "invisible":
                    return ShaderDatabase.Invisible;
                case "silhouette":
                    return ShaderDatabase.Silhouette;
                case "worldterrain":
                    return ShaderDatabase.WorldTerrain;
                case "worldocean":
                    return ShaderDatabase.WorldOcean;
                case "metaoverlay":
                    return ShaderDatabase.MetaOverlay;
                default:
                    Log.Warning($"Building_ExtraGraphics: Shader '{shaderName}' not found, using TransparentPostLight as fallback");
                    return ShaderDatabase.TransparentPostLight;
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            // 不调用基类的 DrawAt，完全自定义渲染

            // 更新动画状态
            UpdateAnimations();
            // 绘制所有配置的图形层
            DrawGraphicLayers(drawLoc, flip);
            // 新增：绘制护盾
            var shieldComp = this.GetComp<ThingComp_AreaShield>();
            if (shieldComp != null)
            {
                shieldComp.PostDraw();
            }
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

        // 绘制单个图形层 - 现在支持三种旋转动画
        private void DrawGraphicLayer(Vector3 baseDrawPos, bool flip, GraphicLayerData layer)
        {
            if (string.IsNullOrEmpty(layer.texturePath))
            {
                Log.Warning($"Building_ExtraGraphics: Empty texture path in layer for {def.defName}");
                return;
            }

            // 获取Shader
            Shader shader = GetShaderByName(layer.shaderName);

            // 获取图形（现在传入Shader）
            Graphic graphic = GetCachedGraphic(layer.texturePath, layer.scale, layer.color, shader);

            // 计算图层动画偏移
            Vector3 animationOffset = Vector3.zero;
            float rotationAngle = 0f;
            int layerIndex = ModExtension.graphicLayers.IndexOf(layer);
            
            // 根据动画类型应用不同的动画效果
            switch (layer.animationType)
            {
                case AnimationType.Hover:
                    if (layer.enableAnimation && layerHoverOffsets.ContainsKey(layerIndex))
                    {
                        animationOffset.z = layerHoverOffsets[layerIndex];
                    }
                    break;
                    
                case AnimationType.RotateZ:
                case AnimationType.RotateY:
                case AnimationType.RotateX:
                    if (layer.enableAnimation && layerRotationAngles.ContainsKey(layerIndex))
                    {
                        rotationAngle = layerRotationAngles[layerIndex];
                    }
                    break;
            }

            // 最终绘制位置 = 基础位置 + 图层偏移 + 动画偏移
            Vector3 drawPos = baseDrawPos + layer.offset + animationOffset;
            
            // 如果启用了旋转动画，使用特殊方法绘制
            if ((layer.animationType == AnimationType.RotateZ || 
                 layer.animationType == AnimationType.RotateY ||
                 layer.animationType == AnimationType.RotateX) && 
                layer.enableAnimation)
            {
                // 使用自定义旋转绘制方法
                DrawWithRotation(graphic, drawPos, flip, rotationAngle, layer);
            }
            else
            {
                // 普通绘制
                graphic.Draw(drawPos, flip ? base.Rotation.Opposite : base.Rotation, this, 0f);
            }
        }

        // 使用矩阵变换绘制旋转图形 - 支持三种旋转轴
        private void DrawWithRotation(Graphic graphic, Vector3 drawPos, bool flip, float rotationAngle, GraphicLayerData layer)
        {
            try
            {
                // 获取网格和材质
                Mesh mesh = graphic.MeshAt(flip ? base.Rotation.Opposite : base.Rotation);
                Material mat = graphic.MatAt(flip ? base.Rotation.Opposite : base.Rotation);
                
                if (mesh == null || mat == null)
                {
                    Log.Warning($"Building_ExtraGraphics: Unable to get mesh or material for rotating layer");
                    return;
                }
                
                // 根据旋转类型创建不同的旋转矩阵
                Quaternion rotation = Quaternion.identity;
                
                switch (layer.animationType)
                {
                    case AnimationType.RotateZ:
                        // 绕Z轴旋转（2D平面旋转）
                        rotation = Quaternion.Euler(0f, 0f, rotationAngle);
                        break;
                        
                    case AnimationType.RotateY:
                        // 绕Y轴旋转（3D旋转，类似旋转门）
                        rotation = Quaternion.Euler(0f, rotationAngle, 0f);
                        break;
                        
                    case AnimationType.RotateX:
                        // 绕X轴旋转（3D旋转，类似翻跟斗）
                        rotation = Quaternion.Euler(rotationAngle, 0f, 0f);
                        break;
                }
                
                // 如果图层有旋转中心偏移，需要调整位置
                Vector3 pivotOffset = new Vector3(layer.pivotOffset.x, 0, layer.pivotOffset.y);
                
                // 最终绘制位置 = 基础位置 + 图层偏移 + 旋转中心偏移
                Vector3 finalDrawPos = drawPos + pivotOffset;
                
                // 创建变换矩阵
                // 注意：Graphic已经应用了缩放，所以这里使用Vector3.one
                Matrix4x4 matrix = Matrix4x4.TRS(
                    finalDrawPos,     // 位置
                    rotation,         // 旋转
                    Vector3.one       // 缩放（已由Graphic处理）
                );
                
                // 使用RimWorld的绘制方法
                GenDraw.DrawMeshNowOrLater(mesh, matrix, mat, false);
                
                // 如果需要双面渲染，再绘制一次
                if (layer.doubleSided)
                {
                    GenDraw.DrawMeshNowOrLater(mesh, matrix, mat, false);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Building_ExtraGraphics: Error drawing rotating layer: {ex}");
            }
        }

        // 更新所有图层的动画状态
        private void UpdateAnimations()
        {
            int currentTick = Find.TickManager.TicksGame;
            
            if (currentTick != lastTick)
            {
                // 更新每个图层的动画
                for (int i = 0; i < ModExtension.graphicLayers.Count; i++)
                {
                    var layer = ModExtension.graphicLayers[i];
                    
                    if (!layer.enableAnimation)
                        continue;
                    
                    // 初始化动画时间
                    if (!layerAnimationTimes.ContainsKey(i))
                    {
                        layerAnimationTimes[i] = layer.animationStartTime;
                        // 为旋转动画初始化旋转角度
                        if (layer.animationType == AnimationType.RotateZ || 
                            layer.animationType == AnimationType.RotateY ||
                            layer.animationType == AnimationType.RotateX)
                        {
                            layerRotationAngles[i] = 0f;
                        }
                    }
                    
                    // 更新动画时间
                    layerAnimationTimes[i] += Time.deltaTime;
                    
                    // 根据动画类型更新不同的状态
                    switch (layer.animationType)
                    {
                        case AnimationType.Hover:
                            // 计算该图层的悬浮偏移
                            float hoverSpeed = layer.animationSpeed > 0 ? layer.animationSpeed : ModExtension.globalAnimationSpeed;
                            float hoverIntensity = layer.animationIntensity > 0 ? layer.animationIntensity : ModExtension.globalAnimationIntensity;
                            
                            float hoverOffset = Mathf.Sin(layerAnimationTimes[i] * hoverSpeed + layer.animationPhase) * hoverIntensity;
                            layerHoverOffsets[i] = hoverOffset;
                            break;
                            
                        case AnimationType.RotateZ:
                        case AnimationType.RotateY:
                        case AnimationType.RotateX:
                            // 计算该图层的旋转角度
                            float rotateSpeed = layer.animationSpeed > 0 ? layer.animationSpeed : ModExtension.globalAnimationSpeed;
                            
                            // 旋转角度计算：动画时间 × 旋转速度（度/秒）
                            float rotationAngle = layerAnimationTimes[i] * rotateSpeed;
                            
                            // 如果设置了动画强度且小于360，则限制旋转范围
                            if (layer.animationIntensity > 0 && layer.animationIntensity < 360f)
                            {
                                // 使用正弦波创建来回旋转效果
                                rotationAngle = Mathf.Sin(layerAnimationTimes[i] * rotateSpeed * Mathf.Deg2Rad) * layer.animationIntensity;
                            }
                            else if (layer.animationIntensity >= 360f)
                            {
                                // 完整旋转：取模确保在0-360度之间
                                rotationAngle %= 360f;
                            }
                            
                            layerRotationAngles[i] = rotationAngle;
                            
                            // 调试输出
                            if (DebugSettings.godMode && i == 0 && Find.TickManager.TicksGame % 60 == 0)
                            {
                                // 只在开发模式下，每60帧输出一次第一条图层的旋转信息
                                Log.Message($"{layer.animationType} 图层 {i}: 角度={rotationAngle:F1}°, 时间={layerAnimationTimes[i]:F2}s, 速度={rotateSpeed}°/s");
                            }
                            break;
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
        
        // 调试方法：手动触发旋转测试
        public void TestRotation()
        {
            for (int i = 0; i < ModExtension.graphicLayers.Count; i++)
            {
                var layer = ModExtension.graphicLayers[i];
                if (layer.animationType == AnimationType.RotateZ || 
                    layer.animationType == AnimationType.RotateY ||
                    layer.animationType == AnimationType.RotateX)
                {
                    layerRotationAngles[i] = 45f; // 设置为45度测试
                    break;
                }
            }
        }
    }

    // 动画类型枚举 - 现在有五种动画类型
    public enum AnimationType
    {
        None,      // 无动画
        Hover,     // 上下浮动
        RotateZ,   // 绕Z轴旋转（2D平面旋转）- 原来的Rotate
        RotateY,   // 绕Y轴旋转（3D旋转，类似旋转门）- 新增
        RotateX    // 绕X轴旋转（3D旋转，类似翻跟斗）- 新增
    }

    // 主要的 ModExtension 定义
    public class ExtraGraphicsExtension : DefModExtension
    {
        // 全局动画参数（作为默认值）
        public float globalAnimationSpeed = 2f;           // 全局动画速度
        public float globalAnimationIntensity = 0.1f;     // 全局动画强度
        
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

    // 单个图形层的配置数据 - 增强版
    public class GraphicLayerData
    {
        // 基础配置
        public string texturePath;                    // 纹理路径（必需）
        public Vector2 scale = Vector2.one;          // 缩放比例
        public Color color = Color.white;            // 颜色
        public int drawOrder = 0;                    // 绘制顺序（数字小的先绘制）
        public string shaderName = "TransparentPostLight"; // Shader名称
        
        // 位置配置 - 使用环世界坐标系
        // X: 左右偏移, Y: 图层深度, Z: 上下偏移
        public Vector3 offset = Vector3.zero;
        
        // 动画配置
        public AnimationType animationType = AnimationType.Hover; // 动画类型
        public bool enableAnimation = true;          // 是否启用动画
        public float animationSpeed = 0f;            // 动画速度（0表示使用全局速度）
        public float animationIntensity = 0f;        // 动画强度（0表示使用全局强度）
        public float animationPhase = 0f;            // 动画相位（用于错开动画）
        public float animationStartTime = 0f;        // 动画开始时间偏移
        
        // 旋转动画专用配置
        public Vector2 pivotOffset = Vector2.zero;   // 旋转中心偏移（相对于图层的中心）
        public bool doubleSided = false;             // 是否双面渲染（对于旋转物体）
        
        // 兼容旧字段（为了向后兼容）
        [Obsolete("Use enableAnimation instead")]
        public bool enableHover
        {
            get => enableAnimation && animationType == AnimationType.Hover;
            set 
            { 
                enableAnimation = value;
                if (value && animationType == AnimationType.None)
                    animationType = AnimationType.Hover;
            }
        }
        
        [Obsolete("Use animationSpeed instead")]
        public float hoverSpeed
        {
            get => animationSpeed;
            set => animationSpeed = value;
        }
        
        [Obsolete("Use animationIntensity instead")]
        public float hoverIntensity
        {
            get => animationIntensity;
            set => animationIntensity = value;
        }
        
        [Obsolete("Use animationPhase instead")]
        public float hoverPhase
        {
            get => animationPhase;
            set => animationPhase = value;
        }
        
        // 向后兼容：Rotate应该映射到RotateZ
        [Obsolete("Use animationType instead")]
        public AnimationType animationTypeCompat
        {
            get => animationType;
            set 
            { 
                animationType = value;
                // 如果旧值是Rotate，映射到RotateZ
                if (animationType == AnimationType.RotateZ)
                    animationType = AnimationType.RotateZ;
            }
        }
    }
}

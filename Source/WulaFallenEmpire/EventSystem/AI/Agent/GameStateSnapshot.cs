using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Agent
{
    /// <summary>
    /// 游戏状态快照 - 包含 AI 决策所需的所有游戏信息
    /// </summary>
    public class GameStateSnapshot
    {
        // 时间信息
        public int Hour;
        public string Season;
        public int DayOfQuadrum;
        public int Year;
        public float GameSpeedMultiplier;
        
        // 殖民者信息
        public List<PawnSnapshot> Colonists = new List<PawnSnapshot>();
        public List<PawnSnapshot> Prisoners = new List<PawnSnapshot>();
        public List<PawnSnapshot> Animals = new List<PawnSnapshot>();
        
        // 资源统计
        public Dictionary<string, int> Resources = new Dictionary<string, int>();
        
        // 建筑和蓝图
        public int TotalBuildings;
        public int PendingBlueprints;
        public int ConstructionFrames;
        
        // 威胁
        public List<ThreatSnapshot> Threats = new List<ThreatSnapshot>();
        
        // 最近事件
        public List<string> RecentMessages = new List<string>();
        
        // 地图信息
        public string BiomeName;
        public float OutdoorTemperature;
        public string Weather;
        
        /// <summary>
        /// 生成给 VLM 的文本描述
        /// </summary>
        public string ToPromptText()
        {
            var sb = new StringBuilder();
            
            // 时间
            sb.AppendLine($"# 当前游戏状态");
            sb.AppendLine();
            sb.AppendLine($"## 时间");
            sb.AppendLine($"- {Season}，第 {DayOfQuadrum} 天，{Hour}:00");
            sb.AppendLine($"- 第 {Year} 年");
            sb.AppendLine();
            
            // 环境
            sb.AppendLine($"## 环境");
            sb.AppendLine($"- 生物群系: {BiomeName}");
            sb.AppendLine($"- 室外温度: {OutdoorTemperature:F1}°C");
            sb.AppendLine($"- 天气: {Weather}");
            sb.AppendLine();
            
            // 殖民者
            sb.AppendLine($"## 殖民者 ({Colonists.Count}人)");
            foreach (var c in Colonists.Take(10))
            {
                string status = c.CurrentJob ?? "空闲";
                string health = c.HealthPercent >= 0.9f ? "健康" : 
                               c.HealthPercent >= 0.5f ? "受伤" : "重伤";
                string mood = c.MoodPercent >= 0.7f ? "😊" :
                             c.MoodPercent >= 0.4f ? "😐" : "😞";
                sb.AppendLine($"- {c.Name}: {health} {c.HealthPercent:P0}, 心情{mood} {c.MoodPercent:P0}, {status}");
            }
            if (Colonists.Count > 10)
            {
                sb.AppendLine($"- ... 还有 {Colonists.Count - 10} 人");
            }
            sb.AppendLine();
            
            // 资源
            sb.AppendLine($"## 主要资源");
            var importantResources = new[] { "Silver", "Steel", "Plasteel", "ComponentIndustrial", "Gold", "MealSimple", "MealFine", "MedicineHerbal", "MedicineIndustrial", "WoodLog" };
            foreach (var res in importantResources)
            {
                if (Resources.TryGetValue(res, out int count) && count > 0)
                {
                    string label = GetResourceLabel(res);
                    sb.AppendLine($"- {label}: {count}");
                }
            }
            sb.AppendLine();
            
            // 建造
            if (PendingBlueprints > 0 || ConstructionFrames > 0)
            {
                sb.AppendLine($"## 建造进度");
                if (PendingBlueprints > 0) sb.AppendLine($"- 待建蓝图: {PendingBlueprints}");
                if (ConstructionFrames > 0) sb.AppendLine($"- 建造中: {ConstructionFrames}");
                sb.AppendLine();
            }
            
            // 威胁
            if (Threats.Count > 0)
            {
                sb.AppendLine($"## ⚠️ 威胁");
                foreach (var t in Threats)
                {
                    sb.AppendLine($"- {t.Description} (距离: {t.Distance:F0})");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine($"## 威胁: 暂无");
                sb.AppendLine();
            }
            
            // 最近消息
            if (RecentMessages.Count > 0)
            {
                sb.AppendLine($"## 最近事件");
                foreach (var msg in RecentMessages.Skip(Math.Max(0, RecentMessages.Count - 5)))
                {
                    sb.AppendLine($"- {msg}");
                }
            }
            
            return sb.ToString();
        }
        
        private static string GetResourceLabel(string defName)
        {
            return defName switch
            {
                "Silver" => "银",
                "Steel" => "钢铁",
                "Plasteel" => "玻璃钢",
                "ComponentIndustrial" => "零部件",
                "Gold" => "金",
                "MealSimple" => "简单食物",
                "MealFine" => "精致食物",
                "MedicineHerbal" => "草药",
                "MedicineIndustrial" => "医药",
                "WoodLog" => "木材",
                _ => defName
            };
        }
    }
    
    /// <summary>
    /// 殖民者/Pawn 快照
    /// </summary>
    public class PawnSnapshot
    {
        public string Name;
        public float HealthPercent;
        public float MoodPercent;
        public bool IsDrafted;
        public bool IsDowned;
        public string CurrentJob;
        public int X;
        public int Z;
    }
    
    /// <summary>
    /// 威胁快照
    /// </summary>
    public class ThreatSnapshot
    {
        public string Description;
        public float Distance;
        public int Count;
        public bool IsHostile;
    }
}

// HediffCompProperties_SwitchableHediff.cs
using RimWorld;
using Verse;
using System.Collections.Generic;
using UnityEngine;
using System.Text;

namespace WulaFallenEmpire
{
    public class HediffCompProperties_SwitchableHediff : HediffCompProperties
    {
        // 可切换的hediff列表
        public List<HediffDef> availableHediffs = new List<HediffDef>();
        
        // 默认选择的hediff索引
        public int defaultHediffIndex = 0;
        
        // Gizmo图标路径
        public string gizmoIconPath = "UI/Commands/Default";
        
        // 是否显示当前状态的提示
        public bool showStatusInGizmo = true;
        
        // 可自定义的标签和描述
        public string switchLabel = "WULA_SwitchableHediff_SwitchLabel"; // 默认翻译键
        public string switchDesc = "WULA_SwitchableHediff_SwitchDesc";   // 默认翻译键
        public string statusLabel = "WULA_SwitchableHediff_StatusLabel"; // 默认翻译键
        public string statusDesc = "WULA_SwitchableHediff_StatusDesc";   // 默认翻译键
        
        public HediffCompProperties_SwitchableHediff()
        {
            compClass = typeof(HediffComp_SwitchableHediff);
        }
    }

    public class HediffComp_SwitchableHediff : HediffComp
    {
        public HediffCompProperties_SwitchableHediff Props => (HediffCompProperties_SwitchableHediff)props;
        
        // 当前选择的hediff索引
        private int currentHediffIndex = -1;
        
        // 当前激活的hediff引用
        private Hediff activeHediff;
        
        public override void CompPostMake()
        {
            base.CompPostMake();
            
            // 初始化当前选择
            if (currentHediffIndex == -1 && Props.availableHediffs.Count > 0)
            {
                currentHediffIndex = Props.defaultHediffIndex;
                if (currentHediffIndex >= Props.availableHediffs.Count)
                    currentHediffIndex = 0;
                
                // 应用初始hediff
                ApplySelectedHediff();
            }
        }
        
        // 应用当前选择的hediff
        private void ApplySelectedHediff()
        {
            // 移除之前激活的hediff
            if (activeHediff != null && Pawn.health.hediffSet.hediffs.Contains(activeHediff))
            {
                Pawn.health.RemoveHediff(activeHediff);
            }
            activeHediff = null;
            
            // 应用新的hediff
            if (currentHediffIndex >= 0 && currentHediffIndex < Props.availableHediffs.Count)
            {
                var hediffDef = Props.availableHediffs[currentHediffIndex];
                if (hediffDef != null)
                {
                    activeHediff = HediffMaker.MakeHediff(hediffDef, Pawn);
                    activeHediff.Severity = 1f; // 默认严重性为1
                    Pawn.health.AddHediff(activeHediff);
                    
                    Log.Message($"[SwitchableHediff] Applied {hediffDef.defName} to {Pawn.LabelShort}");
                }
            }
        }
        
        // 切换到特定索引的hediff
        private void SwitchToHediff(int index)
        {
            if (index >= 0 && index < Props.availableHediffs.Count)
            {
                currentHediffIndex = index;
                ApplySelectedHediff();
                
                // 发送切换消息
                var hediffDef = Props.availableHediffs[index];
                if (hediffDef != null)
                {
                    Messages.Message("WULA_SwitchableHediff_SwitchedTo".Translate(hediffDef.label), 
                                   Pawn, MessageTypeDefOf.SilentInput);
                }
            }
        }
        
        // 获取当前hediff名称
        private string GetCurrentHediffName()
        {
            if (currentHediffIndex >= 0 && currentHediffIndex < Props.availableHediffs.Count)
            {
                var hediffDef = Props.availableHediffs[currentHediffIndex];
                return hediffDef?.label ?? "WULA_Unknown".Translate();
            }
            return "WULA_None".Translate();
        }
        
        // 获取当前hediff描述
        private string GetCurrentHediffDesc()
        {
            if (currentHediffIndex >= 0 && currentHediffIndex < Props.availableHediffs.Count)
            {
                var hediffDef = Props.availableHediffs[currentHediffIndex];
                return hediffDef?.description ?? "WULA_NoDescription".Translate();
            }
            return string.Empty;
        }
        
        // 获取特定hediff的描述
        private string GetHediffDescription(int index)
        {
            if (index >= 0 && index < Props.availableHediffs.Count)
            {
                var hediffDef = Props.availableHediffs[index];
                return hediffDef?.description ?? "WULA_NoDescription".Translate();
            }
            return string.Empty;
        }
        
        // 获取特定hediff的详细工具提示（包含效果信息）
        private string GetHediffDetailedTooltip(int index)
        {
            if (index >= 0 && index < Props.availableHediffs.Count)
            {
                var hediffDef = Props.availableHediffs[index];
                if (hediffDef == null) return string.Empty;
                
                StringBuilder sb = new StringBuilder();
                
                // 添加描述
                if (!hediffDef.description.NullOrEmpty())
                {
                    sb.AppendLine(hediffDef.description);
                }
                return sb.ToString().TrimEndNewlines();
            }
            return string.Empty;
        }
        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            // 只有玩家派系的pawn才显示Gizmo
            if (Pawn.Faction == Faction.OfPlayer && Props.availableHediffs.Count > 1)
            {
                // 主切换按钮 - 显示当前状态
                Command_Action mainButton = new Command_Action
                {
                    // 使用可自定义的标签，如果没有自定义则使用翻译键
                    defaultLabel = Props.switchLabel.Translate(),
                    defaultDesc = "WULA_SwitchableHediff_CurrentMode".Translate(GetCurrentHediffName()) + "\n" + 
                                 Props.switchDesc.Translate(),
                    icon = ContentFinder<Texture2D>.Get(Props.gizmoIconPath, false) ?? BaseContent.BadTex,
                    action = () => {
                        // 显示选择菜单
                        ShowHediffSelectionMenu();
                    },
                    hotKey = KeyBindingDefOf.Misc2
                };
                
                yield return mainButton;
                
                // 如果启用了状态显示，添加一个信息Gizmo
                if (Props.showStatusInGizmo)
                {
                    Command_Action statusButton = new Command_Action
                    {
                        defaultLabel = Props.statusLabel.Translate(GetCurrentHediffName()),
                        defaultDesc = Props.statusDesc.Translate() + "\n\n" + 
                                     "WULA_SwitchableHediff_CurrentDesc".Translate(GetCurrentHediffDesc()),
                        icon = ContentFinder<Texture2D>.Get("UI/Commands/Info", false) ?? BaseContent.BadTex,
                        action = () => {
                            // 显示当前hediff的详细信息
                            ShowCurrentHediffInfo();
                        }
                    };
                    
                    yield return statusButton;
                }
            }
        }
        
        // 显示hediff选择菜单
        private void ShowHediffSelectionMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            
            for (int i = 0; i < Props.availableHediffs.Count; i++)
            {
                int index = i; // 捕获当前索引
                var hediffDef = Props.availableHediffs[i];
                string label = hediffDef?.label ?? "WULA_Unknown".Translate();
                string description = GetHediffDetailedTooltip(i); // 获取详细工具提示
                
                // 标记当前选择的项目
                string prefix = (i == currentHediffIndex) ? "✓ " : "   ";
                
                // 创建选项
                var option = new FloatMenuOption(
                    prefix + label,
                    () => {
                        SwitchToHediff(index);
                    }
                );
                
                // 设置工具提示 - 使用详细的描述信息
                option.tooltip = description;
                
                options.Add(option);
            }
            
            // 显示浮动菜单
            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options, "WULA_SwitchableHediff_SelectMode".Translate()));
            }
        }
        
        // 显示当前hediff的详细信息
        private void ShowCurrentHediffInfo()
        {
            if (currentHediffIndex >= 0 && currentHediffIndex < Props.availableHediffs.Count)
            {
                var hediffDef = Props.availableHediffs[currentHediffIndex];
                string description = GetHediffDetailedTooltip(currentHediffIndex); // 使用详细工具提示
                
                Messages.Message(
                    "WULA_SwitchableHediff_CurrentModeInfo".Translate(hediffDef?.label, description), 
                    MessageTypeDefOf.SilentInput
                );
            }
        }
        
        public override string CompTipStringExtra
        {
            get
            {
                string baseTip = base.CompTipStringExtra ?? "";
                string currentEffect = "WULA_SwitchableHediff_CurrentEffect".Translate(GetCurrentHediffName());
                
                if (!string.IsNullOrEmpty(baseTip))
                    return baseTip + "\n" + currentEffect;
                else
                    return currentEffect;
            }
        }
        
        public override string CompLabelInBracketsExtra
        {
            get
            {
                return GetCurrentHediffName();
            }
        }
        
        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref currentHediffIndex, "currentHediffIndex", -1);
            
            // 加载后重新应用hediff
            if (Scribe.mode == LoadSaveMode.PostLoadInit && currentHediffIndex != -1)
            {
                ApplySelectedHediff();
            }
        }
        
        // 当父hediff被移除时，也要移除激活的hediff
        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            
            if (activeHediff != null && Pawn.health.hediffSet.hediffs.Contains(activeHediff))
            {
                Pawn.health.RemoveHediff(activeHediff);
            }
            activeHediff = null;
        }
    }
}

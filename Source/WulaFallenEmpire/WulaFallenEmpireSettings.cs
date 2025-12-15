using Verse;

namespace WulaFallenEmpire
{
    public class WulaFallenEmpireSettings : ModSettings
    {
        public string apiKey = "sk-xxxxxxxx";
        public string baseUrl = "https://api.deepseek.com";
        public string model = "deepseek-chat";
        public int maxContextTokens = 100000;
        public bool enableDebugLogs = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref apiKey, "apiKey", "sk-xxxxxxxx");
            Scribe_Values.Look(ref baseUrl, "baseUrl", "https://api.deepseek.com");
            Scribe_Values.Look(ref model, "model", "deepseek-chat");
            Scribe_Values.Look(ref maxContextTokens, "maxContextTokens", 100000);
            Scribe_Values.Look(ref enableDebugLogs, "enableDebugLogs", false);
            base.ExposeData();
        }
    }
}

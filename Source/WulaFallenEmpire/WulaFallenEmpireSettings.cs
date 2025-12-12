using Verse;

namespace WulaFallenEmpire
{
    public class WulaFallenEmpireSettings : ModSettings
    {
        public string apiKey = "";
        public string baseUrl = "https://api.openai.com/v1";
        public string model = "gpt-3.5-turbo";

        public override void ExposeData()
        {
            Scribe_Values.Look(ref apiKey, "apiKey", "");
            Scribe_Values.Look(ref baseUrl, "baseUrl", "https://api.openai.com/v1");
            Scribe_Values.Look(ref model, "model", "gpt-3.5-turbo");
            base.ExposeData();
        }
    }
}
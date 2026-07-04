using System;
using WulaFallenEmpire;

namespace WulaFallenEmpire.EventSystem.AI
{
    public static class AIProviderFactory
    {
        public static IAIProvider Create(WulaFallenEmpireSettings settings)
        {
            if (settings == null) return null;
            AIProviderType providerType = ParseProviderType(settings.aiProviderType);
            switch (providerType)
            {
                case AIProviderType.AnthropicMessages:
                    return new AnthropicMessagesProvider(settings.anthropicApiKey, settings.anthropicBaseUrl, settings.anthropicModel);
                case AIProviderType.Gemini:
                    return new GeminiProvider(settings.geminiApiKey, settings.geminiBaseUrl, settings.geminiModel);
                default:
                    return new OpenAIChatProvider(settings.apiKey, settings.baseUrl, settings.model);
            }
        }

        public static AIProviderType ParseProviderType(string value)
        {
            if (Enum.TryParse(value, true, out AIProviderType parsed)) return parsed;
            return AIProviderType.OpenAIChat;
        }

        public static AIToolProtocolMode ParseToolProtocolMode(string value)
        {
            if (Enum.TryParse(value, true, out AIToolProtocolMode parsed)) return parsed;
            return AIToolProtocolMode.NativeToolCalling;
        }
    }
}

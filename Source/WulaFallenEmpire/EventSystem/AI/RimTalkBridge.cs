using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI
{
    public static class RimTalkBridge
    {
        private static bool? _isRimTalkActive;
        private static Type _aiClientFactoryType;
        private static Type _aiClientInterfaceType;
        private static Type _roleEnum;
        private static MethodInfo _getAIClientAsyncMethod;
        private static MethodInfo _getChatCompletionAsyncMethod;

        public static bool IsRimTalkActive
        {
            get
            {
                if (!_isRimTalkActive.HasValue)
                {
                    _isRimTalkActive = ModsConfig.IsActive("RimTalk.Mod"); // Replace with actual PackageId if different
                    if (_isRimTalkActive.Value)
                    {
                        InitializeReflection();
                    }
                }
                return _isRimTalkActive.Value;
            }
        }

        private static void InitializeReflection()
        {
            try
            {
                // Assuming RimTalk assembly is loaded
                Assembly rimTalkAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "RimTalk");

                if (rimTalkAssembly == null)
                {
                    Log.Error("[WulaFallenEmpire] RimTalk assembly not found despite mod being active.");
                    _isRimTalkActive = false;
                    return;
                }

                _aiClientFactoryType = rimTalkAssembly.GetType("RimTalk.Client.AIClientFactory");
                _aiClientInterfaceType = rimTalkAssembly.GetType("RimTalk.Client.IAIClient");
                _roleEnum = rimTalkAssembly.GetType("RimTalk.Data.Role");

                if (_aiClientFactoryType != null)
                {
                    _getAIClientAsyncMethod = _aiClientFactoryType.GetMethod("GetAIClientAsync", BindingFlags.Public | BindingFlags.Static);
                }
                
                if (_aiClientInterfaceType != null)
                {
                    _getChatCompletionAsyncMethod = _aiClientInterfaceType.GetMethod("GetChatCompletionAsync");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[WulaFallenEmpire] Failed to initialize RimTalk reflection: {ex}");
                _isRimTalkActive = false;
            }
        }

        public static async Task<string> GetChatCompletion(string instruction, List<(string role, string message)> messages)
        {
            if (!IsRimTalkActive || _getAIClientAsyncMethod == null || _getChatCompletionAsyncMethod == null)
            {
                return null;
            }

            try
            {
                // Get AI Client
                var clientTask = (Task)_getAIClientAsyncMethod.Invoke(null, null);
                await clientTask.ConfigureAwait(false);
                
                // The Task returns an IAIClient object
                object client = ((dynamic)clientTask).Result;

                if (client == null) return null;

                // Prepare messages list
                // List<(Role role, string message)>
                var tupleType = typeof(ValueTuple<,>).MakeGenericType(_roleEnum, typeof(string));
                var listType = typeof(List<>).MakeGenericType(tupleType);
                var messageList = Activator.CreateInstance(listType);
                var addMethod = listType.GetMethod("Add");

                foreach (var msg in messages)
                {
                    object roleValue = Enum.Parse(_roleEnum, msg.role, true);
                    object tuple = Activator.CreateInstance(tupleType, roleValue, msg.message);
                    addMethod.Invoke(messageList, new object[] { tuple });
                }

                // Call GetChatCompletionAsync
                var completionTask = (Task)_getChatCompletionAsyncMethod.Invoke(client, new object[] { instruction, messageList });
                await completionTask.ConfigureAwait(false);

                // The Task returns a Payload object
                object payload = ((dynamic)completionTask).Result;
                
                // Payload has a 'Response' property (or similar, based on previous analysis it was 'Content' or 'Response')
                // Checking previous analysis: Payload has 'Content' property for the text response.
                PropertyInfo contentProp = payload.GetType().GetProperty("Content");
                return contentProp?.GetValue(payload) as string;
            }
            catch (Exception ex)
            {
                Log.Error($"[WulaFallenEmpire] Error calling RimTalk AI: {ex}");
                return null;
            }
        }
    }
}
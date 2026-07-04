using System;
using System.Threading;
using System.Threading.Tasks;

namespace WulaFallenEmpire.EventSystem.AI
{
    public interface IAIProvider
    {
        Task<AIProviderResponse> SendAsync(AIProviderRequest request, CancellationToken cancellationToken);
        Task<AIProviderResponse> StreamAsync(AIProviderRequest request, Action<AIStreamEvent> onEvent, CancellationToken cancellationToken);
    }
}

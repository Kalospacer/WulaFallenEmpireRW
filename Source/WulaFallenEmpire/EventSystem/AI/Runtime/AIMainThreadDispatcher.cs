using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace WulaFallenEmpire.EventSystem.AI
{
    public static class AIMainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();

        public static Task<T> InvokeAsync<T>(Func<T> action)
        {
            var tcs = new TaskCompletionSource<T>();
            Queue.Enqueue(() =>
            {
                try
                {
                    tcs.SetResult(action());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }

        public static Task<T> InvokeAsync<T>(Func<Task<T>> action)
        {
            var tcs = new TaskCompletionSource<T>();
            Queue.Enqueue(async () =>
            {
                try
                {
                    tcs.SetResult(await action());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }

        public static void Flush()
        {
            while (Queue.TryDequeue(out var action))
            {
                action();
            }
        }
    }
}

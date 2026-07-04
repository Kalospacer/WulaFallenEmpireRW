using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using WulaFallenEmpire;

namespace WulaFallenEmpire.EventSystem.AI
{
    [StaticConstructorOnStartup]
    public static class AIMainThreadDispatcher
    {
        private const int DefaultMaxActionsPerFlush = 64;
        private const int DefaultMaxFlushMilliseconds = 8;
        private static readonly ConcurrentQueue<QueuedAction> Queue = new ConcurrentQueue<QueuedAction>();
        private static int _mainThreadId = -1;
        private static int _pumpInitialized;

        private sealed class QueuedAction
        {
            public Action Execute;
            public CancellationToken CancellationToken;
            public string Label;
            public Stopwatch Stopwatch;
        }

        static AIMainThreadDispatcher()
        {
            InitializePump();
        }

        private sealed class AIMainThreadPump : MonoBehaviour
        {
            private void Awake()
            {
                RegisterMainThread();
            }

            private void Update()
            {
                RegisterMainThread();
                Flush(DefaultMaxActionsPerFlush, DefaultMaxFlushMilliseconds);
            }
        }

        public static void InitializePump()
        {
            if (Interlocked.Exchange(ref _pumpInitialized, 1) == 1) return;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    RegisterMainThread();
                    var go = new GameObject("WulaAI_MainThreadPump");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    go.AddComponent<AIMainThreadPump>();
                    WulaLog.Debug("[WulaAI][MainThread] Unity frame pump initialized.");
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _pumpInitialized, 0);
                    Log.Error("[WulaAI][MainThread] Failed to initialize pump: " + ex);
                }
            });
        }

        public static void RegisterMainThread()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default(CancellationToken), string label = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledTask<T>(cancellationToken);
            }
            if (IsMainThread)
            {
                return ExecuteImmediately(action, cancellationToken, label);
            }

            var tcs = new TaskCompletionSource<T>();
            var queued = new QueuedAction
            {
                CancellationToken = cancellationToken,
                Label = SafeLabel(label),
                Stopwatch = Stopwatch.StartNew()
            };
            queued.Execute = () =>
            {
                if (queued.CancellationToken.IsCancellationRequested)
                {
                    TrySetCanceled(tcs);
                    LogDispatcher(queued.Label, "cancelled before start", queued.Stopwatch.ElapsedMilliseconds);
                    return;
                }
                try
                {
                    LogDispatcher(queued.Label, "started", queued.Stopwatch.ElapsedMilliseconds);
                    tcs.SetResult(action());
                    LogDispatcher(queued.Label, "completed", queued.Stopwatch.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                    LogDispatcher(queued.Label, "failed: " + ex.Message, queued.Stopwatch.ElapsedMilliseconds);
                }
            };
            Queue.Enqueue(queued);
            LogDispatcher(queued.Label, "queued", 0);
            InitializePump();
            return tcs.Task;
        }

        public static Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default(CancellationToken), string label = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledTask<T>(cancellationToken);
            }
            if (IsMainThread)
            {
                return ExecuteImmediatelyAsync(action, cancellationToken, label);
            }

            var tcs = new TaskCompletionSource<T>();
            var queued = new QueuedAction
            {
                CancellationToken = cancellationToken,
                Label = SafeLabel(label),
                Stopwatch = Stopwatch.StartNew()
            };
            queued.Execute = async () =>
            {
                if (queued.CancellationToken.IsCancellationRequested)
                {
                    TrySetCanceled(tcs);
                    LogDispatcher(queued.Label, "cancelled before start", queued.Stopwatch.ElapsedMilliseconds);
                    return;
                }
                try
                {
                    LogDispatcher(queued.Label, "started", queued.Stopwatch.ElapsedMilliseconds);
                    tcs.SetResult(await action());
                    LogDispatcher(queued.Label, "completed", queued.Stopwatch.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                    LogDispatcher(queued.Label, "failed: " + ex.Message, queued.Stopwatch.ElapsedMilliseconds);
                }
            };
            Queue.Enqueue(queued);
            LogDispatcher(queued.Label, "queued", 0);
            InitializePump();
            return tcs.Task;
        }

        public static void Flush()
        {
            Flush(int.MaxValue, int.MaxValue);
        }

        public static void Flush(int maxActions, int maxMilliseconds)
        {
            RegisterMainThread();
            int processed = 0;
            var stopwatch = Stopwatch.StartNew();
            int actionLimit = Math.Max(1, maxActions);
            int timeLimit = Math.Max(1, maxMilliseconds);
            while (processed < actionLimit && stopwatch.ElapsedMilliseconds <= timeLimit && Queue.TryDequeue(out var action))
            {
                processed++;
                action.Execute();
            }
        }

        private static bool IsMainThread => _mainThreadId >= 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        private static Task<T> ExecuteImmediately<T>(Func<T> action, CancellationToken cancellationToken, string label)
        {
            var safeLabel = SafeLabel(label);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogDispatcher(safeLabel, "started inline", 0);
                var result = action();
                LogDispatcher(safeLabel, "completed inline", stopwatch.ElapsedMilliseconds);
                return Task.FromResult(result);
            }
            catch (OperationCanceledException)
            {
                LogDispatcher(safeLabel, "cancelled inline", stopwatch.ElapsedMilliseconds);
                return CancelledTask<T>(cancellationToken);
            }
            catch (Exception ex)
            {
                LogDispatcher(safeLabel, "failed inline: " + ex.Message, stopwatch.ElapsedMilliseconds);
                var tcs = new TaskCompletionSource<T>();
                tcs.SetException(ex);
                return tcs.Task;
            }
        }

        private static async Task<T> ExecuteImmediatelyAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken, string label)
        {
            var safeLabel = SafeLabel(label);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogDispatcher(safeLabel, "started inline", 0);
                var result = await action();
                LogDispatcher(safeLabel, "completed inline", stopwatch.ElapsedMilliseconds);
                return result;
            }
            catch (OperationCanceledException)
            {
                LogDispatcher(safeLabel, "cancelled inline", stopwatch.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                LogDispatcher(safeLabel, "failed inline: " + ex.Message, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        private static Task<T> CancelledTask<T>(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<T>();
            TrySetCanceled(tcs);
            return tcs.Task;
        }

        private static void TrySetCanceled<T>(TaskCompletionSource<T> tcs)
        {
            tcs.TrySetCanceled();
        }

        private static string SafeLabel(string label)
        {
            return string.IsNullOrWhiteSpace(label) ? "main-thread-action" : label.Trim();
        }

        private static void LogDispatcher(string label, string stage, long elapsedMs)
        {
            WulaLog.Debug($"[WulaAI][MainThread][{label}] {stage}, elapsedMs={elapsedMs}, queued={Queue.Count}");
        }
    }
}

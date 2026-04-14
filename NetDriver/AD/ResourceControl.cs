using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace NetDriver.AD
{
    internal static class ResourceControl
    {
        private readonly static ConcurrentDictionary<Task, CancellationTokenSource> _backgroundTasks = new();
        private static bool working = true;

        public static async Task Shutdown()
        {
            working = false;
            foreach (var t in _backgroundTasks.Keys)
            {
                if (_backgroundTasks.TryGetValue(t, out var tokenSource))
                {
                    if (tokenSource.Token.CanBeCanceled) await tokenSource.CancelAsync();
                }
            }

            await Task.WhenAll(Tasks);

            _backgroundTasks.Clear();
        }

        public static List<Task> Tasks { get 
            {
                return _backgroundTasks.Keys.ToList();
            }
        }

        public static async Task TerminateTask(Task tsk)
        {
            if (!working) return;
            if (_backgroundTasks.TryGetValue(tsk, out var tokenSource))
            {
                if (tokenSource.Token.CanBeCanceled) await tokenSource.CancelAsync();
                await tsk;
            }
        }

        public static bool AnnounceTask(Task tsk, CancellationTokenSource cts)
        {
            if (!working) return false;
            if (_backgroundTasks.TryAdd(tsk, cts))
            {
                tsk.ContinueWith(_ =>
                {
                    if (_backgroundTasks.TryRemove(tsk, out var removedCts))
                        removedCts.Dispose();
                }, TaskScheduler.Default);
                return true;
            }
            return false;
        }
    }
}

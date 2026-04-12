using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace NetDriver.AD
{
    internal static class ResourceControl
    {
        private readonly static ConcurrentDictionary<Task, CancellationTokenSource> _backgroundTasks = new();
        private readonly static CancellationTokenSource _globalCts = new CancellationTokenSource();

        public static async Task Shutdown()
        {
            await _globalCts.CancelAsync();
            await Task.WhenAll(Tasks);

            foreach (var task in Tasks)
            {
                if (_backgroundTasks.TryRemove(task, out var cts))
                    cts.Dispose();
            }

            _globalCts.Dispose();
        }

        public static List<Task> Tasks { get 
            {
                return _backgroundTasks.Keys.ToList();
            }
        }

        public static CancellationTokenSource CreateLinkedToken(CancellationToken? t = null)
        {
            if (t != null)
            {
                return CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token, t.Value);
            }
            return CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token);
        }
        public static async Task TerminateTask(Task tsk)
        {
            if (_backgroundTasks.TryGetValue(tsk, out var tokenSource))
            {
                if (tokenSource.Token.CanBeCanceled) await tokenSource.CancelAsync();
                await tsk;
            }
        }

        public static bool AnnounceTask(Task tsk, CancellationTokenSource cts)
        {
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

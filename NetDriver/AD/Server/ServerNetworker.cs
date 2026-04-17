using Shared.Source.tools;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace NetDriver.AD.Server
{
    public class ServerNetworker : INetworker
    {
        private bool _working = true;
        public readonly Socket socket = new(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp
        );
        private readonly Func<Socket, CancellationToken, Task> accepting;

        public ServerNetworker(Func<Message, Socket, CancellationToken, Task> proc, Func<Socket, CancellationToken, Task> acceptingFunc, IPEndPoint endPoint) : base(proc)
        {
            socket.Bind(endPoint);
            socket.Listen();

            accepting = acceptingFunc;
            var ts = new CancellationTokenSource();
            if (!_backgroundTask.TryAdd(AcceptingAsync(ts.Token), ts))
            {
                ts.Cancel();
                ts.Dispose();
            }
        }

        private async Task AcceptingAsync(CancellationToken ct)
        {
            while (_working)
            {
                var clientConnection = await socket.AcceptAsync(ct);
                var ts = new CancellationTokenSource();
                if (!_backgroundTask.TryAdd(accepting(clientConnection, ts.Token), ts))
                {
                    ts.Cancel();
                    ts.Dispose();
                }
            }
        }

        public override async Task DisposeAsync()
        {
            await base.DisposeAsync();

            socket.Close();
            socket.Dispose();
        }
    }
}

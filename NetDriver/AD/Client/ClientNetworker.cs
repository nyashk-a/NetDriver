using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetDriver.AD.Client
{
    public class ClientNetworker : INetworker
    {
        public readonly Socket socket = new(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp
        );

        public ClientNetworker(Func<Message, Socket, CancellationToken, Task> proc, IPEndPoint adress) : base(proc)
        {
            socket.Connect(adress);


            var ts = new CancellationTokenSource();
            if (!_backgroundTask.TryAdd(ListeningSocket(socket, ts.Token), ts))
            {
                ts.Cancel();
                ts.Dispose();
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

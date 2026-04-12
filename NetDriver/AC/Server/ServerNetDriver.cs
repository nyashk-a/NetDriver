using Shared.Source.tools;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetDriver.AC.Server
{
    public class ServerNetDriver : INetdriverCore
    {
        public readonly Socket socket = new(
            AddressFamily.InterNetwork, 
            SocketType.Stream, 
            ProtocolType.Tcp
        );

        public readonly ConcurrentDictionary<Socket, DisposableListening> Users = new();
        private readonly CancellationTokenSource _cts = new();
        public ServerNetDriver(Func<Request, Task> Processor, IPEndPoint endPoint)
        {
            processor = Processor;
            InitalizeNetDriver();


            socket.Bind(endPoint);
            socket.Listen();

            _backgroundTasks.Add(AcceptAsync());
        }

        private async Task AcceptAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var clientConnection = await socket.AcceptAsync(_cts.Token);
                    var dl = new DisposableListening();
                    var tsk = ListeningSocket(clientConnection, dl.Cts.Token);
                    Users.TryAdd(clientConnection, dl.Init(tsk));
                    _backgroundTasks.Add(tsk);
                }
                catch (Exception e)
                {
                    DebugTool.Log(new DebugTool.log(DebugTool.log.Level.Error, e.Message, LOGFOLDER));
                }
            }
        }

        public override void Shutdown()
        {
            try
            {
                    _cts.Cancel();
                    foreach (var s in Users)
                    {
                        s.Key.Close();
                        s.Value.Cts.Cancel();
                    }
                    foreach (var s in Users)
                    {
                        s.Key.Dispose();
                        s.Value.Cts.Dispose();
                    }
                socket.Close();
                socket.Dispose();
                _cts.Dispose();
                base.Shutdown();
            }
            catch (Exception e)
            {
                DebugTool.Log(new DebugTool.log(DebugTool.log.Level.Error, e.Message, LOGFOLDER));
            }
        }

        public class DisposableListening() : IDisposable
        {
            private Task Listening;
            public readonly CancellationTokenSource Cts = new();

            public DisposableListening Init(Task listening)
            {
                Listening = listening;
                return this;
            }

            public void Dispose()
            {
                Cts.Cancel();
                Cts.Dispose();
            }
        }
    }
}

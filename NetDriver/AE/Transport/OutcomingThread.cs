using NetDriver.AE;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace NetDriver.AE
{
    internal class OutcomingController : IAsyncDisposable
    {
        private readonly Socket socket;
        private readonly Task writing;
        private readonly Channel<byte[]> outcomingBuffer = Channel.CreateUnbounded<byte[]>();
        private readonly CancellationTokenSource _cts = new();

        public OutcomingController(Socket sock)
        {
            socket = sock;
            writing = Sending();
        }

        private async Task Sending()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var content = await outcomingBuffer.Reader.ReadAsync(_cts.Token);

                    await socket.SendAsync(content);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                outcomingBuffer.Writer.TryComplete();
            }
        }

        public async Task Send(byte[] content)
        {
            await outcomingBuffer.Writer.WriteAsync(content);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            await writing;
            _cts.Dispose();
        }
    }
}

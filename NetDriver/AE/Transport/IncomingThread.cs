using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Channels;

namespace NetDriver.AE
{
    internal class IncomingController : IDisposable
    {
        private readonly Socket socket;
        private readonly Task redading;
        private readonly Channel<byte> incomingBuffer = Channel.CreateUnbounded<byte>();
        private readonly CancellationTokenSource _cts = new();

        public IncomingController(Socket sock)
        {
            socket = sock;
            redading = Reading();
        }

        private async Task Reading()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        var buffer = new byte[2048];
                        var realCount = await socket.ReceiveAsync(buffer, _cts.Token);
                        for (int i = 0; i < realCount; i++)
                        {
                            await incomingBuffer.Writer.WriteAsync(buffer[i], _cts.Token);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }
            finally
            {
                incomingBuffer.Writer.TryComplete();
            }
        }

        public async Task<byte[]> GetChunk(UInt32 chunkSize, int timeout=2000)
        {
            var buffer = new byte[chunkSize];

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            cts.CancelAfter(timeout);

            for (int i = 0; i < chunkSize; i++)
            {
                try
                {
                    buffer[i] = await incomingBuffer.Reader.ReadAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            return buffer;
        }

        public async Task DisposeAsync()
        {
            _cts.Cancel();
            await redading;
            _cts.Dispose();
        }

        void IDisposable.Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }
    }
}
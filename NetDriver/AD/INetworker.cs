using AVcontrol;
using JabrAPI;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace NetDriver.AD
{
    public abstract partial class INetworker
    {
        private readonly static int TimeOut = 500;
        public readonly Func<Message, Socket, Task> Processor;

        private readonly ConcurrentDictionary<Guid, Request> _waitingList = new();
        private readonly Channel<IncomingRequest> _incomingList = Channel.CreateBounded<IncomingRequest>(1024 * 16);

        public INetworker(Func<Message, Socket, Task> proc)
        {
            Processor = proc;

            var ts = new CancellationTokenSource();
            if (ResourceControl.AnnounceTask(IncomingHandler(ts.Token), ts))
            {
                ts.Cancel();
                ts.Dispose();
            }
        }


        public async Task ListeningSocket(Socket sock, CancellationToken t)
        {
            while (!t.IsCancellationRequested)
            {
                var h = new byte[21];
                int read = 0;
                while (read < h.Length)
                {
                    read += await sock.ReceiveAsync(h.AsMemory(read, h.Length - read));
                }
                read = 0;

                var conf = Message.PartialParse(h);

                var lastFragment = new byte[conf.expectedSize];

                while (read < conf.expectedSize)
                {
                    read += await sock.ReceiveAsync(lastFragment.AsMemory(read, lastFragment.Length - read));
                }

                var msg = new Message(conf, lastFragment);

                await _incomingList.Writer.WriteAsync(new IncomingRequest(sock, msg));
            }
        }

        private async Task IncomingHandler(CancellationToken cts)
        {
            var reader = _incomingList.Reader;

            await foreach (var ir in reader.ReadAllAsync(cts))
            {
                switch (ir.message.msgType)
                {
                    case Message.Types.ConfigurateMessage:
                        var suid = new Guid(ir.message.content.AsSpan(0, 16));
                        int singlePackSize = FromBinary.LittleEndian<int>(ir.message.content.AsSpan(16, 4));
                        int packCount = FromBinary.LittleEndian<int>(ir.message.content.AsSpan(16 + 4, 4));
                        string name = FromBinary.Utf16(ir.message.content.AsSpan(16 + 4 + 4, ir.message.content.Length - (16 + 4 + 4)));
                        break;
                    case Message.Types.PartFromFileMessage:
                        break;
                    case Message.Types.AnswerToMessage:
                        if (_waitingList.TryGetValue(ir.message.msgSuid, out var rq))
                        {
                            rq.Activate(ir.message);
                        }
                        break;
                    default:
                        var ts = new CancellationTokenSource();
                        if (ResourceControl.AnnounceTask(Processor(ir.message, ir.socket), ts))
                        {
                            ts.Cancel();
                            ts.Dispose();
                        }
                        break;
                }
            }
        }

        public async virtual Task<Message?> SendWithCallback(Socket sock, Message msg)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var tcs = new TaskCompletionSource<Message>();
            var rq = new Request(tcs);
            _waitingList.TryAdd(msg.msgSuid, rq);

            await sock.SendAsync(msg.pack);

            var res = await Task.WhenAny(tcs.Task, Task.Delay(TimeOut));
            if (res == tcs.Task)
            {
                _waitingList.TryRemove(msg.msgSuid, out _);
                return (await tcs.Task);
            }
            return null;
        }

        public async virtual Task SendWithoutCallback(Socket sock, Message msg)
        {
            await sock.SendAsync(msg.pack);
        }
    }
}

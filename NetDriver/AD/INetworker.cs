using AVcontrol;
using JabrAPI;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Threading.Channels;

namespace NetDriver.AD
{
    public abstract partial class INetworker
    {
        private readonly static int TimeOut = 500;
        protected Func<Message, Socket, CancellationToken, Task> Processor;

        private readonly ConcurrentDictionary<Guid, Request> _waitingList = new();
        private readonly Channel<IncomingRequest> _incomingList = Channel.CreateBounded<IncomingRequest>(1024 * 16);

        private readonly ConcurrentDictionary<Guid, ContentBuilder> _buildersList = new();
        protected readonly ConcurrentDictionary<Task, CancellationTokenSource> _backgroundTask = new();

        public INetworker(Func<Message, Socket, CancellationToken, Task> proc)
        {
            Processor = proc;

            var ts = new CancellationTokenSource();
            if (!_backgroundTask.TryAdd(IncomingHandler(ts.Token), ts))
            {
                ts.Cancel();
                ts.Dispose();
            }
            var ct = new CancellationTokenSource();
            if (!_backgroundTask.TryAdd(Cleaning(ct.Token), ct))
            {
                ct.Cancel();
                ct.Dispose();
            }
        }

        public async virtual Task DisposeAsync()
        {
            var b = new List<Task>();
            foreach (var a in _backgroundTask)
            {
                b.Add(a.Key);
                a.Value.Cancel();
            }
            foreach (var a in _buildersList.Keys)
            {
                b.Add(DisposeBuildder(a));
            }
            await Task.WhenAll(b);
            foreach (var a in _backgroundTask.Keys)
            {
                a.Dispose();
            }

            _backgroundTask.Clear();
            _buildersList.Clear();
        }

        public async Task ListeningSocket(Socket sock, CancellationToken t)
        {
            while (!t.IsCancellationRequested)
            {
                try
                {
                    var h = new byte[21];
                    int read = 0;
                    while (read < h.Length)
                    {
                        read += await sock.ReceiveAsync(h.AsMemory(read, h.Length - read), t);
                    }
                    read = 0;

                    var conf = Message.PartialParse(h);

                    var lastFragment = new byte[conf.expectedSize];

                    while (read < conf.expectedSize)
                    {
                        read += await sock.ReceiveAsync(lastFragment.AsMemory(read, lastFragment.Length - read), t);
                    }

                    var msg = new Message(conf, lastFragment);

                    await _incomingList.Writer.WriteAsync(new IncomingRequest(sock, msg));
                }
                catch (OperationCanceledException)
                {

                }
                catch (Exception e)
                {
                    Console.WriteLine($"нужно сделать логи но чуть позже ({e})\n\n");
                }
            }
        }

        private async Task IncomingHandler(CancellationToken cts)
        {
            var reader = _incomingList.Reader;

            try
            {
                await foreach (var ir in reader.ReadAllAsync(cts))
                {
                    try
                    {
                        switch (ir.message.msgType)
                        {
                            case Message.Types.ConfigurateMessage:
                                var suidC = new Guid(ir.message.content.AsSpan(0, 16));
                                int singlePackSize = FromBinary.LittleEndian<int>(ir.message.content.AsSpan(16, 4));
                                int packCount = FromBinary.LittleEndian<int>(ir.message.content.AsSpan(16 + 4, 4));
                                string name = FromBinary.Utf16(ir.message.content.AsSpan(16 + 4 + 4, ir.message.content.Length - (16 + 4 + 4)));

                                _buildersList.TryAdd(suidC, new ContentBuilder(name, singlePackSize, packCount, DisposeBuildder, suidC));
                                await SendWithoutCallback(ir.socket, new Message(ToBinary.Utf16("81"), Message.Types.AnswerToMessage, ir.message.msgSuid));
                                break;


                            case Message.Types.PartFromFileMessage:
                                var suidP = new Guid(ir.message.content.AsSpan(0, 16));

                                if (_buildersList.TryGetValue(suidP, out var cb))
                                    await cb.WriteNewPack(ir.message.content, ir.message.packNum.Value);
                                await SendWithoutCallback(ir.socket, new Message(ToBinary.Utf16("catch"), Message.Types.AnswerToMessage, ir.message.msgSuid));
                                break;


                            case Message.Types.AnswerToMessage:
                                if (_waitingList.TryGetValue(ir.message.msgSuid, out var rq))
                                {
                                    rq.Activate(ir.message);
                                }
                                break;


                            default:
                                var ts = new CancellationTokenSource();
                                if (!_backgroundTask.TryAdd(Processor(ir.message, ir.socket, ts.Token), ts))
                                {
                                    ts.Cancel();
                                    ts.Dispose();
                                }
                                break;
                        }
                    }
                    catch (OperationCanceledException)
                    {

                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"нужно сделать логи но чуть позже ({e})\n\n");
                    }
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception e)
            {
                Console.WriteLine($"нужно сделать логи но чуть позже ({e})\n\n");
            }
        }

        public async virtual Task<Message?> SendWithCallback(Socket sock, Message msg)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeOut));
            var tcs = new TaskCompletionSource<Message>();
            var rq = new Request(tcs);
            _waitingList.TryAdd(msg.msgSuid, rq);
            try
            {
                int sent = 0;
                while (sent < msg.pack.Length)
                    sent += await sock.SendAsync(msg.pack.AsMemory(sent));

                await tcs.Task.WaitAsync(cts.Token);
                return tcs.Task.Result;
            }
            catch
            {
                return null;
            }
            finally
            {
                _waitingList.TryRemove(msg.msgSuid, out _);
            }
        }

        public async virtual Task SendWithoutCallback(Socket sock, Message msg)
        {
            int sent = 0;
            while (sent < msg.pack.Length)
                sent += await sock.SendAsync(msg.pack.AsMemory(sent));
        }

        private async Task Cleaning(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var a = _backgroundTask.Keys;
                var res = await Task.WhenAny(a);

                if (_backgroundTask.TryRemove(res, out var cts))
                {
                    cts.Dispose();
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace NetDriver.AE
{
    public delegate Task IncomingEvent(ResultContent content); 
    internal class LogicProcessor : IAsyncDisposable
    {
        private IncomingEvent _incomingEvent;

        public readonly FrameControllerOutput output = new();
        private readonly FrameControllerInput input = new();

        private readonly IncomingController incoming;
        private readonly OutcomingController outcoming;

        private readonly CancellationTokenSource _cts = new();

        private Task A;
        private Task B;
        private Task C;
        private Task D;
        private Task E;
        public LogicProcessor(IncomingEvent ievent, Socket sock)
        {
            _incomingEvent = ievent;

            incoming = new(sock);
            outcoming = new(sock);

            A = ExecutorA();
            B = ExecutorB();
            C = ExecutorC();
            D = ExecutorD();
            E = ExecutorE();
        }

        private async Task ExecutorA()
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

            try
            {
                await foreach (var sf in input.simpleleOutput.Reader.ReadAllAsync(cts.Token))
                {
                    await _incomingEvent.Invoke(new ResultContent((ResultContent.Type)sf.header.type, sf.content.content, sf.content.frameuid));
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ExecutorB()
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

            try 
            { 
                await foreach (var sf in input.answersOnReq.Reader.ReadAllAsync(cts.Token))
                {
                    output.CatchAnswer(sf);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ExecutorC()
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

            try
            {

                await foreach (var sf in input.SystemSend.Reader.ReadAllAsync(cts.Token))
                {
                    await output.SendSingle(sf);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ExecutorD()
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

            try
            {
                await foreach (var sf in output.outcomingStack.Reader.ReadAllAsync(cts.Token))
                {
                    await outcoming.Send(sf);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ExecutorE()
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

            while (!cts.IsCancellationRequested)
            {
                var h = await incoming.GetChunk(9);
                if (h.Length == 0) continue;
                var header = FrameParser.UnpackHeader(h);

                var c = await incoming.GetChunk(header.contentSize);
                if (c.Length == 0) continue;
                var content = FrameParser.UnpackContent(c);

                await input.Distribute(new netframe(header, content));
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();

            await outcoming.DisposeAsync();
            await incoming.DisposeAsync();
            output.Dispose();
            input.Dispose();

            await A;
            await B;
            await C;
            await D;
            await E;

            _cts.Dispose();
        }
    }

    public class ResultContent(ResultContent.Type type, byte[] content, Guid? uid=null)
    {
        public readonly Type type = type;

        public readonly byte[] content = content;

        public readonly Guid? frameuid = uid;

        public enum Type : byte
        {
            single = 0,
            from = 1,
            into = 2,
        }
    }

    public enum FileParametrs
    {
        Straight,
        Random,
        Reverse,
    }
}

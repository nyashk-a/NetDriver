using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace NetDriver.AC
{
    //public class MassiveContentBuilder : IDisposable
    //{
    //    private static readonly string swapDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
    //    private readonly ConcurrentDictionary<int, Message> _hash = new();
    //    private readonly Channel<Message> _queueToWrite;
    //    private readonly int expectedQuantity;
    //    private readonly CancellationTokenSource _cts = new();
    //    private readonly Task _bgTask;
    //    private bool _disposed = false;
    //    private readonly FileStream _filewriter;
    //    private volatile int actualNumder = 0;
    //    private readonly Action<MassiveContentBuilder> _disposeSelf;

    //    public readonly string pathToFolder;
    //    public readonly Guid FileGuid;



    //    public MassiveContentBuilder(Action<MassiveContentBuilder> disposeSelf, Guid fileGuid, int expectedQuantity, string fileName)
    //    {
    //        _disposeSelf = disposeSelf;

    //        this.expectedQuantity = expectedQuantity;
    //        FileGuid = fileGuid;
    //        pathToFolder = Path.Combine(swapDir, fileName);
    //        _queueToWrite = Channel.CreateBounded<Message>(expectedQuantity);

    //        _bgTask = WritingContent(_cts.Token);

    //        Directory.CreateDirectory(swapDir);
    //        _filewriter = File.Create(pathToFolder);
    //    }
    //    public async Task WritePackage(Message msg)
    //    {
    //        if (_disposed) return;

    //        int current = Interlocked.CompareExchange(ref actualNumder, 0, 0);
    //        if (msg.serialNumber >= current)
    //        {
    //            _queueToWrite.Writer.TryWrite(msg);
    //        }
    //        _hash.TryAdd(msg.serialNumber, msg);
    //    }

    //    private async Task WritingContent(CancellationToken token)
    //    {
    //        var reader = _queueToWrite.Reader;
    //        try
    //        {
    //            await foreach (var msg in reader.ReadAllAsync(token))
    //            {
    //                if (msg.serialNumber == actualNumder)
    //                {
    //                    byte[] cnt = new byte[msg.content.Length - 16];
    //                    Array.Copy(msg.content, 16, cnt, 0, cnt.Length);
    //                    await _filewriter.WriteAsync(cnt);
    //                    _hash.TryRemove(actualNumder, out _);
    //                    actualNumder = actualNumder + 1;
    //                }

    //                while (_hash.TryGetValue(actualNumder, out var nMsg))
    //                {
    //                    byte[] cnt = new byte[nMsg.content.Length - 16];
    //                    Array.Copy(nMsg.content, 16, cnt, 0, cnt.Length);
    //                    await _filewriter.WriteAsync(cnt);
    //                    _hash.TryRemove(actualNumder, out _);
    //                    actualNumder = actualNumder + 1;
    //                }

    //                if (actualNumder == expectedQuantity)
    //                {
    //                    _queueToWrite.Writer.Complete();
    //                }
    //            }
    //        }
    //        finally
    //        {
    //            _disposeSelf(this);
    //        }
    //    }

    //    public void Dispose()
    //    {
    //        _disposed = true;
    //        _cts.Cancel();
    //        _bgTask.Wait(TimeSpan.FromSeconds(5));
    //        _queueToWrite.Writer.TryComplete();
    //        _filewriter?.Dispose();
    //        _cts.Dispose();
    //    }
    //}

    public class MassiveContentBuilder : IDisposable
    {
        private static readonly string swapDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");


        public readonly string PathToFile;
        public readonly Guid FileGuid;

        private readonly FileStream _fileStream;

        private readonly int _expectedQuantity;
        private readonly int _chunkSize;
        private readonly long _dataSize;

        private int _nowCount = 0;
        private long _totalBytesWritten = 0;

        private readonly Action<MassiveContentBuilder> _disposeFunc;
        private readonly object _lock = new object();

        private readonly Channel<Message> _queueToWrite;
        private readonly Task _fileWriterFunc;
        private readonly CancellationTokenSource _cts = new();
        private readonly HashSet<int> _addedList = new();

        public MassiveContentBuilder(Action<MassiveContentBuilder> disposeSelf, Guid fileGuid, int expectedQuantity, int chunkSize, long dataSize , string fileName)
        {
            _expectedQuantity = expectedQuantity;
            _disposeFunc = disposeSelf;
            _chunkSize = chunkSize;
            _dataSize = dataSize;

            PathToFile = Path.Combine(swapDir, fileName);
            FileGuid = fileGuid;

            Directory.CreateDirectory(swapDir);
            _fileStream = File.Create(PathToFile);
            _queueToWrite = Channel.CreateBounded<Message>(_expectedQuantity);

            _fileWriterFunc = WritingContent(_cts.Token);
        }

        public void WritePackage(Message msg)
        {
            if (_addedList.Contains(msg.serialNumber)) return;

            _queueToWrite.Writer.TryWrite(msg);
            _addedList.Add(msg.serialNumber);
        }

        private async Task WritingContent(CancellationToken token)
        {
            var reader = _queueToWrite.Reader;
            try
            {
                await foreach (var msg in reader.ReadAllAsync(token))
                {
                    long position = (long)msg.serialNumber * _chunkSize;

                    byte[] cnt = new byte[msg.content.Length - 16];
                    Array.Copy(msg.content, 16, cnt, 0, cnt.Length);

                    _fileStream.Position = position;

                    await _fileStream.WriteAsync(cnt, 0, cnt.Length);
                    lock (_lock) { _nowCount++; }

                    Interlocked.Add(ref _totalBytesWritten, cnt.Length);
                    lock (_lock)
                    {
                        if (_nowCount == _expectedQuantity)
                        {
                            _queueToWrite.Writer.Complete();
                        }
                    }
                }
            }
            finally
            {
                _fileStream.SetLength(Interlocked.Read(ref _totalBytesWritten));
                await _fileStream.FlushAsync();
                _disposeFunc(this);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _queueToWrite.Writer.TryComplete();
            _fileWriterFunc.Wait(TimeSpan.FromSeconds(10));
            _fileStream?.Dispose();
            _cts.Dispose();
        }
    }
}

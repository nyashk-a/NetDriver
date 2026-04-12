using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NetDriver.AC
{
    public class MassiveContentBuilder
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

        private readonly Func<MassiveContentBuilder, Task> _disposeFunc;
        private readonly object _lock = new object();

        private readonly Channel<Message> _queueToWrite;
        private readonly Task _fileWriterFunc;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<int, byte> _addedList = new(); // экей ConcurrentHashSet<>
        private bool _isDisposed = false;

        public MassiveContentBuilder(Func<MassiveContentBuilder, Task> disposeSelf, Guid fileGuid, int expectedQuantity, int chunkSize, long dataSize , string fileName)
        {
            _expectedQuantity = expectedQuantity;
            _disposeFunc = disposeSelf;
            _chunkSize = chunkSize;
            _dataSize = dataSize;

            PathToFile = Path.Combine(swapDir, fileName);
            FileGuid = fileGuid;

            Directory.CreateDirectory(swapDir);
            _fileStream = new FileStream(PathToFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            _queueToWrite = Channel.CreateBounded<Message>(_expectedQuantity);

            _fileWriterFunc = WritingContent(_cts.Token);

            _ = _fileWriterFunc.ContinueWith(async _ => await _disposeFunc(this), TaskScheduler.Default);
        }

        public async Task WritePackage(Message msg)
        {
            if (!_addedList.TryAdd(msg.serialNumber, 0)) return;

            await _queueToWrite.Writer.WriteAsync(msg);
        }

        private async Task WritingContent(CancellationToken token)
        {
            var reader = _queueToWrite.Reader;
            try
            {
                await foreach (var msg in reader.ReadAllAsync(token))
                {
                    if (msg.content.Length < 16) continue;

                    long position = (long)msg.serialNumber * _chunkSize;

                    _fileStream.Position = position;

                    await _fileStream.WriteAsync(msg.content.AsMemory(16, msg.content.Length - 16), token);

                    Interlocked.Add(ref _totalBytesWritten, msg.content.Length - 16);
                    if (Interlocked.Increment(ref _nowCount) == _expectedQuantity)
                    {
                        _queueToWrite.Writer.Complete();
                    }
                }
            }
            finally
            {
                //_fileStream.SetLength(Interlocked.Read(ref _totalBytesWritten));          мб это банально не нужно?
                await _fileStream.FlushAsync();
            }
        }

        public async Task DisposeAsync()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _cts.Cancel();
            _queueToWrite.Writer.TryComplete();


            await _fileWriterFunc;
            _fileStream?.DisposeAsync();
            _cts.Dispose();
        }
    }
}

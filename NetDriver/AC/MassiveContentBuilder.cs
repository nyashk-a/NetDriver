using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NetDriver.AC
{
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
        private readonly ConcurrentDictionary<int, byte> _addedList = new(); // экей ConcurrentHashSet<>

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
            if (!_addedList.TryAdd(msg.serialNumber, 0)) return;

            _queueToWrite.Writer.TryWrite(msg);
        }

        private async Task WritingContent(CancellationToken token)
        {
            var reader = _queueToWrite.Reader;
            try
            {
                await foreach (var msg in reader.ReadAllAsync(token))
                {
                    long position = (long)msg.serialNumber * _chunkSize;

                    _fileStream.Position = position;

                    await _fileStream.WriteAsync(msg.content.AsMemory(16, msg.content.Length - 16), token);

                    Interlocked.Increment(ref _nowCount);

                    Interlocked.Add(ref _totalBytesWritten, msg.content.Length - 16);
                    if (Volatile.Read(ref _nowCount) == _expectedQuantity)
                    {
                        _queueToWrite.Writer.Complete();
                    }
                }
            }
            finally
            {
                //_fileStream.SetLength(Interlocked.Read(ref _totalBytesWritten));          мб это банально не нужно?
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

using AVcontrol;
using Shared.Source.tools;
using System.Net.Sockets;
using System.Threading.Channels;

namespace NetDriver.AC
{
    public abstract partial class INetdriverCore
    {
        private readonly Channel<Guid> _builderDisposeChannel = Channel.CreateUnbounded<Guid>();


        public async Task SendMassiveMesage(Socket sock, string pathToFile, int part = 1024 * 1024 * 32, IProgress<string> progress=null)
        {
            string fileName = Path.GetFileName(pathToFile);
            FileInfo fileInfo = new FileInfo(pathToFile);
            long fileSize = fileInfo.Length;
            int piceCount = (int)((fileSize + part - 1) / part);
            int complitedCount = 0;


            Guid mg = Guid.NewGuid();
            byte[] mainGuid = mg.ToByteArray();

            var configMessage = MassiveMessageConfigParser(mg, fileSize, part, fileName, piceCount);

            List<Task<Message?>> sendingData = new();

            var firstAns = await SendReqMessageAsync(sock, configMessage);
            if (firstAns != null && FromBinary.Utf16(firstAns.content) == "ready")
            {
                using (FileStream fs = new FileStream(pathToFile, FileMode.Open, FileAccess.Read))
                {
                    byte[] buffer = new byte[part];
                    int sn = 0;
                    int bytesRead;

                    while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        byte[] dataToSend = new byte[bytesRead + 16];
                        Array.Copy(mainGuid, 0, dataToSend, 0, mainGuid.Length);
                        Array.Copy(buffer, 0, dataToSend, 16, bytesRead);

                        var msg = new Message(null, dataToSend, sn);
                        sendingData.Add(SendReqMessageAsync(sock, msg));
                        sn++;
                    } 
                }
                while (sendingData.Any())
                {
                    var completedTask = await Task.WhenAny(sendingData);

                    var res = await completedTask;
                    sendingData.Remove(completedTask);

                    if (FromBinary.Utf16(res?.content) == "catch")
                    {
                        complitedCount++;

                        progress?.Report($"{complitedCount / piceCount * 100.0f:F1}%");
                    }
                    else
                    {
                        DebugTool.Log(new DebugTool.log(
                        DebugTool.log.Level.Warning,
                        $"broken data send with {FromBinary.Utf16(res?.content)}",
                        LOGFOLDER));
                    }
                }
            }
            else
            {
                DebugTool.Log(new DebugTool.log(
                    DebugTool.log.Level.Warning, 
                    "the other party is not responding", 
                    LOGFOLDER));
            }
        }

        private Message MassiveMessageConfigParser(Guid msgGuid, long allDataSize, int chunkSize, string fileName, int piceCount)
        {
            //                  16    8                 4                 auto
            // message conten - [guid][size of all data][single pack size][fileName]

            var dt = ToBinary.Utf16(fileName);
            byte[] mainGuid = msgGuid.ToByteArray();

            var confMessageData = new byte[dt.Length + 16 + 8 + 4];

            Array.Copy(mainGuid, 0, confMessageData, 0, mainGuid.Length);               // [guid]
            Array.Copy(ToBinary.LittleEndian(allDataSize), 0, confMessageData, 16, 8);  // [data size]
            Array.Copy(ToBinary.LittleEndian(chunkSize), 0, confMessageData, 16 + 8, 4);// [single pack size]
            Array.Copy(dt, 0, confMessageData, 16 + 8 + 4, dt.Length);

            return new Message(msgGuid, confMessageData, piceCount);
        }

        private MassiveContentBuilder MassiveMessageConfigParser(Message msg)
        {
            Guid msgGuid;
            long allDataSize;
            int chunkSize;
            string fileName;
            int piceCount;

            msgGuid = new Guid(msg.content.AsSpan(0, 16));
            allDataSize = FromBinary.LittleEndian<Int64>(msg.content.AsSpan(16, 8).ToArray());
            chunkSize = FromBinary.LittleEndian<int>(msg.content.AsSpan(16 + 8, 4).ToArray());
            fileName = FromBinary.Utf16(msg.content.AsSpan(16 + 8 + 4, (msg.content.Length - 16 - 8 - 4)).ToArray());
            piceCount = msg.serialNumber;

            return new MassiveContentBuilder(
                ReportClosure,
                msgGuid,
                piceCount,
                chunkSize,
                allDataSize,
                fileName
                );
        }


        private async Task DisposeBuilderController(CancellationToken cancellationToken = default)
        {
            var reader = _builderDisposeChannel.Reader;

            await foreach (var gd in reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    if (_contentBuilder.TryGetValue(gd, out var pkgBuilder))
                    {
                        await pkgBuilder.DisposeAsync();
                        _contentBuilder.TryRemove(gd, out _);
                    }
                }
                catch (Exception ex)
                {
                    DebugTool.Log(new DebugTool.log(DebugTool.log.Level.Error, ex.Message, LOGFOLDER));
                }
            }
        }
        private async Task ReportClosure(MassiveContentBuilder self)            // только для MassiveContentBuilder!
        {
            await _builderDisposeChannel.Writer.WriteAsync(self.FileGuid);
        }
    }
}

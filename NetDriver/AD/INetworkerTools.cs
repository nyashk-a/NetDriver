using AVcontrol;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NetDriver.AD
{
    public abstract partial class INetworker
    {
        public async Task SendMassiveMessage(Socket sock, string pathToFile, int part = 1024 * 1024 * 32)
        {
            try
            {
                byte[] fileName = ToBinary.Utf16(Path.GetFileName(pathToFile));
                FileInfo fileInfo = new FileInfo(pathToFile);
                long fileSize = fileInfo.Length;
                int piceCount = (int)((fileSize + part - 1) / part);

                var fileSuid = Guid.NewGuid();

                //[suid : 16][single pack size : 4][pack num : 4][name : name lenght]
                var mcontent = new byte[16 + 4 + 4 + fileName.Length];

                Buffer.BlockCopy(fileSuid.ToByteArray(), 0, mcontent, 0, 16);
                Buffer.BlockCopy(ToBinary.LittleEndian(part), 0, mcontent, 16, 4);
                Buffer.BlockCopy(ToBinary.LittleEndian(piceCount), 0, mcontent, 16 + 4, 4);
                Buffer.BlockCopy(fileName, 0, mcontent, 16 + 4 + 4, fileName.Length);

                var mc = new Message(mcontent, Message.Types.ConfigurateMessage);

                var c_res = await SendWithCallback(sock, mc);

                if (c_res != null && FromBinary.Utf16(c_res.content) == "81")
                {
                    var sendingData = new Dictionary<Task<Message?>, int>();
                    using (FileStream fs = new FileStream(pathToFile, FileMode.Open, FileAccess.Read))
                    {
                        byte[] buffer = new byte[part];
                        int sn = 0;
                        int bytesRead;

                        while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            Console.WriteLine($"send pack №{sn}");
                            //[guid : 16][content : contentsize]
                            byte[] dataToSend = new byte[bytesRead + 16];
                            Array.Copy(fileSuid.ToByteArray(), 0, dataToSend, 0, 16);
                            Array.Copy(buffer, 0, dataToSend, 16, bytesRead);

                            var msg = new Message(dataToSend, Message.Types.PartFromFileMessage, null, sn);
                            sendingData.Add(SendWithCallback(sock, msg), sn);
                            sn++;
                        }
                    }

                    while (sendingData.Any())
                    {
                        var ct = await Task.WhenAny(sendingData.Keys);



                        var res = await ct;

                        if (FromBinary.Utf16(res.content) != "catch")
                        {
                            var buffer = new byte[part];
                            sendingData.TryGetValue(ct, out var sn);
                            using (var fs = new FileStream(pathToFile, FileMode.Open, FileAccess.Read))
                            {
                                fs.Seek(sn * part, SeekOrigin.Begin);
                                int bytesRead = fs.Read(buffer, 0, part);
                            }
                            byte[] dataToSend = new byte[part + 16];
                            Array.Copy(fileSuid.ToByteArray(), 0, dataToSend, 0, 16);
                            Array.Copy(buffer, 0, dataToSend, 16, part);

                            var msg = new Message(dataToSend, Message.Types.PartFromFileMessage, null, sn);
                            Console.WriteLine($"re-send pack №{sn}");
                            sendingData.Add(SendWithCallback(sock, msg), sn);
                        }
                        sendingData.Remove(ct, out _);
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

        private async Task DisposeBuildder(Guid suid)
        {
            if (_buildersList.Remove(suid, out var cb))
                await cb.DisposeAsync();
        }
    }
}

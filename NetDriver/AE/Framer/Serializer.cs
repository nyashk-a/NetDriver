using System;
using System.Buffers;
using AVcontrol;

namespace NetDriver.AE
{
    internal static class FrameBuilder
    {
        public static byte[] PackHeader(netframe.Header header)
        {
            var output = new byte[4 + 1 + 4];

            Buffer.BlockCopy(ToBinary.LittleEndian(header.contentSize), 0, output, 0, 4);

            output[4] = (byte)header.type;

            Buffer.BlockCopy(ToBinary.LittleEndian(header.numInFlow), 0, output, 4 + 1, 4);

            return output;
        }

        public static byte[] PackContent(netframe.Content content)
        {
            var output = new byte[content.content.Length + 16];

            Buffer.BlockCopy(content.frameuid.ToByteArray(), 0, output, 0, 16);

            Buffer.BlockCopy(content.content, 0, output, 16, content.content.Length);

            return output;
        }

        public static byte[] PackFrame(netframe frame)
        {
            var h = PackHeader(frame.header);
            var c = PackContent(frame.content);

            var output = new byte[h.Length + c.Length];

            Buffer.BlockCopy(h, 0, output, 0, h.Length);

            Buffer.BlockCopy(c, 0, output, h.Length, c.Length);

            return output;
        }
    }
}
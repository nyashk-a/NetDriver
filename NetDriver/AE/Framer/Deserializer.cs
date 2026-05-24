using System;
using AVcontrol;

namespace NetDriver.AE
{
    internal static class FrameParser
    {
        private const int HeaderSize = 9;
        public static netframe.Header UnpackHeader(ReadOnlySpan<byte> header)
        {
            var length = FromBinary.LittleEndian<uint>(header.Slice(0, 4));
            var type = (netframe.Type)header[4];
            var num = FromBinary.LittleEndian<uint>(header.Slice(5, 4));

            return new netframe.Header(length, type, num);
        }
        public static netframe.Content UnpackContent(ReadOnlySpan<byte> content)
        {
            var guid = new Guid(content.Slice(0, 16));
            var payload = content.Slice(16).ToArray();
            return new netframe.Content(guid, payload);
        }

        public static netframe UnpackFrame(ReadOnlySpan<byte> frame)
        {
            if (frame.Length < HeaderSize)
                throw new ArgumentException($"Frame must be at least {HeaderSize} bytes", nameof(frame));

            var headerSpan = frame.Slice(0, HeaderSize);
            var header = UnpackHeader(headerSpan);

            var contentSpan = frame.Slice(HeaderSize);
            var content = UnpackContent(contentSpan);

            return new netframe(header, content);
        }

        public static netframe BuildFrame(netframe.Type t, Guid frameuid, byte[] content, uint num = 0)
        {
            var header = new netframe.Header((uint)(content.Length + 16), t, num);
            var frameContent = new netframe.Content(frameuid, content);
            return new netframe(header, frameContent);
        }
    }
}
using AVcontrol;

namespace NetDriver.AD
{
    public class Message
    {
        // [content lenght (только длинна полезной нагрузки) : 4]
        // [message type (определяется из енума) : 1]
        // [suid : 16]
        // [content : content lenght]
        // [(опционально, в зависимости от типа) packNumber : 4]

        public enum Types
        {
            ConfigurateMessage = 0b00000000,
            AnswerToMessage = 0b10000000,
            RequestMessage = 0b01000000,
            SingleMessage = 0b11000000,
            PartFromFileMessage = 0b00100000,
        }

        public readonly byte[] content;
        public readonly Types msgType;
        public readonly Guid msgSuid;
        public readonly int? packNum;

        public Message(
            byte[] data,
            Types type,
            Guid? suid = null,
            int? packnum = null
            )
        {
            content = data;
            msgType = type;
            msgSuid = suid == null ? Guid.NewGuid() : suid.Value;
            packNum = packnum;

            if (msgType == Types.PartFromFileMessage && packNum == null) throw new Exception("cant create message with type Types.PartFromFileMessage and without package number");
        }

        public Message(MessageConfig msgConf, byte[] pack)
        {
            content = new byte[msgConf.DataSize];
            Buffer.BlockCopy(pack, 0, content, 0, msgConf.DataSize);

            msgType = msgConf.msgType;
            msgSuid = msgConf.msgSuid;

            packNum = msgType == Types.PartFromFileMessage ? FromBinary.LittleEndian<int>(pack.AsSpan(msgConf.DataSize, 4)) : null;
        }

        public Message(byte[] pack)
        {
            msgType = (Types)pack[4];
            msgSuid = new Guid(pack.AsSpan(5, 16));

            int datasize = FromBinary.LittleEndian<int>(pack.AsSpan(0, 4));
            content = new byte[datasize];
            Buffer.BlockCopy(pack, 16 + 1 + 4, content, 0, datasize);

            packNum = msgType == Types.PartFromFileMessage ? FromBinary.LittleEndian<int>(pack.AsSpan(21 + datasize, 4)) : null;
        }

        public byte[] pack { get
            {
                var pck = new byte[4 + 1 + 16 + content.Length + (msgType == Types.PartFromFileMessage ? 4 : 0)];
                Buffer.BlockCopy(ToBinary.LittleEndian(content.Length), 0, pck, 0, 4);
                pck[4] = (byte)msgType;
                Buffer.BlockCopy(msgSuid.ToByteArray(), 0, pck, 4 + 1, 16);
                Buffer.BlockCopy(content, 0, pck, 4 + 1 + 16, content.Length);
                if (msgType == Types.PartFromFileMessage) Buffer.BlockCopy(ToBinary.LittleEndian(packNum.Value), 0, pck, 4 + 1 + 16 + content.Length, 4);
                return pck;
            }
        }

        public static MessageConfig PartialParse(byte[] pack)
        {
            if (pack.Length != 21) throw new Exception("pack size not is 21");
            return new MessageConfig
            (
                FromBinary.LittleEndian<int>(pack.AsSpan(0, 4)),
                (Types)pack[4],
                new Guid(pack.AsSpan(5, 16))
            );
        }
        public struct MessageConfig (int ds, Types mt, Guid ms)
        {
            public readonly int DataSize = ds;
            public readonly Types msgType = mt;
            public readonly Guid msgSuid = ms;

            public readonly int expectedSize = ds + (mt == Types.PartFromFileMessage ? 4 : 0);
        }
    }
}

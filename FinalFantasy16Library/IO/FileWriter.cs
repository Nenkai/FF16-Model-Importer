using Syroot.BinaryData;
using System;
using System.Collections.Generic;
using System.Diagnostics.SymbolStore;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaToolbox.Core.IO
{
    public class FileWriter : BinaryDataWriter
    {
        public FileWriter(Stream stream) : base(stream) { }
        public FileWriter(string path) : base(new FileStream(path, FileMode.Create, FileAccess.Write)) { }

        public void SetByteOrder(bool bigEndian)
        {
            if (bigEndian)
                this.ByteOrder = ByteOrder.BigEndian;
            else
                this.ByteOrder = ByteOrder.LittleEndian;
        }

        public void WriteSignature(string signature) => Write(Encoding.ASCII.GetBytes(signature));

        public void WriteString(string str) => Write(str, BinaryStringFormat.ZeroTerminated);

        public void WriteStrings(List<string> values, Encoding encoding = null)
        {
            foreach (var value in values)
                Write(value, BinaryStringFormat.ZeroTerminated, encoding ?? Encoding);
        }

        public void SeekBegin(uint Offset) { Seek(Offset, SeekOrigin.Begin); }
        public void SeekBegin(int Offset) { Seek(Offset, SeekOrigin.Begin); }
        public void SeekBegin(long Offset) { Seek(Offset, SeekOrigin.Begin); }

        public void WriteSectionSizeU32(long position, long size)
        {
            using (TemporarySeek(position, System.IO.SeekOrigin.Begin)) {
                Write((uint)(size));
            }
        }

        public void WriteUint32Offset(long target, long relativePosition = 0)
        {
            long pos = Position;
            using (TemporarySeek(target, SeekOrigin.Begin)) {
                Write((uint)(pos - relativePosition));
            }
        }

        public void WriteUint16Offset(long target, long relativePosition)
        {
            long pos = Position;
            using (TemporarySeek(target, SeekOrigin.Begin)) {
                Write((ushort)(pos - relativePosition));
            }
        }

        /// <summary>
        /// Aligns the data by writing bytes (rather than seeking)
        /// </summary>
        /// <param name="alignment"></param>
        /// <param name="value"></param>
        public void AlignBytes(int alignment, byte value = 0x00)
        {
            var startPos = Position;
            long position = Seek((-Position % alignment + alignment) % alignment, SeekOrigin.Current);

            Seek(startPos, System.IO.SeekOrigin.Begin);
            while (Position != position)
            {
                Write(value);
            }
        }

        public void WriteStruct<T>(T item) => Write(item.StructToBytes(ByteOrder == ByteOrder.BigEndian));

        public void WriteMultiStruct<T>(List<T> list)
        {
            foreach (T item in list)
                Write(item.StructToBytes(ByteOrder == ByteOrder.BigEndian));
        }

        public void WriteFixedString(string value, int count)
        {
            var buffer = Encoding.UTF8.GetBytes(value);
            //clamp string
            if (buffer.Length > count)
            {
                buffer = buffer.AsSpan().Slice(0, count).ToArray();
                Console.WriteLine($"Warning! String {value} too long!");
            }

            Write(buffer);
            Write(buffer.Length - count);
        }
    }
}

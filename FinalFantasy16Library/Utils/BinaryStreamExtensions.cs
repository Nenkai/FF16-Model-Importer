using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

using CommunityToolkit.HighPerformance;

using FinalFantasy16Library.IO.Extensions;
using FinalFantasy16Library.Utils;

using SixLabors.ImageSharp;

using Syroot.BinaryData;

namespace FinalFantasy16Library.Utils;

public static class BinaryStreamExtensions
{
    extension(BinaryStream bs)
    {
        public string ReadFixedString(int length)
        {
            return bs.Encoding.GetString(bs.ReadBytes(length)).Replace('\0', ' ');
        }

        public string GetSignature(int length = 4)
        {
            string magic = Encoding.ASCII.GetString(bs.ReadBytes(length));
            bs.Position = 0;

            Debug.WriteLine(magic);

            return magic;
        }

        public void ReadSignature(ReadOnlySpan<byte> expectedMagic)
        {
            uint magic = bs.ReadUInt32();
            if (magic != BinaryPrimitives.ReadInt32LittleEndian(expectedMagic))
                throw new Exception($"Expected {BinaryPrimitives.ReadInt32LittleEndian(expectedMagic):X8} but got {magic:X} instead.");
        }

        public bool CheckSignature(uint expectedMagic, long seek_pos = 0)
        {
            var pos = bs.Position;

            if (seek_pos != 0 && seek_pos + sizeof(uint) <= bs.BaseStream.Length)
                bs.Seek(seek_pos, SeekOrigin.Begin);

            uint magic = bs.ReadUInt32();
            bs.Position = pos;

            return magic == expectedMagic;
        }

        public bool CheckSignature(string expectedMagic, long seek_pos = 0)
        {
            var pos = bs.Position;

            if (seek_pos != 0 && seek_pos + expectedMagic.Length <= bs.BaseStream.Length)
                bs.Seek(seek_pos, SeekOrigin.Begin);

            string magic = bs.Encoding.GetString(bs.ReadBytes(expectedMagic.Length));

            bs.Position = pos;

            return magic == expectedMagic;
        }

        //From kuriimu https://github.com/IcySon55/Kuriimu/blob/master/src/Kontract/IO/BinaryReaderX.cs#L40
        public T ReadStruct<T>() => bs.ReadBytes(Marshal.SizeOf<T>()).BytesToStruct<T>(bs.ByteConverter.Endian == Syroot.BinaryData.Core.Endian.Big);
        public List<T> ReadMultipleStructs<T>(int count) => Enumerable.Range(0, count).Select(_ => bs.ReadStruct<T>()).ToList();
        public List<T> ReadMultipleStructs<T>(uint count) => Enumerable.Range(0, (int)count).Select(_ => bs.ReadStruct<T>()).ToList();

        public Half ReadHalf() => BitConverter.Int16BitsToHalf(bs.ReadInt16());

        public void WriteSectionSizeU32(long position, long size)
        {
            using (bs.TemporarySeek(position, SeekOrigin.Begin))
                bs.Write((uint)(size));
        }

        public void WriteUint32Offset(long target, long relativePosition = 0)
        {
            long pos = bs.Position;
            using (bs.TemporarySeek(target, SeekOrigin.Begin))
                bs.Write((uint)(pos - relativePosition));
        }

        public void WriteUint16Offset(long target, long relativePosition)
        {
            long pos = bs.Position;
            using (bs.TemporarySeek(target, SeekOrigin.Begin))
                bs.Write((ushort)(pos - relativePosition));
        }

        public void WriteStruct<T>(T item)
            => bs.Write(item.StructToBytes(bs.ByteConverter.Endian == Syroot.BinaryData.Core.Endian.Big));

        public void WriteMultiStruct<T>(List<T> list)
        {
            foreach (T item in list)
                bs.Write(item.StructToBytes(bs.ByteConverter.Endian == Syroot.BinaryData.Core.Endian.Big));
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

            bs.Write(buffer);
            bs.WriteInt32(buffer.Length - count);
        }

        public void WriteHalf(Half half) => bs.WriteInt16(BitConverter.HalfToInt16Bits(half));
    }
}

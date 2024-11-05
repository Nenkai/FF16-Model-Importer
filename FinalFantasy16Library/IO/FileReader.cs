using Syroot.BinaryData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaToolbox.Core.IO
{
    public class FileReader : BinaryDataReader
    {
        public FileReader(string path, bool closed = false)
       : base(new FileStream(path, FileMode.Open, FileAccess.Read), Encoding.UTF8, closed)
        {

        }

        public FileReader(Stream stream, bool closed = false) 
            : base(stream, Encoding.UTF8, closed)
        {

        }

        public string ReadFixedString(int length)
        {
           return this.Encoding.GetString(this.ReadBytes((int)length)).Replace('\0', ' ');
        }

        public string ReadStringZeroTerminated() => this.ReadString(BinaryStringFormat.ZeroTerminated);

        public string GetSignature(int length = 4)
        {
            string magic = Encoding.ASCII.GetString(ReadBytes(length));
            this.Position = 0;

            Debug.WriteLine(magic);

            return magic;
        }

        public void ReadSignature(string expected_magic)
        {
            string magic = Encoding.GetString(ReadBytes(expected_magic.Length));
            if (expected_magic != magic)
                throw new Exception($"Expected {expected_magic} but got {magic} instead.");
        }

        public bool CheckSignature(uint expected_magic, long seek_pos = 0)
        {
            var pos = this.Position;

            if (seek_pos != 0 && seek_pos + sizeof(uint) <= this.BaseStream.Length)
                this.Seek(seek_pos, SeekOrigin.Begin);

            uint magic = ReadUInt32();
            this.Position = pos;

            return magic == expected_magic;
        }

        public bool CheckSignature(string expected_magic, long seek_pos = 0)
        {
            var pos = this.Position;

            if (seek_pos != 0 && seek_pos + expected_magic.Length <= this.BaseStream.Length)
                this.Seek(seek_pos, SeekOrigin.Begin);

            string magic = Encoding.GetString(ReadBytes(expected_magic.Length));

            this.Position = pos;

            return magic == expected_magic;
        }

        public void SetByteOrder(ushort bom)
        {
            if (bom == 0xFEFF)
                ByteOrder = ByteOrder.BigEndian;
            else
                ByteOrder = ByteOrder.LittleEndian;
        }

        public void SetByteOrder(bool bigEndian)
        {
            this.ByteOrder = bigEndian ? ByteOrder.BigEndian : ByteOrder.LittleEndian;
        }

        public void SeekBegin(long offset) => this.Seek(offset, SeekOrigin.Begin);

        //From kuriimu https://github.com/IcySon55/Kuriimu/blob/master/src/Kontract/IO/BinaryReaderX.cs#L40
        public T ReadStruct<T>() => ReadBytes(Marshal.SizeOf<T>()).BytesToStruct<T>(ByteOrder == ByteOrder.BigEndian);
        public List<T> ReadMultipleStructs<T>(int count) => Enumerable.Range(0, count).Select(_ => ReadStruct<T>()).ToList();
        public List<T> ReadMultipleStructs<T>(uint count) => Enumerable.Range(0, (int)count).Select(_ => ReadStruct<T>()).ToList();
    }
}

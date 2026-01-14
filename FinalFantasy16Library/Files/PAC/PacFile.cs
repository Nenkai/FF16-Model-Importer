using System.Runtime.InteropServices;

using Syroot.BinaryData;

using FinalFantasy16Library.Utils;

namespace FinalFantasy16Library.Files.PAC;

public class PacFile
{
    public List<FileEntry> Files = [];

    public string ArchiveDirName;

    public PacFile(Stream stream)
    {
        Read(new BinaryStream(stream));
    }

    private void Read(BinaryStream r)
    {
        r.ReadSignature("PACK"u8);
        uint headerSize = r.ReadUInt32(); //size

        r.Position = 0;
        byte[] header = r.ReadBytes((int)headerSize);

        var reader = new BinaryStream(new MemoryStream(header));

        reader.Position = 8;
        uint numFiles = reader.ReadUInt32();
        bool useChunks = reader.ReadBoolean();
        bool encrypted = reader.ReadBoolean();
        ushort numChunks = reader.ReadUInt16();
        ulong packSize = reader.ReadUInt64();

        reader.Position = 0x18;
        if (encrypted)
            XorEncrypt.CryptHeaderPart(header.AsSpan(0x18, 0x100));

        ArchiveDirName = reader.ReadString(StringCoding.ZeroTerminated);

        reader.Position = 0x118;
        ulong chunkOffset = reader.ReadUInt64();
        ulong stringTableOffset = reader.ReadUInt64();
        ulong stringTableSize = reader.ReadUInt64();

        reader.Position = 0x400;
        var fileHeaders = reader.ReadMultipleStructs<PacFileEntry>(numFiles);

        reader.Position = (int)chunkOffset;
        var chunkHeaders = reader.ReadMultipleStructs<ChunkEntry>(numChunks);

        if (encrypted)
            XorEncrypt.CryptHeaderPart(header.AsSpan((int)stringTableOffset, (int)stringTableSize));

        foreach (var fHeader in fileHeaders)
        {
            FileEntry f = new FileEntry();
            f.Header = fHeader;

            reader.Position = (int)fHeader.DataOffset;
            f.Data = r.ReadBytes((int)fHeader.CompressedFileSize);

            reader.Position = (int)fHeader.FileNameOffset;
            f.FileName = reader.ReadString(StringCoding.ZeroTerminated);

            // Debug.WriteLine($"{f.FileName} {fHeader.CompressedFileSize}");

            Files.Add(f);
        }
    }

    public class FileEntry
    {
        public PacFileEntry Header;
        public byte[] Data;
        public string FileName;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class ChunkEntry
    {
        public ulong DataOffset;
        public int CompressedChunkSize;
        public int ChunkDecompressedSize;
        public int Padding;
        public ushort ChunkIndex;
        public ushort NumFilesInChunk;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class PacFileEntry
    {
        public uint CompressedFileSize;
        public byte IsCompressed;
        public ChunkedCompressionFlags ChunkedFlags;
        public ushort Padding;
        public ulong DecompressedFileSize;
        public ulong DataOffset;
        public ulong ChunkDefOffset;
        public ulong FileNameOffset;
        public uint FileNameHash;
        public uint CRC32Checksum;
        public uint Padding2;
        public uint ChunkHeaderSize;
    }

    public enum ChunkedCompressionFlags : byte
    {
        None,
        UseSpecificChunk,
        UseMultipleChunks,
        UseSharedChunk,
    }
}

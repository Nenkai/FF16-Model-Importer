using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Serialization;

using AvaloniaToolbox.Core.IO;
using AvaloniaToolbox.Core.Textures;

using Microsoft.Toolkit.HighPerformance.Extensions;

using Syroot.BinaryData;

using static FinalFantasy16Library.Files.NXDF.LayoutInfo;

namespace FinalFantasy16Library.Files.NXDF
{
    public class NxdFile
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class Header
        {
            public Magic Magic = "NXDF";
            public uint Version = 1;
            public Type NexType = Type.ROWS;
            public Category CategoryType = Category.RowsLocalized;
            public byte UsesBaseRowId = 0;
            public byte Reserved = 0;

            public uint BaseRowID;

            public uint Padding2;
            public uint Padding3;
            public uint Padding4;
            public uint Padding5;
        }

        public enum Type : byte
        {
            ROWS = 1,
            ROWSETS,
            DOUBLEKEYED,
        }

        public enum Category : byte
        {
            RowsUnlocalized = 1,
            RowsLocalized,
            RowSetsUnlocalized,
            RowSetsLocalized,
            DoubleKeyedUnlocalized,
            DoubleKeyedLocalized,
        }

        public class RowSet
        {
            public int ID;
            public uint Offset;

            public List<RowHeader> Rows = new List<RowHeader>();
        }

        public class RowHeader
        {
            public int RowID;

            public int RowDataOffset;

            public List<ColumnValue> Values = new List<ColumnValue>();
        }

        public class LocalizedContent
        {
            [XmlAttribute]
            public int Row;

            [XmlElement("Strings")]
            public List<LocalizedString> Strings = new List<LocalizedString>();
        }

        [XmlRoot(ElementName = "LocalizedString", IsNullable = false)]
        public class LocalizedString
        {
            public int Column;

            public string Value;
        }

        public class ColumnValue
        {
            public object Value;

            public ColumnValue() { }

            public ColumnValue(object value)
            {
                Value = value;
            }
        }

        public List<LocalizedContent> Localize = new List<LocalizedContent>();

        [XmlIgnore]
        public List<RowHeader> Rows = new List<RowHeader>();

        public List<RowSet> RowSets = new List<RowSet>();

        public Header NxdHeader = new Header();

        [XmlIgnore]
        public LayoutInfo Layout;

        public NxdFile() { }

        public NxdFile(string path)
        {
            Load(File.OpenRead(path), path);
        }

        public NxdFile(Stream stream, string path)
        {
            Load(stream, path);
        }

        public void Load(Stream stream, string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (File.Exists(Path.Combine("Layouts", $"{name}.layout")))
                Layout = new LayoutInfo(Path.Combine("Layouts", $"{name}.layout"));

            using (var reader = new FileReader(stream))
            {
                NxdHeader = reader.ReadStruct<Header>();

                foreach (var col in Layout.Columns)
                    Console.WriteLine($"{name} {col.Type}");

                switch (NxdHeader.CategoryType)
                {
                    case Category.RowsLocalized:
                    case Category.RowsUnlocalized:
                        {
                            uint rowOffset = reader.ReadUInt32(); //48
                            uint numRows = reader.ReadUInt32();

                            reader.SeekBegin(rowOffset);
                            RowHeader[] rows = new RowHeader[numRows];
                            for (int i = 0; i < numRows; i++)
                            {
                                long start = reader.BaseStream.Position;

                                rows[i] = new RowHeader()
                                {
                                    RowID = reader.ReadInt32(),
                                    RowDataOffset = reader.ReadInt32() + (int)start,
                                };
                            }
                            var size = rows[1].RowDataOffset - rows[0].RowDataOffset;
                            //calculate string table pos
                            var ofs = rows[0].RowDataOffset + rows.Length * size;

                            reader.SeekBegin(ofs);
                            reader.Align(4);

                            bool[] string_types = new bool[size / 4];

                            Dictionary<long, string> stringTable = ParseStringTable(reader);
                            for (int i = 0; i < numRows; i++)
                            {
                                //for the first row, get a list of strings
                                if (i == 0 && Layout == null)
                                {
                                    reader.SeekBegin(rows[i].RowDataOffset);

                                    Layout = new LayoutInfo();
                                    for (int j = 0; j < size / 4; j++)
                                    {
                                        long start = reader.Position;
                                        var ofsv = reader.ReadUInt32() + start;
                                        string_types[j] = stringTable.ContainsKey(ofsv); //check if string offset

                                        Layout.Columns.Add(new Column()
                                        {
                                            Type = stringTable.ContainsKey(ofsv) ? "string" : "int"
                                        });
                                    }
                                }
                                reader.SeekBegin(rows[i].RowDataOffset);
                                rows[i].Values = ParseRow(reader, size, stringTable, Layout);
                            }
                            Rows.AddRange(rows);
                        }


                        foreach (var row in Rows)
                        {
                            List<LocalizedString> values = new List<LocalizedString>();
                            for (int i = 0; i < row.Values.Count; i++)
                            {
                                if (row.Values[i].Value is string)
                                {
                                    string str = (string)row.Values[i].Value;
                                    if (!string.IsNullOrEmpty(str))
                                        values.Add(new LocalizedString()
                                        {
                                            Value = str,
                                            Column = i,
                                        });
                                }
                            }

                            if (values.Count > 0)
                                Localize.Add(new LocalizedContent()
                                {
                                    Strings = values,
                                    Row = Rows.IndexOf(row),
                                });
                        }
                        break;
                    case Category.RowSetsLocalized:
                    case Category.RowSetsUnlocalized:
                        {
                            var pos = reader.Position;

                            uint headerSize = reader.ReadUInt32();
                            uint arrayCount = reader.ReadUInt32();
                            uint reserved = reader.ReadUInt32();
                            uint rowInfoOffset = reader.ReadUInt32();
                            uint maxRowCount = reader.ReadUInt32();

                            RowSet[] rowSets = new RowSet[arrayCount];
                            for (int i = 0; i < arrayCount; i++)
                            {
                                reader.SeekBegin(pos + headerSize + i * 12);

                                rowSets[i] = new RowSet();

                                long start = reader.BaseStream.Position;

                                rowSets[i].ID = reader.ReadInt32();
                                rowSets[i].Offset = reader.ReadUInt32() + (uint)start;
                                uint rowLength = reader.ReadUInt32();

                                RowHeader[] rows = new RowHeader[rowLength];

                                reader.SeekBegin(rowSets[i].Offset);
                                for (int j = 0; j < rowLength; j++)
                                {
                                    long rowStart = reader.BaseStream.Position;

                                    uint rowSetID = reader.ReadUInt32();
                                    uint rowIndex = reader.ReadUInt32();
                                    var dataOffset = reader.ReadUInt32() + rowStart;

                                    rows[j] = new RowHeader()
                                    {
                                        RowID = (int)rowIndex,
                                        RowDataOffset = (int)dataOffset,
                                    };
                                }
                                rowSets[i].Rows.AddRange(rows);
                            }
                            RowSets.AddRange(rowSets);

                            var row_list = RowSets.SelectMany(x => x.Rows).ToList();

                            var size = RowSets[1].Rows[0].RowDataOffset - RowSets[0].Rows.LastOrDefault().RowDataOffset;
                            if (Layout != null)
                                size = Layout.Columns.Count * 4;

                            //calculate string table pos
                            var ofs = rowInfoOffset + maxRowCount * 12;

                            reader.SeekBegin(ofs);
                            reader.Align(4);

                            Dictionary<long, string> stringTable = ParseStringTable(reader);

                            //read data
                            foreach (var set in RowSets)
                            {
                                for (int i = 0; i < set.Rows.Count; i++)
                                {
                                    //for the first row, get a list of strings
                                    if (i == 0 && Layout == null)
                                    {
                                        reader.SeekBegin(set.Rows[i].RowDataOffset);

                                        Layout = new LayoutInfo();
                                        for (int j = 0; j < size / 4; j++)
                                        {
                                            long start = reader.Position;
                                            var ofsv = reader.ReadUInt32() + start;

                                            Layout.Columns.Add(new Column()
                                            {
                                                Type = stringTable.ContainsKey(ofsv) ? "string" : "int",
                                                RelativeOffset = true,
                                            });
                                        }
                                    }
                                    reader.SeekBegin(set.Rows[i].RowDataOffset);
                                    set.Rows[i].Values = ParseRow(reader, size, stringTable, Layout);
                                }
                            }
                        }
                        break;
                    case Category.DoubleKeyedUnlocalized:
                    case Category.DoubleKeyedLocalized:
                        {
                            uint headerSize = reader.ReadUInt32();
                            uint count = reader.ReadUInt32();
                            uint rowInfoOffset = reader.ReadUInt32();
                            uint rowCount = reader.ReadUInt32();
                            uint reserved = reader.ReadUInt32();

                        }
                        break;
                }
            }
        }

        private Dictionary<long, string> ParseStringTable(FileReader reader)
        {
            Dictionary<long, string> stringTable = new Dictionary<long, string>();

            while (!reader.EndOfStream)
            {
                long pos = reader.BaseStream.Position;
                stringTable.Add(pos, reader.ReadStringZeroTerminated());
            }

            return stringTable;
        }

        private List<ColumnValue> ParseRow(FileReader reader, int size,
            Dictionary<long, string> stringTable, LayoutInfo layout)
        {
            List<ColumnValue> values = new List<ColumnValue>();

            var col_start = reader.Position;

            var numValues = size / 4; //assume all sizes align to 4 for now
            for (int i = 0; i < numValues; i++)
            {
                long start = reader.Position;

                switch (layout.Columns[i].Type)
                {
                    case "int[]":
                    case "float[]":
                        {
                            int arrayOffset = reader.ReadInt32();
                            int count = reader.ReadInt32();

                            if (layout.Columns[i].RelativeOffset)
                                reader.Position = start + arrayOffset + layout.Columns[i].RelativeShift;
                            else
                                reader.Position = col_start + arrayOffset;

                            if (layout.Columns[i].Type == "int[]")
                            {
                                int[] list_int = reader.ReadInt32s(count);
                                values.Add(new ColumnValue(list_int));
                            }
                            else if (layout.Columns[i].Type == "float[]")
                            {
                                float[] list_int = reader.ReadSingles(count);
                                values.Add(new ColumnValue(list_int));
                            }
                        }
                        break;
                    case "string":
                        {
                            uint value = reader.ReadUInt32();
                            var ofs = value + col_start;
                            if (layout.Columns[i].RelativeOffset)
                                ofs = value + start;

                            ofs += layout.Columns[i].RelativeShift;
                            values.Add(new ColumnValue(stringTable[ofs]));
                        }
                        break;
                    case "float":
                        {
                            float value = reader.ReadSingle();
                            values.Add(new ColumnValue(value));
                        }
                        break;
                    case "int":
                        {
                            int value = reader.ReadInt32();
                            values.Add(new ColumnValue(value));
                        }
                        break;
                    case "uint":
                        {
                            uint value = reader.ReadUInt32();
                            values.Add(new ColumnValue(value));
                        }
                        break;
                    case "byte":
                        {
                            byte value = reader.ReadByte();
                            reader.Align(4);

                            values.Add(new ColumnValue(value));
                        }
                        break;
                    default:
                        {
                            throw new Exception($"Unknown type {layout.Columns[i].Type}");
                        }
                }
            }
            return values;
        }

        private string GetString(FileReader reader, long pos_start)
        {
            var offset = reader.ReadUInt32() + pos_start;
            using (reader.TemporarySeek(offset, SeekOrigin.Begin))
            {
                return reader.ReadStringZeroTerminated();
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            using (var wr = new StringWriter(sb))
            {
                foreach (var row in Rows)
                {
                    var cols = row.Values.Select(x => x.Value).ToArray();
                    if (cols.Length != Rows[0].Values.Count)
                        throw new Exception();

                    wr.WriteLine(string.Join("|", cols));
                }
            }

            return sb.ToString();
        }

        public void Save(string path)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                Save(fs);
            }
        }

        public void Save(Stream stream)
        {
            Dictionary<string, long> stringTable = new Dictionary<string, long>();

            byte[] WriteStringTable()
            {
                //empty string at start
                stringTable.Add("", 0);
                foreach (var row in Rows)
                {
                    foreach (var col in row.Values)
                    {
                        if (!(col.Value is string))
                            continue;

                        if (!stringTable.ContainsKey((string)col.Value))
                            stringTable.Add((string)col.Value, 0);
                    }
                }
                foreach (var row in RowSets.SelectMany(x => x.Rows))
                {
                    foreach (var col in row.Values)
                    {
                        if (!(col.Value is string))
                            continue;

                        if (!stringTable.ContainsKey((string)col.Value))
                            stringTable.Add((string)col.Value, 0);
                    }
                }

                var mem = new MemoryStream();
                using (var writer = new FileWriter(mem))
                {
                    foreach (var str in stringTable)
                    {
                        stringTable[str.Key] = writer.Position;

                        writer.Write(Encoding.UTF8.GetBytes(str.Key));
                        writer.Write((byte)0);
                    }
                }
                return mem.ToArray();
            }

            var stringData = WriteStringTable();

            using (var writer = new FileWriter(stream))
            {
                NxdHeader.Magic = "NXDF";

                writer.WriteStruct(NxdHeader);

                switch (NxdHeader.CategoryType)
                {
                    case Category.RowsUnlocalized:
                    case Category.RowsLocalized:
                        writer.Write(48);
                        writer.Write(Rows.Count);

                        writer.SeekBegin(48);

                        var rowDataOffset = Rows.Count * 8 + writer.Position;

                        for (int i = 0; i < Rows.Count; i++)
                        {
                            long relative = writer.Position;

                            writer.Write(Rows[i].RowID);
                            writer.Write((int)(rowDataOffset - relative)); //data offset

                            rowDataOffset += Rows[i].Values.Count * 4;
                        }

                        var str_offset = Rows.Count * Rows[0].Values.Count * 4 + writer.Position;
                        for (int i = 0; i < Rows.Count; i++)
                            WriteRow(writer, Rows[i], Layout, str_offset, stringTable);

                        writer.Write(stringData);
                        break;
                    case Category.RowSetsUnlocalized:
                    case Category.RowSetsLocalized:

                        long headerPos = writer.Position;
                        const int header_size = 28;

                        //size of row set headers (12 bytes each)
                        var row_sets_size = RowSets.Count * 12;
                        //size of row headers (12 bytes each) + data
                        var row_data_size = RowSets.Sum(x =>
                          x.Rows.Count * 12 + x.Rows.Sum(x => x.Values.Count * 4));
                        //row info section at end
                        var row_info_size = RowSets.Sum(x => x.Rows.Count) * 12;

                        writer.Write(header_size);
                        writer.Write(RowSets.Count);
                        writer.Write(0); //reserved
                        writer.Write((uint)(headerPos + header_size + row_sets_size + row_data_size)); //full row list offset
                        writer.Write(RowSets.Sum(x => x.Rows.Count));
                        writer.Write(new byte[12]);

                        writer.SeekBegin(headerPos + header_size);

                        var rowOffset = row_sets_size + writer.Position; ;
                        for (int i = 0; i < RowSets.Count; i++)
                        {
                            long relative = writer.Position;

                            writer.Write(RowSets[i].ID);
                            writer.Write((uint)(rowOffset - relative)); //offset for later
                            writer.Write(RowSets[i].Rows.Count);

                            rowOffset += RowSets[i].Rows.Count * 12 + RowSets[i].Rows.Sum(x => x.Values.Count * 4);
                        }
                        var str_offset2 = writer.Position + row_data_size + row_info_size;

                        var rowDataStart = writer.Position;

                        for (int i = 0; i < RowSets.Count; i++)
                        {
                            //row headers, then data
                            var rowDataOffset2 = RowSets[i].Rows.Count * 12 + writer.Position;
                            foreach (var row in RowSets[i].Rows)
                            {
                                long relative = writer.Position;

                                writer.Write(RowSets[i].ID); //array index
                                writer.Write(row.RowID);
                                writer.Write((int)(rowDataOffset2 - relative)); //data offset
                                rowDataOffset2 += row.Values.Count * 4;
                            }
                            foreach (var row in RowSets[i].Rows)
                                WriteRow(writer, row, Layout, str_offset2, stringTable);
                        }

                        for (int i = 0; i < RowSets.Count; i++)
                        {
                            //header
                            rowDataStart += RowSets[i].Rows.Count * 12;

                            foreach (var row in RowSets[i].Rows)
                            {
                                long relative = writer.Position;

                                writer.Write(RowSets[i].ID); //array index
                                writer.Write(row.RowID);
                                writer.Write((int)(rowDataStart - relative));

                                rowDataStart += row.Values.Count * 4;
                            }
                        }
                        writer.Write(stringData);
                        break;
                }
            }
        }

        private void WriteRow(FileWriter writer, RowHeader row, LayoutInfo layoutInfo,
            long str_offset, Dictionary<string, long> stringTable)
        {
            long col_start = writer.Position;
            for (int i = 0; i < row.Values.Count; i++)
            {
                var col = row.Values[i];
                long relative_pos = writer.Position;

                if (col.Value is string)
                {
                    var shift = layoutInfo.Columns[i].RelativeShift;

                    if (layoutInfo.Columns[i].RelativeOffset)
                        writer.Write((uint)(str_offset + stringTable[(string)col.Value] - relative_pos - shift));
                    else
                        writer.Write((uint)(str_offset + stringTable[(string)col.Value] - col_start - shift));
                }
                else if (col.Value is float)
                    writer.Write((float)col.Value);
                else if (col.Value is int)
                    writer.Write((int)col.Value);
                else if (col.Value is uint)
                    writer.Write((uint)col.Value);
                else if (col.Value is byte)
                    writer.Write((byte)col.Value);
                else if (col.Value is int[])
                {
                    writer.Write(((int[])col.Value).Length);
                    writer.Write((int[])col.Value);
                }
                else
                    throw new Exception($"Unsupported type for {col.Value}!");
            }
        }

        public string ToXml()
        {
            using (var writer = new StringWriter())
            {
                var serializer = new XmlSerializer(typeof(NxdFile));
                serializer.Serialize(writer, this);
                writer.Flush();
                return writer.ToString();
            }
        }

        public void FromXML(string xml)
        {
            var xmlSerializer = new XmlSerializer(typeof(NxdFile));
            using (var stringReader = new StringReader(xml))
            {
                NxdFile ob = (NxdFile)xmlSerializer.Deserialize(stringReader);
                Rows = ob.Rows;
                NxdHeader = ob.NxdHeader;
            }
        }
    }
}

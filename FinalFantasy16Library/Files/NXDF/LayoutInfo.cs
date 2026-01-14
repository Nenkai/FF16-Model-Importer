namespace FinalFantasy16Library.Files.NXDF;

public class LayoutInfo
{
    public List<Column> Columns = [];

    public LayoutInfo() { }

    public LayoutInfo(string path)
    {
        using (var reader = new StreamReader(path))
        {
            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine();
                if (line.StartsWith("add_column"))
                {
                    Column col = new Column();
                    var values = line.Split("|");
                    col.Label = values[1];
                    col.Type = values[2];
                    if (values.Length >= 4 && values[3] == "rel")
                        col.RelativeOffset = true;
                    if (values.Length >= 5)
                        col.RelativeShift = int.Parse(values[4]);

                    Columns.Add(col);
                }
            }
        }
    }

    public class Column
    {
        public string Label;
        public string Type;
        public bool RelativeOffset = false;
        public int RelativeShift = 0;
    }
}

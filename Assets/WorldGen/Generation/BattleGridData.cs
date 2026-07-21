using System.Collections.Generic;
using System.Text;

namespace WorldGen.Generation
{
    /// <summary>One 5-ft square of a room's battle map. Byte-backed and order-stable: the wire format
    /// (BattleGridCodec) maps these to single letters, so REORDERING or REMOVING a member silently
    /// rewrites saved maps. Append only.</summary>
    public enum GridCell : byte
    {
        Empty  = 0,   // outside the room — a hole in the grid rectangle
        Floor  = 1,
        Wall   = 2,
        Door   = 3,   // a HAND-PAINTED door. Doors derived from links are never written here (see BattleGridGenerator.ProjectDoors)
        Rough  = 4,   // difficult terrain
        Liquid = 5,
        Chasm  = 6,
    }

    /// <summary>A room's battle map AS STORED. Absent (null on Room.Grid) until the DM edits one, so a
    /// project that never uses battle maps does not grow. Cells is the run-length string; the working
    /// representation is GridBuffer.</summary>
    public class BattleGrid
    {
        public int Width;
        public int Height;
        public string Cells;
    }

    /// <summary>The working (decoded) grid. Row y=0 is the BOTTOM row — matching Texture2D's row order,
    /// so the renderer needs no vertical flip. Note this is the opposite of tile space, where Y grows
    /// DOWN the screen (DungeonProjection.TileToLocal negates ty); every tile↔grid conversion flips Y.</summary>
    public class GridBuffer
    {
        public readonly int Width;
        public readonly int Height;
        public readonly GridCell[] Cells;

        public GridBuffer(int width, int height)
        {
            Width = width; Height = height;
            Cells = new GridCell[width * height];
        }

        public int Index(int x, int y) => y * Width + x;
        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
        public GridCell Get(int x, int y) => Cells[Index(x, y)];
        public void Set(int x, int y, GridCell v) => Cells[Index(x, y)] = v;

        public GridBuffer Clone()
        {
            var c = new GridBuffer(Width, Height);
            System.Array.Copy(Cells, c.Cells, Cells.Length);
            return c;
        }

        /// <summary>Decode a stored grid. Returns null if the model is missing or its
        /// string does not decode EXACTLY — a corrupt map is regenerated whole by the caller, never
        /// loaded half-way.</summary>
        public static GridBuffer FromModel(BattleGrid g)
        {
            if (g == null) return null;
            if (g.Width <= 0 || g.Height <= 0) return null;
            // Dimensions come from a file, and TryDecode sizes an allocation from their product.
            // Reject oversized grids before TryDecode creates a multi-gigabyte List.
            if (g.Width < BattleGridCodec.MinSide || g.Width > BattleGridCodec.MaxSide) return null;
            if (g.Height < BattleGridCodec.MinSide || g.Height > BattleGridCodec.MaxSide) return null;
            if (!BattleGridCodec.TryDecode(g.Cells, g.Width, g.Height, out var cells)) return null;
            var buf = new GridBuffer(g.Width, g.Height);
            System.Array.Copy(cells, buf.Cells, cells.Length);
            return buf;
        }

        public BattleGrid ToModel() =>
            new BattleGrid { Width = Width, Height = Height, Cells = BattleGridCodec.Encode(Cells) };
    }

    /// <summary>Run-length text codec for a grid's cells: a sequence of &lt;count&gt;&lt;letter&gt; runs,
    /// row-major, bottom row first. "400F" is a 20x20 floor. Text so a save stays readable and diffable;
    /// STRICT so a malformed string can never load as a partial map.</summary>
    public static class BattleGridCodec
    {
        public const int MinSide = 4;
        public const int MaxSide = 40;

        public static int Clamp(int side) => side < MinSide ? MinSide : (side > MaxSide ? MaxSide : side);

        public static char ToChar(GridCell c)
        {
            switch (c)
            {
                case GridCell.Empty:  return 'E';
                case GridCell.Floor:  return 'F';
                case GridCell.Wall:   return 'W';
                case GridCell.Door:   return 'D';
                case GridCell.Rough:  return 'R';
                case GridCell.Liquid: return 'L';
                default:              return 'C';   // Chasm
            }
        }

        public static bool TryFromChar(char ch, out GridCell c)
        {
            switch (ch)
            {
                case 'E': c = GridCell.Empty;  return true;
                case 'F': c = GridCell.Floor;  return true;
                case 'W': c = GridCell.Wall;   return true;
                case 'D': c = GridCell.Door;   return true;
                case 'R': c = GridCell.Rough;  return true;
                case 'L': c = GridCell.Liquid; return true;
                case 'C': c = GridCell.Chasm;  return true;
                default:  c = GridCell.Empty;  return false;
            }
        }

        public static string Encode(GridCell[] cells)
        {
            var sb = new StringBuilder();
            if (cells == null || cells.Length == 0) return "";
            int runStart = 0;
            for (int i = 1; i <= cells.Length; i++)
                if (i == cells.Length || cells[i] != cells[runStart])
                {
                    sb.Append(i - runStart).Append(ToChar(cells[runStart]));
                    runStart = i;
                }
            return sb.ToString();
        }

        /// <summary>Parse `s` into exactly width*height cells. Any deviation — a bad letter, a run with no
        /// count, a total that over- or under-shoots — fails the WHOLE parse.</summary>
        public static bool TryDecode(string s, int width, int height, out GridCell[] cells)
        {
            cells = null;
            if (string.IsNullOrEmpty(s) || width <= 0 || height <= 0) return false;
            int total = width * height;
            var acc = new List<GridCell>(total);
            int i = 0;
            while (i < s.Length)
            {
                int start = i;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                if (i == start) return false;                 // run with no count
                if (i >= s.Length) return false;              // count with no letter
                if (!int.TryParse(s.Substring(start, i - start), out int count) || count <= 0) return false;
                if (!TryFromChar(s[i], out var cell)) return false;
                i++;
                if (acc.Count + count > total) return false;  // overshoot — reject before allocating
                for (int k = 0; k < count; k++) acc.Add(cell);
            }
            if (acc.Count != total) return false;             // undershoot
            cells = acc.ToArray();
            return true;
        }
    }
}

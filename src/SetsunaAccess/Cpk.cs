using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SetsunaAccess
{
    /// <summary>
    /// Minimal read-only CRI CPK archive reader (unencrypted @UTF tables, CRILAYLA compression),
    /// enough to read single files out of the game's parameter.cpk. Pure logic, unit tested.
    /// </summary>
    internal sealed class Cpk
    {
        private struct Entry { public long Offset; public int Size; }

        private readonly string _path;
        private readonly Dictionary<string, Entry> _files = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public Cpk(string path)
        {
            _path = path;
            using (var fs = File.OpenRead(path))
            {
                var header = ReadUtf(ReadAt(fs, 0x10, 0x800), 0)[0];
                var toc = Convert.ToInt64(header["TocOffset"]);
                var content = Convert.ToInt64(header["ContentOffset"]);
                var tocSize = Convert.ToInt32(header["TocSize"]);
                var add = content != 0 ? Math.Min(toc, content) : toc;
                foreach (var row in ReadUtf(ReadAt(fs, toc + 0x10, tocSize), 0))
                {
                    var dir = row["DirName"] as string;
                    var name = row["FileName"] as string;
                    var full = string.IsNullOrEmpty(dir) ? name : dir + "/" + name;
                    _files[full] = new Entry { Offset = Convert.ToInt64(row["FileOffset"]) + add, Size = Convert.ToInt32(row["FileSize"]) };
                }
            }
        }

        public int Count { get { return _files.Count; } }

        public bool Contains(string name) { return _files.ContainsKey(name); }

        /// <summary>The file's bytes, decompressed; null if the archive has no such file.</summary>
        public byte[] Read(string name)
        {
            Entry e;
            if (!_files.TryGetValue(name, out e)) return null;
            byte[] data;
            using (var fs = File.OpenRead(_path)) data = ReadAt(fs, e.Offset, e.Size);
            return IsLayla(data) ? Crilayla(data) : data;
        }

        private static byte[] ReadAt(Stream s, long offset, int count)
        {
            s.Position = offset;
            var buf = new byte[count];
            var got = 0;
            while (got < count)
            {
                var n = s.Read(buf, got, count - got);
                if (n <= 0) break;
                got += n;
            }
            return buf;
        }

        // ---- @UTF tables (big-endian) ---------------------------------------------------

        private static uint U32(byte[] b, int p) { return (uint)(b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3]); }
        private static ushort U16(byte[] b, int p) { return (ushort)(b[p] << 8 | b[p + 1]); }

        internal static List<Dictionary<string, object>> ReadUtf(byte[] b, int pos)
        {
            if (b[pos] != '@' || b[pos + 1] != 'U' || b[pos + 2] != 'T' || b[pos + 3] != 'F')
                throw new System.Exception("not a @UTF table");
            var baseOff = pos + 8;
            var rowsOff = (int)U32(b, pos + 8) + baseOff;
            var strOff = (int)U32(b, pos + 12) + baseOff;
            var dataOff = (int)U32(b, pos + 16) + baseOff;
            var ncols = U16(b, pos + 24);
            var rowLen = U16(b, pos + 26);
            var nrows = (int)U32(b, pos + 28);

            Func<int, string> str = o =>
            {
                var start = strOff + o;
                var end = start;
                while (end < b.Length && b[end] != 0) end++;
                return Encoding.UTF8.GetString(b, start, end - start);
            };

            var names = new string[ncols];
            var storages = new int[ncols];
            var types = new int[ncols];
            var consts = new object[ncols];
            var p = pos + 0x20;
            for (var c = 0; c < ncols; c++)
            {
                var flags = b[p];
                names[c] = str((int)U32(b, p + 1));
                p += 5;
                storages[c] = flags & 0xF0;
                types[c] = flags & 0x0F;
                if (storages[c] == 0x30) consts[c] = Value(b, ref p, types[c], str, dataOff);
            }

            var rows = new List<Dictionary<string, object>>(nrows);
            for (var r = 0; r < nrows; r++)
            {
                p = rowsOff + r * rowLen;
                var row = new Dictionary<string, object>();
                for (var c = 0; c < ncols; c++)
                {
                    if (storages[c] == 0x50) row[names[c]] = Value(b, ref p, types[c], str, dataOff);
                    else row[names[c]] = storages[c] == 0x30 ? consts[c] : null;
                }
                rows.Add(row);
            }
            return rows;
        }

        private static object Value(byte[] b, ref int p, int type, Func<int, string> str, int dataOff)
        {
            object v;
            switch (type)
            {
                case 0: v = b[p]; p += 1; break;
                case 1: v = (sbyte)b[p]; p += 1; break;
                case 2: v = U16(b, p); p += 2; break;
                case 3: v = (short)U16(b, p); p += 2; break;
                case 4: v = U32(b, p); p += 4; break;
                case 5: v = (int)U32(b, p); p += 4; break;
                case 6: v = (ulong)U32(b, p) << 32 | U32(b, p + 4); p += 8; break;
                case 7: v = (long)((ulong)U32(b, p) << 32 | U32(b, p + 4)); p += 8; break;
                case 8:
                {
                    var f = new[] { b[p + 3], b[p + 2], b[p + 1], b[p] };
                    v = BitConverter.ToSingle(f, 0); p += 4; break;
                }
                case 0xA: v = str((int)U32(b, p)); p += 4; break;
                case 0xB: v = null; p += 8; break; // data blobs aren't needed here
                default: throw new System.Exception("@UTF type " + type);
            }
            return v;
        }

        // ---- CRILAYLA -------------------------------------------------------------------

        private static bool IsLayla(byte[] d)
        {
            return d.Length > 16 && Encoding.ASCII.GetString(d, 0, 8) == "CRILAYLA";
        }

        internal static byte[] Crilayla(byte[] data)
        {
            var usize = BitConverter.ToInt32(data, 8);
            var hsize = BitConverter.ToInt32(data, 12);
            var output = new byte[usize + 0x100];
            Buffer.BlockCopy(data, 16 + hsize, output, 0, 0x100);
            var o = output.Length - 1;
            var pos = 16 + hsize - 1; // compressed stream is read backwards
            int bits = 0, nbits = 0;

            Func<int, int> get = n =>
            {
                var v = 0;
                while (n > 0)
                {
                    if (nbits == 0) { bits = data[pos--]; nbits = 8; }
                    var take = Math.Min(n, nbits);
                    v = (v << take) | ((bits >> (nbits - take)) & ((1 << take) - 1));
                    nbits -= take;
                    n -= take;
                }
                return v;
            };

            var levels = new[] { 2, 3, 5, 8 };
            while (o >= 0x100)
            {
                if (get(1) != 0)
                {
                    var back = get(13) + 3;
                    var length = 3;
                    var more = true;
                    foreach (var lvl in levels)
                    {
                        var n = get(lvl);
                        length += n;
                        if (n != (1 << lvl) - 1) { more = false; break; }
                    }
                    if (more)
                    {
                        int n;
                        do { n = get(8); length += n; } while (n == 0xFF);
                    }
                    for (var i = 0; i < length; i++) { output[o] = output[o + back]; o--; }
                }
                else
                {
                    output[o--] = (byte)get(8);
                }
            }
            return output;
        }
    }
}

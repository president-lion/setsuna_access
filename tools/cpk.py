"""Minimal CRI CPK reader: lists and extracts files (with CRILAYLA decompression).

usage: python -I cpk.py <archive.cpk> <out_dir> [substring filter]
"""
import os
import struct
import sys


def read_utf(buf, pos):
    """Parse an @UTF table at buf[pos:]; returns a list of row dicts."""
    assert buf[pos:pos + 4] == b'@UTF', 'not a @UTF table at %x' % pos
    base = pos + 8
    rows_off, str_off, data_off, _name, ncols, rowlen, nrows = struct.unpack_from('>IIIIHHI', buf, pos + 8)
    rows_off += base; str_off += base; data_off += base

    def string(o):
        s = buf[str_off + o:]
        return s[:s.index(b'\0')].decode('utf-8', 'replace')

    fmt = {0: ('>B', 1), 1: ('>b', 1), 2: ('>H', 2), 3: ('>h', 2), 4: ('>I', 4), 5: ('>i', 4),
           6: ('>Q', 8), 7: ('>q', 8), 8: ('>f', 4)}

    def value(t, p):
        if t in fmt:
            f, n = fmt[t]
            return struct.unpack_from(f, buf, p)[0], n
        if t == 0xA:
            return string(struct.unpack_from('>I', buf, p)[0]), 4
        if t == 0xB:
            o, n = struct.unpack_from('>II', buf, p)
            return buf[data_off + o:data_off + o + n], 8
        raise ValueError('type %x' % t)

    cols = []
    p = pos + 0x20
    for _ in range(ncols):
        flags = buf[p]; name = string(struct.unpack_from('>I', buf, p + 1)[0]); p += 5
        storage, t = flags & 0xF0, flags & 0x0F
        const = None
        if storage == 0x30:
            const, n = value(t, p); p += n
        cols.append((name, storage, t, const))

    out = []
    for r in range(nrows):
        p = rows_off + r * rowlen
        row = {}
        for name, storage, t, const in cols:
            if storage == 0x50:
                row[name], n = value(t, p); p += n
            elif storage == 0x30:
                row[name] = const
            else:
                row[name] = None
        out.append(row)
    return out


def crilayla(data):
    """Decompress a CRILAYLA block (bits read backwards from the end)."""
    usize, hsize = struct.unpack_from('<II', data, 8)
    header = data[16 + hsize:16 + hsize + 0x100]
    comp = data[16:16 + hsize]
    out = bytearray(usize + 0x100)
    out[:0x100] = header
    o = len(out) - 1
    pos = len(comp) - 1
    bits = 0
    nbits = 0

    def get(n):
        nonlocal pos, bits, nbits
        v = 0
        while n:
            if nbits == 0:
                bits = comp[pos]; pos -= 1; nbits = 8
            take = min(n, nbits)
            v = (v << take) | ((bits >> (nbits - take)) & ((1 << take) - 1))
            nbits -= take; n -= take
        return v

    end = 0x100
    while o >= end:
        if get(1):
            back = get(13) + 3
            length = 3
            for lvl in (2, 3, 5, 8):
                n = get(lvl)
                length += n
                if n != (1 << lvl) - 1:
                    break
            else:
                while True:
                    n = get(8)
                    length += n
                    if n != 0xFF:
                        break
            for _ in range(length):
                out[o] = out[o + back]; o -= 1
        else:
            out[o] = get(8); o -= 1
    return bytes(out)


def main():
    path, outdir = sys.argv[1], sys.argv[2]
    filt = sys.argv[3] if len(sys.argv) > 3 else ''
    buf = open(path, 'rb').read()
    hdr = read_utf(buf, 0x10)[0]
    toc = hdr['TocOffset']; content = hdr['ContentOffset']
    add = min(toc, content) if content else toc
    files = read_utf(buf, toc + 0x10)
    n = 0
    for f in files:
        name = '/'.join(x for x in (f.get('DirName'), f.get('FileName')) if x)
        if filt and filt not in name:
            continue
        off = f['FileOffset'] + add
        data = buf[off:off + f['FileSize']]
        if data[:8] == b'CRILAYLA':
            data = crilayla(data)
        dst = os.path.join(outdir, name)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        open(dst, 'wb').write(data)
        n += 1
    print('extracted', n, 'of', len(files))


if __name__ == '__main__':
    main()

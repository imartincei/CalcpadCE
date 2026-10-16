/**
 * Minimal STORED-only ZIP writer, which keeps the code short and works fine here (PNGs are
 * already compressed, plot SVGs are tiny) without pulling in JSZip. Layout: [local header +
 * data]* + [central dir header]* + end-of-central-dir.
 */

export interface ZipEntry {
    name: string;
    bytes: Uint8Array;
}

export function buildZip(entries: ZipEntry[]): Uint8Array {
    const parts: Uint8Array[] = [];
    const central: Uint8Array[] = [];
    let offset = 0;

    for (const entry of entries) {
        const nameBytes = new TextEncoder().encode(entry.name);
        const crc = crc32(entry.bytes);
        const size = entry.bytes.length;

        const localHeader = new Uint8Array(30 + nameBytes.length);
        const lv = new DataView(localHeader.buffer);
        lv.setUint32(0, 0x04034b50, true);   // local file header signature
        lv.setUint16(4, 20, true);           // version needed
        lv.setUint16(6, 0, true);            // flags
        lv.setUint16(8, 0, true);            // method: STORED
        lv.setUint16(10, 0, true);           // mod time
        lv.setUint16(12, 0x21, true);        // mod date (1980-01-01)
        lv.setUint32(14, crc, true);
        lv.setUint32(18, size, true);        // compressed size
        lv.setUint32(22, size, true);        // uncompressed size
        lv.setUint16(26, nameBytes.length, true);
        lv.setUint16(28, 0, true);           // extra length
        localHeader.set(nameBytes, 30);

        parts.push(localHeader, entry.bytes);

        const centralHeader = new Uint8Array(46 + nameBytes.length);
        const cv = new DataView(centralHeader.buffer);
        cv.setUint32(0, 0x02014b50, true);   // central dir signature
        cv.setUint16(4, 20, true);           // version made by
        cv.setUint16(6, 20, true);           // version needed
        cv.setUint16(8, 0, true);            // flags
        cv.setUint16(10, 0, true);           // method
        cv.setUint16(12, 0, true);           // mod time
        cv.setUint16(14, 0x21, true);        // mod date
        cv.setUint32(16, crc, true);
        cv.setUint32(20, size, true);
        cv.setUint32(24, size, true);
        cv.setUint16(28, nameBytes.length, true);
        cv.setUint16(30, 0, true);           // extra length
        cv.setUint16(32, 0, true);           // comment length
        cv.setUint16(34, 0, true);           // disk number
        cv.setUint16(36, 0, true);           // internal attrs
        cv.setUint32(38, 0, true);           // external attrs
        cv.setUint32(42, offset, true);      // local header offset
        centralHeader.set(nameBytes, 46);
        central.push(centralHeader);

        offset += localHeader.length + entry.bytes.length;
    }

    const centralOffset = offset;
    let centralSize = 0;
    for (const c of central) centralSize += c.length;

    const eocd = new Uint8Array(22);
    const ev = new DataView(eocd.buffer);
    ev.setUint32(0, 0x06054b50, true);       // end of central dir signature
    ev.setUint16(4, 0, true);                // disk number
    ev.setUint16(6, 0, true);                // disk with central dir
    ev.setUint16(8, entries.length, true);   // entries on this disk
    ev.setUint16(10, entries.length, true);  // total entries
    ev.setUint32(12, centralSize, true);
    ev.setUint32(16, centralOffset, true);
    ev.setUint16(20, 0, true);               // comment length

    let total = 0;
    for (const p of parts) total += p.length;
    for (const c of central) total += c.length;
    total += eocd.length;

    const out = new Uint8Array(total);
    let pos = 0;
    for (const p of parts) { out.set(p, pos); pos += p.length; }
    for (const c of central) { out.set(c, pos); pos += c.length; }
    out.set(eocd, pos);
    return out;
}

const CRC_TABLE = (() => {
    const t = new Uint32Array(256);
    for (let n = 0; n < 256; n++) {
        let c = n;
        for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        t[n] = c >>> 0;
    }
    return t;
})();

function crc32(bytes: Uint8Array): number {
    let c = 0xffffffff;
    for (let i = 0; i < bytes.length; i++) c = CRC_TABLE[(c ^ bytes[i]) & 0xff] ^ (c >>> 8);
    return (c ^ 0xffffffff) >>> 0;
}

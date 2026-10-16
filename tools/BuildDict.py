"""Compile ECDICT's ecdict.csv into the compact QDIC binary used by QuickTranslate.

    python BuildDict.py <ecdict.csv> [core|wide|full] [out.bin]

Default output is <repo>/dict.bin next to the .csproj.

QDIC layout (little endian, all offsets relative to their section base):
    header  20 B : b'QDIC' + u16 version + u16 flags + u32 count + u32 keyBase + u32 dataBase
    index   12 B per entry, sorted by key bytes: u32 keyOff, u16 keyLen, u32 dataOff, u16 dataLen
    keys        : UTF-8, lower-case, ASCII, ascending byte order
    data        : UTF-8 payload, fields joined by 0x1f
                  phonetic | pos | translation | tag | exchange | collins | oxford | bnc | frq

Payloads are stored once even when several keys (inflected forms) share them.
"""
import csv
import os
import struct
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ.get('TEMP', '.'), 'ecdict.csv')
TIER = (sys.argv[2] if len(sys.argv) > 2 else 'core').lower()
OUT = sys.argv[3] if len(sys.argv) > 3 else os.path.normpath(os.path.join(HERE, '..', 'dict.bin'))

# bnc / frq are corpus ranks: 1 is the most frequent word, 0 means "unranked".
RANK = {'core': 30000, 'wide': 60000, 'full': 10 ** 9}[TIER]
COLLINS = {'core': 2, 'wide': 1, 'full': 0}[TIER]


def num(text):
    try:
        return int((text or '').strip())
    except ValueError:
        return 0


def keep(row):
    if row[7].strip():                      # exam tag: zk gk cet4 cet6 ky toefl ielts gre
        return True
    if row[6].strip() == '1':               # Oxford 3000
        return True
    if num(row[5]) >= COLLINS:              # Collins star rating
        return True
    return any(0 < num(row[i]) <= RANK for i in (8, 9))


def payload_of(row):
    return '\x1f'.join([
        row[1].strip(), row[4].strip(), row[3].strip().replace('\\n', '\n'),
        row[7].strip(), row[10].strip(), row[5].strip(),
        row[6].strip(), row[8].strip(), row[9].strip()])


def read_rows():
    with open(SRC, 'r', encoding='utf-8', errors='replace', newline='') as handle:
        reader = csv.reader(handle)
        next(reader)
        for row in reader:
            if len(row) < 13:
                row = row + [''] * (13 - len(row))
            yield row


def build(entries, path):
    keys = bytearray()
    data = bytearray()
    index = bytearray()
    seen = {}
    for key, value in sorted(entries.items()):
        raw = key.encode('utf-8')
        if value in seen:
            offset = seen[value]
        else:
            offset = len(data)
            seen[value] = offset
            data += value.encode('utf-8')
        index += struct.pack('<IHIH', len(keys), len(raw), offset, len(value.encode('utf-8')))
        keys += raw
    key_base = 20 + len(index)
    blob = (b'QDIC' + struct.pack('<HHIII', 1, 0, len(entries), key_base, key_base + len(keys))
            + bytes(index) + bytes(keys) + bytes(data))
    with open(path, 'wb') as handle:
        handle.write(blob)
    return len(blob), len(index), len(keys), len(data)


def lookup(path, word):
    with open(path, 'rb') as handle:
        _, _, _, count, key_base, data_base = struct.unpack('<4sHHIII', handle.read(20))
        low, high, want = 0, count - 1, word.lower().encode('utf-8')
        while low <= high:
            mid = (low + high) // 2
            handle.seek(20 + mid * 12)
            key_off, key_len, data_off, data_len = struct.unpack('<IHIH', handle.read(12))
            handle.seek(key_base + key_off)
            key = handle.read(key_len)
            if key == want:
                handle.seek(data_base + data_off)
                return handle.read(data_len).decode('utf-8')
            if key < want:
                low = mid + 1
            else:
                high = mid - 1
        return None


def main():
    started = time.time()
    rows = 0
    entries = {}
    for row in read_rows():
        rows += 1
        word = row[0].strip()
        if not word or not row[3].strip():
            continue
        key = word.lower()
        if not all(('a' <= c <= 'z') or c in "'- " for c in key):
            continue
        if not keep(row):
            continue
        value = payload_of(row)
        if len(value) > len(entries.get(key, '')):
            entries[key] = value
    base = len(entries)

    # Inflected forms (went, ran, children...) inherit their lemma's payload via the
    # ECDICT "exchange" field, which carries "0:<lemma>" for non-lemma entries.
    if TIER != 'full':
        for row in read_rows():
            key = row[0].strip().lower()
            if not key or key in entries:
                continue
            lemma = next((p[2:].strip().lower() for p in row[10].split('/') if p.startswith('0:')), None)
            if lemma and lemma in entries:
                entries[key] = entries[lemma]

    total, index, keys, data = build(entries, OUT)
    print('tier %s: %d rows -> %d entries (%d lemmas + %d forms)'
          % (TIER, rows, len(entries), base, len(entries) - base))
    print('%s = %.2f MB (index %.2f + keys %.2f + data %.2f) in %.1f s'
          % (OUT, total / 1048576.0, index / 1048576.0, keys / 1048576.0, data / 1048576.0,
             time.time() - started))
    for probe in ('hello', 'running', 'went', 'children', 'ephemeral'):
        found = lookup(OUT, probe)
        print('  %-10s %s' % (probe, (found or '(miss)').split('\x1f')[2].replace('\n', ' | ')[:56]))


if __name__ == '__main__':
    main()

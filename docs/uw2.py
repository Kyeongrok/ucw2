"""대항해시대2(DOS/Win95) 자산 읽개.

LS10/LS11 컨테이너, 세계지도, 항구지도를 푼다.
KOUKAI2.EXE(Win95판) 0x0041C070·0x0041C240·0x0041C290·0x0041C300 을 그대로 옮긴 것.
"""
import struct, os

# ---------------------------------------------------------------- LS10/LS11

class _Bits:
    """MSB 먼저 읽는 비트 읽개(EXE 0x0041C1B0)."""
    __slots__ = ('d', 'p', 'cur', 'n')
    def __init__(self, d): self.d = d; self.p = 0; self.cur = 0; self.n = 0
    def get(self, k):
        v = 0
        for _ in range(k):
            if self.n == 0:
                if self.p >= len(self.d): return -1
                self.cur = self.d[self.p]; self.p += 1; self.n = 8
            self.n -= 1
            v = (v << 1) | ((self.cur >> 7) & 1)
            self.cur = (self.cur << 1) & 0xFF
        return v
    def sym(self):
        """엘리아스 감마꼴 부호(EXE 0x0041C240). 0 이 나올 때까지 k 를 센 뒤 k 비트를 더 읽는다."""
        k = 0
        while True:
            b = self.get(1)
            if b < 0: return -1
            k += 1
            if b == 0: break
        v = self.get(k)
        return -1 if v < 0 else (1 << k) - 2 + v

def unpack(packed, want, table):
    """한 청크를 푼다(EXE 0x0041C290). 심볼 <256 은 글자, 그보다 크면 (거리, 길이) 짝이다."""
    br = _Bits(packed); out = bytearray()
    while len(out) < want:
        s = br.sym()
        if s < 0: raise ValueError('스트림이 모자랍니다')
        if s < 256:
            out.append(table[s])
        else:
            n = br.sym()
            if n < 0: raise ValueError('길이가 모자랍니다')
            n += 3
            st = len(out) - (s - 256)
            if st < 0: raise ValueError('거리가 지도 밖입니다')
            for i in range(n): out.append(out[st + i])
    return bytes(out)

def read_archive(path):
    """LS10/LS11 파일을 열어 청크 목록을 낸다."""
    d = open(path, 'rb').read()
    if d[:2] != b'LS': raise ValueError('LS 파일이 아닙니다: %r' % d[:4])
    table = d[16:272]
    first = struct.unpack_from('>I', d, 280)[0]      # 셋째 낱말 = 첫 청크 자리
    count = (first - 0x110) // 12                    # EXE 0x0041C109 과 같은 셈
    out = []
    for i in range(count):
        pk, un, off = struct.unpack_from('>III', d, 272 + 12 * i)
        raw = d[off:off + pk]
        out.append(raw if pk == un else unpack(raw, un, table))
    return out

# ---------------------------------------------------------------- 세계지도

MAP_W, MAP_H = 1080, 540      # 칸
TILE = 12                     # 조각 한 변(칸)
TILES_X, TILES_Y = 90, 45     # 조각 격자
BAND = 30                     # 청크 하나가 맡는 조각 열 수
ROWS_PER_CHUNK = 1350         # 조각 수 = BAND * TILES_Y

SEA, LAND = 0, 15             # 칸 값

def read_worldmap(path='WORLDMAP.LZW'):
    """세계지도를 1080x540 칸 배열로 낸다."""
    chunks = read_archive(path)
    grid = [[SEA] * MAP_W for _ in range(MAP_H)]
    for ci, c in enumerate(chunks):
        offs = [struct.unpack_from('<H', c, 2 * k)[0] for k in range(ROWS_PER_CHUNK)]
        body = c[2 * ROWS_PER_CHUNK:]
        for i in range(ROWS_PER_CHUNK):
            end = offs[i + 1] if i + 1 < ROWS_PER_CHUNK else len(body)
            r = body[offs[i]:end]
            cells = _tile(r)
            ty, tx = i // BAND, ci * BAND + i % BAND
            for y in range(TILE):
                row = grid[ty * TILE + y]
                for x in range(TILE):
                    row[tx * TILE + x] = cells[y * TILE + x]
    return grid

# 본보기 조각 여섯 벌(EXE VA 0x004582D8). 머리 0x78~0x7D 가 이것을 고른다.
# 자국이 꺼진 칸은 이 본보기에서 가져온다 — "앞 값을 잇는" 것이 아니다.
def _templates():
    t = []
    for k in range(6):
        c = [0] * (TILE * TILE)
        for y in range(TILE):
            for x in range(TILE):
                if   k == 0: v = LAND if x < 6 else SEA      # 0x78 왼쪽 뭍
                elif k == 1: v = SEA if x < 6 else LAND      # 0x79 오른쪽 뭍
                elif k == 2: v = LAND if y < 6 else SEA      # 0x7A 위쪽 뭍
                elif k == 3: v = SEA if y < 6 else LAND      # 0x7B 아래쪽 뭍
                elif k == 4: v = LAND                        # 0x7C 온통 뭍
                else:        v = SEA                         # 0x7D 온통 바다
                c[y * TILE + x] = v
        t.append(c)
    return t

TEMPLATES = _templates()

def _tile(r):
    """조각 하나(EXE 0x0041DD00). 1바이트면 본보기 그대로, 아니면 자국이 켜진 칸만 새 값이다."""
    h = r[0]
    base = TEMPLATES[(h & 0x7F) - 0x78]
    if h & 0x80:
        return list(base)
    bm, vals = r[1:19], r[19:]
    vi = 0
    cells = [0] * (TILE * TILE)
    for b in range(TILE * TILE):
        if (bm[b >> 3] >> (7 - (b & 7))) & 1:
            cells[b] = vals[vi]; vi += 1
        else:
            cells[b] = base[b]
    return cells

# ---------------------------------------------------------------- 그림

def tiles_4bpp(data, tw=16, th=16):
    """면 넷을 겹쳐 그린 조각들을 [조각][칸] 색인 배열로 편다."""
    per_plane = tw * th // 8
    stride = tw // 8
    out = []
    for t in range(len(data) // (per_plane * 4)):
        px = []
        for y in range(th):
            for x in range(tw):
                v = 0
                for p in range(4):
                    b = data[t * per_plane * 4 + p * per_plane + y * stride + (x >> 3)]
                    v |= ((b >> (7 - (x & 7))) & 1) << p
                px.append(v)
        out.append(px)
    return out


# ---------------------------------------------------------------- 칸 -> 칩

CHIP_LAND = 0x41      # 뭍 칩. 값 <16 의 켜진 자리에 이것이 들어간다
CHIP_MIN = 0x34       # 이보다 작은 칩 번호는 바다(0)로 눌린다

def quad_table(data1_chunk18):
    """그림표 256칸 x 4바이트. DATA1.LZW 18번 청크다."""
    return [list(data1_chunk18[v*4:v*4+4]) for v in range(256)]

def cell_quad(v, table):
    """칸 값 하나를 2x2 칩으로. 차례는 좌상·우상·좌하·우하다(0x0041DEF2)."""
    if v < 16:
        return [CHIP_LAND if v & 8 else 0, CHIP_LAND if v & 4 else 0,
                CHIP_LAND if v & 2 else 0, CHIP_LAND if v & 1 else 0]
    q = table[v]
    return [c if c >= CHIP_MIN else 0 for c in q]

def chip_map(grid, table):
    """1080x540 칸을 2160x1080 칩으로 편다."""
    H = len(grid); W = len(grid[0])
    out = [[0]*(W*2) for _ in range(H*2)]
    for y in range(H):
        row = grid[y]; a = out[y*2]; b = out[y*2+1]
        for x in range(W):
            lt, rt, lb, rb = cell_quad(row[x], table)
            a[x*2] = lt; a[x*2+1] = rt
            b[x*2] = lb; b[x*2+1] = rb
    return out

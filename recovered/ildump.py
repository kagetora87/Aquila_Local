import struct, sys, os, re

EXE, OUTDIR = sys.argv[1], sys.argv[2]
PDB = sys.argv[3] if len(sys.argv) > 3 else None
TARGETS = sys.argv[4].split('|') if len(sys.argv) > 4 else []

data = open(EXE, 'rb').read()

# ---------- single-file bundle ----------
SIG = bytes([0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
             0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae])
p = data.find(SIG)
hdr = struct.unpack_from('<q', data, p - 8)[0]
def rdstr(buf, o):
    n = 0; shift = 0
    while True:
        b = buf[o]; o += 1
        n |= (b & 0x7f) << shift; shift += 7
        if not b & 0x80: break
    return buf[o:o+n].decode('utf-8'), o + n
major, minor, count = struct.unpack_from('<IIi', data, hdr)
o = hdr + 12
bid, o = rdstr(data, o)
if major >= 2: o += 8*4 + 8
entries = []
for _ in range(count):
    off, size = struct.unpack_from('<qq', data, o); o += 16
    csize = 0
    if major >= 6:
        csize = struct.unpack_from('<q', data, o)[0]; o += 8
    typ = data[o]; o += 1
    path, o = rdstr(data, o)
    entries.append((path, off, size, csize, typ))
print('bundle v%d.%d, %d files' % (major, minor, count))
main = None
for e in entries:
    if e[0].lower().endswith('.dll') and ('aquila' in e[0].lower()):
        print('  candidate:', e)
        if e[0].lower() == 'aquila local.dll': main = e
if not main:
    for e in entries:
        if e[0].lower().endswith('.dll') and e[4] == 1: print('  asm:', e[0])
    sys.exit('main assembly not found')
path, off, size, csize, typ = main
blob = data[off:off + (csize or size)]
if csize:
    import zlib
    blob = zlib.decompress(blob, -15)
open(os.path.join(OUTDIR, 'AquilaLocal.dll'), 'wb').write(blob)
dll = blob

# ---------- metadata ----------
def parse_md(buf, mdoff):
    sig, = struct.unpack_from('<I', buf, mdoff)
    assert sig == 0x424A5342
    vlen, = struct.unpack_from('<I', buf, mdoff + 12)
    o = mdoff + 16 + vlen + 2
    nstreams, = struct.unpack_from('<H', buf, o); o += 2
    streams = {}
    for _ in range(nstreams):
        so, ss = struct.unpack_from('<II', buf, o); o += 8
        e = buf.index(b'\0', o); name = buf[o:e].decode(); o = (e + 4) & ~3
        streams[name] = (mdoff + so, ss)
    return streams

def pe_rva(buf, rva):
    pe = struct.unpack_from('<I', buf, 0x3c)[0]
    nsec = struct.unpack_from('<H', buf, pe + 6)[0]
    opt = struct.unpack_from('<H', buf, pe + 20)[0]
    s = pe + 24 + opt
    for i in range(nsec):
        vs, va, rawsz, rawp = struct.unpack_from('<IIII', buf, s + i*40 + 8)
        if va <= rva < va + max(vs, rawsz): return rva - va + rawp
    raise ValueError(rva)

pe = struct.unpack_from('<I', dll, 0x3c)[0]
magic = struct.unpack_from('<H', dll, pe + 24)[0]
ddoff = pe + 24 + (96 if magic == 0x10b else 112)
cli_rva = struct.unpack_from('<I', dll, ddoff + 14*8)[0]
cli = pe_rva(dll, cli_rva)
md_rva = struct.unpack_from('<I', dll, cli + 8)[0]
streams = parse_md(dll, pe_rva(dll, md_rva))

C = 'coded'
CODED = {
 'TypeDefOrRef': (2, [0x02,0x01,0x1B]), 'HasConstant': (2,[0x04,0x08,0x17]),
 'HasCustomAttribute': (5,[0x06,0x04,0x01,0x02,0x08,0x09,0x0A,0x00,0x0E,0x17,0x14,0x11,0x1A,0x1B,0x20,0x23,0x26,0x27,0x28,0x2A,0x2C,0x2B]),
 'HasFieldMarshal': (1,[0x04,0x08]), 'HasDeclSecurity': (2,[0x02,0x06,0x20]),
 'MemberRefParent': (3,[0x02,0x01,0x1A,0x06,0x1B]), 'HasSemantics': (1,[0x14,0x17]),
 'MethodDefOrRef': (1,[0x06,0x0A]), 'MemberForwarded': (1,[0x04,0x06]),
 'Implementation': (2,[0x26,0x23,0x27]), 'CustomAttributeType': (3,[None,None,0x06,0x0A,None]),
 'ResolutionScope': (2,[0x00,0x1A,0x23,0x01]), 'TypeOrMethodDef': (1,[0x02,0x06]),
}
S='s'; G='g'; B='b'
SCHEMA = {
 0x00:[2,S,G,G,G], 0x01:['ResolutionScope',S,S], 0x02:[4,S,S,'TypeDefOrRef',0x04,0x06],
 0x03:[0x04], 0x04:[2,S,B], 0x05:[0x06], 0x06:[4,2,2,S,B,0x08], 0x07:[0x08], 0x08:[2,2,S],
 0x09:[0x02,'TypeDefOrRef'], 0x0A:['MemberRefParent',S,B], 0x0B:[2,'HasConstant',B],
 0x0C:['HasCustomAttribute','CustomAttributeType',B], 0x0D:['HasFieldMarshal',B],
 0x0E:[2,'HasDeclSecurity',B], 0x0F:[2,4,0x02], 0x10:[4,0x04], 0x11:[B], 0x12:[0x02,0x14],
 0x13:[0x14], 0x14:[2,S,'TypeDefOrRef'], 0x15:[0x02,0x17], 0x16:[0x17], 0x17:[2,S,B],
 0x18:[2,0x06,'HasSemantics'], 0x19:[0x02,'MethodDefOrRef','MethodDefOrRef'], 0x1A:[S], 0x1B:[B],
 0x1C:[2,'MemberForwarded',S,0x1A], 0x1D:[4,0x04], 0x1E:[4,4], 0x1F:[4],
 0x20:[4,2,2,2,2,4,B,S,S], 0x21:[4], 0x22:[4,4,4], 0x23:[2,2,2,2,4,B,S,S,B], 0x24:[4,0x23],
 0x25:[4,4,4,0x23], 0x26:[4,S,B], 0x27:[4,4,S,S,'Implementation'], 0x28:[4,4,S,'Implementation'],
 0x29:[0x02,0x02], 0x2A:[2,2,'TypeOrMethodDef',S], 0x2B:['MethodDefOrRef',B], 0x2C:[0x2A,'TypeDefOrRef'],
 0x30:[B,G,B,G], 0x31:[0x30,B],
}

def read_tables(buf, tso, extra_rows=None):
    heap = buf[tso + 6]
    valid, = struct.unpack_from('<Q', buf, tso + 8)
    o = tso + 24
    rows = {}
    for i in range(64):
        if valid >> i & 1:
            rows[i] = struct.unpack_from('<I', buf, o)[0]; o += 4
    allrows = dict(extra_rows or {}); allrows.update(rows)
    ssz = 4 if heap & 1 else 2; gsz = 4 if heap & 2 else 2; bsz = 4 if heap & 4 else 2
    def colsize(c):
        if c == S: return ssz
        if c == G: return gsz
        if c == B: return bsz
        if isinstance(c, str):
            bits, tabs = CODED[c]
            mx = max(allrows.get(t, 0) for t in tabs if t is not None)
            return 2 if mx < (1 << (16 - bits)) else 4
        if c in (2, 4) and not isinstance(c, str):
            pass
        return None
    tables = {}
    for t in sorted(rows):
        sch = SCHEMA[t]
        sizes = []
        for c in sch:
            if isinstance(c, int) and c not in (2, 4) or (isinstance(c, int) and c in (2,4) and False):
                pass
        # explicit: ints 2/4 are fixed widths EXCEPT table ids; disambiguate via marker
        sizes = []
        for c in sch:
            if c in (S, G, B) or isinstance(c, str): sizes.append(colsize(c))
            elif isinstance(c, int) and c >= 0x100: pass
            else: sizes.append(None)
        tables[t] = (sch, sizes)
    return rows, allrows, o, (ssz, gsz, bsz)

# Table-index columns vs fixed-width ints are ambiguous for 2/4 (0x02 TypeDef, 0x04 Field).
# Resolve by wrapping table refs explicitly.
class T:
    def __init__(s, t): s.t = t
def fix(s):
    out = []
    for i, c in enumerate(s):
        out.append(c)
    return out
SCHEMA2 = {
 0x00:[2,S,G,G,G], 0x01:['ResolutionScope',S,S], 0x02:[4,S,S,'TypeDefOrRef',T(0x04),T(0x06)],
 0x03:[T(0x04)], 0x04:[2,S,B], 0x05:[T(0x06)], 0x06:[4,2,2,S,B,T(0x08)], 0x07:[T(0x08)], 0x08:[2,2,S],
 0x09:[T(0x02),'TypeDefOrRef'], 0x0A:['MemberRefParent',S,B], 0x0B:[2,'HasConstant',B],
 0x0C:['HasCustomAttribute','CustomAttributeType',B], 0x0D:['HasFieldMarshal',B],
 0x0E:[2,'HasDeclSecurity',B], 0x0F:[2,4,T(0x02)], 0x10:[4,T(0x04)], 0x11:[B], 0x12:[T(0x02),T(0x14)],
 0x13:[T(0x14)], 0x14:[2,S,'TypeDefOrRef'], 0x15:[T(0x02),T(0x17)], 0x16:[T(0x17)], 0x17:[2,S,B],
 0x18:[2,T(0x06),'HasSemantics'], 0x19:[T(0x02),'MethodDefOrRef','MethodDefOrRef'], 0x1A:[S], 0x1B:[B],
 0x1C:[2,'MemberForwarded',S,T(0x1A)], 0x1D:[4,T(0x04)], 0x1E:[4,4], 0x1F:[4],
 0x20:[4,2,2,2,2,4,B,S,S], 0x21:[4], 0x22:[4,4,4], 0x23:[2,2,2,2,4,B,S,S,B], 0x24:[4,T(0x23)],
 0x25:[4,4,4,T(0x23)], 0x26:[4,S,B], 0x27:[4,4,S,S,'Implementation'], 0x28:[4,4,S,'Implementation'],
 0x29:[T(0x02),T(0x02)], 0x2A:[2,2,'TypeOrMethodDef',S], 0x2B:['MethodDefOrRef',B], 0x2C:[T(0x2A),'TypeDefOrRef'],
 0x30:[B,G,B,G], 0x31:[T(0x30),B],
}

def load_tables(buf, tso, extra_rows=None):
    heap = buf[tso + 6]
    valid, = struct.unpack_from('<Q', buf, tso + 8)
    o = tso + 24
    rows = {}
    for i in range(64):
        if valid >> i & 1:
            rows[i] = struct.unpack_from('<I', buf, o)[0]; o += 4
    if heap & 0x40: o += 4
    allrows = dict(extra_rows or {}); allrows.update(rows)
    sz = {S: 4 if heap & 1 else 2, G: 4 if heap & 2 else 2, B: 4 if heap & 4 else 2}
    def csz(c):
        if isinstance(c, T): return 2 if allrows.get(c.t, 0) < 65536 else 4
        if c in (S, G, B): return sz[c]
        if isinstance(c, str):
            bits, tabs = CODED[c]
            mx = max(allrows.get(t, 0) for t in tabs if t is not None)
            return 2 if mx < (1 << (16 - bits)) else 4
        return c
    tables = {}
    for t in sorted(rows):
        if t not in SCHEMA2: break
        sch = SCHEMA2[t]; sizes = [csz(c) for c in sch]; rs = sum(sizes)
        lst = []
        for r in range(rows[t]):
            vals = []; q = o
            for s_ in sizes:
                vals.append(struct.unpack_from('<H' if s_ == 2 else '<I', buf, q)[0]); q += s_
            lst.append(vals); o += rs
        tables[t] = lst
    return tables, rows

def decode_coded(kind, v):
    bits, tabs = CODED[kind]
    return tabs[v & ((1 << bits) - 1)], v >> bits

tso = streams['#~'][0] if '#~' in streams else streams['#-'][0]
tables, rows = load_tables(dll, tso)
str_off = streams['#Strings'][0]; us_off = streams['#US'][0]; blob_off = streams['#Blob'][0]
def gs(i):
    e = dll.index(b'\0', str_off + i); return dll[str_off + i:e].decode('utf-8', 'replace')
def cuint(buf, o):
    b = buf[o]
    if b & 0x80 == 0: return b, o + 1
    if b & 0xC0 == 0x80: return ((b & 0x3f) << 8) | buf[o+1], o + 2
    return ((b & 0x1f) << 24) | (buf[o+1] << 16) | (buf[o+2] << 8) | buf[o+3], o + 4
def us(i):
    n, o = cuint(dll, us_off + i)
    return dll[o:o + n - 1].decode('utf-16-le', 'replace') if n else ''

TD = tables[0x02]; MD = tables[0x06]; TR = tables.get(0x01, []); MR = tables.get(0x0A, []); FD = tables.get(0x04, [])
nest = {r[0]: r[1] for r in tables.get(0x29, [])}
def tdname(i):
    r = TD[i-1]; n = gs(r[1]); ns = gs(r[2])
    if i in nest: return tdname(nest[i]) + '/' + n
    return (ns + '.' if ns else '') + n
meth_owner = {}; field_owner = {}
for i, r in enumerate(TD, 1):
    m_end = TD[i][5] if i < len(TD) else len(MD) + 1
    for m in range(r[5], m_end): meth_owner[m] = i
    f_end = TD[i][4] if i < len(TD) else len(FD) + 1
    for f in range(r[4], f_end): field_owner[f] = i
def trname(i):
    r = TR[i-1]; return (gs(r[2]) + '.' if gs(r[2]) else '') + gs(r[1])
def typename(tab, idx):
    if tab == 0x02: return tdname(idx)
    if tab == 0x01: return trname(idx)
    if tab == 0x1B: return 'TypeSpec#%d' % idx
    if tab == 0x1A: return 'ModuleRef#%d' % idx
    if tab == 0x06: return mdname(idx)
    return '%02x#%d' % (tab, idx)
def mdname(i): return tdname(meth_owner.get(i, 1)) + '::' + gs(MD[i-1][3])
def tok(t):
    tab, idx = t >> 24, t & 0xffffff
    try:
        if tab == 0x06: return mdname(idx)
        if tab == 0x0A:
            r = MR[idx-1]; pt, pi = decode_coded('MemberRefParent', r[0])
            return typename(pt, pi) + '::' + gs(r[1])
        if tab == 0x04: return tdname(field_owner.get(idx, 1)) + '::' + gs(FD[idx-1][1])
        if tab == 0x2B:
            r = tables[0x2B][idx-1]; mt, mi = decode_coded('MethodDefOrRef', r[0])
            return tok((mt << 24) | mi) + '<..>'
        if tab in (0x01, 0x02, 0x1B): return typename(tab, idx)
        if tab == 0x70: return repr(us(idx))
        if tab == 0x11: return 'sig#%d' % idx
    except Exception as ex: return '?%08x(%s)' % (t, ex)
    return '%08x' % t

# ---------- opcodes ----------
N, U8, I8, I32, I64, R4, R8, TK, BR1, BR4, SW, U16 = range(12)
OP = {}
def d(code, name, arg=N): OP[code] = (name, arg)
for i, n in enumerate(['nop','break','ldarg.0','ldarg.1','ldarg.2','ldarg.3','ldloc.0','ldloc.1','ldloc.2','ldloc.3','stloc.0','stloc.1','stloc.2','stloc.3']): d(i, n)
for c, n in [(0x0E,'ldarg.s'),(0x0F,'ldarga.s'),(0x10,'starg.s'),(0x11,'ldloc.s'),(0x12,'ldloca.s'),(0x13,'stloc.s')]: d(c, n, U8)
d(0x14,'ldnull')
for i in range(9): d(0x16+i, 'ldc.i4.%d' % i)
d(0x15,'ldc.i4.m1'); d(0x1F,'ldc.i4.s',I8); d(0x20,'ldc.i4',I32); d(0x21,'ldc.i8',I64); d(0x22,'ldc.r4',R4); d(0x23,'ldc.r8',R8)
d(0x25,'dup'); d(0x26,'pop'); d(0x27,'jmp',TK); d(0x28,'call',TK); d(0x29,'calli',TK); d(0x2A,'ret')
for i, n in enumerate(['br','brfalse','brtrue','beq','bge','bgt','ble','blt','bne.un','bge.un','bgt.un','ble.un','blt.un']):
    d(0x2B+i, n+'.s', BR1); d(0x38+i, n, BR4)
d(0x45,'switch',SW)
for c in range(0x46, 0x6F): d(c, 'op%02x' % c)
names46 = ['ldind.i1','ldind.u1','ldind.i2','ldind.u2','ldind.i4','ldind.u4','ldind.i8','ldind.i','ldind.r4','ldind.r8','ldind.ref','stind.ref','stind.i1','stind.i2','stind.i4','stind.i8','stind.r4','stind.r8','add','sub','mul','div','div.un','rem','rem.un','and','or','xor','shl','shr','shr.un','neg','not','conv.i1','conv.i2','conv.i4','conv.i8','conv.r4','conv.r8','conv.u4','conv.u8']
for i, n in enumerate(names46): d(0x46+i, n)
for c, n in [(0x6F,'callvirt'),(0x70,'cpobj'),(0x71,'ldobj'),(0x72,'ldstr'),(0x73,'newobj'),(0x74,'castclass'),(0x75,'isinst'),(0x79,'unbox'),(0x7B,'ldfld'),(0x7C,'ldflda'),(0x7D,'stfld'),(0x7E,'ldsfld'),(0x7F,'ldsflda'),(0x80,'stsfld'),(0x81,'stobj'),(0x8C,'box'),(0x8D,'newarr'),(0x8F,'ldelema'),(0xA3,'ldelem'),(0xA4,'stelem'),(0xA5,'unbox.any'),(0xC2,'refanyval'),(0xC6,'mkrefany'),(0xD0,'ldtoken')]: d(c, n, TK)
d(0x76,'conv.r.un'); d(0x7A,'throw'); d(0x8E,'ldlen')
for c in list(range(0x82,0x8C)) + list(range(0x90,0xA3)) + list(range(0xB3,0xBB)) + [0xC3] + list(range(0xD1,0xDD)) + [0xDF,0xE0]:
    OP.setdefault(c, ('op%02x' % c, N))
d(0xDC,'endfinally'); d(0xDD,'leave',BR4); d(0xDE,'leave.s',BR1); d(0xD3,'conv.i'); d(0xE0,'conv.u')
OPFE = {0x00:('arglist',N),0x01:('ceq',N),0x02:('cgt',N),0x03:('cgt.un',N),0x04:('clt',N),0x05:('clt.un',N),0x06:('ldftn',TK),0x07:('ldvirtftn',TK),
        0x09:('ldarg',U16),0x0A:('ldarga',U16),0x0B:('starg',U16),0x0C:('ldloc',U16),0x0D:('ldloca',U16),0x0E:('stloc',U16),0x0F:('localloc',N),
        0x11:('endfilter',N),0x12:('unaligned.',U8),0x13:('volatile.',N),0x14:('tail.',N),0x15:('initobj',TK),0x16:('constrained.',TK),
        0x17:('cpblk',N),0x18:('initblk',N),0x19:('no.',U8),0x1A:('rethrow',N),0x1C:('sizeof',TK),0x1D:('refanytype',N),0x1E:('readonly.',N)}

def method_il(mi):
    rva = MD[mi-1][0]
    if not rva: return None
    o = pe_rva(dll, rva); h = dll[o]
    if h & 3 == 2: return dll[o+1:o+1+(h >> 2)]
    flags_size, = struct.unpack_from('<H', dll, o); hs = (flags_size >> 12) * 4
    codesize, = struct.unpack_from('<I', dll, o + 4)
    return dll[o+hs:o+hs+codesize]

def disasm(il):
    out = []; o = 0
    while o < len(il):
        start = o; b = il[o]; o += 1
        if b == 0xFE:
            name, a = OPFE.get(il[o], ('fe%02x' % il[o], N)); o += 1
        else:
            name, a = OP.get(b, ('?%02x' % b, N))
        arg = ''
        if a == U8: arg = str(il[o]); o += 1
        elif a == I8: arg = str(struct.unpack_from('<b', il, o)[0]); o += 1
        elif a == U16: arg = str(struct.unpack_from('<H', il, o)[0]); o += 2
        elif a == I32: arg = str(struct.unpack_from('<i', il, o)[0]); o += 4
        elif a == I64: arg = str(struct.unpack_from('<q', il, o)[0]); o += 8
        elif a == R4: arg = repr(struct.unpack_from('<f', il, o)[0]); o += 4
        elif a == R8: arg = repr(struct.unpack_from('<d', il, o)[0]); o += 8
        elif a == TK: t = struct.unpack_from('<I', il, o)[0]; o += 4; arg = tok(t); out.append((start, name, arg, t)); continue
        elif a == BR1: dv = struct.unpack_from('<b', il, o)[0]; o += 1; arg = 'IL_%04x' % (o + dv)
        elif a == BR4: dv = struct.unpack_from('<i', il, o)[0]; o += 4; arg = 'IL_%04x' % (o + dv)
        elif a == SW:
            n = struct.unpack_from('<I', il, o)[0]; o += 4
            tg = [struct.unpack_from('<i', il, o + 4*k)[0] for k in range(n)]; o += 4*n
            arg = ','.join('IL_%04x' % (o + t) for t in tg)
        out.append((start, name, arg, None))
    return out

# ---------- PDB ----------
seqpts = {}; docs = {}
if PDB and os.path.exists(PDB):
    pdb = open(PDB, 'rb').read()
    ps = parse_md(pdb, 0)
    po, plen = ps['#Pdb']
    # #Pdb: 20 id, 4 entrypoint, 8 referenced tables bitmask, rows[]
    ref, = struct.unpack_from('<Q', pdb, po + 24); q = po + 32; extra = {}
    for i in range(64):
        if ref >> i & 1: extra[i] = struct.unpack_from('<I', pdb, q)[0]; q += 4
    ptab, prow = load_tables(pdb, ps['#~'][0], extra)
    pblob = ps['#Blob'][0]; pguid = ps['#GUID'][0]
    def pbl(i):
        n, o = cuint(pdb, pblob + i); return pdb[o:o+n]
    def docname(bi):
        b = pbl(bi); sep = chr(b[0]); o = 1; parts = []
        while o < len(b):
            pi, o = cuint(b, o)
            parts.append(pbl(pi).decode('utf-8', 'replace') if pi else '')
        return sep.join(parts)
    for i, r in enumerate(ptab.get(0x30, []), 1): docs[i] = docname(r[0])
    def sint(b, o):
        v, o2 = cuint(b, o); n = o2 - o
        bits = {1: 7, 2: 14, 4: 29}[n]
        if v & 1: return (v >> 1) - (1 << (bits - 1)), o2
        return v >> 1, o2
    for mi, r in enumerate(ptab.get(0x31, []), 1):
        doc, bi = r
        if not bi: continue
        b = pbl(bi); o = 0
        _, o = cuint(b, o)
        if not doc: doc, o = cuint(b, o)
        pts = []; il = 0; first = True; pl = pc = None
        while o < len(b):
            d_il, o = cuint(b, o)
            if d_il == 0 and not first:
                doc, o = cuint(b, o); continue
            il = d_il if first else il + d_il
            dl, o = cuint(b, o)
            dc, o = (cuint(b, o) if dl == 0 else sint(b, o))
            if dl == 0 and dc == 0: first = False; continue  # hidden
            if pl is None:
                l, o = cuint(b, o); c, o = cuint(b, o)
            else:
                dlv, o = sint(b, o); dcv, o = sint(b, o); l = pl + dlv; c = pc + dcv
            pl, pc = l, c
            pts.append((il, docs.get(doc, '?'), l))
            first = False
        seqpts[mi] = pts

def loc(mi, ilo=None):
    pts = seqpts.get(mi)
    if not pts: return '?'
    if ilo is None: return '%s:%d' % (pts[0][1], pts[0][2])
    best = pts[0]
    for p_ in pts:
        if p_[0] <= ilo: best = p_
    return '%s:%d' % (best[1], best[2])

# ---------- search ----------
all_dis = {}
for mi in range(1, len(MD) + 1):
    il = method_il(mi)
    if il:
        try: all_dis[mi] = disasm(il)
        except Exception as ex: all_dis[mi] = [(0, 'ERR', str(ex), None)]

maxline = {}; nmeth = {}
for mi, pts in seqpts.items():
    for _, dn, ln in pts:
        if ln < 0xfeefee: maxline[dn] = max(maxline.get(dn, 0), ln)
    if pts: nmeth[pts[0][1]] = nmeth.get(pts[0][1], 0) + 1
with open(os.path.join(OUTDIR, 'documents.txt'), 'w', encoding='utf-8') as f:
    f.write('# Original source files recorded in Aquila Local.pdb (v2.0.1)\n# max_line = highest line with a sequence point (lower bound on file length); methods = methods whose body starts in the file\n')
    for dn in sorted(set(docs.values())):
        f.write('%6s  %4s  %s\n' % (maxline.get(dn, '-'), nmeth.get(dn, '-'), dn))
# full dump file
with open(os.path.join(OUTDIR, 'il_full.txt'), 'w', encoding='utf-8') as f:
    for mi, ins in all_dis.items():
        f.write('\n### %s   [%s]\n' % (mdname(mi), loc(mi)))
        for s_, n, a, t in ins: f.write('  IL_%04x %-12s %s\n' % (s_, n, a))

hits = set()
for mi, ins in all_dis.items():
    for s_, n, a, t in ins:
        if n == 'ldstr' and any(x.lower() in a.lower() for x in TARGETS):
            hits.add(mi); print('HIT %s @IL_%04x %s -> %s' % (mdname(mi), s_, loc(mi, s_), a))
print('hit methods:', len(hits))

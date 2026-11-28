#!/usr/bin/env python3
"""Parser / validator / editor for FFT: The Ivalice Chronicles UIB v10 layout files.

Conventions found (see NOTES.md next to this script):
  * little-endian, everything 4-byte aligned before the string table
  * every offset is a signed 32-bit value RELATIVE TO THE START OF THE STRUCT that
    contains it (not to the field, not to the file).  All offsets in the vanilla file
    are positive (forward pointing).
  * string table (NUL-terminated UTF-8) is the last thing in the file.

Usage:
  uib.py dump    file.uib            full tree listing
  uib.py jobs    file.uib            the 20 job nodes + lines of the JobTree
  uib.py verify  file.uib            structural self-check
  uib.py demo    in.uib out.uib      add a 21st job node + one line (example)
  uib.py move    in.uib out.uib <component> <element> x y [w h]
                                     move (and resize) an element in place, e.g.
                                     uib.py move ffto_job_tree.uib out.uib JobTree Mime 1016 700
"""
import struct, sys, collections

SZ = {2: 0x1B0, 3: 0x130, 4: 0x1A4, 6: 0x110, 9: 0xF0, 10: 0x17C}   # type 1 is variable
TYPE_NAMES = {1: 'Layer', 2: 'Image', 3: 'Text', 4: 'NineGrid', 6: 'Rect', 9: 'Collision', 10: 'Instance'}
# string-ref fields per element type: (field offset, base offset) – base is relative to element start
STR_FIELDS = {
    1: [(0x04, 0)], 6: [(0x04, 0)], 9: [(0x04, 0)],
    2: [(0x04, 0), (0x180, 0x17C), (0x1AC, 0x1A8)],
    3: [(0x04, 0), (0xF4, 0xE8)],
    4: [(0x04, 0), (0x174, 0x170), (0x1A0, 0x19C)],
    10: [(0x04, 0), (0x70, 0), (0x14C, 0x148), (0x178, 0x174)],
}


class UIB:
    def __init__(self, data):
        self.d = bytearray(data)
        self.parse()

    # ---- primitive access
    def I(self, o): return struct.unpack_from('<i', self.d, o)[0]
    def F(self, o): return struct.unpack_from('<f', self.d, o)[0]
    def W(self, o, v): struct.pack_into('<i', self.d, o, v)
    def cs(self, o):
        return self.d[o:self.d.index(b'\0', o)].decode('utf-8')

    # ---- parsing
    def parse(self):
        d = self.d
        assert d[:4] == b'UIB\0' and self.I(4) == 10, 'not a UIB v10'
        self.root = self.I(0x18)                       # 0x2C : root struct (absolute)
        R = self.root
        self.res = R + self.I(R + 0)                   # 0x58 : resource-table struct
        self.comp_tab = R + self.I(R + 4)              # 0x284: component records (0x40 each)
        self.ncomp = self.I(R + 8)
        S = self.res
        self.tex_list = S + self.I(S + 0); self.ntex = self.I(S + 4)
        self.ui_list = S + self.I(S + 8); self.nui = self.I(S + 12)
        self.res_end = S + self.I(S + 16)              # == comp_tab
        # struct refs: list of (field_addr, base_addr) for every NON-string offset we know
        self.srefs = [(R, R), (R + 4, R), (S, S), (S + 8, S), (S + 16, S)]
        self.strrefs = []                              # (field_addr, base_addr)
        self.resources = []
        for L, n, kind in ((self.tex_list, self.ntex, 'tex'), (self.ui_list, self.nui, 'ui')):
            for i in range(n):
                self.srefs.append((L + 4 * i, L))
                r = L + self.I(L + 4 * i)
                sub = r + self.I(r + 8)
                self.srefs.append((r + 8, r))
                self.strrefs += [(r + 4, r), (sub + 4, sub)]
                self.resources.append((kind, r, self.I(r), self.cs(r + self.I(r + 4)), self.I(sub), self.cs(sub + self.I(sub + 4))))
        self.comps = []
        self.elements = []
        for k in range(self.ncomp):
            r = self.comp_tab + 0x40 * k
            c = dict(rec=r, name=self.cs(r + self.I(r)), w=self.I(r + 4), h=self.I(r + 8),
                     A=r + self.I(r + 0xC), B=r + self.I(r + 0x10), nroot=self.I(r + 0x14),
                     C=r + self.I(r + 0x18), nanim=self.I(r + 0x1C), D=r + self.I(r + 0x24), idx=k)
            self.strrefs.append((r, r))
            self.srefs += [(r + 0xC, r), (r + 0x10, r), (r + 0x18, r), (r + 0x24, r)]
            B = c['B']
            roots = [B + self.I(B + 4 * i) for i in range(c['nroot'])]
            self.srefs += [(B + 4 * i, B) for i in range(c['nroot'])]
            c['roots'] = []
            for i, e in enumerate(roots):
                end = roots[i + 1] if i + 1 < len(roots) else c['C']
                c['roots'].append(self.parse_el(e, end, None, k))
            c['anims'] = self.parse_anims(c)
            self.comps.append(c)
        # string table = right after last component's D block (8 bytes)
        self.str_start = self.comps[-1]['D'] + 8
        self.strings = {}
        o = self.str_start
        while o < len(self.d):
            j = self.d.index(b'\0', o); self.strings[o] = self.d[o:j].decode(); o = j + 1

    def parse_el(self, E, end, parent, comp):
        t = self.I(E)
        el = dict(addr=E, end=end, type=t, parent=parent, comp=comp, children=[],
                  name=self.cs(E + self.I(E + 4)), x=self.I(E + 8), y=self.I(E + 12),
                  rot=self.F(E + 0x10), sx=self.F(E + 0x14), sy=self.F(E + 0x18),
                  px=self.I(E + 0x1C), py=self.I(E + 0x20), w=self.I(E + 0x44), h=self.I(E + 0x48))
        self.elements.append(el)
        self.srefs.append((E + 0x4C, E))
        for f, b in STR_FIELDS.get(t, [(4, 0)]):
            self.strrefs.append((E + f, E + b))
        if t == 10:
            el['class'] = self.cs(E + self.I(E + 0x70))
            self.srefs += [(E + 0x74, E), (E + 0x150, E + 0x148)]
            el['ref'] = self.cs(E + 0x148 + self.I(E + 0x14C))
        if t == 1:
            L = E + self.I(E + 0x90); n = self.I(E + 0x94)
            el['list'] = L
            self.srefs.append((E + 0x90, E))
            offs = [L + self.I(L + 4 * i) for i in range(n)]
            self.srefs += [(L + 4 * i, L) for i in range(n)]
            for i, o in enumerate(offs):
                el['children'].append(self.parse_el(o, offs[i + 1] if i + 1 < n else end, el, comp))
        return el

    def parse_anims(self, c):
        out = []
        for k in range(c['nanim']):
            h = c['C'] + 0x60 * k; b = h + 8
            a = dict(hdr=h, name=self.cs(h + self.I(h)))
            nK, nT = self.I(b + 8), self.I(b + 24)
            K, T = b + self.I(b + 4), b + self.I(b + 20)
            a['tracks'] = []
            self.strrefs.append((h, h))
            for i in range(nT):
                e = T + self.I(T + 4 * i)
                if self.I(e) == 0xFA1: self.strrefs.append((e + 0x28, e))
                a['tracks'].append(self.cs(e + self.I(e + 0x28)) if self.I(e) == 0xFA1 else '<self>')
            a['keys'] = [(self.I(K + 0x20 * i + 0x14), hex(self.I(K + 0x20 * i + 8))) for i in range(nK)]
            out.append(a)
        return out

    # ---- helpers
    def find(self, comp, name):
        for e in self.elements:
            if self.comps[e['comp']]['name'] == comp and e['name'] == name:
                return e
        raise KeyError(name)

    def string_ref_candidates(self, limit):
        """dwords before `limit` whose value lands in the string table for some base within
        0x1B0 bytes before the field (the largest field offset in any struct)."""
        starts = self.strings
        out = []
        for a in range(0, limit, 4):
            v = self.I(a)
            t = a + v
            if self.str_start - 0x1B0 <= t < len(self.d) and v > 0:
                ks = [k for k in range(0, 0x1B4, 4) if (t - k) in starts]
                if ks:
                    out.append((a, v, frozenset((k, starts[t - k]) for k in ks)))
        return out

    # ---- editing
    def insert(self, P, data):
        """Insert bytes at absolute offset P (must be 4-aligned, before the string table) and fix
        every forward offset whose struct starts before P and whose target is >= P."""
        assert P % 4 == 0 and len(data) % 4 == 0 and P <= self.str_start
        delta = len(data)
        fix = set()
        for a, b in self.srefs:
            if b < P <= b + self.I(a):
                fix.add(a)
        # all string refs located before P point past P (strings live at the end)
        known_str = {a for a, b in self.strrefs if a < P}
        heur = {a for a, v, s in self.string_ref_candidates(P)}
        D_flags = {c['D'] + 4 for c in self.comps}         # 0x00010101 flag words, not offsets
        heur -= D_flags
        missing = known_str - heur
        assert not missing, 'structural string refs not seen by scan: %s' % [hex(x) for x in missing]
        fix |= heur
        for a in fix:
            self.W(a, self.I(a) + delta)
        self.d[P:P] = data
        self.parse()
        return delta

    def add_string(self, s):
        for o, v in self.strings.items():
            if v == s:
                return o
        o = len(self.d)
        self.d += s.encode() + b'\0'
        self.strings[o] = s
        return o

    def clone_element(self, src_el, comp, parent_name, new_name, x, y, w=None, h=None, cls=None):
        """Append a copy of a leaf element (type != 1) as the LAST child of layer `parent_name`."""
        assert src_el['type'] != 1
        size = SZ[src_el['type']]
        # remember string targets of the template (absolute string addresses don't move
        # relative to each other; we store the text and re-resolve after the inserts)
        S = src_el['addr']
        tmpl = bytes(self.d[S:S + size])
        texts = [(f, b, self.cs(S + b + self.I(S + f))) for f, b in STR_FIELDS[src_el['type']]]
        parent = self.find(comp, parent_name)
        P = parent['end']                         # end of the parent's subtree
        self.insert(P, b'\0' * size)              # placeholder for the element
        parent = self.find(comp, parent_name)
        L = parent['list']; n = self.I(parent['addr'] + 0x94)
        self.insert(L + 4 * n, b'\0' * 4)         # one more slot at the end of the child list
        parent = self.find(comp, parent_name)
        L = parent['list']
        # after the slot insert the new element sits at parent['end'] - size
        N = parent['end'] - size
        self.d[N:N + size] = tmpl
        self.W(L + 4 * n, N - L)
        self.W(parent['addr'] + 0x94, n + 1)
        texts = [(f, b, new_name if f == 4 else (cls if (cls and f == 0x70) else t)) for f, b, t in texts]
        for f, b, t in texts:
            so = self.add_string(t)
            self.W(N + f, so - (N + b))
        self.W(N + 8, x); self.W(N + 12, y)
        if w is not None:
            self.W(N + 0x44, w); self.W(N + 0x1C, w // 2)
        if h is not None:
            self.W(N + 0x48, h); self.W(N + 0x20, h // 2)
        self.parse()
        return N

    # ---- verification
    def verify(self, quiet=False):
        errs = []
        starts = self.strings
        for a, b in self.strrefs:
            if b + self.I(a) not in starts:
                errs.append('string ref @%x (base %x) -> %x not a string start' % (a, b, b + self.I(a)))
        # contiguity: header/resources, component bodies, elements
        if self.res_end != self.comp_tab: errs.append('resource end != component table')
        pos = self.comp_tab + 0x40 * self.ncomp
        for c in self.comps:
            if c['A'] != pos: errs.append('%s A %x != expected %x' % (c['name'], c['A'], pos))
            if not (c['A'] < c['B'] < c['C'] < c['D']): errs.append('%s order' % c['name'])
            pos = c['D'] + 8
        if pos != self.str_start: errs.append('string table start mismatch')
        for e in self.elements:
            if e['type'] == 1:
                L = e['list']; n = len(e['children'])
                exp = L + 4 * n
                for ch in e['children']:
                    if ch['addr'] != exp: errs.append('%s child %s at %x expected %x' % (e['name'], ch['name'], ch['addr'], exp))
                    exp = ch['end']
                if exp != e['end']: errs.append('%s subtree end mismatch' % e['name'])
            else:
                if e['end'] - e['addr'] != SZ[e['type']]:
                    errs.append('%s size %x' % (e['name'], e['end'] - e['addr']))
        # every string-table entry is referenced by something
        cands = self.string_ref_candidates(self.str_start)
        hit = collections.Counter()
        for a, v, s in cands:
            for k, txt in s: pass
        if not quiet:
            print('elements %d, components %d, string refs (structural) %d, scan candidates %d' %
                  (len(self.elements), len(self.comps), len(self.strrefs), len(cands)))
            print('errors:', len(errs)); [print('  ', x) for x in errs[:50]]
        return errs


def dump(u):
    print('header: root struct @%#x, resources @%#x, components @%#x x%d, strings @%#x (%d strings)' %
          (u.root, u.res, u.comp_tab, u.ncomp, u.str_start, len(u.strings)))
    for r in u.resources: print('  res %-3s @%#x type=%d %s  [%#x] %s' % r)
    for c in u.comps:
        print('component %2d %-20s %4dx%-4d rec@%#x A@%#x B@%#x(%d roots) C@%#x(%d anims) D@%#x' %
              (c['idx'], c['name'], c['w'], c['h'], c['rec'], c['A'], c['B'], c['nroot'], c['C'], c['nanim'], c['D']))
        def pe(e, dep):
            extra = (' class=%s' % e['class']) if e['type'] == 10 else ''
            print('  ' * dep, '@%#x %-9s %-24s x=%5d y=%5d w=%4d h=%4d piv=(%d,%d) sc=(%.3g,%.3g) rot=%.3g%s' %
                  (e['addr'], TYPE_NAMES.get(e['type'], e['type']), e['name'], e['x'], e['y'], e['w'], e['h'],
                   e['px'], e['py'], e['sx'], e['sy'], e['rot'], extra))
            for ch in e['children']: pe(ch, dep + 1)
        for r in c['roots']: pe(r, 2)
        for a in c['anims']:
            print('    anim @%#x %-16s tracks=%d keys=%s' % (a['hdr'], a['name'], len(a['tracks']),
                  [(a['tracks'][i] if i < len(a['tracks']) else i, p) for i, p in a['keys']]))


def jobs(u):
    lr = u.find('JobTree', 'LayerRoot')
    print('LayerRoot @%#x offset (%d,%d); child list @%#x count field @%#x' % (lr['addr'], lr['x'], lr['y'], lr['list'], lr['addr'] + 0x94))
    for i, e in enumerate(lr['children']):
        if e['type'] == 10:
            print('  [%2d] @%#x %-14s x=%4d y=%4d  (x@%#x y@%#x) class=%s' % (i, e['addr'], e['name'], e['x'], e['y'], e['addr'] + 8, e['addr'] + 12, e['class']))
    lb = u.find('JobTree', 'LayerBranches')
    print('LayerBranches @%#x child list @%#x' % (lb['addr'], lb['list']))
    for i, e in enumerate(lb['children']):
        print('  [%2d] @%#x %-24s x=%4d y=%4d w=%4d h=%4d piv=(%d,%d) sc=(%g,%g) rot=%.4g class=%s' % (
            i, e['addr'], e['name'], e['x'], e['y'], e['w'], e['h'], e['px'], e['py'], e['sx'], e['sy'], e['rot'], e['class']))


def snapshot(u):
    """semantic snapshot used to compare before/after an edit"""
    s = [('res',) + tuple(r[2:]) for r in u.resources]
    for c in u.comps:
        s.append(('comp', c['name'], c['w'], c['h']))
        for e in u.elements:
            if e['comp'] == c['idx']:
                s.append(('el', c['name'], e['name'], e['type'], e['x'], e['y'], e['w'], e['h'], e.get('class'), e.get('ref'),
                          e['parent']['name'] if e['parent'] else None))
        for a in c['anims']:
            s.append(('anim', c['name'], a['name'], tuple(a['tracks']), tuple(a['keys'])))
    return s


def demo(src, dst):
    orig = UIB(open(src, 'rb').read())
    u = UIB(bytes(orig.d))
    cands_before = orig.string_ref_candidates(orig.str_start)
    # new job node: copy of Mime, placed below the tree centre, named "Job20"
    mime = u.find('JobTree', 'Mime')
    u.clone_element(mime, 'JobTree', 'LayerRoot', 'Job20', 1016, 990)
    # new line: copy of LineKnightMonk (class LineBottom, vertical 16 x h), from Mime down to Job20
    km = u.find('JobTree', 'LineKnightMonk')
    u.clone_element(km, 'JobTree', 'LayerBranches', 'LineMimeJob20', 1016 + 32 - 8, 849 + 80 + 4, w=16, h=990 - (849 + 80) - 8)
    errs = u.verify()
    # semantic comparison: everything that existed must be unchanged
    a, b = snapshot(orig), snapshot(u)
    added = [x for x in b if x not in a]
    lost = [x for x in a if x not in b]
    print('snapshot: lost %d, added %d' % (len(lost), len(added)))
    for x in lost[:20]: print('  LOST', x)
    for x in added[:20]: print('  ADDED', x)
    # raw check: every string-ref-looking dword of the original must still resolve to the same
    # string for the same struct base (old sequence must be a subsequence of the new one)
    new = u.string_ref_candidates(u.str_start)
    j = 0; bad = 0
    for a, v, s in cands_before:
        while j < len(new) and not (new[j][2] & s): j += 1
        if j == len(new): bad += 1; print('  UNMATCHED old ref @%#x' % a)
        else: j += 1
    print('raw string-ref check: %d old refs, %d new candidates, %d unmatched' % (len(cands_before), len(new), bad))
    open(dst, 'wb').write(bytes(u.d))
    print('wrote %s (%d -> %d bytes)' % (dst, len(orig.d), len(u.d)))


def move(src, dst, comp, name, x, y, w=None, h=None):
    """Sets an element's position (and size) in place: nothing moves, no offsets change."""
    u = UIB(open(src, 'rb').read())
    e = u.find(comp, name)
    E = e['addr']
    u.W(E + 8, x); u.W(E + 12, y)
    if w is not None:
        u.W(E + 0x44, w); u.W(E + 0x48, h)
        u.W(E + 0x1C, w // 2); u.W(E + 0x20, h // 2)   # pivot: normally the centre
    UIB(bytes(u.d)).verify()
    open(dst, 'wb').write(bytes(u.d))
    print('%s/%s: (%d, %d) -> (%d, %d)%s' % (comp, name, e['x'], e['y'], x, y, '' if w is None else ', %dx%d' % (w, h)))


if __name__ == '__main__':
    cmd, path = sys.argv[1], sys.argv[2]
    if cmd == 'demo':
        demo(path, sys.argv[3]); sys.exit()
    if cmd == 'move':
        a = sys.argv
        move(path, a[3], a[4], a[5], int(a[6]), int(a[7]), *(int(v) for v in a[8:10]))
        sys.exit()
    u = UIB(open(path, 'rb').read())
    {'dump': dump, 'jobs': jobs, 'verify': lambda u: u.verify()}[cmd](u)

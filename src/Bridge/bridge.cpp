


#include <windows.h>
#include <tlhelp32.h>
#include <stdint.h>
#include <stdio.h>
#include <stdarg.h>
#include <string.h>
#include "ElementHashes.h"
#include "SkillIds.h"


typedef struct _MonoObject { void* vtable; } MonoObject;
typedef struct _MonoClass MonoClass;
typedef struct _MonoMethod MonoMethod;
typedef struct _MonoField MonoField;
typedef struct _MonoString { void* vtable; } MonoString;
typedef struct _MonoDomain MonoDomain;
typedef struct _MonoImage MonoImage;
typedef struct _MonoAssembly MonoAssembly;
typedef struct _MonoVTable MonoVTable;
typedef struct _MonoType MonoType;

typedef struct _MonoArray { void* vtable; } MonoArray;


static MonoDomain* (*m_domain_get)(void);
static MonoDomain* (*m_root_domain)(void);
static void (*m_thread_attach)(MonoDomain*);
static MonoAssembly* (*m_domain_assembly_open)(MonoDomain*, const char*);
static void (*m_domain_assembly_foreach)(MonoDomain*, void(*)(MonoAssembly*, void*), void*);
static MonoImage* (*m_assembly_get_image)(MonoAssembly*);
static const char* (*m_image_get_name)(MonoImage*);
static MonoClass* (*m_class_from_name)(MonoImage*, const char*, const char*);
static MonoType* (*m_class_get_type)(MonoClass*);
static MonoMethod* (*m_class_get_method_from_name)(MonoClass*, const char*, int);
static MonoField* (*m_class_get_field_from_name)(MonoClass*, const char*);
static MonoObject* (*m_object_new)(MonoDomain*, MonoClass*);
static void* (*m_compile_method)(MonoMethod*);
static MonoObject* (*m_runtime_invoke)(MonoMethod*, void*, void**, MonoObject**);
static MonoObject* (*m_runtime_invoke_array)(MonoMethod*, void*, MonoArray*, MonoObject**);
static void (*m_field_get_value)(MonoObject*, MonoField*, void*);
static void (*m_field_set_value)(MonoObject*, MonoField*, void*);
static MonoObject* (*m_value_box)(MonoDomain*, MonoClass*, void*);
static void* (*m_object_unbox)(MonoObject*);
static char* (*m_array_addr_with_size)(void*, int, uintptr_t);
static uintptr_t (*m_array_length)(void*);
static void* (*m_array_new)(MonoDomain*, MonoClass*, uintptr_t);
static MonoString* (*m_string_new)(MonoDomain*, const char*);
static MonoImage* (*m_image_loaded)(const char*);
static void (*m_assembly_foreach)(void(*)(MonoAssembly*, void*), void*);
static MonoImage* (*m_get_corlib)(void);
static void (*m_gc_walk_heap)(int, void(*)(MonoObject*, MonoClass*, uintptr_t, uintptr_t, uintptr_t*, void*), void*);
static MonoObject* (*m_type_get_object)(MonoDomain*, MonoType*);
static MonoClass* (*m_object_get_class)(MonoObject*);
static uint32_t (*m_field_get_offset)(MonoField*);
static const char* (*m_field_get_name)(MonoField*);
static const char* (*m_class_get_name)(MonoClass*);
static const char* (*m_class_get_namespace)(MonoClass*);
static MonoClass* (*m_class_get_parent)(MonoClass*);
static MonoMethod* (*m_class_get_methods)(MonoClass*, void**);
static const char* (*m_method_get_name)(MonoMethod*);
static MonoVTable* (*m_class_vtable)(MonoDomain*, MonoClass*);
static void* (*m_vtable_static_data)(MonoVTable*);
static int32_t (*m_class_instance_size)(MonoClass*);


static MonoDomain* g_domain;
static MonoImage* g_image_game;
static MonoImage* g_image_core;
static MonoImage* g_image_corlib;
static int g_ready;
static int g_resolved;
static int g_attached;
static wchar_t g_logpath[512];


static void logf(const char* fmt, ...) {
    if (!g_logpath[0]) {
        HMODULE self = 0;
        GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           (LPCSTR)(uintptr_t)logf, &self);
        wchar_t mod[512];
        mod[0] = 0;
        GetModuleFileNameW(self, mod, 512);
        wchar_t* slash = wcsrchr(mod, L'\\');
        if (slash) {
            wcsncpy(g_logpath, mod, 511);
            g_logpath[511] = 0;
            wchar_t* p = wcsrchr(g_logpath, L'\\');
            if (p) wcscpy(p + 1, L"bridge.log");
        } else {
            wcscpy(g_logpath, L"bridge.log");
        }
    }
    char msg[2048];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(msg, sizeof(msg), fmt, ap);
    va_end(ap);
    char line[2300];
    snprintf(line, sizeof(line), "[%lu] %s\n", (unsigned long)GetTickCount(), msg);
    FILE* f = _wfopen(g_logpath, L"a");
    if (f) { fputs(line, f); fclose(f); }
    OutputDebugStringA(line);
}


static HMODULE g_mono_mod;

static void* get_export(HMODULE h, const char* name) {
    return (void*)GetProcAddress(h, name);
}

static void resolve_mono() {
    if (g_resolved) return;
    HMODULE m = GetModuleHandleA("mono-2.0-bdwgc.dll");
    if (!m) { logf("resolve_mono: missing exports"); return; }
    g_mono_mod = m;
#define REQ(fn, sym) fn = (typeof(fn))get_export(m, sym)
    REQ(m_domain_get, "mono_domain_get");
    REQ(m_root_domain, "mono_get_root_domain");
    REQ(m_thread_attach, "mono_thread_attach");
    REQ(m_domain_assembly_open, "mono_domain_assembly_open");
    REQ(m_domain_assembly_foreach, "mono_domain_assembly_foreach");
    REQ(m_assembly_get_image, "mono_assembly_get_image");
    REQ(m_image_get_name, "mono_image_get_name");
    REQ(m_class_from_name, "mono_class_from_name");
    REQ(m_class_get_type, "mono_class_get_type");
    REQ(m_class_get_method_from_name, "mono_class_get_method_from_name");
    REQ(m_class_get_field_from_name, "mono_class_get_field_from_name");
    REQ(m_object_new, "mono_object_new");
    REQ(m_compile_method, "mono_compile_method");
    REQ(m_runtime_invoke, "mono_runtime_invoke");
    REQ(m_runtime_invoke_array, "mono_runtime_invoke_array");
    REQ(m_field_get_value, "mono_field_get_value");
    REQ(m_field_set_value, "mono_field_set_value");
    REQ(m_value_box, "mono_value_box");
    REQ(m_object_unbox, "mono_object_unbox");
    REQ(m_array_addr_with_size, "mono_array_addr_with_size");
    REQ(m_array_length, "mono_array_length");
    REQ(m_array_new, "mono_array_new");
    REQ(m_string_new, "mono_string_new");
    REQ(m_image_loaded, "mono_image_loaded");
    REQ(m_assembly_foreach, "mono_assembly_foreach");
    REQ(m_get_corlib, "mono_get_corlib");
    REQ(m_gc_walk_heap, "mono_gc_walk_heap");
    REQ(m_type_get_object, "mono_type_get_object");
    REQ(m_object_get_class, "mono_object_get_class");
    REQ(m_field_get_offset, "mono_field_get_offset");
    REQ(m_field_get_name, "mono_field_get_name");
    REQ(m_class_get_name, "mono_class_get_name");
    REQ(m_class_get_namespace, "mono_class_get_namespace");
    REQ(m_class_get_parent, "mono_class_get_parent");
    REQ(m_class_get_methods, "mono_class_get_methods");
    REQ(m_method_get_name, "mono_method_get_name");
    REQ(m_class_vtable, "mono_class_vtable");
    REQ(m_vtable_static_data, "mono_vtable_get_static_field_data");
    REQ(m_class_instance_size, "mono_class_instance_size");
#undef REQ
    g_resolved = 1;
}


static void log_assembly_cb(MonoAssembly* a, void* ud) {
    MonoImage* img = m_assembly_get_image ? m_assembly_get_image(a) : 0;
    const char* n = img && m_image_get_name ? m_image_get_name(img) : "?";
    logf("  asm: %s", n);
}

static void find_assembly_cb(MonoAssembly* a, void* ud) {
    MonoImage* img = m_assembly_get_image ? m_assembly_get_image(a) : 0;
    const char* n = img && m_image_get_name ? m_image_get_name(img) : "";
    if (!n[0]) return;
    const char* base = strrchr(n, '\\');
    if (base) base++; else base = n;

    if (!g_image_game && strstr(base, "Assembly-CSharp")) g_image_game = img;
    else if (!g_image_core && strstr(base, "UnityEngine.CoreModule")) g_image_core = img;
    else if (!g_image_corlib && strstr(base, "mscorlib")) g_image_corlib = img;
}

static void find_images() {
    if (g_image_game && g_image_core && g_image_corlib) return;
    if (m_image_loaded) {
        if (!g_image_game) g_image_game = m_image_loaded("Assembly-CSharp.dll");
        if (!g_image_core) g_image_core = m_image_loaded("UnityEngine.CoreModule.dll");
        if (!g_image_corlib) g_image_corlib = m_image_loaded("mscorlib.dll");
    }
    if (g_image_game && g_image_core && g_image_corlib) return;
    if (m_assembly_foreach) m_assembly_foreach(find_assembly_cb, 0);
    else if (m_domain_assembly_foreach && g_domain) m_domain_assembly_foreach(g_domain, find_assembly_cb, 0);
    if (!(g_image_game && g_image_core && g_image_corlib)) {
        logf("image lookup failed, listing assemblies:");
        if (m_assembly_foreach) m_assembly_foreach(log_assembly_cb, 0);
    }
}

static void init() {
    logf("init: begin");
    resolve_mono();
    if (!g_resolved) return;
    if (!m_domain_get || !m_root_domain || !m_thread_attach) return;
    g_domain = m_domain_get();
    if (!g_domain) g_domain = m_root_domain();
    if (!g_domain) { logf("no domain"); return; }
    if (!g_attached) {
        logf("init: domain=%p attaching", g_domain);
        m_thread_attach(g_domain);
        g_attached = 1;
    }
    find_images();
    logf("images: game=%p core=%p corlib=%p ready=%d", g_image_game, g_image_core, g_image_corlib,
         g_image_game && g_image_core && g_image_corlib);
    g_ready = (g_image_game && g_image_core && g_image_corlib) ? 1 : 0;
}


static MonoClass* cls(const char* ns, const char* name) {
    if (!g_image_game || !m_class_from_name) return 0;
    return m_class_from_name(g_image_game, ns, name);
}



static MonoClass* cls_dotted(const char* full) {
    const char* dot = strrchr(full, '.');
    if (dot) {
        char ns[96], nm[96];
        int n = (int)(dot - full);
        if (n >= 96) n = 95;
        memcpy(ns, full, n); ns[n] = 0;
        strncpy(nm, dot + 1, 95); nm[95] = 0;
        return cls(ns, nm);
    }
    return cls("", full);
}

static MonoMethod* m_class_get_method(MonoClass* c, const char* name, int args) {
    return (c && m_class_get_method_from_name) ? m_class_get_method_from_name(c, name, args) : 0;
}

static MonoField* m_class_get_field(MonoClass* c, const char* name) {
    return (c && m_class_get_field_from_name) ? m_class_get_field_from_name(c, name) : 0;
}

static MonoObject* box_int(int v) {
    MonoClass* ic = m_get_corlib ? m_class_from_name(m_get_corlib(), "System", "Int32") : 0;
    if (!ic || !m_value_box) return 0;
    return m_value_box(g_domain, ic, &v);
}

static MonoObject* box_float(float v) {
    MonoClass* fc = m_get_corlib ? m_class_from_name(m_get_corlib(), "System", "Single") : 0;
    if (!fc || !m_value_box) return 0;
    return m_value_box(g_domain, fc, &v);
}

static int unbox_i(MonoObject* o) {
    return (o && m_object_unbox) ? *(int*)m_object_unbox(o) : 0;
}

static MonoObject* invoke_boxed(MonoMethod* m, MonoObject* self, MonoObject** args, int n, MonoObject** exc) {
    if (!m || !m_runtime_invoke) return 0;
    if (exc) *exc = 0;
    return m_runtime_invoke(m, self, (void**)args, exc);
}

static void copy_mono_string(MonoString* s, char* out, int outsz) {
    if (!s || !out || outsz <= 0) return;
    int len = *(int*)((char*)s + 16);
    if (len <= 0) { out[0] = 0; return; }
    unsigned short* chars = (unsigned short*)((char*)s + 20);
    int n = WideCharToMultiByte(CP_UTF8, 0, (LPCWCH)chars, len, out, outsz - 1, 0, 0);
    if (n <= 0) { out[0] = 0; return; }
    out[n] = 0;
}


static MonoMethod* find_method_chain(MonoClass* c, const char* name, int args) {
    for (MonoClass* cur = c; cur && m_class_get_parent; cur = m_class_get_parent(cur)) {
        MonoMethod* m = m_class_get_method(cur, name, args);
        if (m) return m;
    }
    return 0;
}


static MonoField* find_field_chain(MonoClass* c, const char* name) {
    for (MonoClass* cur = c; cur; cur = m_class_get_parent ? m_class_get_parent(cur) : 0) {
        MonoField* f = m_class_get_field(cur, name);
        if (f) return f;
    }
    return 0;
}

static MonoObject* volatile g_game_inst = 0;






static MonoObject* world_instance() {
    MonoClass* wc = cls("", "World");
    MonoField* f = wc ? m_class_get_field(wc, "<Instance>k__BackingField") : 0;
    MonoVTable* vt = (wc && m_class_vtable) ? m_class_vtable(g_domain, wc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    return (sd && f) ? *(MonoObject**)((char*)sd + m_field_get_offset(f)) : 0;
}

static void ensure_game_main_thread() {
    MonoImage* corlib = m_get_corlib ? m_get_corlib() : g_image_corlib;
    MonoClass* tc = (corlib && m_class_from_name) ? m_class_from_name(corlib, "System.Threading", "Thread") : 0;
    MonoMethod* getCur = tc ? m_class_get_method(tc, "get_CurrentThread", 0) : 0;
    MonoClass* gc = cls("", "Game");
    MonoField* mtf = gc ? m_class_get_field(gc, "MainThread") : 0;
    if (!getCur || !mtf) return;
    MonoObject* exc = 0;
    MonoObject* cur = m_runtime_invoke(getCur, 0, 0, &exc);
    if (exc || !cur) return;
    MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, gc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    if (!sd) return;
    MonoObject** slot = (MonoObject**)((char*)sd + m_field_get_offset(mtf));
    if (*slot != cur) {
        logf("main: repaired Game.MainThread %p -> %p", *slot, cur);
        *slot = cur;
    }
}

static MonoObject* game_instance() {
    if (!g_game_inst && !world_instance()) return 0;
    MonoClass* gc = cls("", "Game");
    MonoField* f = gc ? m_class_get_field(gc, "<Instance>k__BackingField") : 0;
    MonoVTable* vt = (gc && m_class_vtable) ? m_class_vtable(g_domain, gc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    if (sd && f) {
        MonoObject* g = *(MonoObject**)((char*)sd + m_field_get_offset(f));
        if (!g) g_game_inst = 0;
        return g;
    }
    return g_game_inst;
}

static int trigger_game_event(int hash, MonoObject* data) {
    MonoObject* game = game_instance();
    if (!game) return -1;
    MonoClass* gc = m_object_get_class(game);
    MonoMethod* trigger = gc ? find_method_chain(gc, "Trigger", 2) : 0;
    if (!trigger) return -2;
    void* a[2] = { &hash, data };
    MonoObject* exc = 0;
    m_runtime_invoke(trigger, game, a, &exc);
    if (exc) {
        MonoClass* ec = m_object_get_class(exc);
        logf("trigger_game_event(%d): exc=%s", hash, (ec && m_class_get_name) ? m_class_get_name(ec) : "?");
        return -3;
    }
    return 1;
}


static int resource_id_offset() {
    static int cached = -2;
    if (cached != -2) return cached;
    MonoClass* rc = cls("", "Resource");
    MonoField* f = rc ? m_class_get_field(rc, "Id") : 0;
    cached = (f && m_field_get_offset) ? (int)m_field_get_offset(f) : -1;
    return cached;
}



static MonoObject* get_skills_db() {
    MonoClass* dbc = cls("", "Db");
    if (!dbc) { logf("sm: no Db class"); return 0; }
    MonoMethod* get = m_class_get_method(dbc, "Get", 0);
    if (!get) { logf("sm: no Db.Get"); return 0; }
    MonoObject* exc = 0;
    MonoObject* db = m_runtime_invoke(get, 0, 0, &exc);
    if (exc || !db) { logf("sm: Db.Get failed"); return 0; }
    MonoField* sf = m_class_get_field(dbc, "Skills");
    if (!sf) { logf("sm: no Skills field"); return 0; }
    return *(MonoObject**)((char*)db + m_field_get_offset(sf));
}


static void dupe_model(MonoObject* obj, char* out, int outsz) {
    if (!out || outsz <= 0) return;
    out[0] = 0;
    MonoClass* mc = cls("", "MinionResume");
    MonoClass* mic = cls("", "MinionIdentity");
    if (!mc || !mic) return;
    MonoField* if_ = m_class_get_field(mc, "identity");
    MonoField* mf = m_class_get_field(mic, "model");
    if (!if_ || !mf) return;
    MonoObject* ident = *(MonoObject**)((char*)obj + m_field_get_offset(if_));
    if (!ident) return;
    MonoObject* tag = *(MonoObject**)((char*)ident + m_field_get_offset(mf));
    if (!tag) return;

    MonoClass* tagc = m_object_get_class(tag);
    if (!tagc) return;

    MonoMethod* tostr = find_method_chain(tagc, "ToString", 0);
    if (tostr) {
        MonoObject* exc = 0;
        MonoObject* r = m_runtime_invoke(tostr, tag, 0, &exc);
        if (r && !exc) copy_mono_string((MonoString*)r, out, outsz);
    }
    if (!out[0]) {
        MonoField* nf = m_class_get_field(tagc, "Name");
        if (!nf) nf = m_class_get_field(tagc, "<Name>k__BackingField");
        if (nf) copy_mono_string(*(MonoString**)((char*)tag + m_field_get_offset(nf)), out, outsz);
    }
}


static int skill_registered(MonoObject* skillsDb, const char* id, char* why, int whysz) {
    if (why && whysz > 0) why[0] = 0;
    if (!skillsDb) { if (why) snprintf(why, whysz, "no-db"); return 0; }
    MonoClass* sc = m_object_get_class(skillsDb);
    if (!sc) { if (why) snprintf(why, whysz, "no-class"); return 0; }

    MonoMethod* exists = 0;
    for (MonoClass* c = sc; c && !exists && m_class_get_parent; c = m_class_get_parent(c))
        exists = m_class_get_method(c, "Exists", 1);
    if (!exists) { if (why) snprintf(why, whysz, "no-exists"); return 0; }
    MonoString* s = m_string_new(g_domain, id);
    void* args[1];
    args[0] = s;
    MonoObject* exc = 0;
    MonoObject* r = m_runtime_invoke(exists, skillsDb, args, &exc);
    if (exc) {
        MonoClass* ec = m_object_get_class(exc);
        const char* en = (ec && m_class_get_name) ? m_class_get_name(ec) : "?";
        if (why) snprintf(why, whysz, "exc=%s", en);
        return 0;
    }
    int ok = (r && m_object_unbox && *(unsigned char*)m_object_unbox(r)) ? 1 : 0;
    if (why) snprintf(why, whysz, "%s", ok ? "ok" : "missing");
    return ok;
}


static int modrm_len(const unsigned char* p) {
    unsigned char m = p[0];
    int mod = (m >> 6) & 3, rm = m & 7;
    int len = 1;
    if (mod != 3 && rm == 4) {
        unsigned char sib = p[1];
        len++;
        if (mod == 0 && (sib & 7) == 5) len += 4;
    }
    if (mod == 1) len += 1;
    else if (mod == 2) len += 4;
    else if (mod == 0 && rm == 5) len += 4;
    return len;
}

static int insn_len(const unsigned char* p) {
    const unsigned char* s = p;
    int rex = 0;
    for (;;) {
        unsigned char b = *s;
        if (b == 0x66 || b == 0x67 || b == 0xF2 || b == 0xF3 ||
            b == 0x2E || b == 0x36 || b == 0x3E || b == 0x26 || b == 0x64 || b == 0x65) { ++s; continue; }
        if (b >= 0x40 && b <= 0x4F) { rex = b; ++s; continue; }
        break;
    }
    unsigned char op = *s++;
    int base = (int)(s - p);

    if (op == 0x0F) {
        unsigned char o2 = *s++;
        base++;
        if (o2 == 0x0F) { return base + 1; }
        switch (o2) {
            case 0x80: case 0x81: case 0x82: case 0x83: case 0x84: case 0x85:
            case 0x86: case 0x87: case 0x88: case 0x89: case 0x8A: case 0x8B:
            case 0x8C: case 0x8D: case 0x8E: case 0x8F: case 0x90: case 0x91:
            case 0x92: case 0x93: case 0x94: case 0x95: case 0x96: case 0x97:
            case 0x98: case 0x99: case 0x9A: case 0x9B: case 0x9C: case 0x9D:
            case 0x9E: case 0x9F: case 0xA0: case 0xA1: case 0xA2: case 0xA3:
            case 0xA4: case 0xA5: case 0xA8: case 0xA9: case 0xAA: case 0xAB:
            case 0xAC: case 0xAD: case 0xAE: case 0xAF: case 0xB0: case 0xB1:
            case 0xB2: case 0xB3: case 0xB4: case 0xB5: case 0xB6: case 0xB7:
            case 0xB8: case 0xB9: case 0xBA: case 0xBB: case 0xBC: case 0xBD:
            case 0xBE: case 0xBF: case 0xC0: case 0xC1: case 0xC2: case 0xC3:
            case 0xC4: case 0xC5: case 0xC6: case 0xC7: case 0xC8: case 0xC9:
            case 0xCA: case 0xCB: case 0xCC: case 0xCD: case 0xCE: case 0xCF:
            case 0x10: case 0x11: case 0x28: case 0x29: case 0x2A: case 0x2B:
            case 0x2E: case 0x2F: case 0x51: case 0x54: case 0x55: case 0x57:
            case 0x58: case 0x59: case 0x5A: case 0x5B: case 0x5C: case 0x5D:
            case 0x5E: case 0x5F: case 0x6E: case 0x6F: case 0x7E: case 0x7F:
            case 0xD0: case 0xD1: case 0xD2: case 0xD3: case 0xD4: case 0xD5:
            case 0xD6: case 0xD7: case 0xD8: case 0xD9: case 0xDA: case 0xDB:
            case 0xDC: case 0xDD: case 0xDE: case 0xDF: case 0xE0: case 0xE1:
            case 0xE2: case 0xE3: case 0xE4: case 0xE5: case 0xE6: case 0xE7:
            case 0xE8: case 0xE9: case 0xEA: case 0xEB: case 0xEC: case 0xED:
            case 0xEE: case 0xEF: case 0xF0: case 0xF1: case 0xF2: case 0xF3:
            case 0xF4: case 0xF5: case 0xF6: case 0xF7: case 0xF8: case 0xF9:
            case 0xFA: case 0xFB: case 0xFC: case 0xFD: case 0xFE: case 0xFF:
                return base + modrm_len(s);
        }
        return -1;
    }

    switch (op) {
        case 0x50: case 0x51: case 0x52: case 0x53: case 0x54: case 0x55:
        case 0x56: case 0x57: case 0x58: case 0x59: case 0x5A: case 0x5B:
        case 0x5C: case 0x5D: case 0x5E: case 0x5F:
            return base;
        case 0x68: return base + 4;
        case 0x6A: return base + 1;
        case 0x90: return base;
        case 0xE9: return base + 4;
        case 0xEB: return base + 1;
        case 0xE8: return base + 4;
        case 0xC3: case 0xC2: return base + (op == 0xC3 ? 0 : 2);
        case 0xB8: case 0xB9: case 0xBA: case 0xBB: case 0xBC: case 0xBD:
        case 0xBE: case 0xBF:
            return base + (rex & 8 ? 8 : 4);
        case 0xC7: return base + modrm_len(s) + ((s[0] >> 6) == 1 ? 1 : ((s[0] >> 6) == 2 ? 4 : 0)) + 4;
        case 0x83: case 0x81: return base + modrm_len(s) + ((op == 0x81) ? 4 : 1);
        case 0x80: case 0x82: return base + modrm_len(s) + 1;
        case 0x8B: case 0x89: case 0x8A: case 0x8D: case 0x88: case 0x3B:
        case 0x39: case 0x2B: case 0x29: case 0x01: case 0x03: case 0x33:
        case 0x31: case 0x23: case 0x21: case 0x85: case 0x84: case 0x87:
        case 0x63: case 0x69: case 0x6B: case 0x0F: case 0xFF:
            return base + modrm_len(s) + ((op == 0x69) ? 4 : (op == 0x6B) ? 1 : 0);
        case 0x70: case 0x71: case 0x72: case 0x73: case 0x74: case 0x75:
        case 0x76: case 0x77: case 0x78: case 0x79: case 0x7A: case 0x7B:
        case 0x7C: case 0x7D: case 0x7E: case 0x7F:
            return base + 1;
        case 0x48: case 0x40: case 0x41: case 0x44: case 0x45:
            return -1;
    }
    return -1;
}


typedef struct {
    int type;
    uintptr_t a, b;
    int i1, i2;
    float f1, f2;
    LONG seq;
} QcCmd;

enum {
    QC_TECH_ALL = 1,
    QC_TECH_PATH,
    QC_TECH_QUEUE,
    QC_COMPLETE_QUEUE,
    QC_RESEARCH_COMPLETE,
    QC_RESEARCH_RESET,
    QC_RESEARCH_PATH,
    QC_RESEARCH_ALL,
    QC_REFRESH_PLAN,
    QC_SKILL_ALL,
    QC_SPAWN,
    QC_SPEED,
    QC_NOTIFY,
    QC_STRESS,
    QC_MORALE,
    QC_MORALE_RESUME,
    QC_MORALE_EXP,
    QC_REVEAL,
    QC_CLEANUP,
};

static volatile float g_stress_cap = -1.f;
static int g_frame = 0;
static int set_stress_cap(float cap);
static int set_morale_floor(float floor);
static int set_morale_exp_cap(float cap);
static int clamp_stress_all(float cap);
static int morale_resume(MonoObject* obj, float floor);
static int swap_dict(MonoObject* obj);
static int feature_put(int cell, int hash, float mass, float temp);
static int feature_speed(int speed);
static int queue_tech(MonoObject* tech);
static int complete_research_queue();
static int research_complete(MonoObject* ti, MonoObject* tech);
static int research_reset();
static int research_complete_path(MonoObject* tech);
static int research_complete_all();
static int refresh_plan();
static int reveal_all();
static void* g_game_update_tramp = 0;

#define QC_CAP 512
static QcCmd g_qc[QC_CAP];
static volatile LONG g_qc_head = 0;
static volatile LONG g_qc_tail = 0;
static HANDLE g_cmd_done_evt = 0;
static volatile LONG g_cmd_seq = 0;
static volatile LONG g_cmd_done_seq = 0;
static volatile int g_cmd_result = 0;

static int qc_enqueue(QcCmd c) {
    LONG h = g_qc_head, t = g_qc_tail;
    if (h - t >= QC_CAP) return 0;
    g_qc[h % QC_CAP] = c;
    InterlockedExchange(&g_qc_head, h + 1);
    return 1;
}

static int qc_dequeue(QcCmd* c) {
    LONG t = g_qc_tail;
    if (g_qc_head == t) return 0;
    *c = g_qc[t % QC_CAP];
    InterlockedExchange(&g_qc_tail, t + 1);
    return 1;
}



static int qc_run_sync(QcCmd c, int timeoutMs, int* outRes) {
    if (outRes) *outRes = 0;
    if (!g_game_update_tramp) return -1;
    if (!game_instance()) return -2;
    if (!g_cmd_done_evt) g_cmd_done_evt = CreateEventA(0, TRUE, FALSE, 0);
    LONG seq = InterlockedIncrement(&g_cmd_seq);
    if (seq == 0) seq = InterlockedIncrement(&g_cmd_seq);
    c.seq = seq;
    if (g_cmd_done_evt) ResetEvent(g_cmd_done_evt);
    if (!qc_enqueue(c)) return -3;

    DWORD start = GetTickCount();
    for (;;) {
        if (InterlockedCompareExchange(&g_cmd_done_seq, 0, 0) == seq) {
            if (outRes) *outRes = g_cmd_result;
            return 1;
        }
        DWORD elapsed = GetTickCount() - start;
        if ((int)elapsed >= timeoutMs) return 0;
        DWORD rem = (DWORD)(timeoutMs - (int)elapsed);
        if (g_cmd_done_evt) {
            WaitForSingleObject(g_cmd_done_evt, rem > 50 ? 50 : rem);
            if (InterlockedCompareExchange(&g_cmd_done_seq, 0, 0) != seq)
                ResetEvent(g_cmd_done_evt);
        } else {
            Sleep(5);
        }
    }
}







static MonoField* g_f_mod_amounts;
static MonoField* g_f_mod_attributes;
static MonoMethod* g_m_amounts_setvalue;
static MonoMethod* g_m_attrs_get;
static MonoMethod* g_m_attrinst_add;
static MonoMethod* g_m_attrmod_ctor;
static float g_morale_mod = 0.f;

static int resolve_stress_morale() {
    if (g_m_amounts_setvalue && g_m_attrs_get) return 1;
    MonoClass* modc = cls("Klei.AI", "Modifiers");
    if (!modc) { logf("sm: no Modifiers class"); return 0; }
    g_f_mod_amounts = m_class_get_field(modc, "amounts");
    g_f_mod_attributes = m_class_get_field(modc, "attributes");
    if (!g_f_mod_amounts || !g_f_mod_attributes) { logf("sm: no Modifiers.amounts/attributes"); return 0; }
    MonoClass* amc = cls("Klei.AI", "Amounts");
    g_m_amounts_setvalue = amc ? m_class_get_method(amc, "SetValue", 2) : 0;
    MonoClass* atc = cls("Klei.AI", "Attributes");
    g_m_attrs_get = atc ? m_class_get_method(atc, "Get", 1) : 0;
    MonoClass* atic = cls("Klei.AI", "AttributeInstance");
    g_m_attrinst_add = atic ? m_class_get_method(atic, "Add", 1) : 0;
    MonoClass* amodc = cls("Klei.AI", "AttributeModifier");
    g_m_attrmod_ctor = amodc ? m_class_get_method(amodc, ".ctor", 6) : 0;
    if (!g_m_amounts_setvalue) { logf("sm: no Amounts.SetValue"); return 0; }
    if (!g_m_attrs_get || !g_m_attrinst_add || !g_m_attrmod_ctor) { logf("sm: morale path incomplete"); }
    logf("sm: resolved ok=1 amounts_set=%p attrs_get=%p", g_m_amounts_setvalue, g_m_attrs_get);
    return 1;
}


static MonoObject* g_minions[512];
static int g_minion_count = 0;

static int collect_live_minions() {
    g_minion_count = 0;
    MonoClass* cc = cls("", "Components");
    if (!cc) return -1;
    MonoField* f = m_class_get_field(cc, "LiveMinionIdentities");
    if (!f) return -2;
    MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, cc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    if (!sd) return -3;
    void* cache = *(void**)((char*)sd + m_field_get_offset(f));
    if (!cache) return -4;
    void* hls = *(void**)((char*)cache + 16);
    if (!hls) return -5;
    void* list = *(void**)((char*)hls + 24);
    if (!list) return -6;
    void* arr = *(void**)((char*)list + 16);
    int n = *(int*)((char*)list + 24);
    if (!arr || n < 0 || n > 512) return -7;
    void* data = (char*)arr + 32;
    for (int i = 0; i < n && i < 512; ++i)
        g_minions[g_minion_count++] = *(MonoObject**)((char*)data + i * 8);
    return g_minion_count;
}


static MonoObject* minion_modifiers(MonoObject* identity) {
    MonoClass* mic = m_object_get_class(identity);
    MonoField* mf = m_class_get_field(mic, "modifiers");
    if (!mf) return 0;
    return *(MonoObject**)((char*)identity + m_field_get_offset(mf));
}

static int clamp_stress_all(float cap) {
    if (!resolve_stress_morale()) return -1;
    int n = collect_live_minions();
    int clamped = 0;
    MonoString* sid = m_string_new(g_domain, "Stress");
    for (int i = 0; i < n; ++i) {
        MonoObject* mods = minion_modifiers(g_minions[i]);
        if (!mods) continue;
        MonoObject* amts = *(MonoObject**)((char*)mods + m_field_get_offset(g_f_mod_amounts));
        if (!amts) continue;
        void* a[2]; a[0] = sid; a[1] = &cap;
        MonoObject* exc = 0;
        m_runtime_invoke(g_m_amounts_setvalue, amts, a, &exc);
        if (!exc) ++clamped;
    }
    return clamped;
}

static int set_stress_cap(float cap) {
    g_stress_cap = cap;
    return 0;
}







static MonoObject* g_morale_applied[512];
static int g_morale_applied_n = 0;

static void morale_track(MonoObject* resume) {
    for (int i = 0; i < g_morale_applied_n; ++i)
        if (g_morale_applied[i] == resume) return;
    if (g_morale_applied_n < 512)
        g_morale_applied[g_morale_applied_n++] = resume;
}


static int set_morale_floor(float floor) {
    g_morale_mod = floor < 0.f ? 0.f : floor;
    if (floor >= 0.f) return 0;
    int n = 0;
    for (int i = 0; i < g_morale_applied_n; ++i)
        if (morale_resume(g_morale_applied[i], -1.f) == 1) ++n;
    g_morale_applied_n = 0;
    return n;
}





static int morale_resume(MonoObject* obj, float floor) {
    logf("morale_resume: enter obj=%p floor=%.1f", obj, floor);
    MonoClass* rc = cls("", "MinionResume");
    if (!rc || !obj) return -1;
    MonoObject* exc = 0;


    MonoClass* dbc = cls("", "Db");
    MonoMethod* get = dbc ? m_class_get_method(dbc, "Get", 0) : 0;
    if (!get) return -2;
    MonoObject* db = m_runtime_invoke(get, 0, 0, &exc);
    if (exc || !db) return -3;
    MonoField* af = m_class_get_field(dbc, "Attributes");
    if (!af) return -4;
    MonoObject* attrs = *(MonoObject**)((char*)db + m_field_get_offset(af));
    if (!attrs) return -5;
    MonoClass* atc = m_object_get_class(attrs);
    MonoField* qf = atc ? m_class_get_field(atc, "QualityOfLife") : 0;
    if (!qf) return -6;
    MonoObject* qol = *(MonoObject**)((char*)attrs + m_field_get_offset(qf));
    if (!qol) return -7;
    MonoClass* qc = m_object_get_class(qol);
    MonoMethod* lookup = qc ? m_class_get_method(qc, "Lookup", 1) : 0;
    if (!lookup) return -8;
    void* la[1]; la[0] = obj;
    exc = 0;
    MonoObject* inst = m_runtime_invoke(lookup, qol, la, &exc);
    if (exc || !inst) { logf("morale_resume: lookup failed exc=%d", exc != 0); return -9; }

    MonoClass* aic = cls("Klei.AI", "AttributeInstance");
    MonoMethod* rem = aic ? m_class_get_method(aic, "Remove", 1) : 0;
    MonoMethod* add = aic ? m_class_get_method(aic, "Add", 1) : 0;
    if (!rem || !add) return -10;



    MonoClass* amc = cls("Klei.AI", "AttributeModifier");
    MonoField* mfs = aic ? m_class_get_field(aic, "Modifiers") : 0;
    MonoField* dfs = amc ? m_class_get_field(amc, "Description") : 0;
    if (mfs && dfs) {
        MonoObject* list = *(MonoObject**)((char*)inst + m_field_get_offset(mfs));
        if (list) {
            void* arr = *(void**)((char*)list + 16);
            int n = *(int*)((char*)list + 24);
            if (arr && n > 0 && n < 4096) {
                MonoObject* stale[64]; int ns = 0;
                int doff = (int)m_field_get_offset(dfs);
                for (int i = 0; i < n && ns < 64; ++i) {
                    MonoObject* mod = *(MonoObject**)((char*)arr + 32 + i * 8);
                    if (!mod) continue;
                    char desc[64]; desc[0] = 0;
                    copy_mono_string(*(MonoString**)((char*)mod + doff), desc, sizeof(desc));
                    if (!strcmp(desc, "Trainer")) stale[ns++] = mod;
                }
                for (int i = 0; i < ns; ++i) {
                    void* ra[1]; ra[0] = stale[i];
                    exc = 0;
                    m_runtime_invoke(rem, inst, ra, &exc);
                }
                if (ns) logf("morale_resume: removed %d old Trainer mod(s)", ns);
            }
        }
    }


    if (floor <= 0.f) return 1;









    MonoString* sid = m_string_new(g_domain, "QualityOfLife");
    MonoString* snm = m_string_new(g_domain, "Trainer");
    MonoObject* mod = (amc && m_object_new) ? m_object_new(g_domain, amc) : 0;
    if (!mod) return -12;
    MonoField* f_id = m_class_get_field(amc, "<AttributeId>k__BackingField");
    MonoField* f_val = m_class_get_field(amc, "<Value>k__BackingField");
    MonoField* f_desc = m_class_get_field(amc, "Description");
    MonoField* f_ro = m_class_get_field(amc, "<IsReadonly>k__BackingField");
    if (!f_id || !f_val || !f_desc || !f_ro) return -12;
    *(MonoObject**)((char*)mod + m_field_get_offset(f_id)) = (MonoObject*)sid;
    *(float*)((char*)mod + m_field_get_offset(f_val)) = floor;
    *(MonoObject**)((char*)mod + m_field_get_offset(f_desc)) = (MonoObject*)snm;
    *(unsigned char*)((char*)mod + m_field_get_offset(f_ro)) = 1;
    void* aa[1]; aa[0] = mod;
    exc = 0;
    m_runtime_invoke(add, inst, aa, &exc);
    if (exc) {
        MonoClass* ec = m_object_get_class(exc);
        logf("morale_resume: add failed exc=%s", (ec && m_class_get_name) ? m_class_get_name(ec) : "?");
        return -13;
    }
    morale_track(obj);
    logf("morale_resume: done obj=%p ok=1", obj);
    return 1;
}








static float g_orig_morale_exp_upper = 0.f;
static int g_morale_exp_saved = 0;

static int set_morale_exp_cap(float cap) {
    MonoClass* dbc = cls("", "Db");
    MonoMethod* get = dbc ? m_class_get_method(dbc, "Get", 0) : 0;
    if (!get) return -1;
    MonoObject* exc = 0;
    MonoObject* db = m_runtime_invoke(get, 0, 0, &exc);
    if (exc || !db) return -2;
    MonoField* af = find_field_chain(dbc, "Attributes");
    if (!af) return -3;
    MonoObject* attrs = *(MonoObject**)((char*)db + m_field_get_offset(af));
    if (!attrs) return -4;
    MonoClass* atc = m_object_get_class(attrs);
    MonoField* qef = atc ? m_class_get_field(atc, "QualityOfLifeExpectation") : 0;
    if (!qef) return -5;
    MonoObject* qolExp = *(MonoObject**)((char*)attrs + m_field_get_offset(qef));
    if (!qolExp) return -6;
    MonoClass* ac = m_object_get_class(qolExp);
    MonoField* ubf = ac ? find_field_chain(ac, "TotalValueUpperBound") : 0;
    if (!ubf) return -7;
    float* ubp = (float*)((char*)qolExp + m_field_get_offset(ubf));
    if (!g_morale_exp_saved) {
        g_orig_morale_exp_upper = *ubp;
        g_morale_exp_saved = 1;
    }
    float target = (cap < 0.f) ? g_orig_morale_exp_upper : cap;
    *ubp = target;



    int n = collect_live_minions();
    MonoMethod* lookup = ac ? find_method_chain(ac, "Lookup", 1) : 0;
    MonoClass* aic = cls("Klei.AI", "AttributeInstance");
    MonoField* odf = aic ? m_class_get_field(aic, "OnDirty") : 0;
    MonoMethod* invokeAct = 0;
    int notified = 0;
    if (lookup && odf && n > 0) {
        int odoff = (int)m_field_get_offset(odf);
        for (int i = 0; i < n; ++i) {
            if (!g_minions[i]) continue;
            void* la[1] = { g_minions[i] };
            exc = 0;
            MonoObject* inst = m_runtime_invoke(lookup, qolExp, la, &exc);
            if (!inst || exc) continue;
            MonoObject* cb = *(MonoObject**)((char*)inst + odoff);
            if (cb) {
                if (!invokeAct) invokeAct = m_class_get_method(m_object_get_class(cb), "Invoke", 0);
                if (invokeAct) { exc = 0; m_runtime_invoke(invokeAct, cb, 0, &exc); }
            }
            ++notified;
        }
    }


    MonoClass* mmc = cls("", "ManagementMenu");
    if (mmc) {
        MonoField* inf = m_class_get_field(mmc, "Instance");
        MonoField* ssf = m_class_get_field(mmc, "skillsScreen");
        MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, mmc) : 0;
        void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
        MonoObject* mm = (sd && inf) ? *(MonoObject**)((char*)sd + m_field_get_offset(inf)) : 0;
        MonoObject* ss = (mm && ssf) ? *(MonoObject**)((char*)mm + m_field_get_offset(ssf)) : 0;
        if (ss) {
            MonoField* df = m_class_get_field(m_object_get_class(ss), "dirty");
            if (df) *(unsigned char*)((char*)ss + m_field_get_offset(df)) = 1;
        }
    }

    logf("set_morale_exp_cap: cap=%.1f target=%.1f notified=%d", cap, target, notified);
    return 1;
}


static int feature_put(int cell, int hash, float mass, float temp) {
    MonoClass* simcls = cls("", "SimMessages");
    if (!simcls) return -1;
    MonoMethod* replace = m_class_get_method(simcls, "ReplaceElement", 8);
    if (!replace) return -2;
    void* ev = 0;
    unsigned char disease = 255;
    int diseaseCount = 0, callbackIdx = -1;
    void* args[8];
    args[0] = &cell;
    args[1] = &hash;
    args[2] = &ev;
    args[3] = &mass;
    args[4] = &temp;
    args[5] = &disease;
    args[6] = &diseaseCount;
    args[7] = &callbackIdx;
    MonoObject* exc = 0;
    m_runtime_invoke(replace, 0, args, &exc);
    return exc ? -3 : 1;
}


static int feature_speed(int speed) {
    if (speed < 1) speed = 1;
    if (speed > 100) speed = 100;
    MonoClass* spd = cls("", "SpeedControlScreen");
    MonoMethod* setspd = m_class_get_method(spd, "SetSpeed", 1);
    MonoMethod* getinst = m_class_get_method(spd, "get_Instance", 0);
    if (setspd && getinst) {
        MonoObject* exc = 0;
        MonoObject* inst = m_runtime_invoke(getinst, 0, 0, &exc);
        if (inst && !exc) {
            int sv = speed;
            void* a[1] = { &sv };
            m_runtime_invoke(setspd, inst, a, &exc);
        }
    }

    MonoClass* timecls = m_class_from_name(g_image_core, "UnityEngine", "Time");
    MonoMethod* setts = m_class_get_method(timecls, "set_timeScale", 1);
    if (setts) {
        float ts = (float)speed;
        void* a[1]; a[0] = &ts;
        MonoObject* exc = 0;
        m_runtime_invoke(setts, 0, a, &exc);
    }
    return speed;
}



static int unlock_tech_chain(MonoObject* tech) {
    if (!tech) return 0;
    MonoClass* tc = cls("", "Tech");
    MonoClass* tic = cls("", "TechInstance");
    MonoClass* rc = cls("", "Research");
    if (!tc || !tic || !rc) return 0;
    MonoField* idf = m_class_get_field(tc, "Id");
    int idoff = idf ? (int)m_field_get_offset(idf) : -1;
    MonoField* rqf = m_class_get_field(tc, "requiredTech");
    int reqoff = rqf ? (int)m_field_get_offset(rqf) : -1;
    MonoField* cpf = m_class_get_field(tic, "complete");
    int compoff = cpf ? (int)m_field_get_offset(cpf) : -1;
    MonoMethod* getti = m_class_get_method(rc, "GetTechInstance", 1);
    MonoField* inf = m_class_get_field(rc, "Instance");
    MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, rc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    MonoObject* research = (sd && inf) ? *(MonoObject**)((char*)sd + m_field_get_offset(inf)) : 0;
    if (!research || !getti || reqoff < 0 || compoff < 0) return 0;

    MonoObject* stack[512];
    MonoObject* visited[512];
    int sp = 0, vn = 0, count = 0;
    stack[sp++] = tech;
    while (sp > 0) {
        MonoObject* t = stack[--sp];
        if (!t) continue;
        int seen = 0;
        for (int i = 0; i < vn; ++i) if (visited[i] == t) { seen = 1; break; }
        if (seen) continue;
        if (vn < 512) visited[vn++] = t;

        char id[96];
        id[0] = 0;
        if (idoff >= 0)
            copy_mono_string(*(MonoString**)((char*)t + idoff), id, sizeof(id));
        if (id[0]) {
            MonoString* s = m_string_new(g_domain, id);
            void* args[1];
            args[0] = s;
            MonoObject* exc = 0;
            MonoObject* ti = m_runtime_invoke(getti, research, args, &exc);
            if (ti && !exc) {
                unsigned char* cp = (unsigned char*)((char*)ti + compoff);
                if (*cp != 1) { *cp = 1; ++count; }
            }
        }

        MonoObject* list = *(MonoObject**)((char*)t + reqoff);
        if (list) {
            void* arr = *(void**)((char*)list + 16);
            int n = *(int*)((char*)list + 24);
            if (arr && n > 0 && n < 256) {
                void* data = (char*)arr + 32;
                for (int i = 0; i < n && sp < 512; ++i)
                    stack[sp++] = *(MonoObject**)((char*)data + i * 8);
            }
        }
    }
    return count;
}


static int feature_tech_all() {
    MonoClass* tc = cls("", "TechInstance");
    MonoClass* rc = cls("", "Research");
    if (!tc || !rc) { logf("tech: class not found"); return -1; }
    MonoField* inf = m_class_get_field(rc, "Instance");
    MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, rc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    if (!sd || !inf) { logf("tech: Research.Instance null"); return -2; }
    MonoObject* research = *(MonoObject**)((char*)sd + m_field_get_offset(inf));
    if (!research) { logf("tech: Research.Instance null"); return -2; }
    MonoField* tf = m_class_get_field(rc, "techs");
    if (!tf) { logf("tech: no techs field"); return -3; }
    MonoObject* dict = *(MonoObject**)((char*)research + m_field_get_offset(tf));
    if (!dict) { logf("tech: techs null"); return -4; }
    MonoClass* dc = m_object_get_class(dict);
    MonoMethod* gv = m_class_get_method(dc, "get_Values", 0);
    if (!gv) { logf("tech: no get_Values"); return -5; }
    MonoObject* exc = 0;
    MonoObject* vals = m_runtime_invoke(gv, dict, 0, &exc);
    if (exc || !vals) { logf("tech: get_Values failed"); return -6; }
    MonoClass* vc = m_object_get_class(vals);
    MonoMethod* gc = m_class_get_method(vc, "get_Count", 0);
    MonoObject* cnt = gc ? m_runtime_invoke(gc, vals, 0, &exc) : 0;
    int n = (cnt && !exc) ? *(int*)m_object_unbox(cnt) : 0;
    logf("tech: techs count=%d", n);
    if (n <= 0) return 0;
    MonoMethod* ct = m_class_get_method(vc, "CopyTo", 1);
    if (!ct) { logf("tech: no CopyTo"); return -7; }
    MonoClass* ac = m_class_from_name(m_get_corlib(), "System", "Array");

    MonoClass* arrc = m_class_from_name(m_get_corlib(), "System", "Array");
    MonoMethod* ci = arrc ? m_class_get_method(arrc, "CreateInstance", 2) : 0;
    MonoObject* arr = 0;
    if (ci) {
        MonoClass* tec = cls("", "Tech");
        MonoType* tt = tec ? m_class_get_type(tec) : 0;
        MonoObject* to = (tt && m_type_get_object) ? m_type_get_object(g_domain, tt) : 0;
        if (to) {
            void* a[2]; a[0] = to; a[1] = &n;
            MonoObject* e2 = 0;
            arr = m_runtime_invoke(ci, 0, a, &e2);
        }
    }
    if (!arr) { logf("tech: CopyTo exception"); return -8; }
    void* a1[1]; a1[0] = arr;
    exc = 0;
    m_runtime_invoke(ct, vals, a1, &exc);
    if (exc) { logf("tech: CopyTo exception"); return -9; }

    MonoField* cpf = m_class_get_field(tc, "complete");
    int compoff = cpf ? (int)m_field_get_offset(cpf) : -1;
    MonoField* tef = m_class_get_field(tc, "tech");
    int teoff = tef ? (int)m_field_get_offset(tef) : -1;
    int unlocked = 0;
    for (int i = 0; i < n; ++i) {
        MonoObject* tech = *(MonoObject**)((char*)arr + 32 + i * 8);
        if (!tech) continue;
        MonoMethod* getti = m_class_get_method(rc, "GetTechInstance", 1);
        MonoObject* exc2 = 0;
        MonoObject* ti = getti ? m_runtime_invoke(getti, research, (void**)&tech, &exc2) : 0;
        if (!ti || exc2) {

            if (teoff >= 0) ti = *(MonoObject**)((char*)tech + teoff);
        }
        if (ti && compoff >= 0) {
            unsigned char* cp = (unsigned char*)((char*)ti + compoff);
            if (*cp != 1) { *cp = 1; ++unlocked; }
        }
    }
    logf("tech: research dict unlock=%d", unlocked);
    return unlocked;
}




static MonoObject* research_instance() {
    MonoClass* rc = cls("", "Research");
    MonoField* inf = rc ? m_class_get_field(rc, "Instance") : 0;
    MonoVTable* vt = rc && m_class_vtable ? m_class_vtable(g_domain, rc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    if (!sd || !inf) return 0;
    MonoObject* r = *(MonoObject**)((char*)sd + m_field_get_offset(inf));
    return r;
}

static int queue_tech(MonoObject* tech) {
    if (!tech) return 0;
    MonoClass* rc = cls("", "Research");
    MonoMethod* add = rc ? m_class_get_method(rc, "AddTechToQueue", 1) : 0;
    MonoObject* research = research_instance();
    if (!add || !research) return -1;


    MonoMethod* gti = rc ? m_class_get_method(rc, "GetTechInstance", 1) : 0;
    if (gti) {
        void* ta[1] = { tech };
        MonoObject* exc = 0;
        MonoObject* ti = m_runtime_invoke(gti, research, ta, &exc);
        MonoClass* tc = cls("", "TechInstance");
        MonoField* cf = tc ? m_class_get_field(tc, "complete") : 0;
        if (ti && !exc && cf) *(unsigned char*)((char*)ti + m_field_get_offset(cf)) = 0;
    }
    void* a[1] = { tech };
    MonoObject* exc = 0;
    m_runtime_invoke(add, research, a, &exc);
    return exc ? -2 : 1;
}

static int complete_research_queue() {
    MonoClass* rc = cls("", "Research");
    MonoMethod* done = rc ? m_class_get_method(rc, "CompleteQueue", 0) : 0;
    MonoObject* research = research_instance();
    if (!done || !research) return -1;
    MonoObject* exc = 0;
    m_runtime_invoke(done, research, 0, &exc);
    return exc ? -2 : 1;
}

static int research_reset() {
    MonoClass* rc = cls("", "Research");
    MonoMethod* set = rc ? m_class_get_method(rc, "SetActiveResearch", 2) : 0;
    MonoObject* research = research_instance();
    if (!set || !research) return -1;
    unsigned char clear = 1;
    void* a[2] = { 0, &clear };
    MonoObject* exc = 0;
    m_runtime_invoke(set, research, a, &exc);
    return exc ? -2 : 1;
}




static int tech_has_valid_id(MonoObject* tech) {
    if (!tech) return 0;
    int idoff = resource_id_offset();
    if (idoff < 0) return 1;
    MonoString* s = *(MonoString**)((char*)tech + idoff);
    if (!s) return 0;
    int len = *(int*)((char*)s + 16);
    return (len > 0 && len < 256) ? 1 : 0;
}

static MonoObject* research_screen_instance() {
    MonoClass* mmc = cls("", "ManagementMenu");
    if (!mmc) return 0;
    MonoField* inf = m_class_get_field(mmc, "Instance");
    MonoField* rsf = m_class_get_field(mmc, "researchScreen");
    MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, mmc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    if (!sd || !inf || !rsf) return 0;
    MonoObject* mm = *(MonoObject**)((char*)sd + m_field_get_offset(inf));
    if (!mm) return 0;
    return *(MonoObject**)((char*)mm + m_field_get_offset(rsf));
}



static MonoObject* current_selected_tech() {

    MonoObject* rscreen = research_screen_instance();
    if (rscreen) {
        MonoClass* rsc = cls("", "ResearchScreen");
        MonoField* crf = rsc ? m_class_get_field(rsc, "currentResearch") : 0;
        if (crf) {
            MonoObject* t = *(MonoObject**)((char*)rscreen + m_field_get_offset(crf));
            if (tech_has_valid_id(t)) return t;
        }
    }

    MonoObject* research = research_instance();
    MonoClass* rc = cls("", "Research");
    MonoClass* tic = cls("", "TechInstance");
    MonoField* tf = tic ? m_class_get_field(tic, "tech") : 0;
    if (research && rc && tf) {
        int toff = (int)m_field_get_offset(tf);
        MonoField* qf = m_class_get_field(rc, "queuedTech");
        if (qf) {
            MonoObject* list = *(MonoObject**)((char*)research + m_field_get_offset(qf));
            if (list) {
                void* arr = *(void**)((char*)list + 16);
                int n = *(int*)((char*)list + 24);
                if (arr && n > 0 && n < 512) {
                    MonoObject* lastTi = *(MonoObject**)((char*)arr + 32 + (n - 1) * 8);
                    if (lastTi) {
                        MonoObject* t = *(MonoObject**)((char*)lastTi + toff);
                        if (tech_has_valid_id(t)) return t;
                    }
                }
            }
        }
        MonoField* af = m_class_get_field(rc, "activeResearch");
        if (af) {
            MonoObject* act = *(MonoObject**)((char*)research + m_field_get_offset(af));
            if (act) {
                MonoObject* t = *(MonoObject**)((char*)act + toff);
                if (tech_has_valid_id(t)) return t;
            }
        }
    }
    return 0;
}







static int research_complete(MonoObject* ti, MonoObject* tech) {
    if (!tech_has_valid_id(tech)) return 0;
    (void)ti;
    MonoClass* tc = cls("", "TechInstance");
    MonoField* cf = tc ? m_class_get_field(tc, "complete") : 0;
    MonoObject* research = research_instance();
    MonoClass* rc = cls("", "Research");
    MonoMethod* gor = rc ? m_class_get_method(rc, "GetOrAdd", 1) : 0;
    if (!cf || !research || !gor) return -1;



    void* ga[1] = { tech };
    MonoObject* exc = 0;
    MonoObject* live = m_runtime_invoke(gor, research, ga, &exc);
    if (!live || exc) return -3;
    unsigned char* cp = (unsigned char*)((char*)live + m_field_get_offset(cf));
    int was_complete = (*cp != 0);
    *cp = 1;
    if (!was_complete) {
        int tr = trigger_game_event(-107300940, tech);
        if (tr < 0) return -2;
    }
    return 1;
}

static int research_complete_path_inner(MonoObject* tech, MonoObject** visited, int* vn, int depth) {
    if (!tech || depth > 64) return 0;
    for (int i = 0; i < *vn; ++i)
        if (visited[i] == tech) return 0;
    if (*vn < 256) visited[(*vn)++] = tech;

    MonoClass* tc = cls("", "Tech");
    MonoField* rf = tc ? m_class_get_field(tc, "requiredTech") : 0;
    int count = 0;
    if (rf) {
        MonoObject* list = *(MonoObject**)((char*)tech + m_field_get_offset(rf));
        if (list) {
            void* arr = *(void**)((char*)list + 16);
            int n = *(int*)((char*)list + 24);
            if (arr && n > 0 && n < 256) {
                for (int i = 0; i < n; ++i) {
                    MonoObject* req = *(MonoObject**)((char*)arr + 32 + i * 8);
                    count += research_complete_path_inner(req, visited, vn, depth + 1);
                }
            }
        }
    }
    if (research_complete(0, tech) == 1) ++count;
    return count;
}

static int research_complete_path(MonoObject* tech) {
    if (!tech_has_valid_id(tech)) return -1;
    MonoObject* visited[256];
    int vn = 0;
    int count = research_complete_path_inner(tech, visited, &vn, 0);
    research_reset();


    trigger_game_event(-107300940, 0);
    refresh_plan();
    return count;
}




static int research_complete_all() {
    MonoClass* dbc = cls("", "Db");
    MonoMethod* dbget = dbc ? m_class_get_method(dbc, "Get", 0) : 0;
    if (!dbget) return -1;
    MonoObject* exc = 0;
    MonoObject* db = m_runtime_invoke(dbget, 0, 0, &exc);
    if (exc || !db) return -2;
    MonoField* tsf = m_class_get_field(dbc, "Techs");
    MonoObject* techsDb = tsf ? *(MonoObject**)((char*)db + m_field_get_offset(tsf)) : 0;
    if (!techsDb) return -3;
    MonoField* resf = find_field_chain(m_object_get_class(techsDb), "resources");
    MonoObject* list = resf ? *(MonoObject**)((char*)techsDb + m_field_get_offset(resf)) : 0;
    if (!list) return -4;
    void* arr = *(void**)((char*)list + 16);
    int n = *(int*)((char*)list + 24);
    if (!arr || n <= 0 || n > 1024) return -5;

    MonoObject* research = research_instance();
    MonoClass* rc = cls("", "Research");
    MonoClass* tic = cls("", "TechInstance");
    MonoMethod* gor = rc ? m_class_get_method(rc, "GetOrAdd", 1) : 0;
    MonoField* cf = tic ? m_class_get_field(tic, "complete") : 0;
    if (!research || !gor || !cf) return -6;
    int compoff = (int)m_field_get_offset(cf);

    research_reset();



    MonoObject* rscreen = research_screen_instance();
    MonoClass* rsc = cls("", "ResearchScreen");
    MonoMethod* getEntry = (rscreen && rsc) ? m_class_get_method(rsc, "GetEntry", 1) : 0;
    MonoClass* rec = cls("", "ResearchEntry");
    MonoMethod* entryDone = rec ? m_class_get_method(rec, "ResearchCompleted", 1) : 0;

    int total = 0, newly = 0;
    for (int i = 0; i < n; ++i) {
        MonoObject* tech = *(MonoObject**)((char*)arr + 32 + i * 8);
        if (!tech_has_valid_id(tech)) continue;
        void* ga[1] = { tech };
        exc = 0;
        MonoObject* live = m_runtime_invoke(gor, research, ga, &exc);
        if (!live || exc) continue;
        unsigned char* cp = (unsigned char*)((char*)live + compoff);
        if (*cp == 0) ++newly;
        *cp = 1;
        ++total;

        if (rscreen && getEntry && entryDone) {
            void* ea[1] = { tech };
            exc = 0;
            MonoObject* entry = m_runtime_invoke(getEntry, rscreen, ea, &exc);
            if (entry && !exc) {
                unsigned char notify = 0;
                void* ba[1] = { &notify };
                exc = 0;
                m_runtime_invoke(entryDone, entry, ba, &exc);
            }
        }
    }

    if (rscreen && rsc) {
        MonoMethod* upb = m_class_get_method(rsc, "UpdateProgressBars", 0);
        MonoMethod* upd = m_class_get_method(rsc, "UpdatePointDisplay", 0);
        if (upb) { exc = 0; m_runtime_invoke(upb, rscreen, 0, &exc); }
        if (upd) { exc = 0; m_runtime_invoke(upd, rscreen, 0, &exc); }
    }






    int tr = trigger_game_event(-107300940, 0);
    int rp = refresh_plan();
    logf("research_complete_all: total=%d newly=%d trigger=%d refresh_plan=%d", total, newly, tr, rp);
    return total;
}


static int notify_tech(MonoObject* screen, MonoObject* tech) {
    if (!tech) return 0;
    int ok = 0;
    MonoClass* rsc = cls("", "ResearchScreen");
    MonoMethod* ge = rsc ? m_class_get_method(rsc, "GetEntry", 1) : 0;
    if (ge && screen) {
        void* a[1]; a[0] = tech;
        MonoObject* exc = 0;
        MonoObject* entry = m_runtime_invoke(ge, screen, a, &exc);
        if (entry && !exc) {
            MonoClass* ec = m_object_get_class(entry);
            MonoMethod* rc = m_class_get_method(ec, "ResearchCompleted", 1);
            if (rc) {
                unsigned char f = 0;
                void* b[1]; b[0] = &f;
                exc = 0;
                m_runtime_invoke(rc, entry, b, &exc);
                ++ok;
            }
        }
    }
    MonoClass* psc = cls("", "PlanScreen");
    MonoMethod* orc = psc ? m_class_get_method(psc, "OnResearchComplete", 1) : 0;
    if (orc) {
        void* a[1]; a[0] = tech;
        MonoObject* exc = 0;
        m_runtime_invoke(orc, 0, a, &exc);
        ++ok;
    }
    return ok;
}





static int add_skill_dict(MonoObject* obj, const char* skill) {
    MonoClass* mc = cls("", "MinionResume");
    if (!mc || !obj) return 0;
    MonoField* mf = m_class_get_field(mc, "MasteryBySkillID");
    if (!mf) return 0;
    MonoObject* dict = *(MonoObject**)((char*)obj + m_field_get_offset(mf));
    if (!dict) return 0;
    MonoClass* dc = m_object_get_class(dict);
    MonoMethod* set = dc ? m_class_get_method(dc, "set_Item", 2) : 0;
    if (!set) return 0;
    MonoString* s = m_string_new(g_domain, skill);
    int one = 1;
    void* args[2];
    args[0] = s;
    args[1] = &one;
    MonoObject* exc = 0;
    m_runtime_invoke(set, dict, args, &exc);
    return exc ? 0 : 1;
}

static int swap_dict(MonoObject* obj) {
    MonoClass* mc = cls("", "MinionResume");
    if (!mc || !obj) return 0;
    MonoField* mf = m_class_get_field(mc, "MasteryBySkillID");
    if (!mf) return 0;
    int off = (int)m_field_get_offset(mf);
    MonoObject* old = *(MonoObject**)((char*)obj + off);
    if (!old) return 0;
    MonoClass* dc = m_object_get_class(old);
    if (!dc || !m_object_new) return 0;
    MonoMethod* set = m_class_get_method(dc, "set_Item", 2);
    if (!set) return 0;
    MonoObject* skills = get_skills_db();
    if (!skills) return 0;
    MonoClass* sc = m_object_get_class(skills);
    if (!sc) return 0;
    MonoObject* nd = m_object_new(g_domain, dc);
    if (!nd) return 0;





    MonoClass* skc = cls("", "Database.Skill");
    MonoField* rmf = skc ? m_class_get_field(skc, "requiredDuplicantModel") : 0;
    int rmoff = rmf ? (int)m_field_get_offset(rmf) : -1;
    MonoField* depf = skc ? m_class_get_field(skc, "deprecated") : 0;
    int depoff = depf ? (int)m_field_get_offset(depf) : -1;
    char model[64];
    dupe_model(obj, model, sizeof(model));
    int isBionic = (strstr(model, "Bionic") != 0);

    int idoff = resource_id_offset();
    int one = 1, ok = 0;





    for (int i = 0; i < SKILL_ID_COUNT; ++i) {
        const char* fname = SKILL_IDS[i];
        MonoField* sf = m_class_get_field(sc, fname);
        if (!sf) continue;
        void* skill = *(void**)((char*)skills + m_field_get_offset(sf));
        if (!skill) continue;
        int isBionicSkill = (_strnicmp(fname, "Bionics", 7) == 0);
        if (isBionic != isBionicSkill) continue;
        if (depoff >= 0 && *(unsigned char*)((char*)skill + depoff)) continue;

        if (rmoff >= 0) {
            MonoString* ms = *(MonoString**)((char*)skill + rmoff);
            char req[64];
            copy_mono_string(ms, req, sizeof(req));
            if (req[0] && (!model[0] || _stricmp(req, model) != 0)) continue;
        }

        char id[96];
        id[0] = 0;
        if (idoff >= 0)
            copy_mono_string(*(MonoString**)((char*)skill + idoff), id, sizeof(id));
        if (!id[0]) strncpy(id, fname, sizeof(id) - 1);






        if (!skill_registered(skills, id, 0, 0)) continue;

        MonoString* s = m_string_new(g_domain, id);
        void* args[2];
        args[0] = s;
        args[1] = &one;
        MonoObject* exc = 0;
        m_runtime_invoke(set, nd, args, &exc);
        if (!exc) ++ok;
    }
    *(MonoObject**)((char*)obj + off) = nd;
    return ok;
}


static int master_skill_all_main(MonoObject* obj) {
    MonoClass* mc = cls("", "MinionResume");
    if (!mc || !obj) return 0;
    MonoMethod* setpts = m_class_get_method(mc, "ForceSetSkillPoints", 1);
    MonoMethod* master = m_class_get_method(mc, "MasterSkill", 1);
    if (!master) return 0;
    if (setpts) {



        int pts = 50;
        void* pa[1] = { &pts };
        MonoObject* exc = 0;
        m_runtime_invoke(setpts, obj, pa, &exc);
    }


    swap_dict(obj);
    MonoObject* skillsDb = get_skills_db();
    if (!skillsDb) return 0;
    MonoClass* sc = m_object_get_class(skillsDb);
    if (!sc) return 0;

    char model[64];
    dupe_model(obj, model, sizeof(model));
    MonoClass* skc = cls("", "Database.Skill");
    MonoField* rmf = skc ? m_class_get_field(skc, "requiredDuplicantModel") : 0;
    int rmoff = rmf ? (int)m_field_get_offset(rmf) : -1;
    MonoField* depf = skc ? m_class_get_field(skc, "deprecated") : 0;
    int depoff = depf ? (int)m_field_get_offset(depf) : -1;
    int idoff = resource_id_offset();






    int isBionic = (strstr(model, "Bionic") != 0);
    int ok = 0;
    for (int i = 0; i < SKILL_ID_COUNT; ++i) {
        const char* fname = SKILL_IDS[i];
        MonoField* sf = m_class_get_field(sc, fname);
        if (!sf) continue;
        void* skill = *(void**)((char*)skillsDb + m_field_get_offset(sf));
        if (!skill) continue;
        int isBionicSkill = (_strnicmp(fname, "Bionics", 7) == 0);
        if (isBionic != isBionicSkill) continue;
        if (depoff >= 0 && *(unsigned char*)((char*)skill + depoff)) continue;
        if (rmoff >= 0) {
            MonoString* ms = *(MonoString**)((char*)skill + rmoff);
            char req[64];
            copy_mono_string(ms, req, sizeof(req));
            if (req[0] && (!model[0] || _stricmp(req, model) != 0)) continue;
        }
        char id[96];
        id[0] = 0;
        if (idoff >= 0)
            copy_mono_string(*(MonoString**)((char*)skill + idoff), id, sizeof(id));
        if (!id[0]) strncpy(id, fname, sizeof(id) - 1);
        if (!skill_registered(skillsDb, id, 0, 0)) continue;
        MonoString* s = m_string_new(g_domain, id);
        MonoObject* a[1] = { (MonoObject*)s };
        MonoObject* exc = 0; invoke_boxed(master, obj, a, 1, &exc);
        if (!exc) ++ok;
    }
    return ok;
}

static int master_dict_all(MonoObject* obj) {
    MonoClass* mc = cls("", "MinionResume");
    if (!mc || !obj) return 0;


    MonoObject* skillsDb = get_skills_db();
    if (!skillsDb) return 0;

    int ok = 0;
    for (int i = 0; i < SKILL_ID_COUNT; ++i) {
        if (!skill_registered(skillsDb, SKILL_IDS[i], 0, 0)) continue;
        ok += add_skill_dict(obj, SKILL_IDS[i]);
    }
    return ok;
}

static int has_skill(MonoObject* obj, const char* skill) {
    MonoClass* mc = cls("", "MinionResume");
    if (!mc || !obj) return -1;
    MonoMethod* hm = m_class_get_method(mc, "HasMasteredSkill", 1);
    if (!hm) return -1;
    MonoString* s = m_string_new(g_domain, skill);
    void* args[1];
    args[0] = s;
    MonoObject* exc = 0;
    MonoObject* r = m_runtime_invoke(hm, obj, args, &exc);
    if (exc || !r) return -1;
    return *(unsigned char*)m_object_unbox(r) ? 1 : 0;
}


static int live_ids(char* out, int outsz) {
    MonoClass* cc = cls("", "Components");
    if (!cc) return -1;
    MonoField* f = m_class_get_field(cc, "LiveMinionIdentities");
    if (!f) return -2;
    MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, cc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    if (!sd) return -3;
    void* cache = *(void**)((char*)sd + m_field_get_offset(f));
    if (!cache) return -4;
    void* hls = *(void**)((char*)cache + 16);
    if (!hls) return -5;
    void* list = *(void**)((char*)hls + 24);
    if (!list) return -6;
    void* arr = *(void**)((char*)list + 16);
    int n = *(int*)((char*)list + 24);
    if (!arr || n < 0 || n > 512) return -7;
    void* data = (char*)arr + 32;
    int used = snprintf(out, outsz, "OK liveids:%d", n);
    for (int i = 0; i < n && used < outsz - 24; ++i) {
        void* id = *(void**)((char*)data + i * 8);
        used += snprintf(out + used, outsz - used, " %llx", (unsigned long long)(uintptr_t)id);
    }
    return n;
}

static int skill_count(MonoObject* obj) {
    MonoClass* mc = cls("", "MinionResume");
    if (!mc || !obj) return -1;
    MonoMethod* gm = m_class_get_method(mc, "get_SkillsMastered", 0);
    if (!gm) return -1;
    MonoObject* exc = 0;
    MonoObject* r = m_runtime_invoke(gm, obj, 0, &exc);
    if (exc || !r) return -1;
    return unbox_i(r);
}





static int grid_cell(int cell, int* idx, float* mass) {
    MonoClass* gc = cls("", "Grid");
    if (!gc) return -1;
    MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, gc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    if (!sd) return -2;
    MonoField* ef = m_class_get_field(gc, "elementIdx");
    MonoField* mf = m_class_get_field(gc, "mass");
    if (!ef || !mf) return -3;
    void* ep = *(void**)((char*)sd + m_field_get_offset(ef));
    void* mp = *(void**)((char*)sd + m_field_get_offset(mf));
    if (!ep || !mp) return -4;
    MonoField* cf = m_class_get_field(gc, "CellCount");
    int cellCount = cf ? *(int*)((char*)sd + m_field_get_offset(cf)) : 0;
    if (cellCount > 0) {
        if (*(unsigned int*)((char*)ep + 24) == (unsigned)cellCount) ep = *(void**)((char*)ep + 32);
        if (*(unsigned int*)((char*)mp + 24) == (unsigned)cellCount) mp = *(void**)((char*)mp + 32);
    }
    *idx = ((unsigned short*)ep)[cell];
    *mass = ((float*)mp)[cell];
    return 0;
}

static int element_state(int idx) {
    MonoClass* elc = cls("", "ElementLoader");
    if (!elc) return -1;
    MonoField* lf = m_class_get_field(elc, "elements");
    MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, elc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    if (!lf || !sd) return -1;
    void* list = *(void**)((char*)sd + m_field_get_offset(lf));
    void* arr = list ? *(void**)((char*)list + 16) : 0;
    int n = arr ? *(int*)((char*)arr + 24) : 0;
    if (idx < 0 || idx >= n) return -1;
    void* elem = *(void**)((char*)arr + 32 + idx * 8);
    MonoClass* ec = cls("", "Element");
    MonoField* sf = ec ? m_class_get_field(ec, "state") : 0;
    if (!elem || !sf) return -1;


    return (int)(*(unsigned char*)((char*)elem + m_field_get_offset(sf)));
}


static void dump_elements(char* out, int outsz) {
    MonoClass* ec = cls("", "Element");
    MonoClass* elc = cls("", "ElementLoader");
    if (!ec || !elc) { snprintf(out, outsz, "ERR no-class"); return; }
    MonoField* fid = m_class_get_field(ec, "id");
    MonoField* fidx = m_class_get_field(ec, "idx");
    MonoField* fst = m_class_get_field(ec, "state");
    MonoField* fmm = m_class_get_field(ec, "maxMass");
    MonoField* flo = m_class_get_field(ec, "lowTemp");
    MonoField* fhi = m_class_get_field(ec, "highTemp");
    MonoField* fnm = m_class_get_field(ec, "<name>k__BackingField");
    MonoField* lf = m_class_get_field(elc, "elements");
    if (!fid || !fidx || !fst || !fmm || !flo || !fhi || !fnm || !lf) {
        snprintf(out, outsz, "ERR missing-field");
        return;
    }
    int off_id = (int)m_field_get_offset(fid), off_idx = (int)m_field_get_offset(fidx);
    int off_st = (int)m_field_get_offset(fst), off_mm = (int)m_field_get_offset(fmm);
    int off_lo = (int)m_field_get_offset(flo), off_hi = (int)m_field_get_offset(fhi), off_nm = (int)m_field_get_offset(fnm);

    MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, elc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    if (!sd) { snprintf(out, outsz, "ERR no-static"); return; }
    void* list = *(void**)((char*)sd + m_field_get_offset(lf));
    void* arr = list ? *(void**)((char*)list + 16) : 0;
    int n = arr ? *(int*)((char*)arr + 24) : 0;
    if (!arr || n <= 0 || n > 1000) { snprintf(out, outsz, "ERR empty"); return; }
    void* data = (char*)arr + 32;

    int used = snprintf(out, outsz, "OK elems:%d", n);
    char nm[160];
    for (int i = 0; i < n && used < outsz - 96; ++i) {
        void* e = *(void**)((char*)data + i * 8);
        if (!e) continue;
        int id = *(int*)((char*)e + off_id);
        int idx = *(unsigned short*)((char*)e + off_idx);
        int state = (int)(*(unsigned char*)((char*)e + off_st));
        float mm = *(float*)((char*)e + off_mm);
        float lo = *(float*)((char*)e + off_lo);
        float hi = *(float*)((char*)e + off_hi);
        nm[0] = 0;
        MonoString* ns = *(MonoString**)((char*)e + off_nm);
        copy_mono_string(ns, nm, sizeof(nm));
        if (!nm[0]) { char t[16]; snprintf(t, sizeof(t), "元素%d", idx); strncpy(nm, t, sizeof(nm) - 1); }
        used += snprintf(out + used, outsz - used, "\n%d|%d|%d|%.4g|%.4g|%.4g|%s",
                         idx, id, state, mm, lo, hi, nm);
    }
}










static int reveal_all() {
    MonoClass* gc = cls("", "Grid");
    if (!gc) return -1;
    MonoField* vf = m_class_get_field(gc, "Visible");
    MonoField* sf = m_class_get_field(gc, "Spawnable");
    MonoField* mf = m_class_get_field(gc, "VisMasks");
    MonoField* wf = m_class_get_field(gc, "WidthInCells");
    MonoField* hf = m_class_get_field(gc, "HeightInCells");
    MonoField* cf = m_class_get_field(gc, "CellCount");
    MonoField* wif = m_class_get_field(gc, "WorldIdx");
    if (!vf || !cf || !wf || !hf) return -2;
    MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, gc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    if (!sd) return -3;

    int gridW = *(int*)((char*)sd + m_field_get_offset(wf));
    int gridH = *(int*)((char*)sd + m_field_get_offset(hf));
    int cellCount = *(int*)((char*)sd + m_field_get_offset(cf));
    void* visArr = *(void**)((char*)sd + m_field_get_offset(vf));
    void* spArr = sf ? *(void**)((char*)sd + m_field_get_offset(sf)) : 0;
    void* mskArr = mf ? *(void**)((char*)sd + m_field_get_offset(mf)) : 0;
    void* wiArr = wif ? *(void**)((char*)sd + m_field_get_offset(wif)) : 0;
    if (!visArr) return -4;

    int n = *(int*)((char*)visArr + 24);
    if (n <= 0 || n > 10000000) return -6;
    if (cellCount > 0 && cellCount < n) n = cellCount;
    unsigned char* visData = (unsigned char*)((char*)visArr + 32);
    unsigned char* spData = 0;
    if (spArr) {
        int sn = *(int*)((char*)spArr + 24);
        if (sn >= n) spData = (unsigned char*)((char*)spArr + 32);
    }
    unsigned char* mskData = 0;
    if (mskArr) {
        int mn = *(int*)((char*)mskArr + 24);
        if (mn >= n) mskData = (unsigned char*)((char*)mskArr + 32);
    }
    unsigned char* wiData = 0;
    if (wiArr) {
        int wn = *(int*)((char*)wiArr + 24);
        if (wn >= n) wiData = (unsigned char*)((char*)wiArr + 32);
    }

    int worldsRevealed = 0;
    int cellsRevealed = 0;




    MonoClass* cmc = cls("", "ClusterManager");
    MonoField* cmif = cmc ? m_class_get_field(cmc, "Instance") : 0;
    MonoVTable* cmvt = (cmc && m_class_vtable) ? m_class_vtable(g_domain, cmc) : 0;
    void* cmsd = (cmvt && m_vtable_static_data) ? m_vtable_static_data(cmvt) : 0;
    MonoObject* cm = (cmsd && cmif) ? *(MonoObject**)((char*)cmsd + m_field_get_offset(cmif)) : 0;
    if (cm && gridW > 0 && gridH > 0) {
        MonoField* awf = m_class_get_field(cmc, "activeWorldIdx");
        MonoField* wcf = m_class_get_field(cmc, "m_worldContainers");
        int activeIdx = awf ? *(int*)((char*)cm + m_field_get_offset(awf)) : -1;
        MonoObject* wlist = wcf ? *(MonoObject**)((char*)cm + m_field_get_offset(wcf)) : 0;
        MonoClass* wcc = cls("", "WorldContainer");
        MonoField* fId = wcc ? m_class_get_field(wcc, "id") : 0;
        MonoField* fOff = wcc ? m_class_get_field(wcc, "worldOffset") : 0;
        MonoField* fSz = wcc ? m_class_get_field(wcc, "worldSize") : 0;
        MonoField* fDisc = wcc ? m_class_get_field(wcc, "isDiscovered") : 0;
        MonoField* fMod = wcc ? m_class_get_field(wcc, "isModuleInterior") : 0;
        if (wlist && fId && fOff && fSz && fDisc && fMod) {
            void* arr = *(void**)((char*)wlist + 16);
            int cnt = *(int*)((char*)wlist + 24);
            int oId = (int)m_field_get_offset(fId);
            int oOff = (int)m_field_get_offset(fOff);
            int oSz = (int)m_field_get_offset(fSz);
            int oDisc = (int)m_field_get_offset(fDisc);
            int oMod = (int)m_field_get_offset(fMod);
            if (arr && cnt > 0 && cnt < 256) {
                for (int i = 0; i < cnt; ++i) {
                    MonoObject* wc = *(MonoObject**)((char*)arr + 32 + i * 8);
                    if (!wc) continue;
                    unsigned char isMod = *(unsigned char*)((char*)wc + oMod);
                    if (isMod) continue;
                    int wid = *(int*)((char*)wc + oId);
                    if (wid < 0 || wid >= 255) continue;
                    unsigned char isDisc = *(unsigned char*)((char*)wc + oDisc);
                    if (!isDisc && wid != activeIdx) continue;
                    int ox = *(int*)((char*)wc + oOff);
                    int oy = *(int*)((char*)wc + oOff + 4);
                    int sx = *(int*)((char*)wc + oSz);
                    int sy = *(int*)((char*)wc + oSz + 4);
                    int x0 = ox < 0 ? 0 : ox;
                    int y0 = oy < 0 ? 0 : oy;
                    int x1 = ox + sx; if (x1 > gridW) x1 = gridW;
                    int y1 = oy + sy; if (y1 > gridH) y1 = gridH;
                    ++worldsRevealed;
                    for (int y = y0; y < y1; ++y) {
                        int row = y * gridW;
                        for (int x = x0; x < x1; ++x) {
                            int c = row + x;
                            if (c < 0 || c >= n) continue;
                            if (wiData && wiData[c] != (unsigned char)wid) continue;
                            visData[c] = 255;
                            if (spData) spData[c] = 255;
                            if (mskData) mskData[c] = (unsigned char)((mskData[c] & ~2) | 1);
                            ++cellsRevealed;
                        }
                    }
                }
            }
        }
    }


    if (cellsRevealed == 0) {
        for (int c = 0; c < n; ++c) {
            if (wiData && wiData[c] == 255) continue;
            visData[c] = 255;
            if (spData) spData[c] = 255;
            if (mskData) mskData[c] = (unsigned char)((mskData[c] & ~2) | 1);
            ++cellsRevealed;
        }
    }



    int spawned = 0, spExc = 0;
    MonoClass* sgc = cls("", "SaveGame");
    MonoField* sgif = sgc ? m_class_get_field(sgc, "Instance") : 0;
    MonoField* wgsf = sgc ? m_class_get_field(sgc, "worldGenSpawner") : 0;
    MonoVTable* sgvt = (sgc && m_class_vtable) ? m_class_vtable(g_domain, sgc) : 0;
    void* sgsd = (sgvt && m_vtable_static_data) ? m_vtable_static_data(sgvt) : 0;
    MonoObject* sg = (sgsd && sgif) ? *(MonoObject**)((char*)sgsd + m_field_get_offset(sgif)) : 0;
    MonoObject* spawner = (sg && wgsf) ? *(MonoObject**)((char*)sg + m_field_get_offset(wgsf)) : 0;
    if (spawner) {
        MonoClass* wgsc = m_object_get_class(spawner);
        MonoField* listf = wgsc ? m_class_get_field(wgsc, "spawnables") : 0;
        MonoObject* slist = listf ? *(MonoObject**)((char*)spawner + m_field_get_offset(listf)) : 0;
        if (slist) {
            void* sarr = *(void**)((char*)slist + 16);
            int scnt = *(int*)((char*)slist + 24);
            MonoMethod* mTry = 0;
            int oCell = -1, oIsSp = -1;
            if (sarr && scnt > 0 && scnt < 200000) {
                for (int i = 0; i < scnt; ++i) {
                    MonoObject* sp = *(MonoObject**)((char*)sarr + 32 + i * 8);
                    if (!sp) continue;
                    if (!mTry) {
                        MonoClass* spc = m_object_get_class(sp);
                        MonoField* fCell = spc ? m_class_get_field(spc, "<cell>k__BackingField") : 0;
                        MonoField* fSp = spc ? m_class_get_field(spc, "<isSpawned>k__BackingField") : 0;
                        mTry = spc ? m_class_get_method(spc, "TrySpawn", 0) : 0;
                        if (!fCell || !fSp || !mTry) break;
                        oCell = (int)m_field_get_offset(fCell);
                        oIsSp = (int)m_field_get_offset(fSp);
                    }
                    if (*(unsigned char*)((char*)sp + oIsSp)) continue;
                    int cell = *(int*)((char*)sp + oCell);
                    if (cell < 0 || cell >= n) continue;
                    if (spData && spData[cell] == 0) continue;
                    MonoObject* exc = 0;
                    m_runtime_invoke(mTry, sp, 0, &exc);
                    if (exc) {
                        ++spExc;
                        if (spExc <= 5) {
                            MonoClass* ec = m_object_get_class(exc);
                            logf("reveal: TrySpawn cell=%d exc=%s", cell, (ec && m_class_get_name) ? m_class_get_name(ec) : "?");
                        }
                    } else {
                        ++spawned;
                    }
                }
            }
        }
    }

    logf("reveal_all: worlds=%d cells=%d spawned=%d exc=%d", worldsRevealed, cellsRevealed, spawned, spExc);
    return spawned;
}


static void game_update_hook(MonoObject* self);

static void qc_dispatch(QcCmd* c) {
    int res = 0;
    switch (c->type) {
        case QC_TECH_ALL:  { res = feature_tech_all(); logf("main tech_all:%d", res); break; }
        case QC_TECH_PATH: { res = unlock_tech_chain((MonoObject*)c->a); logf("main tech_path:%d", res); break; }
        case QC_TECH_QUEUE: { res = queue_tech((MonoObject*)c->a); logf("main tech_queue:%d", res); break; }
        case QC_COMPLETE_QUEUE: { res = complete_research_queue(); logf("main complete_queue:%d", res); break; }
        case QC_RESEARCH_COMPLETE: { res = research_complete((MonoObject*)c->a, (MonoObject*)c->b); logf("main research_complete:%d", res); break; }
        case QC_RESEARCH_RESET: { res = research_reset(); logf("main research_reset:%d", res); break; }
        case QC_RESEARCH_PATH: { res = research_complete_path((MonoObject*)c->a); logf("main research_path:%d", res); break; }
        case QC_RESEARCH_ALL: { res = research_complete_all(); logf("main research_all:%d", res); break; }
        case QC_REFRESH_PLAN: { res = refresh_plan(); logf("main refresh_plan:%d", res); break; }
        case QC_SKILL_ALL: { res = master_skill_all_main((MonoObject*)c->a); logf("main skill_all:%d", res); break; }
        case QC_SPAWN:     { res = feature_put(c->i1, c->i2, c->f1, c->f2); logf("main spawn:%d", res); break; }
        case QC_SPEED:     { res = feature_speed(c->i1); logf("main speed:%d", res); break; }
        case QC_NOTIFY:    { res = notify_tech((MonoObject*)c->a, (MonoObject*)c->b); logf("main notify:%d", res); break; }
        case QC_STRESS:    { res = set_stress_cap(c->f1); logf("main stress_cap:%.1f n:%d", c->f1, res); break; }
        case QC_MORALE:    { res = set_morale_floor(c->f1); logf("main morale_floor:%.1f n:%d", c->f1, res); break; }
        case QC_MORALE_RESUME: { res = morale_resume((MonoObject*)c->a, c->f1); logf("main morale_resume:%.1f n:%d", c->f1, res); break; }
        case QC_MORALE_EXP: { res = set_morale_exp_cap(c->f1); logf("main morale_exp_cap:%.1f n:%d", c->f1, res); break; }
        case QC_REVEAL: { res = reveal_all(); logf("main reveal:%d", res); break; }
        case QC_CLEANUP: {
            if (c->i1) feature_speed(1);
            if (c->i2) set_stress_cap(-1.f);
            if (c->f1 > 0.f) set_morale_floor(-1.f);
            if (c->f2 > 0.f) set_morale_exp_cap(-1.f);
            logf("main cleanup spd=%d str=%d mor=%d mexp=%d", c->i1, c->i2, c->f1 > 0.f ? 1 : 0, c->f2 > 0.f ? 1 : 0);
            res = 1;
            break;
        }
    }
    if (c->seq) {
        g_cmd_result = res;
        InterlockedExchange(&g_cmd_done_seq, c->seq);
        if (g_cmd_done_evt) SetEvent(g_cmd_done_evt);
    }
}

static void game_update_hook(MonoObject* self) {
    if (self && self != g_game_inst) {
        g_game_inst = self;
        ensure_game_main_thread();
    } else {
        g_game_inst = self;
    }
    QcCmd c;
    while (qc_dequeue(&c)) qc_dispatch(&c);
    if (g_stress_cap >= 0.f && ((++g_frame) & 31) == 0)
        clamp_stress_all(g_stress_cap);
    ((void(*)(MonoObject*))g_game_update_tramp)(self);
}

static bool install_game_update_hook() {
    if (g_game_update_tramp) return true;
    MonoClass* gc = cls("", "Game");
    MonoMethod* um = gc ? m_class_get_method(gc, "Update", 0) : 0;
    if (!um || !m_compile_method) { logf("sync: no update entry"); return false; }
    unsigned char* code = (unsigned char*)m_compile_method(um);
    if (!code) { logf("sync: no code"); return false; }

    int need = 14;
    int total = 0;
    while (total < need) {
        int l = insn_len(code + total);
        if (l < 0) { logf("sync: undecodable opcode at +%d (0x%02x)", total, code[total]); return false; }
        total += l;
    }
    logf("sync: prologue %d bytes at %p", total, code);

    void* mem = VirtualAlloc(0, 256, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!mem) { logf("sync: allocation failed"); return false; }
    unsigned char* tramp = (unsigned char*)mem;
    memcpy(tramp, code, total);
    tramp[total] = 0xFF; tramp[total + 1] = 0x25;
    *(int*)(tramp + total + 2) = 0;
    *(void**)(tramp + total + 6) = (void*)(code + total);

    DWORD oldp = 0;
    if (!VirtualProtect(code, total, PAGE_EXECUTE_READWRITE, &oldp)) {
        logf("sync: permission setup failed");
        VirtualFree(mem, 0, MEM_RELEASE);
        return false;
    }
    code[0] = 0xFF; code[1] = 0x25;
    *(int*)(code + 2) = 0;
    *(void**)(code + 6) = (void*)game_update_hook;
    VirtualProtect(code, total, oldp, &oldp);
    FlushInstructionCache(GetCurrentProcess(), code, total);
    FlushInstructionCache(GetCurrentProcess(), tramp, total + 14);

    g_game_update_tramp = tramp;
    logf("sync: update handler installed code=%p tramp=%p", code, tramp);
    return true;
}

static int refresh_plan() {
    MonoClass* pc = cls("", "PlanScreen");
    if (!pc) return -1;
    MonoField* inf = m_class_get_field(pc, "<Instance>k__BackingField");
    if (!inf) return -1;
    MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, pc) : 0;
    void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
    if (!sd) return -1;
    MonoObject* inst = *(MonoObject**)((char*)sd + m_field_get_offset(inf));
    if (!inst) return -2;

    MonoObject* exc = 0;



    MonoField* rdf = m_class_get_field(pc, "_researchedDefs");
    if (rdf) {
        MonoObject* dict = *(MonoObject**)((char*)inst + m_field_get_offset(rdf));
        if (dict) {
            MonoMethod* clr = m_class_get_method(m_object_get_class(dict), "Clear", 0);
            if (clr) { exc = 0; m_runtime_invoke(clr, dict, 0, &exc); }
        }
    }




    MonoField* tef = m_class_get_field(pc, "toggleEntries");
    if (tef) {
        MonoObject* list = *(MonoObject**)((char*)inst + m_field_get_offset(tef));
        if (list) {
            void* arr = *(void**)((char*)list + 16);
            int n = *(int*)((char*)list + 24);
            if (arr && n > 0 && n < 256) {
                MonoMethod* teRef = 0;
                for (int i = 0; i < n; ++i) {
                    MonoObject* te = *(MonoObject**)((char*)arr + 32 + i * 8);
                    if (!te) continue;
                    if (!teRef) teRef = m_class_get_method(m_object_get_class(te), "Refresh", 0);
                    if (teRef) { exc = 0; m_runtime_invoke(teRef, te, 0, &exc); }
                }
            }
        }
    }


    MonoMethod* rbs = m_class_get_method(pc, "RefreshBuildableStates", 1);
    if (rbs) {
        unsigned char force = 1;
        void* a[1] = { &force };
        exc = 0;
        m_runtime_invoke(rbs, inst, a, &exc);
        if (exc) {
            MonoClass* ec = m_object_get_class(exc);
            logf("refresh_plan: RefreshBuildableStates exc=%s", (ec && m_class_get_name) ? m_class_get_name(ec) : "?");
        }
    }


    MonoMethod* fuc = m_class_get_method(pc, "ForceUpdateAllCategoryToggles", 1);
    if (fuc) {
        void* a[1] = { 0 };
        exc = 0;
        m_runtime_invoke(fuc, inst, a, &exc);
    }

    MonoMethod* rf = m_class_get_method(pc, "Refresh", 0);
    if (!rf) return -3;
    exc = 0;
    m_runtime_invoke(rf, inst, 0, &exc);
    if (exc) {
        MonoClass* ec = m_object_get_class(exc);
        logf("refresh_plan: Refresh exc=%s", (ec && m_class_get_name) ? m_class_get_name(ec) : "?");
        return -4;
    }
    MonoMethod* fr = m_class_get_method(pc, "ForceRefreshAllBuildingToggles", 0);
    if (fr) {
        exc = 0;
        m_runtime_invoke(fr, inst, 0, &exc);
        if (exc) {
            MonoClass* ec = m_object_get_class(exc);
            logf("refresh_plan: toggles exc=%s", (ec && m_class_get_name) ? m_class_get_name(ec) : "?");
            return -5;
        }
    }
    return 1;
}

static void reply_sync(char* out, int outsz, QcCmd c, int timeoutMs) {
    int res = 0;
    int st = qc_run_sync(c, timeoutMs, &res);
    if (st == 1) {
        if (res >= 0) snprintf(out, outsz, "OK done:%d", res);
        else snprintf(out, outsz, "ERR fail:%d", res);
    } else if (st == 0) {
        snprintf(out, outsz, "ERR main-timeout");
    } else if (st == -2) {
        snprintf(out, outsz, "ERR not-in-save");
    } else {
        snprintf(out, outsz, "ERR queue:%d", st);
    }
}

static MonoImage* g_helper_img = 0;
static MonoMethod* g_helper_preview = 0;
static char g_helper_path[512] = {0};


static void proc_cmd(char* buf, char* out, int outsz) {

    while (*buf == ' ' || *buf == '\r' || *buf == '\n') memmove(buf, buf + 1, strlen(buf) + 1);
    size_t l = strlen(buf);
    while (l && (buf[l - 1] == ' ' || buf[l - 1] == '\r' || buf[l - 1] == '\n')) buf[--l] = 0;

    if (!_stricmp(buf, "PING")) {
        if (!g_ready) { init(); }


        if (g_ready && !g_game_update_tramp && world_instance()) { install_game_update_hook(); }
        if (g_ready) { snprintf(out, outsz, "OK ready"); }
        else snprintf(out, outsz, "ERR not-ready");
        return;
    }
    if (!_strnicmp(buf, "LOADHELPER ", 11)) {
        if (!g_ready) init();
        if (!g_ready) { snprintf(out, outsz, "ERR not-ready"); return; }
        const char* path = buf + 11;
        while (*path == ' ') ++path;
        if (!path[0]) { snprintf(out, outsz, "ERR args"); return; }
        if (g_helper_preview && !_stricmp(g_helper_path, path)) {
            snprintf(out, outsz, "OK loaded");
            return;
        }
        if (!m_domain_assembly_open || !m_assembly_get_image) {
            snprintf(out, outsz, "ERR no-open-api");
            return;
        }
        MonoAssembly* ha = m_domain_assembly_open(g_domain, path);
        if (!ha) {
            logf("LOADHELPER: open failed path=%s", path);
            snprintf(out, outsz, "ERR open-failed");
            return;
        }
        MonoImage* hi = m_assembly_get_image(ha);
        MonoClass* ec = hi ? m_class_from_name(hi, "ONIPreviewHelper", "Entry") : 0;
        MonoMethod* em = ec ? m_class_get_method(ec, "GeneratePreview", 1) : 0;
        if (!hi || !ec || !em) {
            logf("LOADHELPER: missing entry hi=%p ec=%p em=%p", hi, ec, em);
            snprintf(out, outsz, "ERR missing-entry");
            return;
        }
        g_helper_img = hi;
        g_helper_preview = em;
        strncpy(g_helper_path, path, sizeof(g_helper_path) - 1);
        g_helper_path[sizeof(g_helper_path) - 1] = 0;
        logf("LOADHELPER: ok img=%p method=%p", hi, em);
        snprintf(out, outsz, "OK loaded");
        return;
    }
    if (!_strnicmp(buf, "PREVIEW ", 8)) {

        if (world_instance()) {
            snprintf(out, outsz, "ERR in-save");
            return;
        }
        if (!g_ready) init();
        if (!g_ready) { snprintf(out, outsz, "ERR not-ready"); return; }
        if (!g_helper_preview) { snprintf(out, outsz, "ERR no-helper"); return; }
        const char* pargs = buf + 8;
        while (*pargs == ' ') ++pargs;
        MonoString* ms = m_string_new(g_domain, pargs);
        if (!ms) { snprintf(out, outsz, "ERR string"); return; }
        void* a[1]; a[0] = ms;
        MonoObject* exc = 0;
        MonoObject* ret = m_runtime_invoke(g_helper_preview, 0, a, &exc);
        if (exc) {
            char eb[512] = {0};
            MonoClass* ec = m_object_get_class(exc);
            MonoMethod* ts = ec ? find_method_chain(ec, "ToString", 0) : 0;
            if (ts) {
                MonoObject* exc2 = 0;
                MonoObject* er = m_runtime_invoke(ts, exc, 0, &exc2);
                if (er && !exc2) copy_mono_string((MonoString*)er, eb, sizeof(eb));
            }
            if (!eb[0]) snprintf(eb, sizeof(eb), "%s", (ec && m_class_get_name) ? m_class_get_name(ec) : "?");
            logf("PREVIEW exc: %s", eb);
            snprintf(out, outsz, "ERR exc:%s", eb);
            return;
        }
        if (!ret) { snprintf(out, outsz, "ERR null-ret"); return; }
        copy_mono_string((MonoString*)ret, out, outsz);
        if (!out[0]) snprintf(out, outsz, "ERR empty-ret");
        return;
    }
    if (!_stricmp(buf, "SAVESTATE")) {
        if (!g_ready) init();
        if (!g_ready) { snprintf(out, outsz, "OK loading"); return; }


        if (!world_instance()) {
            g_game_inst = 0;
            snprintf(out, outsz, "OK menu");
            return;
        }
        if (!g_game_update_tramp && !install_game_update_hook()) {
            snprintf(out, outsz, "OK loading");
            return;
        }


        if (!g_game_inst) { snprintf(out, outsz, "OK loading"); return; }
        MonoObject* g = game_instance();
        MonoObject* r = research_instance();
        if (!g || !r) { snprintf(out, outsz, "OK menu"); return; }
        MonoClass* gc = cls("", "Grid");
        MonoClass* pc = cls("", "PlayerController");
        if (!gc || !pc) { snprintf(out, outsz, "OK menu"); return; }
        MonoVTable* gvt = m_class_vtable ? m_class_vtable(g_domain, gc) : 0;
        void* gsd = (gvt && m_vtable_static_data) ? m_vtable_static_data(gvt) : 0;
        MonoField* wf = m_class_get_field(gc, "WidthInCells");
        MonoField* cf = m_class_get_field(gc, "CellCount");
        MonoField* ef = m_class_get_field(gc, "elementIdx");
        MonoField* mf = m_class_get_field(gc, "mass");
        if (!gsd || !wf || !cf || !ef || !mf) { snprintf(out, outsz, "OK menu"); return; }
        int w = *(int*)((char*)gsd + m_field_get_offset(wf));
        int cells = *(int*)((char*)gsd + m_field_get_offset(cf));
        void* ep = *(void**)((char*)gsd + m_field_get_offset(ef));
        void* mp = *(void**)((char*)gsd + m_field_get_offset(mf));
        if (w <= 0 || cells <= 0 || !ep || !mp) { snprintf(out, outsz, "OK menu"); return; }
        MonoVTable* pvt = m_class_vtable ? m_class_vtable(g_domain, pc) : 0;
        void* psd = (pvt && m_vtable_static_data) ? m_vtable_static_data(pvt) : 0;
        MonoField* pif = m_class_get_field(pc, "<Instance>k__BackingField");
        void* pl = (psd && pif) ? *(void**)((char*)psd + m_field_get_offset(pif)) : 0;
        if (!pl) { snprintf(out, outsz, "OK menu"); return; }
        snprintf(out, outsz, "OK insave game:%llx w:%d h:%d",
                 (unsigned long long)(uintptr_t)g, w, cells / w);
        return;
    }
    if (!_stricmp(buf, "ELEMENTS")) { dump_elements(out, outsz); return; }
    if (!_stricmp(buf, "REVEAL")) {
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_REVEAL;
        reply_sync(out, outsz, c, 8000);
        return;
    }
    if (!strncmp(buf, "STRESS", 6)) {
        float cap = (float)atof(buf + 6);
        if (cap < 0.f) cap = -1.f;
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_STRESS; c.f1 = cap;
        reply_sync(out, outsz, c, 2500);
        return;
    }


    if (!_strnicmp(buf, "MORALEEXP", 9)) {
        float cap = (float)atof(buf + 9);
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_MORALE_EXP; c.f1 = cap;
        reply_sync(out, outsz, c, 2500);
        return;
    }
    if (!_strnicmp(buf, "MORALERESUME ", 13)) {
        unsigned long long a = 0; float floor = 0;
        if (sscanf(buf + 13, "%llx %f", &a, &floor) != 2) { snprintf(out, outsz, "ERR args"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_MORALE_RESUME; c.a = (uintptr_t)a; c.f1 = floor;
        reply_sync(out, outsz, c, 2500);
        return;
    }
    if (!strncmp(buf, "MORALE", 6)) {
        float floor = (float)atof(buf + 6);
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_MORALE; c.f1 = floor;
        reply_sync(out, outsz, c, 2500);
        return;
    }
    if (!_stricmp(buf, "TECH_ALL")) {
        int n = feature_tech_all();
        snprintf(out, outsz, n < 0 ? "ERR tech" : "OK tech:%d", n); return;
    }
    if (!_stricmp(buf, "TECHALL")) {
        int n = feature_tech_all();
        snprintf(out, outsz, n < 0 ? "ERR techall:%d" : "OK techall:%d", n); return;
    }
    if (!_strnicmp(buf, "QUEUETECH ", 9)) {
        if (!g_game_update_tramp) { snprintf(out, outsz, "ERR no-sync"); return; }
        unsigned long long a = 0;
        if (sscanf(buf + 9, "%llx", &a) != 1) { snprintf(out, outsz, "ERR args"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_TECH_QUEUE; c.a = (uintptr_t)a;
        snprintf(out, outsz, qc_enqueue(c) ? "OK queued" : "ERR queue-full");
        return;
    }
    if (!_stricmp(buf, "COMPLETEQUEUE")) {
        if (!g_game_update_tramp) { snprintf(out, outsz, "ERR no-sync"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_COMPLETE_QUEUE;
        snprintf(out, outsz, qc_enqueue(c) ? "OK queued" : "ERR queue-full");
        return;
    }
    if (!_stricmp(buf, "RESETRESEARCH")) {
        if (!g_game_update_tramp) { snprintf(out, outsz, "ERR no-sync"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_RESEARCH_RESET;
        snprintf(out, outsz, qc_enqueue(c) ? "OK queued" : "ERR queue-full");
        return;
    }
    if (!_stricmp(buf, "RESEARCHALL")) {
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_RESEARCH_ALL;
        reply_sync(out, outsz, c, 4500);
        return;
    }
    if (!_stricmp(buf, "RESEARCHPATHCUR")) {
        if (!g_game_update_tramp) { snprintf(out, outsz, "ERR no-sync"); return; }
        MonoObject* tech = current_selected_tech();
        if (!tech) { snprintf(out, outsz, "ERR no-selection"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_RESEARCH_PATH; c.a = (uintptr_t)tech;
        reply_sync(out, outsz, c, 3500);
        return;
    }
    if (!_strnicmp(buf, "CLEANUP ", 8)) {
        if (!g_game_update_tramp) { snprintf(out, outsz, "ERR no-sync"); return; }
        int spd = 0, str = 0, mor = 0, mexp = 0;
        if (sscanf(buf + 8, "%d %d %d %d", &spd, &str, &mor, &mexp) < 3) { snprintf(out, outsz, "ERR args"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_CLEANUP;
        c.i1 = spd; c.i2 = str; c.f1 = mor ? 1.f : 0.f; c.f2 = mexp ? 1.f : 0.f;
        snprintf(out, outsz, qc_enqueue(c) ? "OK queued" : "ERR queue-full");
        return;
    }
    if (!_strnicmp(buf, "RESEARCHCOMPLETE ", 17)) {
        if (!g_game_update_tramp) { snprintf(out, outsz, "ERR no-sync"); return; }
        unsigned long long a = 0, b = 0;
        if (sscanf(buf + 17, "%llx %llx", &a, &b) != 2) { snprintf(out, outsz, "ERR args"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_RESEARCH_COMPLETE; c.a = (uintptr_t)a; c.b = (uintptr_t)b;
        snprintf(out, outsz, qc_enqueue(c) ? "OK queued" : "ERR queue-full");
        return;
    }
    if (!_strnicmp(buf, "RESEARCHPATH ", 13)) {
        if (!g_game_update_tramp) { snprintf(out, outsz, "ERR no-sync"); return; }
        unsigned long long a = 0;
        if (sscanf(buf + 13, "%llx", &a) != 1) { snprintf(out, outsz, "ERR args"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_RESEARCH_PATH; c.a = (uintptr_t)a;
        snprintf(out, outsz, qc_enqueue(c) ? "OK queued" : "ERR queue-full");
        return;
    }
    if (!_stricmp(buf, "TECHCOMPLETE")) {
        if (!g_game_update_tramp) { snprintf(out, outsz, "ERR sync"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_TECH_ALL;
        snprintf(out, outsz, qc_enqueue(c) ? "OK queued" : "ERR queue-full");
        return;
    }
    if (!_stricmp(buf, "SKILL_ADD")) {
        int n = 0;


        MonoClass* mc = cls("", "MinionResume");
        MonoMethod* fa = mc ? m_class_get_method(mc, "ForceAddSkillPoint", 0) : 0;
        if (fa) {
            int cnt = collect_live_minions();
            for (int i = 0; i < cnt; ++i) {
                MonoClass* mic = m_object_get_class(g_minions[i]);
                MonoField* rf = mic ? m_class_get_field(mic, "resume") : 0;
                if (!rf) rf = mic ? m_class_get_field(mic, "Resume") : 0;
                MonoObject* res = rf ? *(MonoObject**)((char*)g_minions[i] + m_field_get_offset(rf)) : 0;
                if (!res) continue;
                MonoObject* exc = 0;
                m_runtime_invoke(fa, res, 0, &exc);
                if (!exc) ++n;
            }
        }
        snprintf(out, outsz, n < 0 ? "ERR skilladd" : "OK skilladd:%d", n); return;
    }
    if (!_stricmp(buf, "SKILL_ALL")) {
        int n = 0;
        int cnt = collect_live_minions();
        for (int i = 0; i < cnt; ++i) {
            MonoClass* mic = m_object_get_class(g_minions[i]);
            MonoField* rf = mic ? m_class_get_field(mic, "resume") : 0;
            MonoObject* res = rf ? *(MonoObject**)((char*)g_minions[i] + m_field_get_offset(rf)) : 0;
            if (!res) continue;
            n += master_skill_all_main(res);
        }
        snprintf(out, outsz, n < 0 ? "ERR skillall" : "OK skillall:%d", n); return;
    }
    if (!strncmp(buf, "SPEED", 5)) {
        int s = atoi(buf + 5);
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_SPEED; c.i1 = s;
        reply_sync(out, outsz, c, 2500);
        return;
    }
    if (!strncmp(buf, "SPAWN", 5)) {
        char elem[64] = {0}; int radius = 2; float mass = 1000.f, temp = 293.15f;
        sscanf(buf + 5, "%63s %d %f %f", elem, &radius, &mass, &temp);
        if (radius < 0) radius = 0; if (radius > 30) radius = 30;
        int hash = 0;
        for (int i = 0; i < ELEMENT_HASH_COUNT; ++i) {
            if (!_stricmp(elem, ELEMENT_HASHES[i].name)) { hash = ELEMENT_HASHES[i].value; break; }
        }
        int n = feature_put(0, hash, mass, temp);
        snprintf(out, outsz, n >= 0 ? "OK spawn:%d" : "ERR spawn:%d", n); return;
    }
    if (!_stricmp(buf, "REFRESHPLAN")) {
        if (!g_game_update_tramp) { snprintf(out, outsz, "ERR no-sync"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_REFRESH_PLAN;
        snprintf(out, outsz, qc_enqueue(c) ? "OK queued" : "ERR queue-full");
        return;
    }
    if (!_strnicmp(buf, "SWAPDICT ", 9)) {
        unsigned long long a = 0;
        if (sscanf(buf + 9, "%llx", &a) != 1) { snprintf(out, outsz, "ERR args"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_SKILL_ALL; c.a = (uintptr_t)a;
        reply_sync(out, outsz, c, 4000);
        return;
    }
    if (!_strnicmp(buf, "MASTERALL ", 10)) {
        if (!g_game_update_tramp) { snprintf(out, outsz, "ERR no-sync"); return; }
        unsigned long long a = 0;
        if (sscanf(buf + 10, "%llx", &a) != 1) { snprintf(out, outsz, "ERR args"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_SKILL_ALL; c.a = (uintptr_t)a;
        snprintf(out, outsz, qc_enqueue(c) ? "OK queued" : "ERR queue-full");
        return;
    }
    if (!_strnicmp(buf, "HASSKILL ", 9)) {
        unsigned long long a = 0; char sk[96] = {0};
        if (sscanf(buf + 9, "%llx %95s", &a, sk) != 2) { snprintf(out, outsz, "ERR args"); return; }
        int n = has_skill((MonoObject*)(uintptr_t)a, sk);
        snprintf(out, outsz, "OK hasskill:%d", n);
        return;
    }
    if (!_strnicmp(buf, "SKILLCOUNT ", 11)) {
        unsigned long long a = 0;
        if (sscanf(buf + 11, "%llx", &a) != 1) { snprintf(out, outsz, "ERR args"); return; }
        int n = skill_count((MonoObject*)(uintptr_t)a);
        snprintf(out, outsz, "OK skillcount:%d", n); return;
    }
    if (!_strnicmp(buf, "NOTIFY ", 7)) {
        if (!g_game_update_tramp) { snprintf(out, outsz, "ERR no-sync"); return; }
        unsigned long long a = 0, b = 0;
        if (sscanf(buf + 7, "%llx %llx", &a, &b) != 2) { snprintf(out, outsz, "ERR args"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_NOTIFY; c.a = (uintptr_t)a; c.b = (uintptr_t)b;
        snprintf(out, outsz, qc_enqueue(c) ? "OK queued" : "ERR queue-full");
        return;
    }
    if (!_strnicmp(buf, "SCREENCOUNT ", 12)) {
        unsigned long long a = 0;
        if (sscanf(buf + 12, "%llx", &a) != 1) { snprintf(out, outsz, "ERR args"); return; }
        MonoClass* rsc = cls("", "ResearchScreen");
        MonoField* emf = rsc ? m_class_get_field(rsc, "entryMap") : 0;
        if (!emf) { snprintf(out, outsz, "ERR no-entrymap"); return; }
        MonoObject* map = *(MonoObject**)((char*)(uintptr_t)a + m_field_get_offset(emf));
        if (!map) { snprintf(out, outsz, "OK screencount:0"); return; }
        MonoClass* mc = m_object_get_class(map);
        MonoMethod* gc = m_class_get_method(mc, "get_Count", 0);
        MonoObject* exc = 0;
        MonoObject* r = gc ? m_runtime_invoke(gc, map, 0, &exc) : 0;
        int n = (r && !exc) ? *(int*)m_object_unbox(r) : -1;
        snprintf(out, outsz, "OK screencount:%d", n);
        return;
    }
    if (!_strnicmp(buf, "TECHPATH ", 9)) {
        if (!g_game_update_tramp) { snprintf(out, outsz, "ERR no-sync"); return; }
        unsigned long long a = 0;
        if (sscanf(buf + 9, "%llx", &a) != 1) { snprintf(out, outsz, "ERR args"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_TECH_PATH; c.a = (uintptr_t)a;
        snprintf(out, outsz, qc_enqueue(c) ? "OK queued" : "ERR queue-full");
        return;
    }
    if (!_strnicmp(buf, "NAME ", 5)) {
        unsigned long long a = 0;
        if (sscanf(buf + 5, "%llx", &a) != 1) { snprintf(out, outsz, "ERR args"); return; }
        MonoObject* ident = (MonoObject*)(uintptr_t)a;
        MonoClass* mic = cls("", "MinionIdentity");
        if (!mic) { snprintf(out, outsz, "ERR no-class"); return; }
        char nb[512];
        nb[0] = 0;
        MonoObject* exc = 0;


        MonoField* sf = m_class_get_field(mic, "selectable");
        MonoObject* sel = sf ? *(MonoObject**)((char*)ident + m_field_get_offset(sf)) : 0;
        if (sel) {
            MonoClass* sc = m_object_get_class(sel);
            MonoMethod* gp = find_method_chain(sc, "GetProperName", 0);
            if (gp) {
                exc = 0;
                MonoObject* r = m_runtime_invoke(gp, sel, 0, &exc);
                if (r && !exc) copy_mono_string((MonoString*)r, nb, sizeof(nb));
            }
        }

        if (!nb[0]) {
            MonoMethod* gp = m_class_get_method(mic, "GetProperName", 0);
            exc = 0;
            MonoObject* r = gp ? m_runtime_invoke(gp, ident, 0, &exc) : 0;
            if (r && !exc) copy_mono_string((MonoString*)r, nb, sizeof(nb));
        }

        if (!nb[0]) {
            MonoField* nf = m_class_get_field(mic, "name");
            if (nf) copy_mono_string(*(MonoString**)((char*)ident + m_field_get_offset(nf)), nb, sizeof(nb));
        }

        if (!nb[0]) {
            MonoField* nkf = m_class_get_field(mic, "<nameStringKey>k__BackingField");
            MonoString* key = nkf ? *(MonoString**)((char*)ident + m_field_get_offset(nkf)) : 0;
            if (key) {
                MonoClass* strc = cls("", "Strings");
                MonoMethod* get = strc ? m_class_get_method(strc, "Get", 1) : 0;
                if (get) {
                    void* ga[1]; ga[0] = key;
                    exc = 0;
                    MonoObject* r = m_runtime_invoke(get, 0, ga, &exc);
                    if (r && !exc) copy_mono_string((MonoString*)r, nb, sizeof(nb));
                }
            }
        }


        char model[64];
        model[0] = 0;
        MonoField* mdf = m_class_get_field(mic, "model");
        MonoObject* tag = mdf ? *(MonoObject**)((char*)ident + m_field_get_offset(mdf)) : 0;
        if (tag) {
            MonoClass* tagc = m_object_get_class(tag);
            if (tagc) {
                MonoMethod* tostr = find_method_chain(tagc, "ToString", 0);
                if (tostr) {
                    exc = 0;
                    MonoObject* r = m_runtime_invoke(tostr, tag, 0, &exc);
                    if (r && !exc) copy_mono_string((MonoString*)r, model, sizeof(model));
                }
                if (!model[0]) {
                    MonoField* nf = m_class_get_field(tagc, "Name");
                    if (!nf) nf = m_class_get_field(tagc, "<Name>k__BackingField");
                    if (nf) copy_mono_string(*(MonoString**)((char*)tag + m_field_get_offset(nf)), model, sizeof(model));
                }
            }
        }
        logf("NAME %p name='%s' model='%s'", ident, nb, model);
        snprintf(out, outsz, "OK name:%s|model:%s", nb, model);
        return;
    }
    if (!_strnicmp(buf, "LIVEIDS", 7)) {
        int n = live_ids(out, outsz);
        if (n < 0) snprintf(out, outsz, "ERR liveids:%d", n);
        return;
    }
    if (!_strnicmp(buf, "CELLINFO ", 9)) {
        int cell = 0; int idx = -1; float mass = 0;
        if (sscanf(buf + 9, "%d", &cell) != 1) { snprintf(out, outsz, "ERR args"); return; }
        int r = grid_cell(cell, &idx, &mass);
        if (r != 0) { snprintf(out, outsz, "ERR cellinfo:%d", r); return; }
        int st = element_state(idx);
        snprintf(out, outsz, "OK cell idx:%d mass:%.3f state:%d", idx, mass, st);
        return;
    }
    if (!_strnicmp(buf, "PUTSAFE ", 8)) {
        int cell = 0, hash = 0; float mass = 1000.f, temp = 293.15f;
        if (sscanf(buf + 8, "%d %d %f %f", &cell, &hash, &mass, &temp) < 2) {
            snprintf(out, outsz, "ERR args"); return;
        }
        int idx = -1; float cmass = 0;
        int r = grid_cell(cell, &idx, &cmass);
        if (r != 0) { snprintf(out, outsz, "ERR cellinfo:%d", r); return; }
        int st = element_state(idx);


        if ((st & 3) == 3) { snprintf(out, outsz, "ERR solid idx:%d", idx); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_SPAWN;
        c.i1 = cell; c.i2 = hash; c.f1 = mass; c.f2 = temp;
        reply_sync(out, outsz, c, 2500);
        return;
    }
    if (!_strnicmp(buf, "PUT ", 4)) {
        int cell = 0, hash = 0; float mass = 1000.f, temp = 293.15f;
        if (sscanf(buf + 4, "%d %d %f %f", &cell, &hash, &mass, &temp) < 2) {
            snprintf(out, outsz, "ERR args"); return;
        }
        if (!g_game_update_tramp) { snprintf(out, outsz, "ERR no-sync"); return; }
        QcCmd c; memset(&c, 0, sizeof(c)); c.type = QC_SPAWN;
        c.i1 = cell; c.i2 = hash; c.f1 = mass; c.f2 = temp;
        snprintf(out, outsz, qc_enqueue(c) ? "OK queued" : "ERR queue-full");
        return;
    }
    if (!_strnicmp(buf, "FILL ", 5)) {
        int cell = 0, hash = 0, radius = 2, width = 0, cellCount = 0;
        float mass = 1000.f, temp = 293.15f;
        if (sscanf(buf + 5, "%d %d %d %d %d %f %f", &cell, &hash, &radius, &width, &cellCount, &mass, &temp) < 5) {
            snprintf(out, outsz, "ERR args"); return;
        }
        if (width <= 0 || radius < 0 || radius > 30) { snprintf(out, outsz, "ERR range"); return; }
        int cx = cell % width, cy = cell / width, placed = 0;
        for (int dy = -radius; dy <= radius; ++dy) {
            for (int dx = -radius; dx <= radius; ++dx) {
                int gx = cx + dx, gy = cy + dy;
                if (gx < 0 || gx >= width || gy < 0) continue;
                int c = gy * width + gx;
                if (c < 0 || (cellCount > 0 && c >= cellCount)) continue;
                if (feature_put(c, hash, mass, temp) > 0) ++placed;
            }
        }
        snprintf(out, outsz, "OK fill:%d", placed);
        return;
    }
    if (!_strnicmp(buf, "VTABLE ", 7)) {
        char cn[64] = {0};
        if (sscanf(buf + 7, "%63s", cn) != 1) { snprintf(out, outsz, "ERR args"); return; }
        MonoClass* c = cls_dotted(cn);
        if (!c) { snprintf(out, outsz, "ERR no-class"); return; }
        MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, c) : 0;
        if (!vt) { snprintf(out, outsz, "ERR no-info"); return; }
        snprintf(out, outsz, "OK ready %p", vt);
        return;
    }
    if (!_strnicmp(buf, "FIELD ", 6)) {
        char cn[64] = {0}, fn[64] = {0};
        if (sscanf(buf + 6, "%63s %63s", cn, fn) != 2) { snprintf(out, outsz, "ERR args"); return; }
        MonoClass* c = cls_dotted(cn);
        if (!c) { snprintf(out, outsz, "ERR no-class"); return; }
        MonoField* f = m_class_get_field(c, fn);
        if (!f) { snprintf(out, outsz, "ERR no-field"); return; }
        snprintf(out, outsz, "OK field %s@%d", fn, (int)m_field_get_offset(f));
        return;
    }
    if (!_strnicmp(buf, "STATIC ", 7)) {
        char cn[64] = {0}, fn[64] = {0};
        if (sscanf(buf + 7, "%63s %63s", cn, fn) != 2) { snprintf(out, outsz, "ERR args"); return; }
        MonoClass* c = cls_dotted(cn);
        if (!c) { snprintf(out, outsz, "ERR no-class"); return; }
        MonoField* f = m_class_get_field(c, fn);
        if (!f) { snprintf(out, outsz, "ERR no-field"); return; }
        MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, c) : 0;
        void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
        if (!sd) { snprintf(out, outsz, "ERR no-static"); return; }


        snprintf(out, outsz, "OK static %p off:%d",
                 (char*)sd + (int)m_field_get_offset(f), (int)m_field_get_offset(f));
        return;
    }
    if (!_strnicmp(buf, "INFO ", 5)) {
        char cn[64] = {0};
        if (sscanf(buf + 5, "%63s", cn) != 1) { snprintf(out, outsz, "ERR args"); return; }
        MonoClass* c = cls("", cn);
        if (!c) { snprintf(out, outsz, "ERR no-class"); return; }
        MonoVTable* vt = m_class_vtable ? m_class_vtable(g_domain, c) : 0;
        void* sd = (vt && m_vtable_static_data) ? m_vtable_static_data(vt) : 0;
        snprintf(out, outsz, "OK info klass:%p vt:%p static:%p size:%d", c, vt, sd,
                 (int)(m_class_instance_size ? m_class_instance_size(c) : 0));
        return;
    }
    snprintf(out, outsz, "ERR unknown");
}







static DWORD WINAPI pipe_thread(void* p) {
    char name[256];
    HMODULE base = 0;
    GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       (LPCSTR)(uintptr_t)pipe_thread, &base);
    snprintf(name, sizeof(name), "\\\\.\\pipe\\oni_trainer_%lu_%llx",
             (unsigned long)GetCurrentProcessId(), (unsigned long long)(uintptr_t)base);
    logf("pipe: %s", name);
    HANDLE h = INVALID_HANDLE_VALUE;
    for (;;) {
        if (h == INVALID_HANDLE_VALUE) {
            h = CreateNamedPipeA(name, PIPE_ACCESS_DUPLEX,
                                 PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
                                 1, 65536, 65536, 0, 0);
            if (h == INVALID_HANDLE_VALUE) { Sleep(250); continue; }
        }
        if (!ConnectNamedPipe(h, 0) && GetLastError() != ERROR_PIPE_CONNECTED) {
            CloseHandle(h);
            h = INVALID_HANDLE_VALUE;
            continue;
        }
        for (;;) {
            char buf[2048];
            DWORD n = 0;
            if (!ReadFile(h, buf, sizeof(buf) - 1, &n, 0) || n == 0) break;
            buf[n] = 0;
            char out[65536];
            proc_cmd(buf, out, sizeof(out));
            DWORD w = 0;
            if (!WriteFile(h, out, (DWORD)strlen(out) + 1, &w, 0)) break;
        }
        DisconnectNamedPipe(h);
    }
    return 0;
}


static DWORD WINAPI worker(void* p) {
    logf("worker start pid=%lu", (unsigned long)GetCurrentProcessId());
    g_cmd_done_evt = CreateEventA(0, TRUE, FALSE, 0);

    for (int i = 0; i < 60 && !g_ready; ++i) {
        if (GetModuleHandleA("mono-2.0-bdwgc.dll")) init();
        if (!g_ready) Sleep(250);
    }
    logf("init done ready=%d", g_ready);
    if (g_ready && world_instance()) {
        logf("sync preinstall=%d", install_game_update_hook());
    }
    pipe_thread(0);
    return 0;
}

BOOL APIENTRY DllMain(HMODULE h, DWORD reason, LPVOID reserved) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(h);
        HANDLE th = CreateThread(0, 0, worker, 0, 0, 0);
        if (th) CloseHandle(th);
    }
    return TRUE;
}

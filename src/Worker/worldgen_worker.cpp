



#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <io.h>
#include <fcntl.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <wchar.h>

typedef struct _MonoDomain MonoDomain;
typedef struct _MonoAssembly MonoAssembly;
typedef struct _MonoImage MonoImage;
typedef struct _MonoClass MonoClass;
typedef struct _MonoMethod MonoMethod;
typedef struct _MonoObject MonoObject;
typedef struct _MonoString MonoString;

struct ManagedSpanWrapper {
    void* begin;
    int32_t length;
};

struct Vec2 {
    float x;
    float y;
};

static MonoDomain* (*p_mono_jit_init_version)(const char* root_domain_name, const char* runtime_version);
static void (*p_mono_set_dirs)(const char* assembly_dir, const char* config_dir);
static void (*p_mono_set_assemblies_path)(const char* path);
static void (*p_mono_config_parse)(const char* filename);
static void (*p_mono_add_internal_call)(const char* name, const void* method);
static MonoAssembly* (*p_mono_domain_assembly_open)(MonoDomain* domain, const char* name);
static MonoImage* (*p_mono_assembly_get_image)(MonoAssembly* assembly);
static MonoClass* (*p_mono_class_from_name)(MonoImage* image, const char* name_space, const char* name);
static MonoMethod* (*p_mono_class_get_method_from_name)(MonoClass* klass, const char* name, int param_count);
static MonoObject* (*p_mono_runtime_invoke)(MonoMethod* method, void* obj, void** params, MonoObject** exc);
static MonoString* (*p_mono_string_new)(MonoDomain* domain, const char* text);
static char* (*p_mono_string_to_utf8)(MonoString* s);
static MonoString* (*p_mono_object_to_string)(MonoObject* obj, MonoObject** exc);
static void* (*p_mono_object_unbox)(MonoObject* obj);
static void (*p_mono_free)(void* ptr);

static MonoDomain* g_domain = nullptr;
static wchar_t g_streamingAssetsW[1024];
static wchar_t g_dataPathW[1024];
static wchar_t g_tempPathW[1024];
static wchar_t g_logPathW[1024];
static ULONGLONG g_startTick = 0;
static uint32_t g_rngState = 0x12345678u;
static bool g_debugLog = false;

static void ToForwardSlashes(wchar_t* s) {
    for (; *s; ++s) {
        if (*s == L'\\') *s = L'/';
    }
}

static char* WideToUtf8Alloc(const wchar_t* ws) {
    if (!ws) return nullptr;
    int need = WideCharToMultiByte(CP_UTF8, 0, ws, -1, nullptr, 0, nullptr, nullptr);
    if (need <= 0) return nullptr;
    char* buf = (char*)malloc((size_t)need);
    if (!buf) return nullptr;
    WideCharToMultiByte(CP_UTF8, 0, ws, -1, buf, need, nullptr, nullptr);
    return buf;
}

static void FillSpanUtf16(ManagedSpanWrapper* ret, const wchar_t* src) {
    if (!ret) return;
    if (!src || !*src) {
        ret->begin = nullptr;
        ret->length = 0;
        return;
    }
    size_t len = wcslen(src);
    size_t bytes = (len + 1) * sizeof(uint16_t);
    uint16_t* buf = (uint16_t*)malloc(bytes);
    if (!buf) {
        ret->begin = nullptr;
        ret->length = 0;
        return;
    }
    memcpy(buf, src, bytes);
    ret->begin = buf;
    ret->length = (int32_t)len;
}

static void FillSpanAscii(ManagedSpanWrapper* ret, const char* src) {
    if (!ret) return;
    if (!src || !*src) {
        ret->begin = nullptr;
        ret->length = 0;
        return;
    }
    size_t len = strlen(src);
    uint16_t* buf = (uint16_t*)malloc((len + 1) * sizeof(uint16_t));
    if (!buf) {
        ret->begin = nullptr;
        ret->length = 0;
        return;
    }
    for (size_t i = 0; i <= len; ++i) buf[i] = (uint16_t)(unsigned char)src[i];
    ret->begin = buf;
    ret->length = (int32_t)len;
}



static void* icall_Malloc(int32_t size) {
    return malloc(size > 0 ? (size_t)size : 1);
}

static void icall_Free(void* ptr) {
    if (ptr) free(ptr);
}

static void icall_App_get_streamingAssetsPath_Injected(ManagedSpanWrapper* ret) {
    FillSpanUtf16(ret, g_streamingAssetsW);
}

static void icall_App_get_dataPath_Injected(ManagedSpanWrapper* ret) {
    FillSpanUtf16(ret, g_dataPathW);
}

static void icall_App_get_persistentDataPath_Injected(ManagedSpanWrapper* ret) {
    FillSpanUtf16(ret, g_tempPathW);
}

static void icall_App_get_temporaryCachePath_Injected(ManagedSpanWrapper* ret) {
    FillSpanUtf16(ret, g_tempPathW);
}

static void icall_App_get_consoleLogPath_Injected(ManagedSpanWrapper* ret) {
    FillSpanUtf16(ret, g_logPathW);
}

static void icall_App_get_unityVersion_Injected(ManagedSpanWrapper* ret) {
    FillSpanAscii(ret, "2022.3.50f1");
}

static void icall_App_get_version_Injected(ManagedSpanWrapper* ret) {
    FillSpanAscii(ret, "1.0.0");
}

static void icall_App_get_emptyString_Injected(ManagedSpanWrapper* ret) {
    if (ret) { ret->begin = nullptr; ret->length = 0; }
}

static uint8_t icall_App_get_isPlaying() { return 1; }
static uint8_t icall_App_get_isEditor() { return 0; }
static uint8_t icall_App_get_isBatchMode() { return 1; }
static int32_t icall_App_get_platform() { return 2; }
static int32_t icall_App_get_systemLanguage() { return 10; }
static uint8_t icall_App_HasARGV_Injected(void*) { return 0; }
static void icall_App_SetLogCallbackDefined(uint8_t) {}
static uint8_t icall_App_IsPlaying_Injected(void*) { return 1; }

static void icall_Debug_Internal_Log_Injected(int32_t level, int32_t options, ManagedSpanWrapper* msg, void* obj) {
    (void)options; (void)obj;
    if (!g_debugLog || !msg || !msg->begin || msg->length <= 0) return;
    int need = WideCharToMultiByte(CP_UTF8, 0, (const wchar_t*)msg->begin, msg->length, nullptr, 0, nullptr, nullptr);
    if (need > 0 && need < 4096) {
        char buf[4096];
        WideCharToMultiByte(CP_UTF8, 0, (const wchar_t*)msg->begin, msg->length, buf, need, nullptr, nullptr);
        buf[need] = 0;
        fprintf(stderr, "[Unity:%d] %s\n", level, buf);
        fflush(stderr);
    }
}

static void icall_Debug_Internal_LogException_Injected(MonoObject* ex, void* obj) {
    (void)obj;
    if (!g_debugLog || !ex) return;
    MonoObject* exc2 = nullptr;
    MonoString* ms = p_mono_object_to_string(ex, &exc2);
    if (ms) {
        char* u8 = p_mono_string_to_utf8(ms);
        if (u8) {
            fprintf(stderr, "[UnityExc] %s\n", u8);
            fflush(stderr);
            p_mono_free(u8);
        }
    }
}

static float icall_Time_sinceStartFloat() {
    return (float)((double)(GetTickCount64() - g_startTick) / 1000.0);
}

static double icall_Time_sinceStartDouble() {
    return (double)(GetTickCount64() - g_startTick) / 1000.0;
}

static float icall_Time_get_timeScale() { return 1.0f; }
static void icall_Time_set_timeScale(float) {}
static uint8_t icall_Time_get_inFixedTimeStep() { return 0; }
static int32_t icall_Time_get_frameCount() { return 1; }

static uint32_t NextRng32() {
    uint32_t x = g_rngState ? g_rngState : 0x6D2B79F5u;
    x ^= x << 13;
    x ^= x >> 17;
    x ^= x << 5;
    g_rngState = x;
    return x;
}

static void icall_Random_InitState(int32_t seed) {
    g_rngState = seed ? (uint32_t)seed : 0x6D2B79F5u;
}

static int32_t icall_Random_get_seed() { return (int32_t)g_rngState; }
static void icall_Random_set_seed(int32_t s) { icall_Random_InitState(s); }

static float icall_Random_get_value() {
    return (float)(NextRng32() & 0x00FFFFFFu) / 16777216.0f;
}

static float icall_Random_RangeFloat(float minV, float maxV) {
    return minV + (maxV - minV) * icall_Random_get_value();
}

static int32_t icall_Random_RangeInt(int32_t minV, int32_t maxV) {
    if (maxV <= minV) return minV;
    uint32_t span = (uint32_t)(maxV - minV);
    return minV + (int32_t)(NextRng32() % span);
}

static void icall_Random_GetRandomUnitCircle(Vec2* out) {
    if (!out) return;
    float a = icall_Random_get_value() * 6.283185307f;
    out->x = cosf(a);
    out->y = sinf(a);
}

static void icall_Random_GetRandomUnitSphere(Vec2* ret) {
    if (!ret) return;
    ret->x = 0.0f;
    ret->y = 1.0f;
}

static void icall_Object_GetName_Injected(void* self, ManagedSpanWrapper* ret) {
    (void)self;
    if (ret) { ret->begin = nullptr; ret->length = 0; }
}

static void icall_Object_SetName_Injected(void*, void*) {}
static uint8_t icall_Object_False1(void*) { return 0; }
static uint8_t icall_Object_CurrentThreadIsMainThread() { return 1; }
static int32_t icall_Object_GetOffsetOfInstanceIDInCPlusPlusObject() { return 16; }
static void icall_Object_Destroy(void*, float) {}
static void icall_Object_DestroyImmediate(void*, uint8_t) {}
static void icall_Object_DontDestroyOnLoad(void*) {}
static int32_t icall_Object_GetHideFlags(void*) { return 0; }
static void icall_Object_SetHideFlags(void*, int32_t) {}
static void icall_ScriptableObject_Create(void*) {}

static uint8_t icall_PlayerPrefs_HasKey(void*) { return 0; }
static uint8_t icall_PlayerPrefs_TrySet(void*, int32_t) { return 1; }
static int32_t icall_PlayerPrefs_GetInt(void*, int32_t defVal) { return defVal; }
static float icall_PlayerPrefs_GetFloat(void*, float defVal) { return defVal; }
static void icall_PlayerPrefs_GetString(void*, void*, ManagedSpanWrapper* ret) {
    if (ret) { ret->begin = nullptr; ret->length = 0; }
}
static void icall_PlayerPrefs_Void() {}
static void icall_PlayerPrefs_DeleteKey(void*) {}

static float icall_Mathf_GammaToLinear(float v) {
    if (v <= 0.04045f) return v / 12.92f;
    return powf((v + 0.055f) / 1.055f, 2.4f);
}

static float icall_Mathf_LinearToGamma(float v) {
    if (v <= 0.0031308f) return v * 12.92f;
    return 1.055f * powf(v, 1.0f / 2.4f) - 0.055f;
}

static float icall_Mathf_PerlinNoise(float x, float y) {
    uint32_t h = (uint32_t)(sinf(x * 12.9898f + y * 78.233f) * 43758.5453f);
    return (float)(h & 0xFFFFu) / 65535.0f;
}

static void RegisterInternalCalls() {
    auto R = [](const char* name, const void* fn) {
        p_mono_add_internal_call(name, fn);
    };

    R("UnityEngine.Bindings.BindingsAllocator::Malloc", (const void*)icall_Malloc);
    R("UnityEngine.Bindings.BindingsAllocator::Free", (const void*)icall_Free);
    R("UnityEngine.Bindings.BindingsAllocator::FreeNativeOwnedMemory", (const void*)icall_Free);

    R("UnityEngine.Application::get_streamingAssetsPath_Injected", (const void*)icall_App_get_streamingAssetsPath_Injected);
    R("UnityEngine.Application::get_dataPath_Injected", (const void*)icall_App_get_dataPath_Injected);
    R("UnityEngine.Application::get_persistentDataPath_Injected", (const void*)icall_App_get_persistentDataPath_Injected);
    R("UnityEngine.Application::get_temporaryCachePath_Injected", (const void*)icall_App_get_temporaryCachePath_Injected);
    R("UnityEngine.Application::get_consoleLogPath_Injected", (const void*)icall_App_get_consoleLogPath_Injected);
    R("UnityEngine.Application::get_unityVersion_Injected", (const void*)icall_App_get_unityVersion_Injected);
    R("UnityEngine.Application::get_version_Injected", (const void*)icall_App_get_version_Injected);
    R("UnityEngine.Application::get_buildGUID_Injected", (const void*)icall_App_get_emptyString_Injected);
    R("UnityEngine.Application::get_identifier_Injected", (const void*)icall_App_get_emptyString_Injected);
    R("UnityEngine.Application::get_productName_Injected", (const void*)icall_App_get_emptyString_Injected);
    R("UnityEngine.Application::get_companyName_Injected", (const void*)icall_App_get_emptyString_Injected);
    R("UnityEngine.Application::get_isPlaying", (const void*)icall_App_get_isPlaying);
    R("UnityEngine.Application::get_isEditor", (const void*)icall_App_get_isEditor);
    R("UnityEngine.Application::get_isBatchMode", (const void*)icall_App_get_isBatchMode);
    R("UnityEngine.Application::get_platform", (const void*)icall_App_get_platform);
    R("UnityEngine.Application::get_systemLanguage", (const void*)icall_App_get_systemLanguage);
    R("UnityEngine.Application::HasARGV_Injected", (const void*)icall_App_HasARGV_Injected);
    R("UnityEngine.Application::SetLogCallbackDefined", (const void*)icall_App_SetLogCallbackDefined);
    R("UnityEngine.Application::IsPlaying_Injected", (const void*)icall_App_IsPlaying_Injected);

    R("UnityEngine.DebugLogHandler::Internal_Log_Injected", (const void*)icall_Debug_Internal_Log_Injected);
    R("UnityEngine.DebugLogHandler::Internal_LogException_Injected", (const void*)icall_Debug_Internal_LogException_Injected);

    R("UnityEngine.Time::get_time", (const void*)icall_Time_sinceStartFloat);
    R("UnityEngine.Time::get_unscaledTime", (const void*)icall_Time_sinceStartFloat);
    R("UnityEngine.Time::get_realtimeSinceStartup", (const void*)icall_Time_sinceStartFloat);
    R("UnityEngine.Time::get_timeAsDouble", (const void*)icall_Time_sinceStartDouble);
    R("UnityEngine.Time::get_unscaledTimeAsDouble", (const void*)icall_Time_sinceStartDouble);
    R("UnityEngine.Time::get_realtimeSinceStartupAsDouble", (const void*)icall_Time_sinceStartDouble);
    R("UnityEngine.Time::get_timeScale", (const void*)icall_Time_get_timeScale);
    R("UnityEngine.Time::set_timeScale", (const void*)icall_Time_set_timeScale);
    R("UnityEngine.Time::get_inFixedTimeStep", (const void*)icall_Time_get_inFixedTimeStep);
    R("UnityEngine.Time::get_frameCount", (const void*)icall_Time_get_frameCount);

    R("UnityEngine.Random::InitState", (const void*)icall_Random_InitState);
    R("UnityEngine.Random::get_seed", (const void*)icall_Random_get_seed);
    R("UnityEngine.Random::set_seed", (const void*)icall_Random_set_seed);
    R("UnityEngine.Random::get_value", (const void*)icall_Random_get_value);
    R("UnityEngine.Random::Range", (const void*)icall_Random_RangeFloat);
    R("UnityEngine.Random::RandomRangeInt", (const void*)icall_Random_RangeInt);
    R("UnityEngine.Random::GetRandomUnitCircle", (const void*)icall_Random_GetRandomUnitCircle);
    R("UnityEngine.Random::get_onUnitCircle_Injected", (const void*)icall_Random_GetRandomUnitCircle);
    R("UnityEngine.Random::get_insideUnitSphere_Injected", (const void*)icall_Random_GetRandomUnitSphere);
    R("UnityEngine.Random::get_onUnitSphere_Injected", (const void*)icall_Random_GetRandomUnitSphere);

    R("UnityEngine.Object::GetName_Injected", (const void*)icall_Object_GetName_Injected);
    R("UnityEngine.Object::SetName_Injected", (const void*)icall_Object_SetName_Injected);
    R("UnityEngine.Object::DoesObjectWithInstanceIDExist_Injected", (const void*)icall_Object_False1);
    R("UnityEngine.Object::IsPersistent_Injected", (const void*)icall_Object_False1);
    R("UnityEngine.Object::CurrentThreadIsMainThread", (const void*)icall_Object_CurrentThreadIsMainThread);
    R("UnityEngine.Object::GetOffsetOfInstanceIDInCPlusPlusObject", (const void*)icall_Object_GetOffsetOfInstanceIDInCPlusPlusObject);
    R("UnityEngine.Object::Destroy_Injected", (const void*)icall_Object_Destroy);
    R("UnityEngine.Object::DestroyImmediate_Injected", (const void*)icall_Object_DestroyImmediate);
    R("UnityEngine.Object::DontDestroyOnLoad_Injected", (const void*)icall_Object_DontDestroyOnLoad);
    R("UnityEngine.Object::get_hideFlags_Injected", (const void*)icall_Object_GetHideFlags);
    R("UnityEngine.Object::set_hideFlags_Injected", (const void*)icall_Object_SetHideFlags);

    R("UnityEngine.ScriptableObject::CreateScriptableObject", (const void*)icall_ScriptableObject_Create);
    R("UnityEngine.ScriptableObject::SetDirty_Injected", (const void*)icall_ScriptableObject_Create);

    R("UnityEngine.PlayerPrefs::HasKey_Injected", (const void*)icall_PlayerPrefs_HasKey);
    R("UnityEngine.PlayerPrefs::TrySetInt_Injected", (const void*)icall_PlayerPrefs_TrySet);
    R("UnityEngine.PlayerPrefs::TrySetFloat_Injected", (const void*)icall_PlayerPrefs_TrySet);
    R("UnityEngine.PlayerPrefs::TrySetSetString_Injected", (const void*)icall_PlayerPrefs_TrySet);
    R("UnityEngine.PlayerPrefs::GetInt_Injected", (const void*)icall_PlayerPrefs_GetInt);
    R("UnityEngine.PlayerPrefs::GetFloat_Injected", (const void*)icall_PlayerPrefs_GetFloat);
    R("UnityEngine.PlayerPrefs::GetString_Injected", (const void*)icall_PlayerPrefs_GetString);
    R("UnityEngine.PlayerPrefs::DeleteAll", (const void*)icall_PlayerPrefs_Void);
    R("UnityEngine.PlayerPrefs::Save", (const void*)icall_PlayerPrefs_Void);
    R("UnityEngine.PlayerPrefs::DeleteKey_Injected", (const void*)icall_PlayerPrefs_DeleteKey);

    R("UnityEngine.Mathf::GammaToLinearSpace", (const void*)icall_Mathf_GammaToLinear);
    R("UnityEngine.Mathf::LinearToGammaSpace", (const void*)icall_Mathf_LinearToGamma);
    R("UnityEngine.Mathf::PerlinNoise", (const void*)icall_Mathf_PerlinNoise);
    R("UnityEngine.Mathf::PerlinNoise1D", (const void*)icall_Random_get_value);
}

static void PrintException(MonoObject* exc) {
    if (!exc || !p_mono_object_to_string || !p_mono_string_to_utf8) return;
    MonoObject* exc2 = nullptr;
    MonoString* s = p_mono_object_to_string(exc, &exc2);
    if (s) {
        char* utf8 = p_mono_string_to_utf8(s);
        if (utf8) {
            fprintf(stderr, "%s\n", utf8);
            p_mono_free(utf8);
        }
    }
}

int wmain(int argc, wchar_t** argv) {
    if (argc < 3) {
        fprintf(stderr, "Usage: ONIWorldGenWorker.exe <gameDir> <helperDllPath>\n");
        return 2;
    }

    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);
    setvbuf(stdout, nullptr, _IONBF, 0);
    g_startTick = GetTickCount64();

    wchar_t dbgEnv[8] = { 0 };
    if (GetEnvironmentVariableW(L"ONI_WORKER_DEBUG", dbgEnv, 8) > 0 && dbgEnv[0] == L'1')
        g_debugLog = true;

    const wchar_t* gameDirW = argv[1];
    const wchar_t* helperPathW = argv[2];

    wchar_t monoRuntimeDirW[1024];
    wchar_t monoDllPathW[1024];
    wchar_t managedDirW[1024];
    wchar_t etcDirW[1024];

    swprintf(monoRuntimeDirW, 1024, L"%ls\\MonoBleedingEdge\\EmbedRuntime", gameDirW);
    swprintf(monoDllPathW, 1024, L"%ls\\mono-2.0-bdwgc.dll", monoRuntimeDirW);
    swprintf(managedDirW, 1024, L"%ls\\OxygenNotIncluded_Data\\Managed", gameDirW);
    swprintf(etcDirW, 1024, L"%ls\\MonoBleedingEdge\\etc", gameDirW);

    swprintf(g_dataPathW, 1024, L"%ls\\OxygenNotIncluded_Data", gameDirW);
    swprintf(g_streamingAssetsW, 1024, L"%ls\\OxygenNotIncluded_Data\\StreamingAssets", gameDirW);
    ToForwardSlashes(g_dataPathW);
    ToForwardSlashes(g_streamingAssetsW);

    wchar_t tmpRoot[512] = { 0 };
    GetTempPathW(512, tmpRoot);
    swprintf(g_tempPathW, 1024, L"%lsONITrainer\\worker_tmp", tmpRoot);
    CreateDirectoryW(g_tempPathW, nullptr);
    swprintf(g_logPathW, 1024, L"%ls\\worker.log", g_tempPathW);
    ToForwardSlashes(g_tempPathW);
    ToForwardSlashes(g_logPathW);

    SetDllDirectoryW(monoRuntimeDirW);
    HMODULE hMono = LoadLibraryW(monoDllPathW);
    if (!hMono) {
        fprintf(stderr, "Failed to load mono-2.0-bdwgc.dll (err=%lu)\n", (unsigned long)GetLastError());
        return 3;
    }

#define LOAD_MONO(fn) p_##fn = (decltype(p_##fn))GetProcAddress(hMono, #fn); \
    if (!p_##fn) { fprintf(stderr, "Missing mono export: %s\n", #fn); return 4; }

    LOAD_MONO(mono_jit_init_version);
    LOAD_MONO(mono_set_dirs);
    LOAD_MONO(mono_set_assemblies_path);
    LOAD_MONO(mono_config_parse);
    LOAD_MONO(mono_add_internal_call);
    LOAD_MONO(mono_domain_assembly_open);
    LOAD_MONO(mono_assembly_get_image);
    LOAD_MONO(mono_class_from_name);
    LOAD_MONO(mono_class_get_method_from_name);
    LOAD_MONO(mono_runtime_invoke);
    LOAD_MONO(mono_string_new);
    LOAD_MONO(mono_string_to_utf8);
    LOAD_MONO(mono_object_to_string);
    LOAD_MONO(mono_object_unbox);
    LOAD_MONO(mono_free);
#undef LOAD_MONO

    char* managedU8 = WideToUtf8Alloc(managedDirW);
    char* etcU8 = WideToUtf8Alloc(etcDirW);
    char* helperU8 = WideToUtf8Alloc(helperPathW);
    char* gameU8 = WideToUtf8Alloc(gameDirW);

    p_mono_set_assemblies_path(managedU8);
    p_mono_set_dirs(managedU8, etcU8);
    p_mono_config_parse(nullptr);

    g_domain = p_mono_jit_init_version("ONIWorldGenWorker", "v4.0.30319");
    if (!g_domain) {
        fprintf(stderr, "mono_jit_init_version failed\n");
        return 5;
    }

    RegisterInternalCalls();

    MonoAssembly* helperAsm = p_mono_domain_assembly_open(g_domain, helperU8);
    if (!helperAsm) {
        fprintf(stderr, "Failed to open helper assembly: %s\n", helperU8);
        return 6;
    }

    MonoImage* img = p_mono_assembly_get_image(helperAsm);
    MonoClass* cls = p_mono_class_from_name(img, "ONIPreviewHelper", "Entry");
    if (!cls) {
        fprintf(stderr, "Class ONIPreviewHelper.Entry not found\n");
        return 7;
    }

    MonoMethod* meth = p_mono_class_get_method_from_name(cls, "RunHeadlessWorker", 1);
    if (!meth) {
        fprintf(stderr, "Method RunHeadlessWorker(string) not found\n");
        return 8;
    }

    MonoString* arg = p_mono_string_new(g_domain, gameU8);
    void* args[1] = { arg };
    MonoObject* exc = nullptr;
    MonoObject* res = p_mono_runtime_invoke(meth, nullptr, args, &exc);
    if (exc) {
        PrintException(exc);
        fflush(stderr);
        _exit(9);
    }

    int rc = 0;
    if (res) rc = *(int32_t*)p_mono_object_unbox(res);
    fflush(stdout);
    fflush(stderr);
    _exit(rc);
}

// F1VoiceMapper — standalone radio-voice redirection for F1 Manager 2024.
//
// Clean-room reimplementation of the behaviour observed in Frost Voice Mapper
// Native (see docs/voice-manager-analysis.md), without any UE4SS C++ ABI
// dependency: this is a plain Win32 DLL. It is loaded by a tiny UE4SS Lua mod
// through package.loadlib(path, "*"), so it works with any UE4SS build.
//
// Hooked function (found by byte signature, not by fixed RVA):
//   UDialogueSpeaker* FindSpeaker(void* owner, int32 staffId, int32 role)
//   - iterates TArray<UDialogueSpeaker*> at owner+0x1C0 (Num at +0x1C8)
//   - returns the speaker with speaker+0x2C == staffId and speaker+0x30 == role
// The game reaches it through a `jmp rel32` stub; like the original mod we
// retarget that stub to a near thunk instead of patching protected code.
//
// mappings.txt (next to the DLL):
//   D:<source driver staff id>=<donor driver staff id>
//   E:<source engineer staff id>=<donor engineer staff id>
//   '#' or ';' start a comment. Unprefixed lines are driver mappings.
//   The file is watched: edits apply to the next radio message, no restart.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <atomic>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwctype>
#include <string>
#include <unordered_map>

namespace {

constexpr uint8_t kSignature[] = {
    0x48, 0x8B, 0x81, 0xC0, 0x01, 0x00, 0x00,  // mov    rax, [rcx+1C0h]
    0x48, 0x63, 0x89, 0xC8, 0x01, 0x00, 0x00,  // movsxd rcx, dword [rcx+1C8h]
    0x4C, 0x8D, 0x0C, 0xC8,                    // lea    r9, [rax+rcx*8]
};
constexpr uint32_t kKnownStubRva = 0x1E941C0;  // F1M24 1.11 (both known exe builds)

constexpr ptrdiff_t kSpeakerStaffId = 0x2C;
constexpr ptrdiff_t kSpeakerRole = 0x30;
constexpr ptrdiff_t kSpeakerType = 0x34;  // 1 = driver, 2 = race engineer

constexpr uint8_t kTypeDriver = 1;
constexpr uint8_t kTypeEngineer = 2;

struct Mapping {
    int32_t donor;
    uint8_t type;
};

using FindSpeakerFn = void* (*)(void* owner, int32_t staffId, int32_t role);

FindSpeakerFn g_original = nullptr;
using MappingTable = std::unordered_map<int32_t, Mapping>;

// Published atomically; replaced tables are intentionally leaked (a few bytes
// per reload) so a game thread can never read a freed table.
std::atomic<const MappingTable*> g_mappings{new MappingTable()};
std::wstring g_dir;
SRWLOCK g_logLock = SRWLOCK_INIT;
volatile LONG g_observedLogged = 0;
volatile LONG g_mappedLogged = 0;

void Log(const char* fmt, ...) {
    AcquireSRWLockExclusive(&g_logLock);
    FILE* f = nullptr;
    if (_wfopen_s(&f, (g_dir + L"F1VoiceMapper.log").c_str(), L"a") == 0 && f) {
        SYSTEMTIME t;
        GetLocalTime(&t);
        fprintf(f, "[%02d:%02d:%02d] ", t.wHour, t.wMinute, t.wSecond);
        va_list args;
        va_start(args, fmt);
        vfprintf(f, fmt, args);
        va_end(args);
        fputc('\n', f);
        fclose(f);
    }
    ReleaseSRWLockExclusive(&g_logLock);
}

bool ReadInt(const char*& p, int32_t& out) {
    while (*p == ' ' || *p == '\t') ++p;
    char* end = nullptr;
    long v = strtol(p, &end, 10);
    if (end == p) return false;
    p = end;
    out = static_cast<int32_t>(v);
    return true;
}

void LoadMappings() {
    auto* table = new MappingTable();
    FILE* f = nullptr;
    if (_wfopen_s(&f, (g_dir + L"mappings.txt").c_str(), L"r") != 0 || !f) {
        Log("No mappings.txt found; voice redirection disabled.");
        g_mappings.store(table, std::memory_order_release);
        return;
    }
    char line[512];
    int lineNo = 0;
    while (fgets(line, sizeof line, f)) {
        ++lineNo;
        if (char* c = strpbrk(line, "#;\r\n")) *c = '\0';
        const char* p = line;
        while (*p == ' ' || *p == '\t') ++p;
        if (!*p) continue;

        uint8_t type = kTypeDriver;
        if ((p[0] == 'D' || p[0] == 'd') && p[1] == ':') {
            p += 2;
        } else if ((p[0] == 'E' || p[0] == 'e') && p[1] == ':') {
            type = kTypeEngineer;
            p += 2;
        }
        int32_t source = 0, donor = 0;
        if (!ReadInt(p, source)) { Log("Ignored line %d: bad source id.", lineNo); continue; }
        while (*p == ' ' || *p == '\t') ++p;
        if (*p++ != '=') { Log("Ignored line %d: missing '='.", lineNo); continue; }
        if (!ReadInt(p, donor) || source <= 0 || donor <= 0) {
            Log("Ignored line %d: bad donor id.", lineNo);
            continue;
        }
        (*table)[source] = {donor, type};
        Log("Mapping %c:%d -> %d", type == kTypeDriver ? 'D' : 'E', source, donor);
    }
    fclose(f);
    g_mappings.store(table, std::memory_order_release);
    InterlockedExchange(&g_mappedLogged, 0);
    Log("Loaded %zu voice mapping(s).", table->size());
}

FILETIME MappingsWriteTime() {
    WIN32_FILE_ATTRIBUTE_DATA data{};
    if (!GetFileAttributesExW((g_dir + L"mappings.txt").c_str(), GetFileExInfoStandard, &data)) return {};
    return data.ftLastWriteTime;
}

DWORD WINAPI WatchThread(LPVOID) {
    FILETIME last = MappingsWriteTime();
    for (;;) {
        Sleep(1000);
        FILETIME now = MappingsWriteTime();
        if (CompareFileTime(&now, &last) != 0) {
            last = now;
            Sleep(200);  // let the editor finish writing
            Log("mappings.txt changed; reloading.");
            LoadMappings();
        }
    }
}

bool SpeakerMatches(void* s, int32_t id, int32_t role, uint8_t type) {
    auto* b = static_cast<uint8_t*>(s);
    return *reinterpret_cast<int32_t*>(b + kSpeakerStaffId) == id &&
           *reinterpret_cast<int32_t*>(b + kSpeakerRole) == role &&
           *(b + kSpeakerType) == type;
}

void* HookedFindSpeaker(void* owner, int32_t staffId, int32_t role) {
    const MappingTable& table = *g_mappings.load(std::memory_order_acquire);
    auto it = table.find(staffId);
    if (it == table.end()) {
        void* r = g_original(owner, staffId, role);
        if (InterlockedIncrement(&g_observedLogged) <= 40) {
            Log("Observed lookup id=%d role=%d type=%d -> %s", staffId, role,
                r ? *(static_cast<uint8_t*>(r) + kSpeakerType) : -1, r ? "found" : "none");
        }
        return r;
    }

    const Mapping& m = it->second;
    void* donor = g_original(owner, m.donor, role);
    if (donor && SpeakerMatches(donor, m.donor, role, m.type)) {
        if (InterlockedIncrement(&g_mappedLogged) <= 40) {
            Log("Mapped id=%d -> %d (role=%d type=%d).", staffId, m.donor, role, m.type);
        }
        return donor;
    }
    Log("Could not resolve donor %d for source %d (role=%d); using original.", m.donor, staffId, role);
    return g_original(owner, staffId, role);
}

bool IsExecutable(DWORD protect) {
    return (protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE |
                       PAGE_EXECUTE_WRITECOPY)) != 0;
}

uint8_t* FindSignature(uint8_t* base) {
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    auto* sec = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++sec) {
        if (!(sec->Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;
        uint8_t* p = base + sec->VirtualAddress;
        size_t n = sec->Misc.VirtualSize;
        if (n < sizeof kSignature) continue;
        // Walk committed, readable regions only.
        for (size_t off = 0; off < n;) {
            MEMORY_BASIC_INFORMATION mbi{};
            if (!VirtualQuery(p + off, &mbi, sizeof mbi)) break;
            size_t regionEnd = static_cast<size_t>(static_cast<uint8_t*>(mbi.BaseAddress) +
                                                   mbi.RegionSize - p);
            if (regionEnd > n) regionEnd = n;
            if (mbi.State == MEM_COMMIT && IsExecutable(mbi.Protect) && !(mbi.Protect & PAGE_GUARD)) {
                for (size_t k = off; k + sizeof kSignature <= regionEnd; ++k) {
                    if (p[k] == kSignature[0] && memcmp(p + k, kSignature, sizeof kSignature) == 0) {
                        return p + k;
                    }
                }
            }
            off = regionEnd;
        }
    }
    return nullptr;
}

uint8_t* JmpTarget(uint8_t* stub) {
    return stub + 5 + *reinterpret_cast<int32_t*>(stub + 1);
}

// Finds the `jmp rel32` stub that leads to target: known RVA first, then a scan.
uint8_t* FindStub(uint8_t* base, uint8_t* target) {
    uint8_t* known = base + kKnownStubRva;
    MEMORY_BASIC_INFORMATION mbi{};
    if (VirtualQuery(known, &mbi, sizeof mbi) && mbi.State == MEM_COMMIT && *known == 0xE9 &&
        JmpTarget(known) == target) {
        return known;
    }
    Log("Known stub RVA did not match; scanning for the stub.");
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + reinterpret_cast<IMAGE_DOS_HEADER*>(base)->e_lfanew);
    auto* sec = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++sec) {
        if (!(sec->Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;
        uint8_t* p = base + sec->VirtualAddress;
        size_t n = sec->Misc.VirtualSize;
        for (size_t off = 0; off < n;) {
            if (!VirtualQuery(p + off, &mbi, sizeof mbi)) break;
            size_t regionEnd = static_cast<size_t>(static_cast<uint8_t*>(mbi.BaseAddress) +
                                                   mbi.RegionSize - p);
            if (regionEnd > n) regionEnd = n;
            if (mbi.State == MEM_COMMIT && IsExecutable(mbi.Protect) && !(mbi.Protect & PAGE_GUARD)) {
                for (size_t k = off; k + 5 <= regionEnd; ++k) {
                    if (p[k] == 0xE9 && JmpTarget(p + k) == target) return p + k;
                }
            }
            off = regionEnd;
        }
    }
    return nullptr;
}

uint8_t* AllocNear(uint8_t* anchor) {
    SYSTEM_INFO si;
    GetSystemInfo(&si);
    const uintptr_t gran = si.dwAllocationGranularity;
    const uintptr_t origin = reinterpret_cast<uintptr_t>(anchor) & ~(gran - 1);
    const uintptr_t range = 0x7FFF0000;
    for (uintptr_t delta = gran; delta < range; delta += gran) {
        for (int dir = -1; dir <= 1; dir += 2) {
            uintptr_t addr = dir < 0 ? origin - delta : origin + delta;
            if (dir < 0 && delta > origin) continue;
            void* mem = VirtualAlloc(reinterpret_cast<void*>(addr), si.dwPageSize,
                                     MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE);
            if (mem) return static_cast<uint8_t*>(mem);
        }
    }
    return nullptr;
}

bool InstallHook() {
    auto* base = reinterpret_cast<uint8_t*>(GetModuleHandleW(nullptr));
    if (!base) { Log("ERROR: game module not found."); return false; }

    uint8_t* target = FindSignature(base);
    if (!target) { Log("ERROR: speaker lookup signature not found; nothing patched."); return false; }
    Log("Speaker lookup found at RVA 0x%llX.", static_cast<unsigned long long>(target - base));

    uint8_t* stub = FindStub(base, target);
    if (!stub) { Log("ERROR: no jmp stub leads to the speaker lookup; nothing patched."); return false; }
    Log("Jump stub at RVA 0x%llX.", static_cast<unsigned long long>(stub - base));

    uint8_t* thunk = AllocNear(stub);
    if (!thunk) { Log("ERROR: could not allocate a thunk within 2 GB of the stub."); return false; }
    // mov rax, imm64 ; jmp rax
    thunk[0] = 0x48; thunk[1] = 0xB8;
    void* hook = reinterpret_cast<void*>(&HookedFindSpeaker);
    memcpy(thunk + 2, &hook, sizeof hook);
    thunk[10] = 0xFF; thunk[11] = 0xE0;
    FlushInstructionCache(GetCurrentProcess(), thunk, 12);

    g_original = reinterpret_cast<FindSpeakerFn>(target);

    int64_t rel = thunk - (stub + 5);
    if (rel < INT32_MIN || rel > INT32_MAX) { Log("ERROR: thunk out of rel32 range."); return false; }
    DWORD old = 0;
    if (!VirtualProtect(stub, 5, PAGE_EXECUTE_READWRITE, &old)) {
        Log("ERROR: VirtualProtect failed (%lu).", GetLastError());
        return false;
    }
    int32_t rel32 = static_cast<int32_t>(rel);
    memcpy(stub + 1, &rel32, sizeof rel32);
    VirtualProtect(stub, 5, old, &old);
    FlushInstructionCache(GetCurrentProcess(), stub, 5);
    Log("Hook installed.");
    return true;
}

// ---------------------------------------------------------------------------
// Wwise observers (research step for custom voice lines).
// Radio lines are Wwise external sources (TeamCommsDrivers/Engineers.pck). The
// sound engine API is exported by the exe, so we hook the exported entry points
// that can carry external sources and log what the game passes:
//   AK::SoundEngine::PostEvent (3 overloads)
//   AK::SoundEngine::DynamicSequence::LockPlaylist / UnlockPlaylist
// Each hooked function starts with 15 bytes of position-independent register
// spills, which we move to a trampoline and replace with `jmp [rip]`.

constexpr size_t kPatchLen = 15;

struct InlineHook {
    const char* label;
    const char* exportName;
    uint8_t prologue[kPatchLen];
    void* detour;
    void** original;
    volatile LONG calls;
};

// Copies a wide string from game memory into ASCII, tolerating bad pointers.
bool SafeWide(const wchar_t* s, char* out, size_t cap) {
    __try {
        size_t i = 0;
        for (; i + 1 < cap && s[i]; ++i) out[i] = s[i] < 128 ? static_cast<char>(s[i]) : '?';
        out[i] = '\0';
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        out[0] = '\0';
        return false;
    }
}

bool SafeAnsi(const char* s, char* out, size_t cap) {
    __try {
        size_t i = 0;
        for (; i + 1 < cap && s[i]; ++i) out[i] = s[i];
        out[i] = '\0';
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        out[0] = '\0';
        return false;
    }
}

// Hex dump of n bytes (n <= 64) into out (>= 3*n+1), tolerating bad pointers.
bool SafeHex(const void* p, size_t n, char* out) {
    out[0] = '\0';
    __try {
        auto* b = static_cast<const uint8_t*>(p);
        for (size_t k = 0; k < n; ++k) sprintf_s(out + k * 3, 4, "%02X ", b[k]);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        strcpy_s(out, 8, "<bad>");
        return false;
    }
}

bool SafeRead(const void* p, void* out, size_t n) {
    __try {
        memcpy(out, p, n);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

// Assumed AkExternalSourceInfo layout (32 bytes): cookie u32, codec u32,
// szFile wchar_t*, pInMemory, uiMemorySize u32, idFile u32. Raw bytes are
// logged too so the layout can be checked.
void LogExternal(const char* tag, uint32_t i, const uint8_t* e) {
    uint8_t c[32];
    if (!SafeRead(e, c, sizeof c)) { Log("  %s[%u] <unreadable %p>", tag, i, e); return; }
    char raw[3 * 32 + 1];
    SafeHex(c, 32, raw);
    char file[260] = "";
    const wchar_t* sz = nullptr;
    memcpy(&sz, c + 8, sizeof sz);
    if (sz) SafeWide(sz, file, sizeof file);
    Log("  %s[%u] cookie=%u codec=%u file='%s' raw=%s", tag, i, *reinterpret_cast<uint32_t*>(c),
        *reinterpret_cast<uint32_t*>(c + 4), file, raw);
}

volatile LONG g_detailLogged = 0;
bool WantDetail() { return InterlockedIncrement(&g_detailLogged) <= 600; }

using PostEventIdFn = uint32_t (*)(uint32_t, uint64_t, uint32_t, void*, void*, uint32_t, uint8_t*, uint32_t);
using PostEventStrFn = uint32_t (*)(const char*, uint64_t, uint32_t, void*, void*, uint32_t, uint8_t*, uint32_t);
using PostEventWStrFn = uint32_t (*)(const wchar_t*, uint64_t, uint32_t, void*, void*, uint32_t, uint8_t*, uint32_t);
using LockPlaylistFn = void* (*)(uint32_t playingId);
using UnlockPlaylistFn = int (*)(uint32_t playingId);

PostEventIdFn g_postId = nullptr;
PostEventStrFn g_postStr = nullptr;
PostEventWStrFn g_postWStr = nullptr;
LockPlaylistFn g_lockPlaylist = nullptr;
UnlockPlaylistFn g_unlockPlaylist = nullptr;
thread_local void* t_lockedPlaylist = nullptr;

uint32_t HookedPostEventId(uint32_t, uint64_t, uint32_t, void*, void*, uint32_t, uint8_t*, uint32_t);
uint32_t HookedPostEventStr(const char*, uint64_t, uint32_t, void*, void*, uint32_t, uint8_t*, uint32_t);
uint32_t HookedPostEventWStr(const wchar_t*, uint64_t, uint32_t, void*, void*, uint32_t, uint8_t*, uint32_t);
void* HookedLockPlaylist(uint32_t);
int HookedUnlockPlaylist(uint32_t);

// Spills: mov [rsp+8],rbx ; mov [rsp+10h],rbp|rsi ; mov [rsp+18h],rsi|rdi  or  ... ; push rdi ; sub rsp,20h
InlineHook g_hooks[] = {
    {"PostEvent(id)",
     "?PostEvent@SoundEngine@AK@@YAII_KIP6AXW4AkCallbackType@@PEAUAkCallbackInfo@@@ZPEAXIPEAUAkExternalSourceInfo@@I@Z",
     {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x6C, 0x24, 0x10, 0x48, 0x89, 0x74, 0x24, 0x18},
     reinterpret_cast<void*>(&HookedPostEventId), reinterpret_cast<void**>(&g_postId), 0},
    {"PostEvent(char*)",
     "?PostEvent@SoundEngine@AK@@YAIPEBD_KIP6AXW4AkCallbackType@@PEAUAkCallbackInfo@@@ZPEAXIPEAUAkExternalSourceInfo@@I@Z",
     {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x10, 0x48, 0x89, 0x7C, 0x24, 0x18},
     reinterpret_cast<void*>(&HookedPostEventStr), reinterpret_cast<void**>(&g_postStr), 0},
    {"PostEvent(wchar*)",
     "?PostEvent@SoundEngine@AK@@YAIPEB_W_KIP6AXW4AkCallbackType@@PEAUAkCallbackInfo@@@ZPEAXIPEAUAkExternalSourceInfo@@I@Z",
     {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x6C, 0x24, 0x10, 0x48, 0x89, 0x7C, 0x24, 0x18},
     reinterpret_cast<void*>(&HookedPostEventWStr), reinterpret_cast<void**>(&g_postWStr), 0},
    {"LockPlaylist",
     "?LockPlaylist@DynamicSequence@SoundEngine@AK@@YAPEAVPlaylist@123@I@Z",
     {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x10, 0x57, 0x48, 0x83, 0xEC, 0x20},
     reinterpret_cast<void*>(&HookedLockPlaylist), reinterpret_cast<void**>(&g_lockPlaylist), 0},
    {"UnlockPlaylist",
     "?UnlockPlaylist@DynamicSequence@SoundEngine@AK@@YA?AW4AKRESULT@@I@Z",
     {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x10, 0x57, 0x48, 0x83, 0xEC, 0x20},
     reinterpret_cast<void*>(&HookedUnlockPlaylist), reinterpret_cast<void**>(&g_unlockPlaylist), 0},
};
enum { kPostId, kPostStr, kPostWStr, kLock, kUnlock };

void LogPostExternals(const char* name, uint64_t obj, uint32_t n, uint8_t* ext) {
    Log("PostEvent %s obj=%llu externals=%u", name, static_cast<unsigned long long>(obj), n);
    for (uint32_t i = 0; i < n && i < 4; ++i) LogExternal("ext", i, ext + i * 32);
}

uint32_t HookedPostEventId(uint32_t id, uint64_t obj, uint32_t fl, void* cb, void* ck, uint32_t n, uint8_t* ext,
                           uint32_t pid) {
    InterlockedIncrement(&g_hooks[kPostId].calls);
    if (n && ext && WantDetail()) {
        char name[16];
        sprintf_s(name, "id=%u", id);
        LogPostExternals(name, obj, n, ext);
    }
    return g_postId(id, obj, fl, cb, ck, n, ext, pid);
}

uint32_t HookedPostEventStr(const char* ev, uint64_t obj, uint32_t fl, void* cb, void* ck, uint32_t n, uint8_t* ext,
                            uint32_t pid) {
    InterlockedIncrement(&g_hooks[kPostStr].calls);
    if (n && ext && WantDetail()) {
        char name[200] = "";
        if (ev) SafeAnsi(ev, name, sizeof name);
        LogPostExternals(name, obj, n, ext);
    }
    return g_postStr(ev, obj, fl, cb, ck, n, ext, pid);
}

// ---------------------------------------------------------------------------
// Custom voice lines. The game posts Play_ExtSources_TeamComms with one
// external source whose szFile is e.g. TeamComms\Drivers\LHamilton\013046_DR_LH.wem.
// If packs\<Folder>\<line>.wem (or packs\<Folder>\_any.wem, for testing)
// exists next to the DLL, we hand Wwise that file from memory instead.
// Pack files are Wwise .wem: Vorbis (fmt 0xFFFF) or PCM (fmt 0xFFFE).

constexpr uint32_t kCodecPcm = 1;
constexpr uint32_t kCodecVorbis = 4;

struct PackFile {
    std::string data;
    uint32_t codec;
};

SRWLOCK g_packLock = SRWLOCK_INIT;
// Loaded files stay alive for the whole session: Wwise streams from pInMemory
// after PostEvent returns.
std::unordered_map<std::wstring, PackFile*> g_packCache;

bool SafeCopyWide(const wchar_t* s, wchar_t* out, size_t cap) {
    __try {
        size_t i = 0;
        for (; i + 1 < cap && s[i]; ++i) out[i] = s[i];
        out[i] = L'\0';
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        out[0] = L'\0';
        return false;
    }
}

const PackFile* LoadPackFile(const std::wstring& path) {
    AcquireSRWLockShared(&g_packLock);
    auto it = g_packCache.find(path);
    const PackFile* hit = it != g_packCache.end() ? it->second : nullptr;
    ReleaseSRWLockShared(&g_packLock);
    if (hit) return hit;

    FILE* f = nullptr;
    if (_wfopen_s(&f, path.c_str(), L"rb") != 0 || !f) return nullptr;
    auto* pf = new PackFile();
    char buf[65536];
    size_t got;
    while ((got = fread(buf, 1, sizeof buf, f)) > 0) pf->data.append(buf, got);
    fclose(f);
    const std::string& d = pf->data;
    uint16_t tag = d.size() >= 22 ? *reinterpret_cast<const uint16_t*>(d.data() + 20) : 0;
    if (d.size() < 44 || d.compare(0, 4, "RIFF") != 0 || d.compare(8, 8, "WAVEfmt ") != 0 ||
        (tag != 0xFFFE && tag != 0xFFFF)) {
        Log("Pack file rejected (not a Wwise .wem): %ls", path.c_str());
        delete pf;
        return nullptr;
    }
    pf->codec = tag == 0xFFFF ? kCodecVorbis : kCodecPcm;
    AcquireSRWLockExclusive(&g_packLock);
    auto [pos, inserted] = g_packCache.emplace(path, pf);
    ReleaseSRWLockExclusive(&g_packLock);
    if (!inserted) { delete pf; return pos->second; }
    return pf;
}

bool FileExists(const std::wstring& p) {
    DWORD a = GetFileAttributesW(p.c_str());
    return a != INVALID_FILE_ATTRIBUTES && !(a & FILE_ATTRIBUTE_DIRECTORY);
}

// packs\<Folder>\lines.txt maps game lines to recorded files, so one
// recording serves every line with the same text:  150048_EN_AB.wem=0001.wem
using AliasMap = std::unordered_map<std::wstring, std::wstring>;
std::unordered_map<std::wstring, const AliasMap*> g_aliases;  // by pack dir, guarded by g_packLock

std::wstring Lower(std::wstring s) {
    for (auto& c : s) c = static_cast<wchar_t>(towlower(c));
    return s;
}

const AliasMap* PackAliases(const std::wstring& dir) {
    AcquireSRWLockShared(&g_packLock);
    auto it = g_aliases.find(dir);
    const AliasMap* hit = it != g_aliases.end() ? it->second : nullptr;
    ReleaseSRWLockShared(&g_packLock);
    if (hit) return hit;

    auto* map = new AliasMap();
    FILE* f = nullptr;
    if (_wfopen_s(&f, (dir + L"lines.txt").c_str(), L"r, ccs=UTF-8") == 0 && f) {
        wchar_t line[512];
        while (fgetws(line, ARRAYSIZE(line), f)) {
            if (wchar_t* c = wcspbrk(line, L"#\r\n")) *c = L'\0';
            wchar_t* eq = wcschr(line, L'=');
            if (!eq || eq == line) continue;
            *eq = L'\0';
            (*map)[Lower(line)] = eq + 1;
        }
        fclose(f);
        Log("Pack %ls: %zu aliased lines.", dir.c_str() + g_dir.size(), map->size());
    }
    AcquireSRWLockExclusive(&g_packLock);
    auto [pos, inserted] = g_aliases.emplace(dir, map);
    ReleaseSRWLockExclusive(&g_packLock);
    if (!inserted) { delete map; return pos->second; }
    return map;
}

// Returns true and fills out (one 32-byte AkExternalSourceInfo) when a pack
// file replaces the line referenced by src.
bool SubstituteLine(const uint8_t* src, uint8_t* out) {
    const wchar_t* sz = nullptr;
    memcpy(&sz, src + 8, sizeof sz);
    if (!sz) return false;
    wchar_t path[260];
    if (!SafeCopyWide(sz, path, ARRAYSIZE(path))) return false;
    // TeamComms\<Role>\<Folder>\<line>.wem
    std::wstring p(path);
    if (_wcsnicmp(p.c_str(), L"TeamComms\\", 10) != 0) return false;
    size_t a = p.find(L'\\', 10);
    size_t b = a == std::wstring::npos ? a : p.find(L'\\', a + 1);
    if (b == std::wstring::npos) return false;
    std::wstring folder = p.substr(a + 1, b - a - 1);
    std::wstring line = p.substr(b + 1);
    std::wstring dir = g_dir + L"packs\\" + folder + L"\\";
    if (!FileExists(dir + L"lines.txt") && !FileExists(dir + line) && !FileExists(dir + L"_any.wem")) return false;
    std::wstring file = dir + line;
    const AliasMap& aliases = *PackAliases(dir);
    auto alias = aliases.find(Lower(line));
    if (alias != aliases.end()) file = dir + alias->second;
    if (!FileExists(file)) {
        file = dir + L"_any.wem";
        if (!FileExists(file)) return false;
    }
    const PackFile* pf = LoadPackFile(file);
    if (!pf) return false;
    memcpy(out, src, 32);
    void* mem = const_cast<char*>(pf->data.data());
    uint32_t size = static_cast<uint32_t>(pf->data.size());
    const wchar_t* none = nullptr;
    memcpy(out + 4, &pf->codec, 4);   // idCodec
    memcpy(out + 8, &none, 8);        // szFile
    memcpy(out + 16, &mem, 8);        // pInMemory
    memcpy(out + 24, &size, 4);       // uiMemorySize
    memset(out + 28, 0, 4);           // idFile
    static volatile LONG logged = 0;
    if (InterlockedIncrement(&logged) <= 200) {
        Log("Custom line: %ls -> %ls (codec %u, %u bytes)", path, file.c_str() + g_dir.size(), pf->codec, size);
    }
    return true;
}

uint32_t HookedPostEventWStr(const wchar_t* ev, uint64_t obj, uint32_t fl, void* cb, void* ck, uint32_t n,
                             uint8_t* ext, uint32_t pid) {
    InterlockedIncrement(&g_hooks[kPostWStr].calls);
    if (n && ext && WantDetail()) {
        char name[200] = "";
        if (ev) SafeWide(ev, name, sizeof name);
        LogPostExternals(name, obj, n, ext);
    }
    if (n == 1 && ext) {
        // Research: dump the callback cookie of radio lines to find who is speaking.
        static volatile LONG cookieDumps = 0;
        const wchar_t* sz = nullptr;
        memcpy(&sz, ext + 8, sizeof sz);
        wchar_t probe[12] = L"";
        if (sz) SafeCopyWide(sz, probe, ARRAYSIZE(probe));
        if (_wcsnicmp(probe, L"TeamComms\\", 10) == 0 && InterlockedIncrement(&cookieDumps) <= 40) {
            char file[160] = "";
            SafeWide(sz, file, sizeof file);
            Log("Cookie for %s: flags=0x%X cb=%p cookie=%p pid=%u", file, fl, cb, ck, pid);
            for (int off = 0; ck && off < 0x180; off += 32) {
                char hex[3 * 32 + 1];
                SafeHex(static_cast<uint8_t*>(ck) + off, 32, hex);
                Log("  +%03X %s", off, hex);
            }
        }
        uint8_t replaced[32];
        if (SubstituteLine(ext, replaced)) return g_postWStr(ev, obj, fl, cb, ck, n, replaced, pid);
    }
    return g_postWStr(ev, obj, fl, cb, ck, n, ext, pid);
}

void* HookedLockPlaylist(uint32_t pid) {
    InterlockedIncrement(&g_hooks[kLock].calls);
    void* pl = g_lockPlaylist(pid);
    t_lockedPlaylist = pl;
    return pl;
}

// Playlist is AkArray<PlaylistItem>: { PlaylistItem* items; u32 length; u32 reserved; }.
// PlaylistItem (assumed 24 bytes): { u32 audioNodeID; s32 msDelay; void* customInfo;
// AkExternalSourceArray* externals }, externals = { u32 refCount; u32 count; AkExternalSourceInfo[] }.
void DumpPlaylist(uint32_t pid, const uint8_t* pl) {
    uint8_t arr[16];
    if (!SafeRead(pl, arr, sizeof arr)) { Log("Playlist pid=%u <unreadable %p>", pid, pl); return; }
    const uint8_t* items = nullptr;
    memcpy(&items, arr, sizeof items);
    uint32_t len = *reinterpret_cast<uint32_t*>(arr + 8);
    Log("Playlist pid=%u items=%u", pid, len);
    if (len > 16) len = 16;
    for (uint32_t i = 0; i < len; ++i) {
        uint8_t it[24];
        if (!SafeRead(items + i * 24, it, sizeof it)) { Log("  item[%u] <unreadable>", i); break; }
        char raw[3 * 24 + 1];
        SafeHex(it, 24, raw);
        const uint8_t* ext = nullptr;
        memcpy(&ext, it + 16, sizeof ext);
        Log("  item[%u] node=%u delay=%d raw=%s", i, *reinterpret_cast<uint32_t*>(it),
            *reinterpret_cast<int32_t*>(it + 4), raw);
        if (!ext) continue;
        char head[3 * 48 + 1];
        SafeHex(ext, 48, head);
        Log("    externals@%p head=%s", ext, head);
        uint32_t cnt = 0;
        if (SafeRead(ext + 4, &cnt, 4) && cnt > 0 && cnt <= 4) {
            for (uint32_t k = 0; k < cnt; ++k) LogExternal("src", k, ext + 8 + k * 32);
        }
    }
}

int HookedUnlockPlaylist(uint32_t pid) {
    InterlockedIncrement(&g_hooks[kUnlock].calls);
    void* pl = t_lockedPlaylist;
    t_lockedPlaylist = nullptr;
    if (pl && WantDetail()) DumpPlaylist(pid, static_cast<const uint8_t*>(pl));
    return g_unlockPlaylist(pid);
}

bool InstallInline(InlineHook& h) {
    auto* base = reinterpret_cast<uint8_t*>(GetModuleHandleW(nullptr));
    auto* fn = reinterpret_cast<uint8_t*>(GetProcAddress(reinterpret_cast<HMODULE>(base), h.exportName));
    if (!fn) { Log("%s: export not found; skipped.", h.label); return false; }
    if (memcmp(fn, h.prologue, kPatchLen) != 0) {
        char raw[3 * kPatchLen + 1];
        SafeHex(fn, kPatchLen, raw);
        Log("%s: unexpected prologue (%s); skipped.", h.label, raw);
        return false;
    }
    auto* tramp = static_cast<uint8_t*>(VirtualAlloc(nullptr, 64, MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
    if (!tramp) { Log("%s: trampoline allocation failed.", h.label); return false; }
    // Trampoline: original prologue, then jmp [rip] -> fn + 15.
    memcpy(tramp, fn, kPatchLen);
    uint8_t* back = fn + kPatchLen;
    uint8_t* j = tramp + kPatchLen;
    j[0] = 0xFF; j[1] = 0x25; memset(j + 2, 0, 4); memcpy(j + 6, &back, 8);
    FlushInstructionCache(GetCurrentProcess(), tramp, 64);
    *h.original = tramp;

    // Patch: jmp [rip] -> detour, plus one nop.
    uint8_t patch[kPatchLen];
    patch[0] = 0xFF; patch[1] = 0x25; memset(patch + 2, 0, 4); memcpy(patch + 6, &h.detour, 8); patch[14] = 0x90;
    DWORD old = 0;
    if (!VirtualProtect(fn, kPatchLen, PAGE_EXECUTE_READWRITE, &old)) {
        Log("%s: VirtualProtect failed (%lu).", h.label, GetLastError());
        return false;
    }
    memcpy(fn, patch, kPatchLen);
    VirtualProtect(fn, kPatchLen, old, &old);
    FlushInstructionCache(GetCurrentProcess(), fn, kPatchLen);
    Log("%s: observer installed at RVA 0x%llX.", h.label, static_cast<unsigned long long>(fn - base));
    return true;
}

// Logs per-function call counts every 30 s while they change.
DWORD WINAPI StatsThread(LPVOID) {
    LONG last[ARRAYSIZE(g_hooks)] = {};
    for (;;) {
        Sleep(30000);
        bool changed = false;
        char line[256] = "Wwise calls:";
        for (size_t i = 0; i < ARRAYSIZE(g_hooks); ++i) {
            LONG c = g_hooks[i].calls;
            changed |= c != last[i];
            last[i] = c;
            char part[64];
            sprintf_s(part, " %s=%ld", g_hooks[i].label, c);
            strcat_s(line, part);
        }
        if (changed) Log("%s", line);
    }
}

DWORD WINAPI InitThread(LPVOID) {
    Log("F1VoiceMapper 0.4.2-dev loaded.");
    for (auto& h : g_hooks) InstallInline(h);
    if (HANDLE t = CreateThread(nullptr, 0, StatsThread, nullptr, 0, nullptr)) CloseHandle(t);
    LoadMappings();
    if (g_mappings.load()->empty()) {
        Log("No mappings: running in observe-only mode.");
    }
    if (InstallHook()) {
        if (HANDLE t = CreateThread(nullptr, 0, WatchThread, nullptr, 0, nullptr)) CloseHandle(t);
    }
    return 0;
}

}  // namespace

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(inst);
        wchar_t path[MAX_PATH];
        DWORD len = GetModuleFileNameW(inst, path, MAX_PATH);
        g_dir.assign(path, len);
        g_dir.erase(g_dir.find_last_of(L"\\/") + 1);
        if (HANDLE t = CreateThread(nullptr, 0, InitThread, nullptr, 0, nullptr)) CloseHandle(t);
    }
    return TRUE;
}

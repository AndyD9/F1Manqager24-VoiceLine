// Offline test for F1VoiceMapper: embeds the game's exact FindSpeaker bytes and a
// `jmp rel32` stub in an executable section of this exe, builds fake speakers,
// loads the DLL (which scans *this* module) and checks the redirection.
//
// Expects mappings.txt next to the DLL with: D:17=10, E:682=51, D:99=12345.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

#pragma section(".gtext", read, execute)
// [0]  jmp +11  -> [16]
// [16] the game's FindSpeaker (50 bytes, copied from F1Manager24.exe RVA 0x10A72480)
__declspec(allocate(".gtext")) const unsigned char kGameCode[] = {
    0xE9, 0x0B, 0x00, 0x00, 0x00, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC,
    0x48, 0x8B, 0x81, 0xC0, 0x01, 0x00, 0x00, 0x48, 0x63, 0x89, 0xC8, 0x01, 0x00, 0x00, 0x4C, 0x8D,
    0x0C, 0xC8, 0x4C, 0x39, 0xC8, 0x74, 0x18, 0x48, 0x8B, 0x08, 0x44, 0x39, 0x41, 0x30, 0x75, 0x05,
    0x39, 0x51, 0x2C, 0x74, 0x06, 0x48, 0x83, 0xC0, 0x08, 0xEB, 0xE7, 0x48, 0x89, 0xC8, 0xC3,
    0x31, 0xC0, 0xC3,
};

struct Speaker {
    uint8_t pad[0x2C];
    int32_t staffId;  // +0x2C
    int32_t role;     // +0x30
    uint8_t type;     // +0x34
    uint8_t pad2[0x0B];
};
static_assert(offsetof(Speaker, staffId) == 0x2C && offsetof(Speaker, type) == 0x34);

struct Owner {
    uint8_t pad[0x1C0];
    Speaker** data;  // +0x1C0
    int32_t num;     // +0x1C8
};

using FindFn = Speaker* (*)(Owner*, int32_t, int32_t);

int g_failures = 0;
void Check(bool ok, const char* what) {
    printf("%s  %s\n", ok ? "PASS" : "FAIL", what);
    if (!ok) ++g_failures;
}

int main(int argc, char** argv) {
    if (argc < 2) { printf("usage: hook_test <F1VoiceMapper.dll>\n"); return 2; }

    std::vector<Speaker> speakers = {
        {{}, 17, 0, 1}, {{}, 10, 0, 1},    // drivers
        {{}, 682, 0, 2}, {{}, 51, 0, 2},   // engineers
        {{}, 99, 0, 1},                    // driver whose donor is missing
        {{}, 10, 3, 1},                    // same id, other role
    };
    std::vector<Speaker*> ptrs;
    for (auto& s : speakers) ptrs.push_back(&s);
    Owner owner{};
    owner.data = ptrs.data();
    owner.num = static_cast<int32_t>(ptrs.size());

    auto find = reinterpret_cast<FindFn>(const_cast<unsigned char*>(kGameCode));

    Check(find(&owner, 17, 0) == &speakers[0], "before hook: 17 -> own speaker");

    if (!LoadLibraryA(argv[1])) { printf("LoadLibrary failed %lu\n", GetLastError()); return 2; }
    Sleep(1500);  // hook is installed from a worker thread

    Check(kGameCode[0] == 0xE9 && *reinterpret_cast<const int32_t*>(kGameCode + 1) != 0x0B,
          "stub retargeted to thunk");
    Check(find(&owner, 17, 0) == &speakers[1], "D:17=10 -> driver 10");
    Check(find(&owner, 682, 0) == &speakers[3], "E:682=51 -> engineer 51");
    Check(find(&owner, 10, 0) == &speakers[1], "unmapped 10 -> itself");
    Check(find(&owner, 10, 3) == &speakers[5], "unmapped 10 role 3 -> role-3 speaker");
    Check(find(&owner, 99, 0) == &speakers[4], "D:99=12345 (missing donor) -> falls back to 99");
    Check(find(&owner, 17, 7) == nullptr, "mapped 17 with unknown role -> nullptr, no crash");
    Check(find(&owner, 5555, 0) == nullptr, "unknown id -> nullptr");

    // Hot reload: rewrite mappings.txt next to the DLL, wait for the watcher.
    char dllPath[MAX_PATH];
    GetFullPathNameA(argv[1], MAX_PATH, dllPath, nullptr);
    std::string mappings(dllPath);
    mappings = mappings.substr(0, mappings.find_last_of("\\/") + 1) + "mappings.txt";
    if (FILE* f = fopen(mappings.c_str(), "w")) { fputs("D:10=17\n", f); fclose(f); }
    Sleep(2500);
    Check(find(&owner, 10, 0) == &speakers[0], "hot reload: D:10=17 -> driver 17");
    Check(find(&owner, 17, 0) == &speakers[0], "hot reload: old D:17=10 removed");

    printf(g_failures ? "\n%d FAILURE(S)\n" : "\nALL PASSED\n", g_failures);
    return g_failures ? 1 : 0;
}

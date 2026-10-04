# F1VoiceMapper

Réattribue les voix radio (pilotes / ingénieurs de course) de F1 Manager 2024.
Réimplémentation propre du comportement de Frost Voice Mapper Native, **sans
dépendance à l'ABI C++ d'UE4SS** (cause du crash avec les builds UE4SS
expérimentales, voir `docs/voice-manager-analysis.md`).

## Fonctionnement

- DLL Win32 autonome, chargée par un mini-mod Lua UE4SS
  (`Scripts/main.lua` → `package.loadlib(dll, "*")`).
- `DllMain` lance un thread qui lit `mappings.txt`, trouve la fonction
  `FindSpeaker` **par signature** (18 octets), trouve le `jmp rel32` qui y mène
  (RVA connue `0x1E941C0`, sinon scan), et redirige ce `jmp` vers un thunk
  proche → `HookedFindSpeaker`.
- Si le donneur est introuvable (ou rôle/type différent) → appel original
  avec l'ID source : aucun risque de speaker nul introduit par le mod.
- Sans mapping : mode observation (journalise les 40 premières recherches).

## Fichiers

| Chemin | Rôle |
|---|---|
| `F1VoiceMapper.cpp` | DLL |
| `build.cmd` | compile avec MSVC (VS 2022) → `mod/F1VoiceMapper/F1VoiceMapper.dll` |
| `mod/F1VoiceMapper/` | dossier à copier dans `Win64\ue4ss\Mods\` |
| `test/hook_test.cpp` | test hors-jeu avec les octets exacts de la fonction du jeu |
| `test/build_test.cmd` | compile le test |

## mappings.txt

```text
D:17=10    # pilote 17 parle avec la voix du pilote 10
E:682=51   # ingénieur 682 avec la voix de l'ingénieur 51
```

IDs : `python tools/read_save_staff.py --f1`. Redémarrer le jeu après modification.

## Log

`F1VoiceMapper.log` à côté de la DLL : signature trouvée, stub, « Hook
installed. », puis `Mapped id=… -> …` pendant les échanges radio.

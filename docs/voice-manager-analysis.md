# Analyse — F1M24 Voice Manager v1.1.0

Source analysée : `C:\Users\andyd\Downloads\F1M24VoiceManager-v1.1.0-clean-download-win-x64`
Auteurs indiqués dans la DLL : « Codex and 4evan ».

## 1. Composants

| Fichier | Rôle |
|---|---|
| `F1M24VoiceManager.exe` (161 Mo) | App **.NET self-contained single-file** (WPF/WinForms). Lit la save, propose l'UI, écrit `mappings.txt`, installe le runtime. Demande les droits admin. |
| `runtime/UE4SS/` | UE4SS 3.0.1 (proxy `dwmapi.dll` + `UE4SS.dll`), installé s'il manque, sans écraser l'existant. |
| `runtime/FrostVoiceMapperNative/dlls/main.dll` | **Mod C++ UE4SS** (« Frost Voice Mapper Native 3.1.0 ») qui fait le vrai travail. |
| `.../mappings.txt` | Config lue au démarrage du jeu. |

## 2. Fonctionnement (confirmé)

```text
Staff (pilote / ingénieur) — staff ID issu de la save
   ↓  construction de l'entité staff par le jeu
UDialogueSpeaker lookup (fonction native, RVA 0x1E941C0)
   ↓  détour installé par main.dll
mappings.txt : D:<source_id>=<donor_id>  /  E:<source_id>=<donor_id>
   ↓  si mappé : recherche d'un speaker donneur du même rôle
Speaker du donneur (enregistrements Wwise existants)
   ↓
Événements audio Wwise (AkAudio) — inchangés
```

Extrait du README de la DLL : le jeu **contourne les callbacks UFunction
d'UE4SS** sur ce chemin de dialogue, d'où un détour natif. Le staff ID source,
le type de personnage, les groupes de dialogue et le panoramique restent ceux
d'origine ; seul le speaker (donc les fichiers audio) est remplacé.

Format `mappings.txt` :

```text
D:552=130     # pilote 552 parle avec la voix du pilote 130
E:187=51      # ingénieur 187 avec la voix de l'ingénieur 51
```

Ligne sans préfixe = pilote (legacy). Redémarrage du jeu requis. Log :
`FrostVoiceMapperNative.log`.

## 3. Méthode d'injection (d'après les chaînes de main.dll)

- Récupère le module du jeu, va à une **RVA codée en dur** (`0x1E941C0`).
- Vérifie que la cible est un stub `jmp rel32` puis une **signature d'octets**
  sur la cible « décryptée » (l'exe est vraisemblablement protégé/packé).
- Alloue un thunk exécutable proche, `VirtualProtect`, patche le jmp.
- Si un donneur est introuvable : « using original lookup » → pas de crash.

## 4. Limites

- **Pas de nouvelles voix** : uniquement réaffecter des enregistrements
  existants entre staff du **même rôle**.
- Ingénieurs : seulement **31 profils** donneurs ont des enregistrements audibles.
- Commentaire TV non concerné (radio pilote/ingénieur uniquement).
- **Très dépendant de la version du jeu** : RVA fixe + signature. L'app bloque
  l'installation si le SHA-256 de l'exe ne correspond pas.

## 5. ⚠ Constat sur ta machine

| | SHA-256 de `F1Manager24.exe` |
|---|---|
| Supporté par Voice Manager 1.1.0 | `1198FEB7B1F39653FE51F04C9B0ACB4D125A67D0E8BA6BDA1FA353B3368AB356` |
| **Ton installation** | `7FFE404F37A813C326CD0635001E388715E84FE945093DC802E388897B2050F6` |

**Ils ne correspondent pas** → l'installeur .NET refusera d'installer.

### Vérification binaire (2026-10-03) : le hook est compatible

Steam : build `17356935`, StateFlags 4 (à jour, rien en attente). En-tête PE de
l'exe : compilé le 2025-02-13. Exe protégé (sections `.xtext`, `.udata`…).

Logique de validation de `main.dll` (désassemblée, fonction ~RVA `0x4D14`) :

1. `stub = module + 0x1E941C0`, exige `stub[0] == 0xE9` (jmp rel32).
2. `target = stub + 5 + rel32`, `VirtualQuery` : `MEM_COMMIT` + page exécutable.
3. Les 18 premiers octets de `target` doivent valoir
   `48 8B 81 C0 01 00 00 48 63 89 C8 01 00 00 4C 8D 0C C8`.
4. Alloue un thunk à ±2 Go, réécrit le jmp du stub vers le thunk → détour.

Sur ton exe :

| Contrôle | Résultat |
|---|---|
| `0x1E941C0` = `E9 …` | ✅ `jmp` → RVA `0x10A72480` |
| Signature à la cible | ✅ **identique** (déjà en clair sur disque) |
| Occurrences de la signature dans tout l'exe | 1 seule, à `0x10A72480` |

Fonction ciblée (désassemblée) :

```asm
; UDialogueSpeaker* FindSpeaker(this, int staffId /*edx*/, int role /*r8d*/)
mov    rax, [rcx+1C0h]          ; TArray<UDialogueSpeaker*>.Data
movsxd rcx, dword [rcx+1C8h]    ; TArray.Num
lea    r9, [rax+rcx*8]
loop:  cmp rax, r9 / je notfound
       mov rcx, [rax]
       cmp [rcx+30h], r8d        ; speaker->role
       jne next
       cmp [rcx+2Ch], edx        ; speaker->staffId
       je  found
next:  add rax, 8 / jmp loop
found: mov rax, rcx / ret
```

**Conclusion** : la différence de hash ne touche pas ce chemin de code. Le mod
natif devrait s'installer et fonctionner tel quel ; seul le contrôle SHA-256
de l'installeur .NET bloque. Contournement : installer le mod à la main
(copie dans `ue4ss/Mods/FrostVoiceMapperNative`, entrée dans `mods.txt`,
`mappings.txt`). Pour la robustesse future, notre outil devrait localiser la
fonction **par signature** (unique ici) plutôt que par RVA fixe + hash.

Mapping mémoire déduit : `UDialogueSpeaker+0x2C` = staff ID,
`+0x30` = rôle ; conteneur des speakers à `+0x1C0` de l'objet appelant.

## 6. Compatibilité avec ton installation

- UE4SS déjà présent (même version 3.0.1, via `dwmapi.dll`) → pas de conflit
  de proxy, le mod s'ajouterait dans `ue4ss/Mods/`.
- Mods UE4SS existants (DRS mod, F1SetupScout…) : pas de conflit attendu,
  le détour vise une fonction native différente.

## 7. App .NET décompilée (2026-10-03)

Bundle single-file .NET 8 v6 extrait (script maison), assembly
`F1M24VoiceManager.dll` (51 Ko, ~1 500 lignes C#) décompilée avec ilspycmd.
WinForms, aucune dépendance tierce (SQLite via `winsqlite3.dll` de Windows).

**Lecture de la save** (`SaveGameReader` + `NativeSqlite`) :

1. Chercher la signature `00 05 00 00 00 "None\0" 05 00 00 00 "None\0"`.
2. À `sig + 19 + 4` : `int32 compressedSize`, `int32 uncompressedSize`, puis
   les données zlib commencent 16 octets après.
3. Décompresser → base **SQLite** de la carrière (écrite en fichier temp).
4. Requête :
   `Staff_BasicData` ⨝ `Staff_GameData` (⟕ `Staff_DriverData`),
   `StaffType IN (0, 2)` → 0 = pilote, 2 = ingénieur de course.
5. Noms encodés `[STRING_LITERAL:Value=|Nom|]` ou `[Clé_Nom1]` → décodage.

**Donneurs** (`VoiceCatalog`) : listes **codées en dur** — 108 pilotes et
31 ingénieurs (staff IDs des personnes ayant des enregistrements). Pas de
détection dynamique des assets audio.

**Installation** (`ModInstaller`) : contrôle SHA-256 unique, puis copie dans
`<Win64>\Mods\FrostVoiceMapperNative\` et UE4SS **à la racine de `Win64`**
(ancienne disposition UE4SS). ⚠ Sur une install UE4SS 3.x « ue4ss/ »
(comme la tienne), ça poserait un second `UE4SS.dll` et un dossier `Mods`
que le UE4SS actif ne lit pas → le mod ne serait jamais chargé.
`mappings.txt` : `.bak` unique, écriture atomique (`.tmp` + move).

Portage Python read-only : [`tools/read_save_staff.py`](../tools/read_save_staff.py)
(+ équipe/contrat via `Staff_Contracts` ⨝ `Teams`, option `--f1`, `--json`).

## 8. Installation manuelle sur ta machine (2026-10-03)

`F1Manager24\Binaries\Win64\ue4ss\Mods\FrostVoiceMapperNative\`
(`dlls\main.dll`, `enabled.txt`, `README.txt`, `mappings.txt`). Mapping de
test pour `save10` :

```text
D:17=10   # Sergio Perez -> Max Verstappen
E:682=51  # Carlo Pasetti -> Peter Bonnington
```

Désinstallation : supprimer le dossier `FrostVoiceMapperNative`.

### Résultat du test (2026-10-03) : crash au lancement → mod retiré

- Log du mod : `Frost Voice Mapper Native 3.1.0 loaded.` / `Loaded 2 voice
  mapping(s).` puis rien (pas de « Installed guarded… »).
- Windows : `APPCRASH`, module fautif **`UE4SS.dll`**, `c0000005`,
  offset `0x314880` (2 crashs, 07:52 et 08:02).
- Cause : **incompatibilité ABI UE4SS**. `main.dll` importe
  `RC::CppUserModBase::CppUserModBase()` / `~CppUserModBase()` d'un UE4SS
  3.0.1 compilé le 2024-02-14 (`8ac18fbf…`). Ton `UE4SS.dll` est une build
  expérimentale du 2026-03-27 (`df9e6e9a…`, même étiquette « 3.0.1 Beta »,
  nouveaux réglages `HookEngineTick`, `DefaultExecuteInGameThreadMethod`…).
  La disposition de `CppUserModBase` a changé → crash dans UE4SS.
- Ça ne remet **pas** en cause le détour : il n'a jamais été posé.
- Mod supprimé du jeu ; `mappings.txt` et log conservés dans `backup/`.

Leçon pour notre outil : **ne pas dépendre de l'ABI C++ d'UE4SS** (les
builds expérimentales sont répandues). Un DLL Win32 autonome, chargé par un
mod Lua via `package.loadlib(path, "*")` ou par un autre loader, élimine ce
couplage.

### Remplacement : notre DLL F1VoiceMapper — ✅ validée en jeu (2026-10-03)

Voir [`src/native/F1VoiceMapper`](../src/native/F1VoiceMapper/README.md).
DLL Win32 autonome chargée par un mod Lua (`package.loadlib`), signature +
stub retrouvés à l'exécution, rechargement à chaud de `mappings.txt` (v0.2).

Résultats en jeu (carrière Ferrari) :

- `Hook installed.` à RVA `0x10A72480` / stub `0x1E941C0`, aucun crash,
  cohabitation OK avec DRS mod, F1SetupScout, BPModLoader…
- `Mapped id=1 -> 10` (Hamilton → Verstappen) et `Mapped id=320 -> 51`
  (Dudley → Bonnington), **confirmé à l'oreille**.
- `FindSpeaker` est appelé **à chaque message radio** (pas seulement au
  chargement) → les changements de mapping s'appliquent en direct.
- Valeurs observées : `role` = 0, `type` 1 = pilote, 2 = ingénieur. Les
  staff sans enregistrement renvoient `none` (ex. 615, 645, 649).

## 9. Reste ouvert

- ~~Test en jeu~~ : fait avec F1VoiceMapper (ci-dessus).
- **Licence** : on n'embarque plus leur DLL (F1VoiceMapper est une
  réimplémentation). Créditer Frost Voice Mapper / Codex & 4evan pour la
  découverte de la fonction et du format `mappings.txt`.

## 10. Impact sur le plan (MVP 0.1)

Le MVP 0.1 « Driver → Voice » **existe déjà fonctionnellement** dans Voice
Manager. La valeur ajoutée possible :

- Intégrer/piloter `mappings.txt` depuis notre app (format trivial).
- Supporter plusieurs versions du jeu (signature scan au lieu de RVA fixe).
- Backups horodatés (Voice Manager ne garde qu'un seul `.bak`).
- Vue unifiée avec la partie commentaire (ECS).

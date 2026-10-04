# F1M24 Voice Manager

Gestionnaire de voix pour **F1 Manager 2024** : attribue la voix radio d'un
pilote ou d'un ingénieur de course à n'importe quel membre du staff, en
quelques clics, sans éditer de fichier.

## Utilisation

1. Lancer `dist\F1M24VoiceManager\F1M24VoiceManager.exe` (garder le dossier
   `payload\` à côté).
2. Le jeu, UE4SS et le mod sont détectés automatiquement.
3. Choisir une carrière → **Charger**.
4. Dans « Voix utilisée », choisir un donneur pour chaque personne voulue.
5. **Appliquer** : le mod est installé/mis à jour si besoin (jeu fermé),
   une sauvegarde horodatée est créée, puis `mappings.txt` est écrit.
   Jeu ouvert : la nouvelle voix s'entend au message radio suivant.
6. **Restaurer** revient à n'importe quelle sauvegarde précédente.

Prérequis : UE4SS 3.x installé dans le jeu. Sauvegardes :
`%LOCALAPPDATA%\F1M24VoiceManager\backup\`.

## Structure

| Chemin | Contenu |
|---|---|
| `src/F1M24VoiceManager.Core` | Détection jeu/UE4SS, lecture des sauvegardes (SQLite embarqué), `mappings.txt`, installation du mod, sauvegardes |
| `src/F1M24VoiceManager.App` | Application WPF (.NET 8) |
| `src/native/F1VoiceMapper` | Mod en jeu : DLL C++ autonome + chargeur Lua UE4SS ([README](src/native/F1VoiceMapper/README.md)) |
| `tests/F1M24VoiceManager.Core.Tests` | Tests xUnit (sauvegarde synthétique, installeur, sauvegardes…) |
| `tools/read_save_staff.py` | Lecture des IDs du staff en ligne de commande |
| `docs/` | Analyses ECS, Voice Manager, architecture |

## Build

```bat
publish.cmd
```

Compile la DLL native (MSVC, VS 2022, build reproductible) puis publie
l'application autonome dans `dist\F1M24VoiceManager\`.

Tests : `dotnet test tests\F1M24VoiceManager.Core.Tests` et
`src\native\F1VoiceMapper\test\build_test.cmd` + `hook_test.exe`.

## Crédits

Fonction ciblée et format `mappings.txt` découverts par Frost Voice Mapper
Native (F1M24 Voice Manager, Codex & 4evan). F1VoiceMapper en est une
réimplémentation indépendante de l'ABI UE4SS.

## Licence

Code distribué sous licence [MIT](LICENSE). F1 Manager 2024 et ses ressources
(audio, textes) appartiennent à leurs ayants droit et ne sont pas inclus dans
ce dépôt.

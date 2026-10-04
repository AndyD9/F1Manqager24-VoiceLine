# Analyse — Expanded Commentary System (ECS) v1.1.7

Source analysée : `C:\Users\andyd\Downloads\Expanded Commentary System V1.1.7`
Build de référence : `Gods of Motorsports/English (Original ECS Cast)`.

## 1. Ce qu'est ECS (et ce qu'il n'est pas)

**ECS est un commentaire TEXTE, pas audio.** Aucun fichier audio, aucune
synthèse vocale (pas de pyttsx3, winsound, pygame, SAPI…). Il affiche des
phrases dans un overlay (fenêtre Windows transparente + page web pour OBS).

Conséquence directe pour le plan : ECS ne fournit **aucun « Voice Pack »
audio**. « Voix » dans ECS = nom + portrait + couleur d'un commentateur.
Toute la partie « Voice Engine / Audio Output » du plan reste à construire
(TTS ou lignes enregistrées).

## 2. Architecture

```text
F1 Manager 2024
   │  (lecture mémoire)
   ▼
SimHub + plugin F1Manager2024Plugin (Asviix)
   │  export CSV, 1 fichier par pilote, append continu
   ▼
<TELEMETRY_PATH>/<Circuit>/<Session>/<Nom Pilote>/*.csv
   │
   ▼
telemetry_reader.py   — lecture incrémentale (tail) des CSV avec pandas
   │
   ▼
ai_race_commentator.py (~30 000 lignes, dont ~26 000 phrases)
   ├─ boucle 0,5 s : load_race_state() → run_detection_cycle()
   ├─ handlers d'événements → enqueue(personnage, texte, priority_key)
   ├─ file triée par priorité, TTL par type, max 14 éléments
   ├─ 1 ligne affichée toutes les 4 s
   ├─ écrit memory/current_line.json + memory/commentary_log.txt
   └─ overlay tkinter (layered window, click-through, F9)
   │
   ▼
overlay_server.py — HTTP localhost:8765 → overlay.html (lit current_line.json)
```

`ECS_Launcher.pyw` (tkinter, 604 lignes) : choix du build, édition de
`TELEMETRY_PATH` par regex dans `telemetry_reader.py`, vérif pandas, nettoyage
des dossiers, lancement des deux scripts en `subprocess.Popen`.

## 3. Dépendances

| Dépendance | Obligatoire | Remarque |
|---|---|---|
| Python ≥ 3.9 | oui | dans le PATH |
| pandas, numpy | oui | `pip install pandas` |
| Pillow | non | overlay dégradé sans |
| tkinter | non | pas d'overlay desktop sans |
| SimHub | oui | source unique de télémétrie |
| F1Manager2024Plugin (Hotfix 1.0) | oui | export CSV activé, **tous** les pilotes cochés |

Aucun accès réseau, pas d'API externe.

## 4. Télémétrie utilisée

Colonnes CSV lues (`get_latest_state`) : `timestamp, currentLap, totalLaps,
sessionType, Laps/Time Remaining, driverNumber, teamName, position,
gapToLeader, gapInFront, gapBehind, carInFront, carBehind, speed, SpeedST, rpm,
gear, distanceTravelled, turnNumber, compound, tire_age, {fl,fr,rl,rr}SurfaceTemp,
{..}Temp, {..}BrakeTemp, {..}Deg, fuel, fuelDelta, paceMode, fuelMode, drsMode,
pitstopStatus, driveCleanAir, avoidHighKerbs, dontFightTeammate,
overtakeAggression, defendApproach, engineTemp, engineDeg, gearboxDeg, ersDeg,
currentLapTime, driverBestLap, lastLapTime, last/best S1-S3, bestSessionTime,
weather, waterOnTrack, airTemp, trackTemp, rubber, trackName` + ERS.

Identification du pilote : **nom du dossier parent** du CSV. Cas spéciaux :
`SPECIAL_MAPPING` (`Ferrari1` → nom, quand le plugin écrit « Unknown Unknown »),
`FOLDER_MAPPING`, `TEAM_MAPPING` — codés en dur, différents selon le build
(Gods of Motorsports vs Universal).

Pas de signal direct pour drapeaux jaune/rouge, SC/VSC : ils sont **déduits**
(ex. jaune = ratio de voitures en mode « conserve », rouge = voitures à l'arrêt
ou au garage en masse). Météo : prévue mais désactivée (priorité 0).

## 5. Moteur de règles

- 102 fonctions, ~45 handlers (`handle_*`, `detect_*`).
- 4 rôles : `NARRATOR`, `ANALYST`, `EX_DRIVER`, `PIT_REPORTER`.
- Table `PRIORITY` (38 clés, 0–100) et `EVENT_EXPIRATION` (TTL en s) — c'est
  exactement le système de priorité/expiration décrit en §13 du plan.
- Annulation des événements de position obsolètes quand un nouveau dépassement
  concerne les mêmes pilotes.
- ~60 banques `BANK_*` (listes Python de chaînes), tirage sans répétition
  (`pick()` avec mémoire par banque).
- Placeholders : `{driver}`, `{ahead}`, `{track}`, `{position}`, `{tire}`,
  `{age}`, `{score}`, pronoms genrés (`{driver_pronoun}`, `{driver_possessive}`…)
  via une liste `FEMALE_DRIVERS`.
- Paramètres de réglage : ~70 constantes en tête de fichier (cooldowns, seuils).

## 6. Variantes / « commentary teams »

| Comparaison | Différence |
|---|---|
| Original ECS Cast vs Sky Sports Cast | **23 lignes** : 4 noms, 1 couleur, 4 portraits |
| Gods of Motorsports vs Universal | mappings pilotes dans `telemetry_reader.py` + ~10 lignes |
| Anglais vs Portugais | ~38 000 lignes : toutes les banques traduites |

Une « équipe de commentateurs » = 4 noms + 4 PNG. Une langue = toutes les
banques. Tout est copié-collé dans 7 builds → aucune séparation données/code.

## 7. Points qui rendent l'installation complexe

1. Installer Python + pandas manuellement.
2. Configurer l'export CSV du plugin SimHub, cocher chaque pilote.
3. Faire correspondre le chemin SimHub et `TELEMETRY_PATH` (édition de code).
4. Chemins trop longs → SimHub échoue **silencieusement**.
5. Vider `memory/` et `telemetry/exported_data/` avant chaque course.
6. Choisir le bon build parmi 7 dossiers.

**Constat sur ta machine** : le plugin SimHub exporte vers
`C:\Users\andyd\Desktop\f1 manAGE TEL\telemetry`, pas vers le dossier ECS —
ECS resterait muet en l'état. Ça illustre bien le problème n° 3.

## 8. Points d'intégration exploitables

- **`memory/current_line.json`** (`{character, text, timestamp}`) : interface
  propre et stable. Notre application peut le surveiller et **ajouter la voix**
  (TTS par personnage) sans modifier ECS.
- `memory/shutdown.flag` : fin de diffusion.
- `memory/commentary_log.txt` : historique.
- `TELEMETRY_PATH` : modifiable par regex (comme le fait le launcher) ou mieux,
  par variable d'environnement si on patche.
- `GeneralSettings.json` du plugin SimHub
  (`PluginsData/Common/F1ManagerPlotter.GeneralSettings.json`) :
  `ExporterEnabled`, `ExporterPath`, `TrackedDrivers` → **configurable
  automatiquement** par notre installeur (SimHub fermé, avec backup ; SimHub
  garde déjà des `_Backups/`).

## 9. Décision A / B / C

| Option | Verdict |
|---|---|
| **A — Embarquer ECS tel quel** | ✅ Viable pour MVP 0.2/0.3. On automatise : Python embarqué (ou exe PyInstaller), config SimHub, `TELEMETRY_PATH`, nettoyage, lancement. Licence : le README invite aux contributions mais **pas de licence explicite** → demander l'accord de l'auteur avant redistribution. |
| **B — Réutiliser la télémétrie seule** | ✅ Facile : `telemetry_reader.py` est petit (634 lignes) et indépendant. Le format CSV du plugin est le vrai contrat. |
| **C — Réécrire notre moteur** | Plus tard. La valeur d'ECS est dans les 26 000 phrases et les heuristiques de détection (drapeaux déduits, dépassements confirmés). À réécrire seulement si on veut un format de packs propre (données séparées du code). |

**Recommandation** : A à court terme + couche voix branchée sur
`current_line.json`. Préparer C en extrayant les banques vers un format de
pack (JSON/YAML) par script — ça résout aussi la duplication des 7 builds.

## 10. Questions ouvertes

- Licence/permission de redistribution (contacter l'auteur).
- Compatibilité Python 3.14 (installé ici) non testée — pandas/numpy récents OK a priori.
- Le plugin SimHub peut-il être configuré pendant que SimHub tourne ? (il réécrit probablement son JSON à la fermeture).

# MouseSmooth : notes de développement

Tout ce qu'il faut pour reprendre le projet à froid (consignes pour les
assistants IA : [AGENTS.md](AGENTS.md)). Le [README](README.md)
décrit l'utilisation et le fonctionnement du filtre ; ce fichier couvre le
contexte, le code, les pièges, la publication et la suite.

## Contexte

- **Pour qui** : l'utilisateur principal tremble **surtout pendant le geste**
  (tremblement d'action, type tremblement essentiel), moins au repos.
- **Dépôt** : https://github.com/HTModeLia/TiltMouse (public, pour que
  l'application puisse vérifier les mises à jour). Le produit s'appelle
  MouseSmooth, le dépôt TiltMouse (nom historique).
- **Version publiée** : voir https://github.com/HTModeLia/TiltMouse/releases/latest
  (2.2.0 au 11/11/2026).
- **Cible** : Windows 10 (1607+) et Windows 11, 64 bits. **Les deux doivent
  rester compatibles.**

## Démarrage rapide

Prérequis : .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`).

```bat
dotnet build -c Release
dotnet bin\Release\net10.0-windows\MouseSmooth.dll --selftest
```

`--selftest` doit finir sur `SELFTEST OK` (code de sortie 0). Il simule souris
et curseur sans toucher au vrai curseur : à lancer après chaque modification
du filtre, du moteur ou de l'analyse. Il n'y a pas de CI (retirée
volontairement, voir *Décisions*).

Pour essayer l'appli : lancer `bin\Release\net10.0-windows\MouseSmooth.exe`.
Une instance déjà lancée verrouille son exe : la quitter (icône → *Quitter*)
avant de recompiler ou republier.

## Carte du code

| Fichier | Rôle |
|---|---|
| `Program.cs` | Point d'entrée : `--selftest`, DPI PerMonitorV2, journal des exceptions (`error.log`, affiché au lancement suivant). |
| `TrayContext.cs` | Icône de notification et menu, raccourci Ctrl+Alt+M, vérification des mises à jour, fermeture ordonnée. |
| `MotionEngine.cs` | **Thread moteur** : hook `WH_MOUSE_LL`, horloge 250 Hz (timer haute résolution), `SetCursorPos`, trace du visualiseur, enregistrement du calibrage, collecte des segments du journal. |
| `MotionCore.cs` | Logique de déplacement **sans appel système** (testable) : position voulue V, gel après clic, ré-ancrage si le curseur est déplacé par un autre. |
| `TremorFilter.cs` | Filtre : double moyenne glissante + réactivité aux grands gestes + arrondi avec hystérésis. |
| `TremorAnalysis.cs` | Mesure fréquence/amplitude : `Analyze` (calibrage, main au repos) et `AnalyzeMotion` (journal, pendant l'usage, avec passe-haut). |
| `TremorJournal.cs` | Résume les mesures toutes les 10 min dans `journal.csv`. |
| `UpdateCheck.cs` | Interroge l'API GitHub (dernière release) et compare les versions. |
| `Startup.cs` | Lancement au démarrage (clé `HKCU\...\Run`). |
| `SmoothConfig.cs` | Réglages + lecture/écriture de `settings.json`. |
| `SettingsForm.cs`, `TrajectoryView.cs` | Fenêtre de réglages et visualiseur main/curseur. |
| `CalibrationForm.cs` | Assistant de calibrage (6 s main au repos). |
| `HiddenWindow.cs` | Fenêtre invisible : reçoit le raccourci global, sert au retour vers le thread UI. |
| `NativeMethods.cs` | Toutes les déclarations Win32 (P/Invoke). |
| `SelfTest.cs` | Auto-test (16 vérifications). |

Données de l'utilisateur, dans `%APPDATA%\MouseSmooth\` : `settings.json`,
`journal.csv`, `error.log`. Registre : `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MouseSmooth`.

## Pièges et règles à respecter

- **Deux threads.** Le hook et l'horloge tournent sur le thread moteur ; l'UI
  sur le thread WinForms. L'UI parle au moteur par champs `volatile`,
  `Interlocked` et `PostThreadMessage` (`ApplyConfig`, `SetEnabled`,
  `SetJournal`). Le moteur revient vers l'UI par `BeginInvoke` sur
  `HiddenWindow`. Ne jamais toucher à un contrôle WinForms depuis le moteur.
- **Le hook doit être rapide et ne jamais lever d'exception** : sinon Windows
  le retire (délai dépassé) ou le processus plante. Tout est dans un
  `try/catch` qui laisse passer l'événement.
- **Le délégué du hook est gardé dans un champ** (`_proc`) : sans cela le GC le
  libère et l'application plante au hasard.
- **Déplacement de la main = position proposée par Windows − position actuelle
  du curseur**, jamais la différence entre deux positions proposées (le
  curseur est bloqué là où on l'a mis). L'accélération Windows est donc
  conservée.
- **Événements injectés** (`LLMHF_INJECTED`) : toujours laissés passer, sinon
  nos propres `SetCursorPos` et les autres logiciels seraient bloqués.
- **`SmoothConfig.Journal` n'est pas dans `CopyFrom`** exprès : *Par défaut* ou
  *Annuler* dans les réglages ne doivent pas couper le journal. Tout nouveau
  réglage de lissage, lui, doit être ajouté à `CopyFrom` et `Clamp`.
- **Horloge** : timer `CREATE_WAITABLE_TIMER_HIGH_RESOLUTION` (Windows 10
  1803+), repli `timeBeginPeriod(1)` sinon. Sous Windows 11, `timeBeginPeriod`
  n'est plus garanti pour une appli sans fenêtre visible (mesuré : trous
  jusqu'à 19,6 ms contre 5,7 ms max avec le timer haute résolution).
- **Journal = données de santé** : uniquement des chiffres agrégés, jamais de
  trajectoires ni de positions à l'écran, et rien quand le lissage est coupé
  (souris prêtée). Garder cette règle.
- **Version** : un seul endroit, `<Version>` dans `MouseSmooth.csproj`. La
  vérification des mises à jour compare cette version au tag GitHub `vX.Y.Z`.
- **Compatibilité Windows 10** : toute nouvelle API Win32 doit exister sur
  Windows 10 1607, ou avoir un repli (comme le timer).

## Publier une version

1. Monter `<Version>` dans `MouseSmooth.csproj` (ex. `2.3.0`), compiler,
   `--selftest` OK, commit sur `main`.
2. Construire l'exécutable unique (quitter MouseSmooth s'il tourne depuis
   `publish\`) :
   ```bat
   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
   ```
   Vérifier : `bin\Release\net10.0-windows\win-x64\publish\MouseSmooth.exe --selftest`.
3. Pousser et créer le tag :
   ```bash
   git push origin main
   git tag -a v2.3.0 -m "MouseSmooth 2.3.0 : …"
   git push origin v2.3.0
   ```
4. Créer la release avec l'exe. Le plus simple : la page
   https://github.com/HTModeLia/TiltMouse/releases/new?tag=v2.3.0, glisser
   l'exe, notes en français, *Publish release*. Sans navigateur (Git Bash),
   avec l'accès GitHub déjà enregistré par Git :
   ```bash
   TOKEN=$(printf 'protocol=https\nhost=github.com\n\n' | git credential fill | sed -n 's/^password=//p')
   ID=$(curl -s -H "Authorization: Bearer $TOKEN" \
        -d '{"tag_name":"v2.3.0","name":"MouseSmooth 2.3.0","body":"Notes…"}' \
        https://api.github.com/repos/HTModeLia/TiltMouse/releases | python -c 'import json,sys;print(json.load(sys.stdin)["id"])')
   curl -s -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/octet-stream" \
        --data-binary @bin/Release/net10.0-windows/win-x64/publish/MouseSmooth.exe \
        "https://uploads.github.com/repos/HTModeLia/TiltMouse/releases/$ID/assets?name=MouseSmooth.exe"
   ```
5. Contrôle : `curl -s -A MouseSmooth https://api.github.com/repos/HTModeLia/TiltMouse/releases/latest`
   doit renvoyer le nouveau tag. Les installations 2.2.0+ seront prévenues
   dans la journée.

Le nom de l'asset doit rester `MouseSmooth.exe` : le lien
`releases/latest/download/MouseSmooth.exe` en dépend.

## Décisions prises (et pourquoi)

| Date | Décision | Raison |
|---|---|---|
| 10/11/2026 | .NET Core 3.1 → **.NET 10 LTS** | 3.1 sans correctifs depuis 2022 ; .NET 10 maintenu jusqu'en 11/2028, exécutable unique sans installation. |
| 10/11/2026 | Timer haute résolution | Régularité des pas de 4 ms sous Windows 11 (voir *Pièges*). |
| 10/11/2026 | Journal en chiffres seulement, désactivé par défaut | Donnée de santé (RGPD art. 9) ; mesurer l'évolution du tremblement sur plusieurs jours. |
| 10/11/2026 | **Pas de CI** | Projet personnel, `--selftest` se lance en local ; ajoutée puis retirée à la demande de l'auteur. |
| 10/11/2026 | Mises à jour : **prévenir seulement** | Une mise à jour automatique ratée pourrait laisser l'utilisateur sans lissage. |
| — | WinForms conservé, pas de framework de test | Interface minime ; l'auto-test intégré suffit. |

## Ce qui n'a pas été vérifié

- Comportement réel avec la main de l'utilisateur sur la 2.x (seulement des
  signaux simulés dans l'auto-test).
- Fonctionnement sur un vrai poste Windows 10.
- Affichage réel de la bulle « nouvelle version » (première occasion : la
  release après 2.2.0).
- Case *Lancer au démarrage* après redémarrage de session.

## Pistes pour la suite

- **Exploiter le journal** : après quelques jours chez l'utilisateur, regarder
  si la fréquence varie (heure, fatigue, traitement). Si elle varie beaucoup,
  un réglage fixe ne suffit plus → piste suivante.
- **Filtre adaptatif (WFLC / BMFLC, Riviere et al.)** : suit la fréquence du
  tremblement en continu et le soustrait ; retard presque nul au lieu des
  ~200 ms de la moyenne, plus besoin de calibrer. Plus complexe : à développer
  à côté du filtre actuel et à comparer avec l'utilisateur.
- **Gestes rapides non mesurés** par le journal (> 400 px/s) : le geste
  volontaire fausse l'analyse. À revoir si le tremblement en geste rapide
  compte.
- **SmartScreen** : l'exe n'est pas signé, Windows avertit au premier
  lancement. Une signature de code supprimerait l'avertissement (payant).
- **EcoQoS** (Windows 11) : si des saccades apparaissent malgré le timer,
  désactiver la limitation d'énergie du processus
  (`SetProcessInformation(ProcessPowerThrottling)`).

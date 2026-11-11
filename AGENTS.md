# AGENTS.md : consignes pour les assistants IA

MouseSmooth (dépôt TiltMouse) : application Windows de la zone de notification
qui lisse le curseur pour une personne qui tremble. C# / WinForms / .NET 10,
aucune dépendance NuGet, environ 2 500 lignes à plat à la racine.

Contexte complet, carte du code, décisions et pistes : [DEVELOPPEMENT.md](DEVELOPPEMENT.md).
Utilisation et principe du filtre : [README.md](README.md). Les lire avant une
modification non triviale.

## Commandes

```bash
dotnet build -c Release                                          # doit finir avec 0 avertissement
dotnet bin/Release/net10.0-windows/MouseSmooth.dll --selftest    # doit afficher SELFTEST OK, code 0
```

Exécutable unique (distribution) :

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Il n'y a ni CI ni projet de test séparé : `--selftest` (`SelfTest.cs`) est le
seul filet. Le vrai comportement souris ne peut pas être testé
automatiquement : le dire à l'utilisateur au lieu de le présenter comme vérifié.

## Règles non négociables

1. **Windows 10 (1607+) et Windows 11.** Toute API Win32 ajoutée doit exister
   sur Windows 10 1607 ou avoir un repli (exemple : le timer dans
   `MotionEngine.Run`).
2. **Dépôt public.** Aucune donnée personnelle ou de santé (noms, liens
   familiaux, mesures réelles) dans le code, les commits, la doc ou les
   releases. Parler de « l'utilisateur ».
3. **Journal du tremblement = données de santé.** Seulement des chiffres
   agrégés (fréquence, amplitude) ; jamais de trajectoires ni de positions à
   l'écran ; rien quand le lissage est désactivé ; le fichier ne quitte pas le PC.
4. **Le hook (`MotionEngine.HookProc`) doit rester rapide et ne jamais lever
   d'exception.** Pas d'allocation lourde, pas d'E/S, pas de verrou long.
5. **Threads** : le moteur (hook + horloge) ne touche jamais à un contrôle
   WinForms ; retour vers l'UI par `BeginInvoke` sur `HiddenWindow`. L'UI
   parle au moteur par les méthodes publiques de `MotionEngine`
   (`ApplyConfig`, `SetEnabled`, `SetJournal`…).
6. **Logique testable sans Win32** : le calcul va dans `MotionCore`,
   `TremorFilter`, `TremorAnalysis` (aucun appel système), avec une
   vérification dans `SelfTest.cs`. `MotionEngine` ne fait que la plomberie.
7. **Français** partout : code commenté, messages d'interface, commits, doc.
   Style des commits : verbe au présent, ex. « Corrige… », « Ajoute… ».
8. **Pas d'outillage ajouté** (CI, linters, frameworks de test, dépendances)
   sans avoir d'abord expliqué ce qu'il apporte concrètement et obtenu
   l'accord de l'utilisateur. La CI a déjà été ajoutée puis retirée.

## Recettes

**Ajouter un réglage de lissage**
1. Propriété dans `SmoothConfig` + borne dans `Clamp()` + copie dans `CopyFrom()`.
2. Utilisation dans `TremorFilter.Configure` ou `MotionCore`.
3. Contrôle dans `SettingsForm` (et `SameAs` qui compare deux configs).
4. Ligne dans le tableau *Réglages* du README.
5. Vérification dans `SelfTest.cs`.

Un réglage qui ne relève pas du lissage (comme `Journal`) reste **hors** de
`CopyFrom`, sinon *Par défaut* / *Annuler* dans les réglages l'écrasent.

**Ajouter une déclaration Win32** : uniquement dans `NativeMethods.cs`.

**Publier une version** : procédure complète dans DEVELOPPEMENT.md, section
*Publier une version*. Points clés :
- la version est dans `<Version>` de `MouseSmooth.csproj` et **uniquement là** ;
- le tag est `vX.Y.Z` ; l'asset de la release s'appelle **exactement**
  `MouseSmooth.exe` (lien `releases/latest/download/MouseSmooth.exe` et
  vérification des mises à jour) ;
- notes de version en français, pour un utilisateur non technicien ;
- **demander l'accord avant tout push, tag ou release.**

## Pièges rencontrés

- **Une instance lancée verrouille son exe** : `dotnet build`/`publish` échoue
  avec « Access denied ». Demander à l'utilisateur de quitter MouseSmooth
  depuis son icône ; `taskkill /IM` (sans `/F`) ne suffit pas toujours.
- Shell : Git Bash sous Windows. Chemins `d:/_DEV/TiltMouse` ou `/d/_DEV/TiltMouse`.
  En éditant des fichiers via un script, attention aux `\n` et `\\` dans les
  chaînes C# et dans les chemins Windows du README (déjà cassés deux fois).
- Les avertissements git « LF will be replaced by CRLF » sont sans conséquence.
- La sortie console de `--selftest` affiche mal les accents dans Git Bash :
  c'est l'affichage, pas le code.
- Pour une release sans `gh` : `git credential fill` fournit l'accès GitHub
  déjà enregistré (voir DEVELOPPEMENT.md). Ne jamais afficher ni écrire le jeton.

## Définition de « terminé »

- 0 erreur, 0 avertissement de compilation ; `SELFTEST OK`.
- Nouvelle logique non triviale → nouvelle vérification dans `SelfTest.cs`.
- README (utilisateur) et DEVELOPPEMENT.md (carte du code, décisions, pistes)
  mis à jour si le comportement ou la structure change.
- Dire clairement à l'utilisateur ce qui n'a pas pu être vérifié (souris
  réelle, Windows 10, etc.).

# MouseSmooth

Lisseur de curseur anti-tremblement pour Windows. L'application intercepte les
déplacements de la souris (hook bas niveau), filtre le tremblement de la main et
réinjecte une position stabilisée. Elle réside dans la zone de notification
(systray), fonctionne pour l'utilisateur courant sans droits administrateur.

## Compilation

.NET Core 3.1 SDK (le projet cible `netcoreapp3.1` avec WinForms) :

```bat
dotnet build -c Release
```

L'exécutable est dans `bin\Release\netcoreapp3.1\` (`MouseSmooth.exe`, avec
`MouseSmooth.dll` et le runtime à côté ; copier tout le dossier pour déployer).

## Utilisation

Lancer `MouseSmooth.exe` : une icône apparaît dans le systray.

- **Pastille verte** : lissage actif — **pastille rouge** : lissage désactivé.
- **Menu contextuel** (clic droit) : case *Activé* (toggle), *Réglages…*, *Quitter*.
- **Double-clic** sur l'icône : ouvrir les réglages.
- **Ctrl+Alt+M** : bascule actif/inactif depuis n'importe quelle application.

## Réglages

| Paramètre | Rôle | Plage |
|---|---|---|
| Sensibilité X | gain appliqué aux deltas horizontaux (2.0 = distance doublée) | 0.1 – 3.0 |
| Sensibilité Y | gain appliqué aux deltas verticaux | 0.1 – 3.0 |
| Lissage | force du filtrage (0 = natif, 0.95 = très filtré) | 0 – 0.95 |
| Bande morte X/Y | micro-déplacements résiduels ignorés (px) | 0 – 20 |

Les réglages s'appliquent en temps réel et sont persistés dans
`%APPDATA%\MouseSmooth\settings.json`.

## Fonctionnement (architecture)

1. **Hook `WH_MOUSE_LL`** installé sur le thread UI (une boucle de messages est
   requise). Les événements `WM_MOUSEMOVE` non injectés sont interceptés ;
   clics, molette et injections (autres logiciels d'automatisation) passent tels quels.
2. **Filtre** (`MouseFilter.cs`) : les deltas bruts sont intégrés avec la
   sensibilité par axe dans une position virtuelle ; un suivi lent (passe-bas,
   constante de temps 250 ms) sépare le mouvement volontaire du tremblement ;
   la cible mélange composante lente et rapide selon la force de lissage ; une
   bande morte évite d'appliquer les micro-corrections.
3. **Réinjection absolue** via `SetCursorPos` : position recalculée depuis la
   position matérielle, donc aucune dérive cumulée. Un saut externe (autre
   application, reprise de session) ou un trou temporel > 1 s ré-ancre le filtre
   et laisse passer ce déplacement sans filtrage.

Sécurités intégrées : tout le traitement est mono-thread (thread UI), les
exceptions du hook sont neutralisées, et si `SetCursorPos` échoue de façon
répétée (session verrouillée, bureau sécurisé) le lissage se désactive
automatiquement avec une notification.

## Auto-test

```bat
dotnet bin\Release\netcoreapp3.1\MouseSmooth.dll --selftest
```

Vérifie, sans toucher au curseur réel : neutralisation d'un tremblement ±6 px,
suivi d'un geste lent, application des gains X/Y, ré-ancrage sur saut externe et
sur trou temporel.

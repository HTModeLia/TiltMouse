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

- **Pastille verte** : lissage actif — **rouge** : désactivé — **bleue** : mode
  utilisateur valide (souris native, hook conservé).
- **Menu contextuel** (clic droit) : case *Activé* (toggle), case
  *Utilisateur valide*, *Réglages…*, *Calibrage…*, *Quitter*.
- **Double-clic** sur l'icône : ouvrir les réglages.
- **Ctrl+Alt+M** : bascule actif/inactif depuis n'importe quelle application.

## Réglages

| Paramètre | Rôle | Plage |
|---|---|---|
| Sensibilité X | gain appliqué aux deltas horizontaux (2.0 = distance doublée) | 0.1 – 3.0 |
| Sensibilité Y | gain appliqué aux deltas verticaux | 0.1 – 3.0 |
| Lissage | force du filtrage (0 = natif, 0.95 = très filtré) | 0 – 0.95 |
| Seuil de tremblement | écart de déplacement au-delà duquel le suivi reprend (en dessous : curseur figé) | 0 (désactivé) – 200 |
| Bande morte X/Y | micro-déplacements résiduels ignorés (px) | 0 – 20 |

Les réglages s'appliquent en temps réel et sont persistés dans
`%APPDATA%\MouseSmooth\settings.json`.

## Fonctionnement (architecture)

1. **Hook `WH_MOUSE_LL`** installé sur le thread UI (une boucle de messages est
   requise). Les événements `WM_MOUSEMOVE` non injectés sont interceptés ;
   clics, molette et injections (autres logiciels d'automatisation) passent tels quels.
2. **Filtre** (`MouseFilter.cs`) : les deltas bruts sont intégrés avec la
   sensibilité par axe dans une position « virtuelle » V ; une position
   « engagée » C poursuit V via un passe-bas (constante 250 ms) **seulement
   quand l'écart V−C dépasse le seuil de tremblement** (porte avec hystérésis
   spatiale et lissage temporel 120 ms). Le discriminant est le déplacement
   résiduel, pas la vitesse : un tremblement (la main oscille sur place) garde
   l'écart sous le seuil =&gt; C reste gelée et le curseur aussi ; un geste fait
   croître l'écart sans retour =&gt; la porte s'ouvre et C rattrape V
   progressivement, sans saut ;
3. **Vitesse d'origine préservée** : le mélange filtré/natif est adaptatif à la
   vitesse — lent = très filtré (la main tremble surtout à l'arrêt), rapide =
   quasiment natif. Le gain pointeur, le DPI et l'accélération Windows restent
   inchangés : un geste rapide atteint exactement sa cible d'origine, le
   lissage n'introduit qu'un léger délai, jamais une perte de distance ;
4. **Bande morte** : les micro-corrections résiduelles ne sont pas appliquées.
5. **Réinjection absolue** via `SetCursorPos` : position recalculée depuis la
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
suivi d'un geste lent (distance d'origine préservée), application des gains X/Y,
seuil de tremblement (figeage sous le seuil, déblocage au-dessus), ré-ancrage
sur saut externe et sur trou temporel.

## Mode utilisateur valide

Pour prêter la souris à un utilisateur sans tremblement sans désactiver le
hook : menu systray → *Utilisateur valide* (ou case dans les réglages). Le
hook `WH_MOUSE_LL` reste installé — la bascule est instantanée, le calibrage
et le visualiseur continuent de fonctionner — mais chaque événement est rendu
au système sans le moindre traitement : la souris se comporte exactement comme
sans MouseSmooth. Le profil tremblant (seuil, lissage, sensibilité, bande
morte) est conservé intact et revient dès la sortie du mode.

## Calibrage assisté

Menu systray → *Calibrage…* : posez la main sur la souris sans intention de
mouvement ; après 3 s de préparation, le tremblement réel est mesuré pendant
5 s sur les événements **bruts** du hook (avant tout lissage) : la mesure
reflète le matériel, pas le comportement filtré. L'assistant en déduit un
seuil (40 % au-dessus du tremblement mesuré), un lissage et une bande morte,
applicables en un clic — ils prennent effet immédiatement et sont persistés.

Recommandation : ne bougez pas volontairement pendant la mesure (le résultat
serait surestimé) ; recommencez si besoin.

## Visualiseur du filtre

La fenêtre de réglages affiche en temps réel, pour chaque axe :

- le **disque gris** : position *engagée* C (ce que le filtre a validé) ;
- le **triangle** : position *virtuelle* V (consigne matérielle) — orange
  quand la porte est fermée (tremblement absorbé), vert en suivi natif ;
- la **jauge** : ouverture de la porte (0 = gelé, 100 % = suivi natif).

Utilisation typique : main posée, le triangle reste orange et le curseur est
immobile ; lors d'un geste, il passe vert et le disque le rattrape. Si un
geste lent reste bloqué, baissez le seuil ; si la main posée fait glisser le
curseur, augmentez-le.

## Réglage conseillé

Le plus simple est l'assistant de calibrage (voir ci-dessus). Pour un réglage
manuel :

1. Mettre *Lissage* vers 0,8 et *Seuil de tremblement* à 0 ;
2. monter progressivement le seuil jusqu'à ce que la main posée (sans intention
   de mouvement) ne fasse plus bouger le curseur ;
3. vérifier que les gestes lents restent possibles : un seuil trop haut donne
   l'impression que le pointeur « glisse » au lieu de partir immédiatement ;
4. ajuster *Sensibilité X/Y* en dernier (préférence de vitesse par axe).

# MouseSmooth

Lisseur de curseur pour les personnes qui tremblent. MouseSmooth fait la
**moyenne** du mouvement de la main sur une courte durée : le tremblement, qui
oscille sur place, s'annule, tandis que le mouvement volontaire passe. Le
curseur se déplace de manière fluide, comme pour une main qui ne tremble pas.

L'application réside dans la zone de notification, fonctionne pour
l'utilisateur courant et ne demande aucun droit administrateur. Compatible
Windows 10 (1607 et suivants) et Windows 11.

## Compilation

.NET 10 SDK (WinForms) :

```bat
dotnet build -c Release
```

Pour distribuer : un **exécutable unique**, qui fonctionne sans installer .NET :

```bat
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Résultat : `bin\Release\net10.0-windows\win-x64\publish\MouseSmooth.exe`
(Windows sur ARM l'exécute en émulation x64).

## Utilisation

Lancer `MouseSmooth.exe` : une icône apparaît dans la zone de notification.

- **Pastille verte** : lissage actif. **Grise** : souris native.
- **Ctrl+Alt+M** : active ou désactive le lissage depuis n'importe quelle
  application (pour prêter la souris à une personne qui ne tremble pas).
- **Clic droit** sur l'icône : *Lissage activé*, *Réglages…*, *Calibrage…*,
  *Lancer au démarrage de Windows*, *Journal du tremblement*, *Ouvrir le
  journal*, *Quitter*. **Double-clic** : réglages.

Premier lancement conseillé : **Calibrage…**, puis ajuster si besoin dans les
réglages.

**Mises à jour** : une minute après le lancement, puis une fois par jour,
MouseSmooth demande à GitHub la dernière version publiée. Si elle est plus
récente, une bulle le signale et le menu affiche *Nouvelle version
disponible…* (clic : page de téléchargement). Rien n'est téléchargé ni
installé automatiquement, et aucune donnée n'est envoyée.

## Réglages

| Réglage | Effet | Défaut |
|---|---|---|
| Durée de la moyenne | Le curseur suit la moyenne de la main sur cette durée. Idéal : une période du tremblement (200 ms pour 5 Hz). Plus long = plus stable mais plus de retard. 0 = aucun lissage. | 200 ms |
| Réactivité aux grands gestes | Pendant un déplacement franc, la moyenne est raccourcie (÷4) pour réduire le retard. Le pointage lent reste entièrement lissé. | 50 % |
| Vitesse du curseur | 1,00 = vitesse Windows. Plus lent = tremblement plus petit à l'écran. | 1,00 |
| Stabilisation des clics | Les mouvements de la main sont ignorés pendant cette durée après chaque appui ou relâchement de bouton. | 120 ms |

Les réglages s'appliquent en direct ; *OK* les enregistre dans
`%APPDATA%\MouseSmooth\settings.json`.

La fenêtre de réglages montre les 2 dernières secondes du mouvement : en
**orange** la main (avec son tremblement), en **vert** le curseur lissé.
Quand le lissage est désactivé, le filtre tourne en simulation : on voit ce
qu'il ferait, sans que la souris soit modifiée.

## Calibrage

La main repose sur la souris, détendue, sans chercher à bouger, pendant
6 secondes. MouseSmooth mesure l'**amplitude** et la **fréquence** du
tremblement (la dérive lente de la main est retirée), puis recommande une
durée de moyenne égale à **une période du tremblement** : c'est la durée pour
laquelle la moyenne annule exactement l'oscillation.

## Journal du tremblement

Pour suivre l'évolution du tremblement sur plusieurs jours (fatigue, heure,
traitement…) et vérifier que le réglage reste adapté. Désactivé par défaut :
menu *Journal du tremblement*.

Pendant l'utilisation normale, chaque tranche de 3 s de mouvement continu est
analysée : le geste volontaire (lent) est retiré par un passe-haut (< 2,5 Hz),
puis la fréquence et l'amplitude de ce qui reste sont mesurées. Toutes les
10 minutes, une ligne par situation est ajoutée à
`%APPDATA%\MouseSmooth\journal.csv` (format Excel français) :

| Colonne | Contenu |
|---|---|
| `situation` | `repos` (main posée, < 20 px/s) ou `geste` (geste lent, 20 à 400 px/s) |
| `segments_3s` | nombre de tranches mesurées (au moins 3 pour écrire une ligne) |
| `frequence_hz`, `amplitude_px` | médianes de la période |
| `amplitude_px_max` | pire tranche de la période |
| `lissage_ms` | réglage actif à ce moment |

Confidentialité : **aucune trajectoire ni position à l'écran n'est
enregistrée**, seulement ces chiffres, et le fichier reste sur le PC. Rien
n'est mesuré quand le lissage est désactivé (souris prêtée à quelqu'un
d'autre). Les gestes rapides (> 400 px/s) ne sont pas mesurés : le geste
volontaire y fausserait l'analyse. Les amplitudes sont en pixels de la main
(accélération Windows comprise), indépendamment du réglage de vitesse.

## Fonctionnement

1. **Interception** : un hook souris bas niveau (`WH_MOUSE_LL`) bloque les
   déplacements physiques. Le déplacement de la main est la différence entre la
   position que Windows propose et la position actuelle du curseur
   (l'accélération Windows est donc conservée). Il est ajouté, multiplié par la
   vitesse, à la **position voulue**. Clics, molette et mouvements simulés par
   d'autres logiciels passent sans modification.
2. **Moyenne glissante double** (`TremorFilter.cs`) : toutes les 4 ms (250 Hz),
   la position voulue alimente deux moyennes glissantes de durée W en cascade
   (pondération triangulaire). Un tremblement de période W et ses harmoniques
   sont annulés ; les fréquences voisines sont atténuées de 95 % ou plus. Le
   mouvement volontaire passe intact avec un retard d'environ W.
3. **Réactivité** : le déplacement **net** sur la fenêtre mesure la vitesse
   volontaire (un tremblement a un déplacement net quasi nul). Au-delà de
   200 px/s, la sortie glisse progressivement vers une moyenne 4 fois plus
   courte.
4. **Placement** : le curseur est placé par `SetCursorPos` au pixel près, avec
   une hystérésis de 0,7 px contre le scintillement. Quand la main s'arrête, la
   moyenne converge exactement : le curseur arrive pile à l'endroit voulu.
5. **Thread dédié** : hook et horloge tournent sur leur propre thread. Une
   interface occupée ne peut ni retarder le curseur, ni faire retirer le hook
   par Windows. L'horloge de 4 ms est un timer haute résolution (Windows 10
   1803+), qui reste précis sous Windows 11 même sans fenêtre visible ; repli
   sur `timeBeginPeriod` pour les Windows 10 plus anciens.
   Au repos, le thread dort (aucune consommation CPU).

Sécurités : une exception dans le hook laisse passer l'événement ; un
déplacement externe du curseur (autre logiciel, écran tactile) est respecté et
le filtre repart de là ; si `SetCursorPos` échoue de façon répétée, le lissage
se désactive avec une notification ; toute erreur non gérée est journalisée
dans `%APPDATA%\MouseSmooth\error.log` et affichée au lancement suivant.

Limite : Windows ne transmet pas les mouvements au-dessus d'une fenêtre lancée
en administrateur à une application qui ne l'est pas. Pour lisser aussi sur ces
fenêtres, lancer MouseSmooth en administrateur.

## Auto-test

```bat
dotnet bin\Release\net10.0-windows\MouseSmooth.dll --selftest
```

Simule une souris à 125 Hz et le curseur, puis vérifie : tremblement de 5 Hz
±8 px annulé, tremblement 4 + 7 Hz atténué, geste lent rectiligne malgré le
tremblement, arrivée exacte en fin de geste, réactivité, vitesse,
stabilisation des clics, déplacement externe respecté, changement de réglage
sans saut, et mesure de fréquence et d'amplitude du calibrage.

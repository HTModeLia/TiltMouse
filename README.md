# MouseSmooth

Lisseur de curseur pour les personnes qui tremblent. MouseSmooth fait la
**moyenne** du mouvement de la main sur une courte durée : le tremblement, qui
oscille sur place, s'annule, tandis que le mouvement volontaire passe. Le
curseur se déplace de manière fluide, comme pour une main qui ne tremble pas.

L'application réside dans la zone de notification, fonctionne pour
l'utilisateur courant et ne demande aucun droit administrateur.

## Compilation

.NET Core 3.1 SDK (WinForms) :

```bat
dotnet build -c Release
```

L'exécutable est dans `bin\Release\netcoreapp3.1\` (`MouseSmooth.exe` ; copier
tout le dossier pour déployer).

## Utilisation

Lancer `MouseSmooth.exe` : une icône apparaît dans la zone de notification.

- **Pastille verte** : lissage actif. **Grise** : souris native.
- **Ctrl+Alt+M** : active ou désactive le lissage depuis n'importe quelle
  application (pour prêter la souris à une personne qui ne tremble pas).
- **Clic droit** sur l'icône : *Lissage activé*, *Réglages…*, *Calibrage…*,
  *Quitter*. **Double-clic** : réglages.

Premier lancement conseillé : **Calibrage…**, puis ajuster si besoin dans les
réglages.

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
   par Windows. Au repos, le thread dort (aucune consommation CPU).

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
dotnet bin\Release\netcoreapp3.1\MouseSmooth.dll --selftest
```

Simule une souris à 125 Hz et le curseur, puis vérifie : tremblement de 5 Hz
±8 px annulé, tremblement 4 + 7 Hz atténué, geste lent rectiligne malgré le
tremblement, arrivée exacte en fin de geste, réactivité, vitesse,
stabilisation des clics, déplacement externe respecté, changement de réglage
sans saut, et mesure de fréquence et d'amplitude du calibrage.

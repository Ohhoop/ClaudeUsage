# Claude Usage Widget

Overlay Windows discret, toujours au premier plan, affichant les limites d'usage de l'abonnement Claude. Le nom d'affichage est « Claude Usage Widget »; les identifiants internes (exécutable `ClaudeUsage.exe`, dossier de réglages `%APPDATA%\ClaudeUsage`) restent inchangés.

Limites affichées :

- Session 5 h
- Hebdomadaire globale
- Hebdomadaire par modèle

Chaque ligne montre une barre de progression, le pourcentage utilisé et le délai avant la réinitialisation. L'overlay n'apparaît que lorsqu'un processus `claude` tourne (app de bureau Claude ou Claude Code) et se cache automatiquement sinon.

## Fonctionnement

- Les données proviennent de l'endpoint de compte `https://api.anthropic.com/api/oauth/usage`, interrogé toutes les 5 minutes uniquement quand l'overlay est visible. Aucune consommation d'usage de modèle. En cas de réponse 429, le délai Retry-After est respecté avant tout nouvel essai, et cette échéance est conservée dans les réglages afin qu'un redémarrage de l'application n'envoie pas de requête tant que la pause n'est pas écoulée.
- Le jeton d'accès est lu localement dans `%USERPROFILE%\.claude\.credentials.json`. Il n'est jamais écrit, journalisé ni transmis ailleurs qu'à l'API d'Anthropic.
- La détection de présence vérifie toutes les 5 secondes l'existence d'un processus nommé `claude`.
- Une icône de zone de notification donne accès au menu en tout temps et émet une notification quand une limite se réinitialise, même si l'overlay est caché, tant que l'application tourne.

## Prérequis

- Windows 10 ou plus récent
- .NET Desktop Runtime 8.0

## Compilation et installation

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
powershell -ExecutionPolicy Bypass -File build.ps1 -Install
```

Le premier appel exécute les tests puis publie `dist\ClaudeUsage.exe`. Le second copie en plus l'exécutable vers `%LOCALAPPDATA%\Programs\ClaudeUsage` et le lance; utilisez cette copie au quotidien pour qu'une recompilation n'écrase jamais l'exécutable en cours.

## Utilisation

- Glisser avec le bouton gauche pour déplacer la fenêtre; la position est mémorisée.
- Clic droit (sur l'overlay ou l'icône de zone de notification) : Actualiser, Opacité, Notifier au reset, Lancer avec Claude Code, Réduire dans la zone de notification, Quitter.
- « Réduire dans la zone de notification » masque la fenêtre tout en gardant l'icône et le suivi actifs; l'entrée devient « Afficher » pour la ramener.
- Le pourcentage reflète l'usage réel du compte : il ne change que lorsque vous utilisez Claude, ou retombe lors d'un reset. Le compte à rebours est calculé localement : arrivé à zéro, il attend une minute, déclenche la notification puis repart pour le cycle suivant, sans requête supplémentaire. L'interrogation ne sert qu'à rafraîchir les pourcentages, à rythme constant.
- Les réglages sont conservés dans `%APPDATA%\ClaudeUsage\settings.json`.

## Langues

L'interface suit la langue d'affichage de Windows. Les traductions sont embarquées dans l'exécutable (`translations.json`) pour une quarantaine de langues; l'anglais est la langue par défaut et sert de repli pour toute langue absente ou toute clé manquante. La résolution suit la chaîne de la culture Windows (par exemple `fr-CA`, puis `fr`, puis `en`). Les formats de date et d'heure suivent les paramètres régionaux de Windows. Pour surcharger ou ajouter des traductions sans recompiler, déposez un fichier `translations.json` de même structure à côté de l'exécutable : ses valeurs remplacent celles embarquées.

## Lancement automatique

L'option « Lancer avec Claude Code » gère un hook SessionStart dans `%USERPROFILE%\.claude\settings.json` : cochée, le hook est présent et chaque session Claude Code démarre l'overlay; décochée, le hook est retiré. Le reste du fichier est préservé tel quel. L'application quitte d'elle-même environ 30 secondes après la disparition du dernier processus `claude`. Ouvrir l'app Claude uniquement pour du clavardage, sans session, ne déclenche pas le lancement.

## États dégradés

- « Jeton introuvable » : le fichier de credentials est absent; ouvrez Claude ou Claude Code pour le régénérer.
- « Jeton expiré » : le jeton local n'est plus valide; il sera rafraîchi par Claude Code à sa prochaine utilisation.
- Affichage atténué : dernière valeur connue conservée pendant une erreur réseau; nouvelle tentative chaque minute.

## Limites connues

- Les fenêtres en plein écran exclusif (jeux) peuvent recouvrir l'overlay.
- Les notifications respectent l'assistant de concentration de Windows et peuvent être masquées par celui-ci.

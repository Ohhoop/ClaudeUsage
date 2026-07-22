# ClaudeUsage

Overlay Windows discret, toujours au premier plan, affichant les limites d'usage de l'abonnement Claude :

- Session 5 h
- Hebdomadaire globale
- Hebdomadaire par modèle

Chaque ligne montre une barre de progression, le pourcentage utilisé et le délai avant la réinitialisation. L'overlay n'apparaît que lorsqu'un processus `claude` tourne (app de bureau Claude ou Claude Code) et se cache automatiquement sinon.

## Fonctionnement

- Les données proviennent de l'endpoint de compte `https://api.anthropic.com/api/oauth/usage`, interrogé toutes les 60 secondes quand l'overlay est visible. Aucune consommation d'usage de modèle.
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
- Clic droit (sur l'overlay ou l'icône de zone de notification) : Actualiser, Opacité, Notifier au reset, Lancer avec Claude Code, Quitter.
- Les réglages sont conservés dans `%APPDATA%\ClaudeUsage\settings.json`.

## Lancement automatique

L'option « Lancer avec Claude Code » gère un hook SessionStart dans `%USERPROFILE%\.claude\settings.json` : cochée, le hook est présent et chaque session Claude Code démarre l'overlay; décochée, le hook est retiré. Le reste du fichier est préservé tel quel. L'application quitte d'elle-même environ 30 secondes après la disparition du dernier processus `claude`. Ouvrir l'app Claude uniquement pour du clavardage, sans session, ne déclenche pas le lancement.

## États dégradés

- « Jeton introuvable » : le fichier de credentials est absent; ouvrez Claude ou Claude Code pour le régénérer.
- « Jeton expiré » : le jeton local n'est plus valide; il sera rafraîchi par Claude Code à sa prochaine utilisation.
- Affichage atténué : dernière valeur connue conservée pendant une erreur réseau; nouvelle tentative chaque minute.

## Limites connues

- Les fenêtres en plein écran exclusif (jeux) peuvent recouvrir l'overlay.
- Les notifications respectent l'assistant de concentration de Windows et peuvent être masquées par celui-ci.

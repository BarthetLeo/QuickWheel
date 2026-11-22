# QuickWheel

Roue de raccourcis pour Windows. Appuie sur une touche : une roue s'ouvre autour de la souris,
avec les raccourcis de l'app au premier plan.

Windows 10 ou 11 uniquement.

## Installation

### Version prête à l'emploi

1. Télécharge `QuickWheel.zip` dans les [Releases](../../releases) du dépôt.
2. Décompresse-le où tu veux, sauf dans `C:\Program Files`.
3. Lance `QuickWheel.exe`. Rien d'autre à installer : .NET est inclus.

Pour la démarrer avec Windows, coche **Lancer avec Windows** dans les réglages.

### Depuis les sources

Prérequis : [SDK .NET 9](https://dotnet.microsoft.com/download/dotnet/9.0) et Git.

```powershell
git clone https://github.com/BarthetLeo/QuickWheel.git QuickWheel
cd QuickWheel
dotnet run -- --settings
```

`--settings` ouvre directement l'éditeur. Les autres commandes utiles :

```powershell
dotnet test tests                                              # tests
powershell -ExecutionPolicy Bypass -File tools\publish.ps1     # exe autonome + zip dans publish\
```

`tools\publish.ps1 -Out <dossier>` publie ailleurs. Le dossier de sortie est vidé avant la copie.

## Dans la roue

| Geste | Effet |
|---|---|
| Maintenir la touche, viser une case, relâcher | exécute la case |
| Appui bref | la roue reste ouverte, clic gauche pour choisir |
| Rester un instant sur un dossier | ouvre sa sous-roue |
| Viser le centre (ou clic droit, ou Échap) | remonte d'un dossier, ou ferme |
| Cliquer sur le centre (logo) de la roue principale | ouvre QuickWheel et ses réglages |
| `1` à `9` | exécute la case n°, anneau par anneau depuis le centre |

Touche par défaut : **²** (sous Échap, clavier AZERTY).

## L'éditeur

- **Roues** : une roue globale (affichée quand aucune app ne correspond), et une roue par app.
- **Anneaux** : chaque anneau est découpé en cases égales. Glisse une case d'un anneau à l'autre ;
  lâche-la au-delà du dernier anneau pour créer un anneau ; un anneau vidé disparaît.
  L'épaisseur de chaque anneau se règle sous la roue.
- **Portée** : à la création, un raccourci est *global*, pour *cette app*, ou pour *plusieurs apps*.
  Un raccourci partagé est un seul objet : le modifier le modifie partout. Seule sa place change
  d'une roue à l'autre.
- **Étapes** : un raccourci enchaîne autant d'étapes que tu veux (touches, texte, lancer,
  PowerShell, clic souris, pause). Les étapes s'enchaînent le plus vite possible.
- **Enregistrer une séquence** : clique et tape normalement (« clique ici, ici, puis écris ça »),
  puis Échap. Les temps d'attente ne sont pas gardés : la séquence est rejouée d'un coup,
  et la souris revient à sa place à la fin. Ajoute une étape Pause si une appli a besoin de temps.
- **Dossiers** : une case peut ouvrir une sous-roue (double-clic pour la modifier).
- **Icônes** : 7 188 icônes Material Design (recherche en français ou en anglais, catégories),
  tes images, l'icône d'un .exe, ou un caractère.
- **Import / export** : une roue d'app dans un fichier `.qwheel.json`. Exporter depuis la roue
  globale sauvegarde toute la configuration.

Config : `%APPDATA%\QuickWheel\config.json`. L'ancien format est converti automatiquement
(copie gardée dans `config.json.v1.bak`).

## Limites connues

- Les apps lancées en administrateur ne reçoivent pas les touches d'une app non-admin
  (sécurité Windows). Lance QuickWheel en admin si besoin.
- `Win+L` et `Ctrl+Alt+Suppr` ne peuvent pas être simulés.
- Les emojis s'affichent en monochrome (limite de WPF).

Icônes : [Material Design Icons](https://pictogrammers.com/library/mdi/) par Pictogrammers,
licence Apache 2.0 (voir `Assets/MDI-LICENSE.txt`).

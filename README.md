# AllInOne : outils PDF et images (C# / .NET 8)

Application de bureau Windows (WPF) et outil en ligne de commande qui regroupe les conversions qu'on fait d'habitude sur des sites web. Tout se passe sur l'ordinateur : **aucun fichier n'est envoyé sur Internet**.

Elle permet de :

- **créer un PDF à partir d'une ou plusieurs images** (une page par image, TIFF multipage pris en charge) ;
- **modifier un PDF** : supprimer des pages, en ajouter (depuis d'autres PDF ou des images), les réorganiser, les faire pivoter ;
- **fusionner plusieurs PDF** (et images) en un seul document ;
- **compresser un PDF** (scans, documents pleins de photos) en réduisant ses images, sans toucher au texte ;
- **découper un PDF** : une page par fichier, toutes les N pages ou selon des plages (« 1-3, 4-7, 8- ») ;
- **convertir un PDF en images** (PNG, JPEG, WebP, BMP, GIF, TIFF, résolution au choix) ;
- **convertir des images d'un format à un autre** (PNG, JPEG, WebP, AVIF, BMP, GIF, TIFF), avec redimensionnement optionnel. Les photos **HEIC** des iPhone sont lues partout (conversion, images → PDF) ;
- **convertir des sons** en MP3, WAV, AIFF, FLAC, OGG, Opus ou M4A (et extraire le son d'une vidéo) ;
- **convertir des vidéos** en MP4, MKV, WebM, AVI, OGG, MOV ou **GIF animé** (qualité, résolution maximale, suppression du son, changement de conteneur sans réencodage) ;
- **couper un extrait** d'un son ou d'une vidéo (début et fin) ;
- **extraire les images d'une vidéo** en PNG ou JPEG : toutes, ou à intervalle régulier (1 par seconde…), sur toute la vidéo ou entre deux positions.

Les fichiers d'origine ne sont jamais modifiés, sauf si vous choisissez vous-même de les écraser.

## Structure

| Projet | Rôle |
| --- | --- |
| `src/AllInOne.Core` | Bibliothèque multiplateforme qui contient toute la logique (utilisable depuis n'importe quel programme .NET). |
| `src/AllInOne.App` | Application de bureau WPF (Windows) avec quatre onglets. |
| `src/AllInOne.Cli` | Outil en ligne de commande `allinone-cli` (Windows, Linux, macOS). |
| `tests/AllInOne.Core.Tests` | Tests automatisés (xUnit). |

## Utiliser l'application de bureau

Prérequis : Windows 10/11 et le [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run --project src/AllInOne.App
```

Pour obtenir un dossier prêt à copier (`AllInOne.exe` avec ses DLL à côté) :

```powershell
dotnet publish src/AllInOne.App -c Release -o publish
```

La compilation produit volontairement **plusieurs fichiers** (exe + DLL) plutôt qu'un seul exe géant, et ne garde que les
bibliothèques natives Windows x64. Par défaut, elle dépend du runtime .NET installé ; ajoutez `--self-contained true` pour
embarquer .NET (dossier environ 4 fois plus lourd, mais rien à installer).

**Releases automatiques :** à chaque fusion d'une pull request dans `main`, le workflow `release` publie une
[release GitHub](../../releases) numérotée `v1.0.N` (N s'incrémente à chaque fois) avec deux archives, contenant chacune
`AllInOne.exe`, `allinone-cli.exe` (ligne de commande) et leurs DLL :

| Archive | Taille (zip / décompressé) | Prérequis |
| --- | --- | --- |
| `AllInOne-<version>-win-x64.zip` (légère) | ≈ 21 Mo / 48 Mo | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows propose de l'installer au premier lancement s'il manque) |
| `AllInOne-<version>-win-x64-autonome.zip` | ≈ 86 Mo / 194 Mo | aucun |

L'essentiel du poids restant vient des moteurs natifs : ImageMagick (≈ 25 Mo), SkiaSharp (≈ 12 Mo) et PDFium (≈ 7 Mo).

L'interface reprend le thème sombre de Scan-Online (dégradé rose → violet, cartes arrondies, barre de titre sombre) avec une navigation latérale entre les outils :

1. **Images → PDF** : ajoutez ou glissez des images, ordonnez-les (glisser-déposer, Monter/Descendre, Trier par nom), choisissez la taille de page (A4, A3, A5, Lettre, Légal ou taille de l'image), l'orientation et la marge.
2. **Modifier / fusionner des PDF** : ouvrez un ou plusieurs PDF et/ou images ; toutes les pages s'affichent en vignettes. Sélectionnez-les pour les supprimer (touche Suppr), les pivoter, les déplacer ou glissez-les à la souris. Les fichiers ajoutés sont insérés après la page sélectionnée. « Enregistrer sous » produit le PDF final.
3. **Compresser un PDF** : ajoutez un ou plusieurs PDF et choisissez le niveau (légère 200 ppp, moyenne 150 ppp, forte 100 ppp). Les images sont réduites et recompressées en JPEG ; le texte et les dessins restent vectoriels. Chaque ligne montre la taille avant et après. Un PDF déjà optimisé est simplement copié.
4. **Découper un PDF** : une page par fichier, toutes les N pages, ou selon des plages où chaque plage donne un fichier (`1-3, 4-7, 8-`).
5. **PDF → images** : choisissez un PDF, les pages (ex. `1-3,5`, vide = toutes), le format, la résolution et la qualité.
6. **Convertir des images** : convertissez un lot d'images (dont HEIC) vers un autre format, avec largeur/hauteur maximales optionnelles.
7. **Vidéos et GIF** : ajoutez ou glissez des vidéos (ou des GIF), choisissez le format, dont le GIF animé, la qualité et la résolution maximale. Les champs Début et Fin gardent seulement un extrait (ex. de `1:30` à `2:45`). Les formats « Images PNG » et « Images JPEG » extraient les images de la vidéo (toutes, ou 1 par seconde, 1 toutes les 5 s…) dans un dossier `<nom>_images` ; au-delà d'environ 3 000 images, une confirmation est demandée. Un bouton « Arrêter » interrompt la conversion.
   Chaque ligne affiche les caractéristiques du fichier (durée, résolution, codecs, images/s, débit, taille) puis, pendant la conversion, sa propre barre de progression et le temps restant estimé. Un fichier en échec n'arrête pas les suivants.
8. **Convertir des sons** : MP3, WAV, AIFF, FLAC, OGG Vorbis, Opus ou M4A (AAC), avec qualité, fréquence d'échantillonnage et mono/stéréo. Les vidéos déposées ici donnent leur piste audio. Début et Fin permettent de couper un extrait. Comme pour les vidéos, chaque ligne montre durée, codec, débit, fréquence, canaux et taille, puis l'avancement et le temps restant.

### FFmpeg (conversion vidéo et audio)

Les conversions vidéo et audio utilisent [FFmpeg](https://ffmpeg.org/), lancé comme programme séparé. Il n'est **pas inclus** dans l'exe (il pèserait environ 200 Mo de plus) :
à la première conversion, l'application propose de le télécharger (build GPL de [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds))
dans `%LOCALAPPDATA%\AllInOne\ffmpeg`. Si FFmpeg est déjà installé (à côté de l'exe ou dans le `PATH`), il est utilisé directement.
La variable d'environnement `ALLINONE_FFMPEG` permet d'indiquer un chemin précis.

| Format | Vidéo | Audio |
| --- | --- | --- |
| MP4, MKV, MOV | H.264 (libx264) | AAC |
| WebM | VP9 | Opus |
| AVI | MPEG-4 Part 2 (Xvid) | MP3 |
| OGG | Theora | Vorbis |

## Utiliser la ligne de commande

```bash
dotnet run --project src/AllInOne.Cli -- --help
```

Exemples (les pages sont numérotées à partir de 1) :

```bash
allinone-cli images2pdf album.pdf photo1.jpg photo2.png --size A4 --margin 10
allinone-cli merge tout.pdf partie1.pdf partie2.pdf annexe.png
allinone-cli delete rapport.pdf 2,5-7 -o rapport_court.pdf
allinone-cli reorder rapport.pdf 3,1,2 -o rapport_reordonne.pdf
allinone-cli move rapport.pdf 5 1 -o rapport.pdf
allinone-cli insert rapport.pdf 3 couverture.pdf schema.png -o rapport_complet.pdf
allinone-cli rotate scan.pdf 1-2 90 -o scan_droit.pdf
allinone-cli extract rapport.pdf 4-6 -o extrait.pdf
allinone-cli split rapport.pdf pages/
allinone-cli split rapport.pdf parties/ --ranges "1-3, 4-7, 8-"   # un fichier par plage
allinone-cli split rapport.pdf paquets/ --every 10
allinone-cli compress scan.pdf scan_leger.pdf --level forte
allinone-cli pdf2img rapport.pdf images/ --format png --dpi 200 --pages 1-3
allinone-cli convert sortie/ *.png --format webp --quality 85 --max-width 1920
allinone-cli video sortie/ film.avi clip.mov --format mp4 --quality haute --max-height 1080
allinone-cli video sortie/ film.mp4 --format mkv --copy        # changement de conteneur, sans réencodage
allinone-cli audio sortie/ morceau.wav concert.aiff --format mp3 --quality haute
allinone-cli audio sortie/ film.mp4 --format flac                # extrait le son d'une vidéo
allinone-cli video sortie/ film.mp4 --format gif --start 0:12 --end 0:18 --quality faible
allinone-cli frames sortie/ film.mp4 --format jpg --fps 1 --start 1:00 --end 2:00   # 1 image par seconde
allinone-cli frames sortie/ clip.mov                                               # toutes les images, en PNG
allinone-cli audio sortie/ concert.wav --format mp3 --start 1:30 --end 4:05
allinone-cli convert sortie/ IMG_0001.HEIC IMG_0002.HEIC --format jpg
```

## Bibliothèques utilisées et licences

Toutes les dépendances sont sous licence permissive, compatibles avec un usage personnel ou commercial :

| Bibliothèque | Usage | Licence |
| --- | --- | --- |
| [PDFsharp](https://github.com/empira/PDFsharp) 6.2.4 | Création et modification des PDF | MIT |
| [PDFtoImage](https://github.com/sungaila/PDFtoImage) | Rendu des pages PDF en images | MIT |
| [PDFium](https://pdfium.googlesource.com/pdfium/) (binaires bblanchon) | Moteur de rendu PDF utilisé par PDFtoImage | BSD-3 / Apache 2.0 |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | Graphismes utilisés par PDFtoImage | MIT |
| [Magick.NET](https://github.com/dlemstra/Magick.NET) (ImageMagick) | Lecture et conversion des images | Apache 2.0 |
| [FFmpeg](https://ffmpeg.org/) (téléchargé à part, non inclus) | Conversion vidéo et audio | GPL (programme séparé) |

> **Attention :** PDFsharp est volontairement figé en version **6.2.4**. Les versions 6.5 et suivantes sont passées
> sous licence AGPL-3.0 avec une vérification de licence au démarrage. Ne mettez pas à jour ce paquet sans vérifier sa licence.

FFmpeg est un programme indépendant, exécuté comme processus externe et téléchargé par l'utilisateur : il n'est ni lié ni
redistribué avec AllInOne, dont la licence n'est donc pas affectée par la GPL de FFmpeg.

ImageSharp n'a pas été retenu car sa licence actuelle (Six Labors Split License) n'est pas totalement libre pour un usage commercial.

## Limites connues

- Les PDF protégés par mot de passe ne sont pas pris en charge pour la modification.
- L'application de bureau est réservée à Windows (WPF) ; la bibliothèque et la ligne de commande fonctionnent partout.

## Tests

```bash
dotnet test
```

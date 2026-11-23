# AllInOne : outils PDF et images (C# / .NET 8)

Application de bureau Windows (WPF) et outil en ligne de commande pour :

- **créer un PDF à partir d'une ou plusieurs images** (une page par image, TIFF multipage pris en charge) ;
- **modifier un PDF** : supprimer des pages, en ajouter (depuis d'autres PDF ou des images), les réorganiser, les faire pivoter ;
- **fusionner plusieurs PDF** (et images) en un seul document ;
- **convertir un PDF en images** (PNG, JPEG, WebP, BMP, GIF, TIFF, résolution au choix) ;
- **convertir des images d'un format à un autre** (PNG, JPEG, WebP, BMP, GIF, TIFF), avec redimensionnement optionnel.

Les fichiers d'origine ne sont jamais modifiés, sauf si vous choisissez vous-même de les écraser.

## Structure

| Projet | Rôle |
| --- | --- |
| `src/AllInOne.Core` | Bibliothèque multiplateforme qui contient toute la logique (utilisable depuis n'importe quel programme .NET). |
| `src/AllInOne.App` | Application de bureau WPF (Windows) avec quatre onglets. |
| `src/AllInOne.Cli` | Outil en ligne de commande `allinone` (Windows, Linux, macOS). |
| `tests/AllInOne.Core.Tests` | Tests automatisés (xUnit). |

## Utiliser l'application de bureau

Prérequis : Windows 10/11 et le [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run --project src/AllInOne.App
```

Pour obtenir un `.exe` autonome (sans installer .NET sur le poste cible) :

```powershell
dotnet publish src/AllInOne.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

L'intégration continue GitHub Actions produit aussi ces exécutables (artefact `AllInOne-win-x64`).

Onglets :

1. **Images → PDF** : ajoutez ou glissez des images, ordonnez-les (glisser-déposer, Monter/Descendre, Trier par nom), choisissez la taille de page (A4, A3, A5, Lettre, Légal ou taille de l'image), l'orientation et la marge.
2. **Modifier / fusionner des PDF** : ouvrez un ou plusieurs PDF et/ou images ; toutes les pages s'affichent en vignettes. Sélectionnez-les pour les supprimer (touche Suppr), les pivoter, les déplacer ou glissez-les à la souris. Les fichiers ajoutés sont insérés après la page sélectionnée. « Enregistrer sous » produit le PDF final.
3. **PDF → images** : choisissez un PDF, les pages (ex. `1-3,5`, vide = toutes), le format, la résolution et la qualité.
4. **Convertir des images** : convertissez un lot d'images vers un autre format, avec largeur/hauteur maximales optionnelles.

## Utiliser la ligne de commande

```bash
dotnet run --project src/AllInOne.Cli -- --help
```

Exemples (les pages sont numérotées à partir de 1) :

```bash
allinone images2pdf album.pdf photo1.jpg photo2.png --size A4 --margin 10
allinone merge tout.pdf partie1.pdf partie2.pdf annexe.png
allinone delete rapport.pdf 2,5-7 -o rapport_court.pdf
allinone reorder rapport.pdf 3,1,2 -o rapport_reordonne.pdf
allinone move rapport.pdf 5 1 -o rapport.pdf
allinone insert rapport.pdf 3 couverture.pdf schema.png -o rapport_complet.pdf
allinone rotate scan.pdf 1-2 90 -o scan_droit.pdf
allinone extract rapport.pdf 4-6 -o extrait.pdf
allinone split rapport.pdf pages/
allinone pdf2img rapport.pdf images/ --format png --dpi 200 --pages 1-3
allinone convert sortie/ *.png --format webp --quality 85 --max-width 1920
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

> **Attention :** PDFsharp est volontairement figé en version **6.2.4**. Les versions 6.5 et suivantes sont passées
> sous licence AGPL-3.0 avec une vérification de licence au démarrage. Ne mettez pas à jour ce paquet sans vérifier sa licence.

ImageSharp n'a pas été retenu car sa licence actuelle (Six Labors Split License) n'est pas totalement libre pour un usage commercial.

## Limites connues

- Les PDF protégés par mot de passe ne sont pas pris en charge pour la modification.
- L'application de bureau est réservée à Windows (WPF) ; la bibliothèque et la ligne de commande fonctionnent partout.

## Tests

```bash
dotnet test
```

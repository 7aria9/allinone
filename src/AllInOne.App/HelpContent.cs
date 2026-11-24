using AllInOne.Core.Localization;

namespace AllInOne.App;

/// <summary>Une rubrique de l'aide : un titre, une courte présentation, les étapes à suivre et quelques astuces.</summary>
internal sealed record HelpTopic(string Icon, string Title, string Intro, IReadOnlyList<string> Steps, IReadOnlyList<string> Tips)
{
    public string Label => $"{Icon}   {Title}";
}

/// <summary>
/// Contenu de l'aide, dans la langue en cours. Les rubriques 1 à 9 suivent l'ordre des écrans de la navigation
/// (rubrique 1 = premier écran), ce qui permet d'ouvrir directement l'aide de l'écran affiché.
/// </summary>
internal static class HelpContent
{
    /// <summary>Indice de la rubrique qui correspond à l'écran <paramref name="page"/> (0 = Images → PDF).</summary>
    public static int TopicForPage(int page) => page < 0 ? 0 : page + 1;

    public static IReadOnlyList<HelpTopic> Topics() =>
    [
        new("👋", L.T("Premiers pas"),
            L.T("LocalConvert regroupe les conversions qu'on fait d'habitude sur des sites web, mais tout se passe sur ton ordinateur : tes fichiers ne sont jamais envoyés sur Internet."),
            [
                L.T("Choisis un outil dans la colonne de gauche."),
                L.T("Ajoute tes fichiers avec le bouton « Ajouter » ou glisse-les depuis l'Explorateur, le Finder ou ton gestionnaire de fichiers."),
                L.T("Règle les options (format, qualité…) puis choisis le dossier de destination."),
                L.T("Lance la conversion avec le gros bouton en bas. La barre d'état suit la progression ; le bouton « Arrêter » interrompt les longues tâches."),
                L.T("À la fin, LocalConvert propose d'ouvrir le fichier ou le dossier créé."),
            ],
            [
                L.T("Tes fichiers d'origine ne sont jamais modifiés : le résultat est toujours enregistré dans un nouveau fichier."),
                L.T("Sans destination choisie, les fichiers sont créés dans un sous-dossier à côté du premier fichier de la liste."),
                L.T("Chaque écran garde ses réglages et sa liste quand tu passes à un autre outil."),
                L.T("La langue se change en haut à droite. La touche F1 ouvre l'aide de l'écran affiché."),
            ]),

        new("🖼", L.T("Images → PDF"),
            L.T("Rassemble des photos ou des scans dans un seul PDF, une image par page."),
            [
                L.T("Ajoute tes images (PNG, JPEG, HEIC, WebP…) ou glisse-les dans la liste."),
                L.T("Mets-les dans l'ordre : glisse-les à la souris, utilise « Monter » et « Descendre », ou « Trier » par nom de fichier."),
                L.T("Choisis la taille de page (A4, Lettre US…) ou « Taille de l'image » pour garder les dimensions de chaque image, puis l'orientation et la marge."),
                L.T("Clique sur « Créer le PDF » et choisis où l'enregistrer."),
            ],
            [
                L.T("En orientation « Automatique », chaque page suit le sens de son image (portrait ou paysage)."),
                L.T("« Agrandir les petites images » les étire pour remplir la page ; décoche-le pour garder leur taille réelle."),
            ]),

        new("📄", L.T("Modifier / fusionner"),
            L.T("Supprime, fais pivoter, réordonne des pages ou fusionne plusieurs PDF et images en un seul document."),
            [
                L.T("Ajoute un ou plusieurs PDF (ou des images) : toutes leurs pages s'affichent en vignettes."),
                L.T("Sélectionne des pages (Ctrl ou Maj pour en prendre plusieurs) puis supprime-les, fais-les pivoter ou déplace-les avec les flèches, « Au début » ou « À la fin »."),
                L.T("Tu peux aussi glisser les vignettes à la souris pour changer leur ordre."),
                L.T("Clique sur « Enregistrer sous… » pour créer le nouveau PDF."),
            ],
            [
                L.T("Pour fusionner, ajoute simplement plusieurs fichiers : les fichiers ajoutés sont insérés après la page sélectionnée."),
                L.T("La touche Suppr supprime les pages sélectionnées. « Inverser » retourne l'ordre de toutes les pages."),
                L.T("Rien n'est modifié tant que tu n'as pas enregistré : « Tout fermer » repart de zéro."),
            ]),

        new("🗜", L.T("Compresser un PDF"),
            L.T("Allège les PDF lourds (scans, documents pleins de photos) pour les envoyer plus facilement."),
            [
                L.T("Ajoute un ou plusieurs PDF."),
                L.T("Choisis le niveau : Légère (presque aucune perte), Moyenne (conseillé) ou Forte (le plus léger, pour l'e-mail)."),
                L.T("Choisis la destination puis clique sur « Compresser »."),
            ],
            [
                L.T("Seules les images sont réduites : le texte reste net et sélectionnable."),
                L.T("Un PDF déjà optimisé ne peut pas toujours être allégé : il est alors simplement copié."),
            ]),

        new("✂", L.T("Découper un PDF"),
            L.T("Sépare un PDF en plusieurs fichiers plus petits."),
            [
                L.T("Choisis un PDF avec « Parcourir… » ou glisse-le sur l'écran."),
                L.T("Choisis le découpage : une page par fichier, toutes les N pages, ou selon des plages de pages."),
                L.T("Pour les plages, sépare-les par des virgules : « 1-3, 4-7, 8- » donne trois PDF (8- va jusqu'à la fin)."),
                L.T("Clique sur « Découper » : la liste des fichiers créés s'affiche à droite."),
            ],
            [
                L.T("Pour extraire seulement quelques pages, indique une seule plage, par exemple « 5-9 »."),
            ]),

        new("🧩", L.T("PDF → images"),
            L.T("Transforme chaque page d'un PDF en image (PNG, JPEG…)."),
            [
                L.T("Choisis un PDF avec « Parcourir… » ou glisse-le sur l'écran : un aperçu de la première page s'affiche."),
                L.T("Indique les pages à convertir (par exemple 1-3,5 ou 4-), ou laisse vide pour toutes."),
                L.T("Choisis le format et la résolution : 150 DPI suffit pour l'écran, 300 DPI pour l'impression."),
                L.T("Clique sur « Convertir en images »."),
            ],
            [
                L.T("Plus la résolution est haute, plus les images sont nettes… et lourdes."),
            ]),

        new("📝", L.T("PDF → Word / texte"),
            L.T("Récupère le texte de PDF, de scans ou de photos de documents dans un fichier modifiable : Word (.docx), OpenDocument (.odt, LibreOffice) ou texte brut (.txt)."),
            [
                L.T("Ajoute des PDF ou des images (photo d'une page, capture d'écran…)."),
                L.T("Choisis le format à créer."),
                L.T("Laisse « Lire le texte des scans et des images (OCR) » coché pour reconnaître le texte des pages scannées et des photos."),
                L.T("Choisis la destination puis clique sur « Convertir »."),
            ],
            [
                L.T("Le texte, les paragraphes, les titres et le gras sont repris, une page du PDF par page du document."),
                L.T("Les images, les tableaux et la mise en page exacte ne sont pas repris."),
                L.T("L'OCR prend quelques secondes par page et fonctionne sans Internet. Il reconnaît les langues à alphabet latin (français, anglais, allemand, espagnol…)."),
                L.T("Sur les Mac à processeur Intel, l'OCR n'est pas disponible : seul le texte déjà présent dans les PDF est repris."),
            ]),

        new("🔁", L.T("Convertir des images"),
            L.T("Convertit un lot d'images d'un format à un autre, avec redimensionnement si besoin."),
            [
                L.T("Ajoute tes images : PNG, JPEG, WebP, HEIC (photos d'iPhone), AVIF, TIFF, BMP, GIF, ICO…"),
                L.T("Choisis le format de sortie (PNG, JPEG, WebP, BMP, GIF ou TIFF) et la qualité."),
                L.T("Pour réduire les images, indique une largeur ou une hauteur maximale en pixels : les proportions sont conservées."),
                L.T("Choisis la destination puis clique sur « Convertir »."),
            ],
            [
                L.T("Laisse largeur et hauteur vides pour garder la taille d'origine."),
                L.T("Pour envoyer des photos HEIC d'un iPhone à quelqu'un, convertis-les en JPEG."),
            ]),

        new("🎬", L.T("Vidéos et GIF"),
            L.T("Convertit des vidéos dans un autre format, crée des GIF animés, garde un extrait ou extrait les images d'une vidéo."),
            [
                L.T("Ajoute tes vidéos (MP4, MKV, MOV, AVI, WebM…)."),
                L.T("Choisis le format : une vidéo, un GIF animé, ou des images PNG ou JPEG (indique alors combien d'images garder)."),
                L.T("Règle la qualité et la résolution maximale ; coche « Supprimer le son » si besoin."),
                L.T("Pour ne garder qu'un passage, indique un début et une fin (par exemple 1:30 et 2:45)."),
                L.T("Clique sur « Convertir »."),
            ],
            [
                L.T("« Changer seulement de conteneur » est très rapide et sans perte, mais ne marche que si les codecs d'origine sont acceptés par le nouveau format (par exemple MP4 → MKV)."),
                L.T("Pour un GIF léger, garde un extrait court et une petite résolution (480p ou moins)."),
                L.T("Les vidéos et les sons sont convertis par FFmpeg (voir la rubrique « Questions fréquentes »)."),
            ]),

        new("🎵", L.T("Convertir des sons"),
            L.T("Convertit des fichiers audio, garde un extrait ou récupère la bande son d'une vidéo."),
            [
                L.T("Ajoute tes sons (MP3, WAV, FLAC, M4A, OGG, WMA…) ou des vidéos pour en extraire le son."),
                L.T("Choisis le format : MP3, WAV, AIFF, FLAC, OGG, Opus ou M4A."),
                L.T("Règle la qualité, la fréquence et les canaux, ou laisse « Comme l'original »."),
                L.T("Pour couper le son, indique un début et une fin, puis clique sur « Convertir »."),
            ],
            [
                L.T("WAV, AIFF et FLAC sont sans perte. MP3, M4A, OGG et Opus donnent des fichiers bien plus légers."),
            ]),

        new("❓", L.T("Questions fréquentes"),
            L.T("Les réponses aux questions qu'on se pose le plus souvent."),
            [],
            [
                L.T("Mes fichiers sont-ils envoyés quelque part ? Non. Tout est fait sur ton ordinateur, même l'OCR. Seul le téléchargement de FFmpeg (une fois, sous Windows) utilise Internet."),
                L.T("Qu'est-ce que FFmpeg ? Un logiciel libre qui convertit les vidéos et les sons. Sous Windows, LocalConvert propose de le télécharger à la première conversion (environ 200 Mo, une seule fois). Sous macOS et Linux, installe-le avec brew ou apt."),
                L.T("Une conversion échoue ? Le message d'erreur s'affiche et les détails sont enregistrés dans le dossier « logs » de LocalConvert, dans les données de ton compte (sous Windows : %LOCALAPPDATA%\\LocalConvert\\logs)."),
                L.T("Peut-on automatiser les conversions ? Oui : le programme en ligne de commande localconvert-cli, livré avec l'application, fait les mêmes conversions (lance-le sans argument pour voir la liste des commandes)."),
                L.T("Où trouver la dernière version ? Sur la page GitHub du projet, rubrique « Releases » (lien dans « À propos »)."),
            ]),
    ];
}

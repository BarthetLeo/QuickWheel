using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace QuickWheel.Models;

public sealed record IconEntry(string Name, string Category, string[] Aliases, string PathData)
{
    public string Ref => "mdi:" + Name;
}

/// <summary>
/// Catalogue Material Design Icons (Pictogrammers, licence Apache 2.0) embarqué dans l'exe.
/// Recherche par nom anglais, alias, catégorie, ou mot-clé français courant.
/// </summary>
public static class IconCatalog
{
    private static readonly Lazy<List<IconEntry>> _all = new(Load);
    private static readonly Lazy<Dictionary<string, IconEntry>> _byName = new(() => _all.Value.ToDictionary(e => e.Name));

    public static IReadOnlyList<IconEntry> All => _all.Value;

    public static IconEntry? Get(string name) => _byName.Value.TryGetValue(name, out var e) ? e : null;

    public static readonly Dictionary<string, string> CategoryFr = new()
    {
        ["Autres"] = "Autres", ["Home Automation"] = "Maison connectée", ["Files / Folders"] = "Fichiers et dossiers",
        ["Alpha / Numeric"] = "Lettres et chiffres", ["Arrow"] = "Flèches", ["Date / Time"] = "Date et heure",
        ["Text / Content / Format"] = "Texte et mise en forme", ["Account / User"] = "Comptes et personnes",
        ["Food / Drink"] = "Nourriture et boissons", ["Gaming / RPG"] = "Jeux vidéo et RPG", ["Banking"] = "Banque",
        ["Cellphone / Phone"] = "Téléphone", ["Math"] = "Maths", ["Sport"] = "Sport", ["Automotive"] = "Automobile",
        ["Video / Movie"] = "Vidéo et cinéma", ["Settings"] = "Réglages", ["Weather"] = "Météo",
        ["Alert / Error"] = "Alertes et erreurs", ["Lock"] = "Sécurité", ["Shape"] = "Formes",
        ["Transportation + Road"] = "Transport routier", ["Photography"] = "Photo", ["Battery"] = "Batterie",
        ["Navigation"] = "Navigation", ["Database"] = "Bases de données", ["Audio"] = "Audio", ["Science"] = "Science",
        ["Transportation + Other"] = "Transports divers", ["Places"] = "Lieux", ["Medical / Hospital"] = "Médical",
        ["Form"] = "Formulaires", ["People / Family"] = "Famille", ["Developer / Languages"] = "Développement",
        ["Nature"] = "Nature", ["Printer"] = "Impression", ["Music"] = "Musique", ["Device / Tech"] = "Appareils",
        ["Animal"] = "Animaux", ["Edit / Modify"] = "Édition", ["Hardware / Tools"] = "Outils", ["Cloud"] = "Cloud",
        ["View"] = "Affichage", ["Shopping"] = "Shopping", ["Geographic Information System"] = "Cartographie",
        ["Transportation + Flying"] = "Aérien", ["Currency"] = "Monnaies", ["Emoji"] = "Émotions",
        ["Vector"] = "Vectoriel", ["Holiday"] = "Fêtes", ["Agriculture"] = "Agriculture",
        ["Notification"] = "Notifications", ["Drawing / Art"] = "Dessin et art", ["Clothing"] = "Vêtements",
        ["Tooltip"] = "Bulles", ["Religion"] = "Religion", ["Color"] = "Couleurs", ["Arrange"] = "Disposition",
        ["Transportation + Water"] = "Maritime", ["Health / Beauty"] = "Santé et beauté",
    };

    public static string CategoryName(string raw) => CategoryFr.TryGetValue(raw, out var fr) ? fr : raw;

    /// <summary>Mots français fréquents → termes anglais du catalogue.</summary>
    private static readonly Dictionary<string, string[]> FrenchWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["maison"] = new[] { "home", "house" }, ["accueil"] = new[] { "home" }, ["dossier"] = new[] { "folder" },
        ["fichier"] = new[] { "file" }, ["copier"] = new[] { "copy" }, ["coller"] = new[] { "paste" },
        ["couper"] = new[] { "cut", "scissors" }, ["annuler"] = new[] { "undo", "cancel" }, ["refaire"] = new[] { "redo" },
        ["enregistrer"] = new[] { "save", "content-save" }, ["sauvegarder"] = new[] { "save", "backup" },
        ["supprimer"] = new[] { "delete", "trash" }, ["poubelle"] = new[] { "trash", "delete" },
        ["recherche"] = new[] { "search", "magnify" }, ["chercher"] = new[] { "search", "magnify" },
        ["réglages"] = new[] { "cog", "settings" }, ["paramètres"] = new[] { "cog", "settings" }, ["engrenage"] = new[] { "cog" },
        ["étoile"] = new[] { "star" }, ["coeur"] = new[] { "heart" }, ["cœur"] = new[] { "heart" },
        ["musique"] = new[] { "music" }, ["lecture"] = new[] { "play" }, ["pause"] = new[] { "pause" },
        ["suivant"] = new[] { "skip-next", "next" }, ["précédent"] = new[] { "skip-previous", "previous" },
        ["son"] = new[] { "volume" }, ["volume"] = new[] { "volume" }, ["muet"] = new[] { "mute", "volume-off" },
        ["micro"] = new[] { "microphone" }, ["caméra"] = new[] { "camera", "video" }, ["photo"] = new[] { "camera", "image" },
        ["image"] = new[] { "image" }, ["vidéo"] = new[] { "video", "movie" }, ["film"] = new[] { "movie", "film" },
        ["téléphone"] = new[] { "phone" }, ["mail"] = new[] { "email" }, ["courriel"] = new[] { "email" },
        ["message"] = new[] { "message", "chat" }, ["discussion"] = new[] { "chat", "forum" },
        ["utilisateur"] = new[] { "account" }, ["personne"] = new[] { "account", "human" }, ["groupe"] = new[] { "account-group" },
        ["calendrier"] = new[] { "calendar" }, ["horloge"] = new[] { "clock" }, ["heure"] = new[] { "clock" },
        ["date"] = new[] { "calendar" }, ["minuteur"] = new[] { "timer" }, ["alarme"] = new[] { "alarm" },
        ["cadenas"] = new[] { "lock" }, ["verrou"] = new[] { "lock" }, ["clé"] = new[] { "key" }, ["clef"] = new[] { "key" },
        ["écran"] = new[] { "monitor" }, ["ordinateur"] = new[] { "laptop", "desktop", "monitor" },
        ["clavier"] = new[] { "keyboard" }, ["souris"] = new[] { "mouse" }, ["imprimante"] = new[] { "printer" },
        ["capture"] = new[] { "screenshot", "camera" }, ["terminal"] = new[] { "console", "terminal" },
        ["code"] = new[] { "code", "xml" }, ["navigateur"] = new[] { "web", "earth", "google-chrome" },
        ["internet"] = new[] { "web", "earth" }, ["web"] = new[] { "web" }, ["lien"] = new[] { "link" },
        ["onglet"] = new[] { "tab" }, ["fenêtre"] = new[] { "window" }, ["fermer"] = new[] { "close" },
        ["ouvrir"] = new[] { "open" }, ["ajouter"] = new[] { "plus", "add" }, ["plus"] = new[] { "plus" },
        ["moins"] = new[] { "minus" }, ["modifier"] = new[] { "pencil", "edit" }, ["crayon"] = new[] { "pencil" },
        ["stylo"] = new[] { "pen" }, ["pinceau"] = new[] { "brush" }, ["couleur"] = new[] { "palette", "color" },
        ["texte"] = new[] { "text", "format" }, ["gras"] = new[] { "bold" }, ["italique"] = new[] { "italic" },
        ["liste"] = new[] { "list" }, ["tableau"] = new[] { "table" }, ["graphique"] = new[] { "chart" },
        ["flèche"] = new[] { "arrow" }, ["haut"] = new[] { "up" }, ["bas"] = new[] { "down" },
        ["gauche"] = new[] { "left" }, ["droite"] = new[] { "right" }, ["rafraîchir"] = new[] { "refresh" },
        ["actualiser"] = new[] { "refresh" }, ["télécharger"] = new[] { "download" }, ["envoyer"] = new[] { "send", "upload" },
        ["nuage"] = new[] { "cloud" }, ["météo"] = new[] { "weather" }, ["soleil"] = new[] { "sun", "weather-sunny" },
        ["lune"] = new[] { "moon" }, ["pluie"] = new[] { "rain" }, ["feu"] = new[] { "fire" },
        ["épée"] = new[] { "sword" }, ["bouclier"] = new[] { "shield" }, ["jeu"] = new[] { "gamepad", "controller" },
        ["manette"] = new[] { "controller", "gamepad" }, ["dé"] = new[] { "dice" }, ["potion"] = new[] { "bottle", "flask" },
        ["magie"] = new[] { "magic", "wizard" }, ["château"] = new[] { "castle" }, ["couronne"] = new[] { "crown" },
        ["voiture"] = new[] { "car" }, ["avion"] = new[] { "airplane" }, ["train"] = new[] { "train" },
        ["vélo"] = new[] { "bike", "bicycle" }, ["carte"] = new[] { "map", "card" }, ["lieu"] = new[] { "map-marker" },
        ["argent"] = new[] { "cash", "currency" }, ["euro"] = new[] { "currency-eur" }, ["banque"] = new[] { "bank" },
        ["panier"] = new[] { "cart", "basket" }, ["boutique"] = new[] { "store", "shopping" },
        ["café"] = new[] { "coffee" }, ["nourriture"] = new[] { "food" }, ["livre"] = new[] { "book" },
        ["note"] = new[] { "note" }, ["notes"] = new[] { "note", "notebook" }, ["tâche"] = new[] { "checkbox", "check" },
        ["valider"] = new[] { "check" }, ["alerte"] = new[] { "alert" }, ["info"] = new[] { "information" },
        ["aide"] = new[] { "help" }, ["question"] = new[] { "help", "question" }, ["cloche"] = new[] { "bell" },
        ["notification"] = new[] { "bell" }, ["base"] = new[] { "database" }, ["serveur"] = new[] { "server" },
        ["wifi"] = new[] { "wifi" }, ["batterie"] = new[] { "battery" }, ["énergie"] = new[] { "power", "flash" },
        ["éteindre"] = new[] { "power" }, ["outil"] = new[] { "tools", "wrench", "hammer" }, ["outils"] = new[] { "tools", "toolbox" },
        ["marteau"] = new[] { "hammer" }, ["clé-anglaise"] = new[] { "wrench" }, ["bug"] = new[] { "bug" },
        ["robot"] = new[] { "robot" }, ["ia"] = new[] { "robot", "brain" }, ["cerveau"] = new[] { "brain" },
        ["chat"] = new[] { "cat", "chat" }, ["chien"] = new[] { "dog" }, ["arbre"] = new[] { "tree" }, ["fleur"] = new[] { "flower" },
        ["sport"] = new[] { "soccer", "basketball", "run" }, ["drapeau"] = new[] { "flag" }, ["cadeau"] = new[] { "gift" },
        ["éclair"] = new[] { "flash", "lightning" }, ["œil"] = new[] { "eye" }, ["oeil"] = new[] { "eye" },
        ["zoom"] = new[] { "magnify-plus", "magnify-minus" }, ["partager"] = new[] { "share" }, ["pouce"] = new[] { "thumb" },
    };

    public static IEnumerable<string> Categories() =>
        All.Select(e => e.Category).Distinct().OrderBy(c => c == "Autres" ? "~" : CategoryName(c));

    public static List<IconEntry> Search(string? query, string? category = null)
    {
        IEnumerable<IconEntry> src = All;
        if (!string.IsNullOrEmpty(category)) src = src.Where(e => e.Category == category);
        if (string.IsNullOrWhiteSpace(query)) return src.ToList();

        var terms = new List<string>();
        foreach (var word in query.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            terms.Add(word);
            if (FrenchWords.TryGetValue(word, out var en)) terms.AddRange(en);
            // Préfixe de mot français (« dossi » → dossier)
            else if (word.Length >= 3)
                foreach (var kv in FrenchWords.Where(kv => kv.Key.StartsWith(word, StringComparison.OrdinalIgnoreCase)).Take(3))
                    terms.AddRange(kv.Value);
        }

        int Score(IconEntry e)
        {
            int best = int.MaxValue;
            foreach (var t in terms)
            {
                if (e.Name == t) best = Math.Min(best, 0);
                else if (e.Name.StartsWith(t)) best = Math.Min(best, 1);
                else if (e.Name.Contains(t)) best = Math.Min(best, 2);
                else if (e.Aliases.Any(a => a.Contains(t))) best = Math.Min(best, 3);
                else if (CategoryName(e.Category).Contains(t, StringComparison.OrdinalIgnoreCase)) best = Math.Min(best, 4);
            }
            return best;
        }

        return src.Select(e => (e, s: Score(e)))
            .Where(x => x.s != int.MaxValue)
            .OrderBy(x => x.s).ThenBy(x => x.e.Name.Length).ThenBy(x => x.e.Name)
            .Select(x => x.e)
            .ToList();
    }

    private static List<IconEntry> Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames().First(n => n.EndsWith("mdi.tsv.gz"));
        using var stream = asm.GetManifestResourceStream(resName)!;
        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        var list = new List<IconEntry>(7500);
        while (reader.ReadLine() is { } line)
        {
            var p = line.Split('\t');
            if (p.Length < 4) continue;
            list.Add(new IconEntry(p[0], p[1], p[2].Length == 0 ? Array.Empty<string>() : p[2].Split(','), p[3]));
        }
        return list;
    }
}

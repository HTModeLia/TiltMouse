using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace MouseSmooth
{
    /// <summary>
    /// Vérifie sur GitHub si une version plus récente est publiée (dernière release,
    /// hors préversions). Prévient seulement : rien n'est téléchargé ni remplacé.
    /// Aucune donnée envoyée hormis la requête elle-même.
    /// </summary>
    internal static class UpdateCheck
    {
        public const string ReleasesPage = "https://github.com/HTModeLia/TiltMouse/releases/latest";
        private const string LatestApi = "https://api.github.com/repos/HTModeLia/TiltMouse/releases/latest";

        public static Version Current
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        /// <summary>Version publiée si elle est plus récente que celle-ci, sinon null (y compris hors ligne).</summary>
        public static async Task<Version> FindNewerAsync()
        {
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("MouseSmooth"); // exigé par l'API GitHub
                    string json = await http.GetStringAsync(LatestApi).ConfigureAwait(false);
                    using (var doc = JsonDocument.Parse(json))
                    {
                        Version latest = ParseTag(doc.RootElement.GetProperty("tag_name").GetString());
                        return latest != null && latest > Current ? latest : null;
                    }
                }
            }
            catch
            {
                return null; // pas de réseau, GitHub indisponible, réponse inattendue : on réessaiera plus tard
            }
        }

        /// <summary>« v2.1.0 » → 2.1.0 (trois composantes, comparables à <see cref="Current"/>).</summary>
        public static Version ParseTag(string tag)
        {
            if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out Version v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }
}

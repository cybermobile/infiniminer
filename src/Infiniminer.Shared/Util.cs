using System.Globalization;
using System.Net.Http;

namespace Infiniminer
{
    public static class TextUtil
    {
        // Strips everything outside printable ASCII; the game's fonts only cover this range.
        public static string Sanitize(string input)
        {
            if (input == null)
                return "";
            System.Text.StringBuilder output = new System.Text.StringBuilder(input.Length);
            foreach (char c in input)
                if (c >= 32 && c <= 126)
                    output.Append(c);
            return output.ToString();
        }
    }

    /* Loads in a datafile consisting of key/value pairs, in the format of
     * "key = value", which can be read out through the Data dictionary.
     */
    public class DatafileLoader
    {
        readonly Dictionary<string, string> dataDict = new Dictionary<string, string>();
        public Dictionary<string, string> Data
        {
            get { return dataDict; }
        }

        public DatafileLoader(string filename)
        {
            if (!File.Exists(filename))
                return;

            foreach (string line in File.ReadAllLines(filename))
            {
                string[] args = line.Split('=');
                if (args.Length == 2 && !line.TrimStart().StartsWith('#'))
                    dataDict[args[0].Trim()] = args[1].Trim();
            }
        }

        public string GetString(string key, string defaultValue)
        {
            return dataDict.TryGetValue(key, out string value) ? value : defaultValue;
        }

        public bool GetBool(string key, bool defaultValue)
        {
            return dataDict.TryGetValue(key, out string value) && bool.TryParse(value, out bool result) ? result : defaultValue;
        }

        public int GetInt(string key, int defaultValue)
        {
            return dataDict.TryGetValue(key, out string value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : defaultValue;
        }

        public uint GetUInt(string key, uint defaultValue)
        {
            return dataDict.TryGetValue(key, out string value) && uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint result) ? result : defaultValue;
        }

        public float GetFloat(string key, float defaultValue)
        {
            return dataDict.TryGetValue(key, out string value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : defaultValue;
        }

        // Where the config file, and everything else the game writes, lives:
        //  1. the working directory, if it has the config file (a server run from a data folder, dev runs);
        //  2. next to the executable, if that folder can be written (an unzipped download stays portable);
        //  3. otherwise a per-user folder (installed under Program Files, inside a macOS .app bundle, or an AppImage),
        //     seeded with the default files shipped next to the executable.
        public static string ResolveDataDirectory(string configFileName, params string[] otherSeedFiles)
        {
            // An AppImage counts as installed even when it runs unpacked into a temporary (writable) folder.
            bool appImage = Environment.GetEnvironmentVariable("APPIMAGE") != null;
            return ResolveDataDirectory(Environment.CurrentDirectory, AppContext.BaseDirectory, UserDataDirectory(),
                directory => !appImage && IsWritable(directory), configFileName, otherSeedFiles);
        }

        public static string ResolveDataDirectory(string currentDirectory, string baseDirectory, string userDirectory,
            Func<string, bool> isWritable, string configFileName, params string[] otherSeedFiles)
        {
            if (File.Exists(Path.Combine(currentDirectory, configFileName)))
                return currentDirectory;
            if (!IsInsideAppBundle(baseDirectory) && isWritable(baseDirectory))
                return baseDirectory;

            try
            {
                Directory.CreateDirectory(userDirectory);
                foreach (string file in otherSeedFiles.Prepend(configFileName))
                {
                    string source = Path.Combine(baseDirectory, file), target = Path.Combine(userDirectory, file);
                    if (File.Exists(source) && !File.Exists(target))
                        File.Copy(source, target);
                }
                return userDirectory;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Nowhere to write: run from the shipped defaults, settings just won't be saved.
                return baseDirectory;
            }
        }

        // %APPDATA%\Infiniminer on Windows, ~/Library/Application Support/Infiniminer on macOS,
        // $XDG_CONFIG_HOME/Infiniminer (usually ~/.config/Infiniminer) on Linux.
        public static string UserDataDirectory()
        {
            if (OperatingSystem.IsMacOS())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Infiniminer");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), "Infiniminer");
        }

        // Writing into a signed .app bundle would break its signature even when it is allowed.
        static bool IsInsideAppBundle(string directory)
        {
            return directory.Replace('\\', '/').Contains(".app/Contents/", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsWritable(string directory)
        {
            try
            {
                string probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public static class ServerListUrl
    {
        // "http://host:8080" means the list's standard endpoint (/post for servers, /plain for clients);
        // a URL with a path is used as given, so the original style of full URLs keeps working.
        public static string Endpoint(string url, string path)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && (uri.AbsolutePath == "/" || uri.AbsolutePath == ""))
                return url.TrimEnd('/') + path;
            return url;
        }
    }

    public static class HttpRequest
    {
        static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public static async Task<string> PostAsync(string url, Dictionary<string, string> parameters)
        {
            using HttpResponseMessage response = await client.PostAsync(url, new FormUrlEncodedContent(parameters)).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
        }

        public static async Task<string> GetAsync(string url)
        {
            return (await client.GetStringAsync(url).ConfigureAwait(false)).Trim();
        }
    }
}

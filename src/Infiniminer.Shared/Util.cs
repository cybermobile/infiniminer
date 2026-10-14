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

        // Config files are looked up in the working directory first (so a server can be run from a data
        // directory), then next to the executable (so `dotnet run` and double-clicking both work).
        public static string ResolveDataDirectory(string configFileName)
        {
            if (File.Exists(Path.Combine(Environment.CurrentDirectory, configFileName)))
                return Environment.CurrentDirectory;
            return AppContext.BaseDirectory;
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

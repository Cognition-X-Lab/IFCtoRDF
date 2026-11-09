using Azure;
using Azure.AI.Translation.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IFCtoRDF.Utils
{
    public static class Translator
    {
        private static TextTranslationClient _translator;
        private static readonly ConcurrentDictionary<string, string> _cache = new ConcurrentDictionary<string, string>();

        public static string Endpoint = "https://api.cognitive.microsofttranslator.com/";
        public static string AzureTranslatorRegion = "eastasia";
        public static string AzureTranslatorApiKey;

        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        public static void CreateTranslator()
        {
            if (AzureTranslatorApiKey == null || AzureTranslatorApiKey.Length == 0)
            {
                throw new Exception("Azure Translator API Key is not set.");
            }
            else
            {
                try
                {
                    //Dictionary<string, string> credentials = LoadCredentials();
                    _translator = new TextTranslationClient(
                        new AzureKeyCredential(AzureTranslatorApiKey),
                        new Uri(Endpoint),
                        AzureTranslatorRegion);
                }
                catch (Exception ex)
                {
                    throw new Exception(ex.Message);
                }
            }

            _translator = null;
        }

        //private static Dictionary<string, string> LoadCredentials()
        //{
        //    DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        //    string path = Path.Combine(directory.FullName, "credentials.txt");
        //    if (File.Exists(path))
        //    {
        //        var credentials = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        //        foreach (string rawLine in File.ReadAllLines(path))
        //        {
        //            string line = rawLine.Trim();
        //            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
        //                continue;

        //            int separator = line.IndexOf('=');
        //            if (separator <= 0)
        //                continue;

        //            string key = line.Substring(0, separator).Trim();
        //            string value = line.Substring(separator + 1).Trim();
        //            credentials[key] = value;
        //        }

        //        string apiKey;
        //        string region;
        //        string endpoint;
        //        if (!credentials.TryGetValue("AzureTranslatorApiKey", out apiKey) ||
        //            string.IsNullOrWhiteSpace(apiKey) ||
        //            !credentials.TryGetValue("Endpoint", out endpoint) ||
        //            string.IsNullOrWhiteSpace(endpoint) ||
        //            !credentials.TryGetValue("AzureTranslatorRegion", out region) ||
        //            string.IsNullOrWhiteSpace(region))
        //        {
        //            throw new InvalidDataException(
        //                "credentials.txt must contain AzureTranslatorApiKey, Endpoint, and AzureTranslatorRegion entries.");
        //        }

        //        return credentials;
        //    }
        //    else
        //    {
        //        throw new FileNotFoundException(
        //            $"credentials.txt was not found beside the application at {path}.\n" +
        //            "Copy credentials_example.txt to credentials.txt and fill in the Azure Translator settings.");
        //    }
        //}

        public static async Task<bool> HasInternetConnectionAsync()
        {
            try
            {
                using (HttpResponseMessage response = await _httpClient.GetAsync("https://www.microsoft.com"))
                {
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsProbablyEnglish(string text)
        {
            foreach (char c in text)
            {
                if (c > 127)
                {
                    return false;
                }
            }

            return true;
        }

        public static async Task<string> TranslateToEnglishAsync(string text, bool decodeIfc = true)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            if (IsProbablyEnglish(text))
            {
                return text;
            }

            if (_translator == null)
            {
                throw new Exception("Translator Setup is Required");
            }

            if (decodeIfc)
            {
                text = IfcDecoder.Decode(text);
            }

            string translatedText;
            if (_cache.TryGetValue(text, out translatedText))
            {
                return translatedText;
            }

            try
            {
                string sourceLanguage = GetSourceLanguage(text);
                Response<IReadOnlyList<TranslatedTextItem>> response = await _translator.TranslateAsync("en", new string[] { text }, sourceLanguage);

                translatedText = response.Value[0].Translations[0].Text;

                if (!string.IsNullOrWhiteSpace(translatedText))
                {
                    _cache[text] = translatedText;
                    return translatedText;
                }
            }
            catch (Exception)
            {
            }

            return text;
        }

        private static string GetSourceLanguage(string text)
        {
            bool hasHangul = false;
            bool hasChinese = false;

            foreach (char c in text)
            {
                if (c >= 0xAC00 && c <= 0xD7AF)
                {
                    hasHangul = true;
                    break;
                }

                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    hasChinese = true;
                }
            }

            if (hasHangul)
                return "ko";

            if (hasChinese)
                return "zh-Hans";

            return null;
        }
    }

    public static class IfcDecoder
    {
        public static string Decode(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            StringBuilder output = new StringBuilder();

            int i = 0;

            while (i < text.Length)
            {
                // \X2\
                if (i + 3 < text.Length &&
                    text[i] == '\\' &&
                    (text[i + 1] == 'X' || text[i + 1] == 'x') &&
                    text[i + 2] == '2' &&
                    text[i + 3] == '\\')
                {
                    i += 4;

                    int end = text.IndexOf(@"\X0\", i, StringComparison.OrdinalIgnoreCase);

                    if (end < 0)
                    {
                        output.Append(text, i - 4, text.Length - (i - 4));
                        break;
                    }

                    string hex = text.Substring(i, end - i);

                    try
                    {
                        for (int j = 0; j + 3 < hex.Length; j += 4)
                        {
                            ushort value = ushort.Parse(
                                hex.Substring(j, 4),
                                NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture);

                            output.Append((char)value);
                        }
                    }
                    catch
                    {
                        output.Append(@"\X2\");
                        output.Append(hex);
                        output.Append(@"\X0\");
                    }

                    i = end + 4;
                    continue;
                }

                // \X4\
                if (i + 3 < text.Length &&
                    text[i] == '\\' &&
                    (text[i + 1] == 'X' || text[i + 1] == 'x') &&
                    text[i + 2] == '4' &&
                    text[i + 3] == '\\')
                {
                    i += 4;

                    int end = text.IndexOf(@"\X0\", i, StringComparison.OrdinalIgnoreCase);

                    if (end < 0)
                    {
                        output.Append(text, i - 4, text.Length - (i - 4));
                        break;
                    }

                    string hex = text.Substring(i, end - i);

                    try
                    {
                        for (int j = 0; j + 7 < hex.Length; j += 8)
                        {
                            int codePoint = int.Parse(
                                hex.Substring(j, 8),
                                NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture);

                            output.Append(char.ConvertFromUtf32(codePoint));
                        }
                    }
                    catch
                    {
                        output.Append(@"\X4\");
                        output.Append(hex);
                        output.Append(@"\X0\");
                    }

                    i = end + 4;
                    continue;
                }

                output.Append(text[i]);
                i++;
            }

            return output.ToString();
        }
    }
}

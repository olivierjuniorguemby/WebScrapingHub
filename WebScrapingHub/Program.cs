using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WebScrapingHub.Services;

namespace WebScrapingHub
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .AddEnvironmentVariables()
                .Build();

            var eexOptions = config.GetSection("Eex").Get<EexOptions>()
                            ?? throw new InvalidOperationException("Section Eex introuvable dans appsettings.json");

            var jsonOutPath = config["Output:JsonPath"] ?? throw new InvalidOperationException("Output:JsonPath manquant");
            var csvOutPath = config["Output:CsvPath"];

            Console.WriteLine("=================================");
            Console.WriteLine("EEX SCRAPER CONSOLE");
            Console.WriteLine("=================================");

            var scraper = new EexScraper();
            IReadOnlyList<EexPriceRow> rows;

            try
            {
                rows = scraper.FetchPrices(eexOptions);
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ ERREUR SCRAPING");
                Console.WriteLine(ex);
                return;
            }

            Console.WriteLine($"✅ {rows.Count} lignes récupérées");

            try
            {
                var jsonDirectory = Path.GetDirectoryName(jsonOutPath);
                if (!string.IsNullOrWhiteSpace(jsonDirectory))
                    Directory.CreateDirectory(jsonDirectory);

                var export = rows
                    .OrderBy(r => r.Market)
                    .ThenBy(r => r.Area)
                    .ThenBy(r => r.Delivery)
                    .ThenBy(r => r.Product ?? "")
                    .ThenBy(r => r.Date)
                    .Select(r => new
                    {
                        market = r.Market,
                        date = r.Date.ToString("yyyy-MM-dd"),
                        area = r.Area,
                        product = r.Product,
                        delivery = r.Delivery,
                        price = r.Price
                    })
                    .ToList();

                var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(jsonOutPath, json, Encoding.UTF8);
                Console.WriteLine($"✅ JSON généré : {jsonOutPath}");

                await UploadJsonToGitHubIfConfigured(json, config);
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ ERREUR EXPORT JSON");
                Console.WriteLine(ex);
            }

            if (!string.IsNullOrWhiteSpace(csvOutPath))
            {
                try
                {
                    var csvDirectory = Path.GetDirectoryName(csvOutPath);
                    if (!string.IsNullOrWhiteSpace(csvDirectory))
                        Directory.CreateDirectory(csvDirectory);

                    using var sw = new StreamWriter(csvOutPath, false, Encoding.UTF8);
                    sw.WriteLine("market;date;area;product;delivery;price");

                    foreach (var r in rows)
                    {
                        sw.WriteLine(
                            $"{r.Market};{r.Date:yyyy-MM-dd};{r.Area};{r.Product ?? ""};{r.Delivery};{r.Price.ToString(CultureInfo.InvariantCulture)}");
                    }

                    Console.WriteLine($"✅ CSV généré : {csvOutPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("⚠️ CSV non généré");
                    Console.WriteLine(ex.Message);
                }
            }

            Console.WriteLine("=================================");
            Console.WriteLine("FIN OK");
            Console.WriteLine("=================================");
        }

        private static async Task UploadJsonToGitHubIfConfigured(string jsonContent, IConfiguration config)
        {
            var owner = config["GitHub:Owner"];
            var repo = config["GitHub:Repo"];
            var branch = config["GitHub:Branch"];
            var path = config["GitHub:FilePath"];
            var token = config["GitHub:Token"];

            if (string.IsNullOrWhiteSpace(owner) ||
                string.IsNullOrWhiteSpace(repo) ||
                string.IsNullOrWhiteSpace(branch) ||
                string.IsNullOrWhiteSpace(path) ||
                string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("ℹ️ Upload GitHub ignoré : configuration incomplète ou token absent.");
                return;
            }

            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EexScraper");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            string? sha = null;

            var getResponse = await client.GetAsync(apiUrl);
            if (getResponse.IsSuccessStatusCode)
            {
                var existingJson = await getResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(existingJson);
                sha = doc.RootElement.GetProperty("sha").GetString();
            }

            var payload = new
            {
                message = $"Update EEX prices {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
                content = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonContent)),
                branch,
                sha
            };

            var body = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var putResponse = await client.PutAsync(apiUrl, body);
            putResponse.EnsureSuccessStatusCode();

            Console.WriteLine("✅ JSON envoyé sur GitHub avec succès");
        }
    }
}

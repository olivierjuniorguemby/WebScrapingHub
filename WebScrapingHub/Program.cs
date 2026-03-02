using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WebScrapingHub.Services;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace WebScrapingHub
{
    internal class Program
    {
        static async Task Main(string[] args)
        {

            // ===============================
            // CONFIG
            // ===============================
            var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

            var eexUrl = config["Eex:Url"]!;
            var areas = config.GetSection("Eex:Areas").Get<string[]>() ?? Array.Empty<string>();
            var products = config.GetSection("Eex:Products").Get<string[]>() ?? Array.Empty<string>();
            var deliveries = config.GetSection("Eex:Deliveries").Get<string[]>() ?? Array.Empty<string>();

            var jsonOutPath = config["Output:JsonPath"]!;
            var csvOutPath = config["Output:CsvPath"];

            Console.WriteLine("=================================");
            Console.WriteLine("EEX SCRAPER CONSOLE");
            Console.WriteLine("=================================");

            var scraper = new EexScraper();

            IReadOnlyList<EexPriceRow> rows;

            try
            {
                rows = scraper.FetchPrices(eexUrl, areas, products, deliveries);
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ ERREUR SCRAPING");
                Console.WriteLine(ex);
                return;
            }

            Console.WriteLine($"✅ {rows.Count} lignes récupérées");

            // ===============================
            // JSON EXPORT
            // ===============================
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(jsonOutPath)!);

                var export = rows
                    .OrderBy(r => r.Delivery)
                    .ThenBy(r => r.Product)
                    .ThenBy(r => r.Date)
                    .Select(r => new
                    {
                        date = r.Date.ToString("yyyy-MM-dd"),
                        area = r.Area,
                        product = r.Product,
                        delivery = r.Delivery,
                        price = r.Price
                    });

                var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                File.WriteAllText(jsonOutPath, json);

                Console.WriteLine($"✅ JSON généré : {jsonOutPath}");
                Console.WriteLine("Token length: " + (config["GitHub:Token"]?.Length ?? 0));
                // 🔥 Upload automatique GitHub
                await UploadJsonToGitHub(json, config);

            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ ERREUR EXPORT JSON");
                Console.WriteLine(ex);
            }

            // ===============================
            // CSV EXPORT (optionnel)
            // ===============================
            if (!string.IsNullOrWhiteSpace(csvOutPath))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(csvOutPath)!);

                    using var sw = new StreamWriter(csvOutPath);
                    sw.WriteLine("date;area;product;delivery;price");

                    foreach (var r in rows)
                    {
                        sw.WriteLine(
                            $"{r.Date:yyyy-MM-dd};{r.Area};{r.Product};{r.Delivery};{r.Price.ToString(CultureInfo.InvariantCulture)}");
                    }

                    Console.WriteLine($"✅ CSV généré : {csvOutPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("⚠️ CSV non généré");
                    Console.WriteLine(ex.Message);
                }
            }

            Console.WriteLine("Traitement terminé avec succès.");
            await Task.Delay(1500);
            //Environment.Exit(0);

            Console.WriteLine("=================================");
            Console.WriteLine("FIN OK");
            Console.WriteLine("=================================");
        }

        // ==================================================
        // UPLOAD JSON TO GITHUB
        // ==================================================
        static async Task UploadJsonToGitHub(string jsonContent, IConfiguration config)
        {
            var owner = config["GitHub:Owner"]!;
            var repo = config["GitHub:Repo"]!;
            var branch = config["GitHub:Branch"]!;
            var path = config["GitHub:FilePath"]!;
            var token = config["GitHub:Token"]!;

            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EexScraper");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            string? sha = null;

            // Vérifier si fichier existe
            var getResponse = await client.GetAsync(apiUrl);
            if (getResponse.IsSuccessStatusCode)
            {
                var json = await getResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                sha = doc.RootElement.GetProperty("sha").GetString();
            }

            var payload = new
            {
                message = $"Update EEX prices {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
                content = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonContent)),
                branch = branch,
                sha = sha
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

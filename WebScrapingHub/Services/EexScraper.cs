using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;
using System.Globalization;

namespace WebScrapingHub.Services
{
    public sealed class EexScraper
    {
        public IReadOnlyList<EexPriceRow> FetchPrices(EexOptions config)
        {
            if (string.IsNullOrWhiteSpace(config.Url))
                throw new InvalidOperationException("Eex:Url est obligatoire.");

            var queries = BuildQueries(config);

            if (queries.Count == 0)
                throw new InvalidOperationException("Aucune requête EEX à exécuter. Vérifiez appsettings.json.");

            var chromeOptions = new ChromeOptions();

            if (config.Headless)
                chromeOptions.AddArgument("--headless=new");

            chromeOptions.AddArgument("--no-sandbox");
            chromeOptions.AddArgument("--disable-dev-shm-usage");
            chromeOptions.AddArgument("--window-size=1920,1080");
            chromeOptions.AddArgument("--lang=en-US");
            chromeOptions.AddArgument("--disable-blink-features=AutomationControlled");

            using var driver = CreateDriver(config, chromeOptions);
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));

            Console.WriteLine("🌐 Ouverture du site EEX...");
            driver.Navigate().GoToUrl(config.Url);

            Thread.Sleep(8000);
            AcceptCookies(driver);

            var results = new List<EexPriceRow>();

            foreach (var query in queries)
            {
                Console.WriteLine($"🔽 {query.Market.ToUpperInvariant()} / {query.Area} / {(query.Product ?? "-")} / {query.Delivery}");

                EnsurePageReady(driver, wait);

                SelectIfProvided(wait, "tableGraph_areaSelect", query.Area);

                if (!string.IsNullOrWhiteSpace(query.Product))
                {
                    SelectIfProvided(wait, "tableGraph_productSelect", query.Product);
                }

                SelectIfProvided(wait, "tableGraph_deliverySelect", query.Delivery);

                ClickElement(wait, By.Id("tableGraph_submitBtn"));
                Thread.Sleep(5000);

                ClickElement(wait, By.Id("tableGraph_showTableRadioBtn"));
                Thread.Sleep(3000);

                wait.Until(ExpectedConditions.PresenceOfAllElementsLocatedBy(
                    By.XPath("//table[@id='tableGraph_dataTable']/tbody/tr")));

                Thread.Sleep(1500);

                var tableRows = driver.FindElements(By.XPath("//table[@id='tableGraph_dataTable']/tbody/tr"));

                foreach (var row in tableRows)
                {
                    var cols = row.FindElements(By.TagName("td"));
                    if (cols.Count != 4)
                        continue;

                    if (!TryParseDate(cols[0].Text.Trim(), out var dt))
                        continue;

                    var priceText = cols[3].Text.Trim().Replace(" ", "").Replace(",", ".");
                    if (!decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                        continue;

                    results.Add(new EexPriceRow(
                        DateOnly.FromDateTime(dt),
                        query.Market,
                        query.Area,
                        query.Product,
                        query.Delivery,
                        price));
                }

                driver.Navigate().Refresh();
                Thread.Sleep(5000);
                AcceptCookies(driver);
            }

            driver.Quit();

            return results
                .GroupBy(x => new { x.Date, x.Market, x.Area, x.Product, x.Delivery })
                .Select(g => g.OrderByDescending(x => x.Date).First())
                .OrderBy(x => x.Market)
                .ThenBy(x => x.Area)
                .ThenBy(x => x.Delivery)
                .ThenBy(x => x.Product)
                .ThenBy(x => x.Date)
                .ToList();
        }

        private static ChromeDriver CreateDriver(EexOptions config, ChromeOptions chromeOptions)
        {
            if (!string.IsNullOrWhiteSpace(config.ChromeDriverPath) && Directory.Exists(config.ChromeDriverPath))
            {
                var service = ChromeDriverService.CreateDefaultService(config.ChromeDriverPath);
                service.HideCommandPromptWindow = true;
                return new ChromeDriver(service, chromeOptions, TimeSpan.FromSeconds(120));
            }

            return new ChromeDriver(chromeOptions);
        }

        private static List<EexScrapeQuery> BuildQueries(EexOptions config)
        {
            var queries = new List<EexScrapeQuery>();

            if (config.Power?.Enabled == true)
            {
                foreach (var area in config.Power.Areas ?? Array.Empty<string>())
                {
                    foreach (var product in config.Power.Products ?? Array.Empty<string>())
                    {
                        foreach (var delivery in config.Power.Deliveries ?? Array.Empty<string>())
                        {
                            queries.Add(new EexScrapeQuery(
                                Market: "power",
                                Area: area,
                                Product: product,
                                Delivery: delivery));
                        }
                    }
                }
            }

            if (config.Gas?.Enabled == true)
            {
                var gasProducts = config.Gas.Products ?? Array.Empty<string>();

                foreach (var area in config.Gas.Areas ?? Array.Empty<string>())
                {
                    foreach (var delivery in config.Gas.Deliveries ?? Array.Empty<string>())
                    {
                        if (gasProducts.Length == 0)
                        {
                            queries.Add(new EexScrapeQuery(
                                Market: "gas",
                                Area: area,
                                Product: null,
                                Delivery: delivery));
                        }
                        else
                        {
                            foreach (var product in gasProducts)
                            {
                                queries.Add(new EexScrapeQuery(
                                    Market: "gas",
                                    Area: area,
                                    Product: product,
                                    Delivery: delivery));
                            }
                        }
                    }
                }
            }

            return queries;
        }

        private static void EnsurePageReady(IWebDriver driver, WebDriverWait wait)
        {
            wait.Until(d =>
                ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString() == "complete");

            AcceptCookies(driver);

            wait.Until(ExpectedConditions.ElementExists(By.Id("tableGraph_areaSelect")));
            wait.Until(ExpectedConditions.ElementExists(By.Id("tableGraph_deliverySelect")));
        }

        private static void SelectIfProvided(WebDriverWait wait, string selectId, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var element = wait.Until(ExpectedConditions.ElementToBeClickable(By.Id(selectId)));
            var select = new SelectElement(element);

            try
            {
                select.SelectByValue(value);
                Thread.Sleep(1000);
                return;
            }
            catch
            {
                // ignore and try text
            }

            try
            {
                select.SelectByText(value);
                Thread.Sleep(1000);
                return;
            }
            catch
            {
                // ignore and try partial
            }

            var option = select.Options.FirstOrDefault(o =>
                string.Equals(o.Text?.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(o.GetAttribute("value")?.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase) ||
                (o.Text?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false));

            if (option == null)
                throw new InvalidOperationException($"Impossible de sélectionner '{value}' dans '{selectId}'.");

            select.SelectByText(option.Text);
            Thread.Sleep(1000);
        }

        private static void ClickElement(WebDriverWait wait, By by)
        {
            var element = wait.Until(ExpectedConditions.ElementToBeClickable(by));
            ((IJavaScriptExecutor)wait.Until(d => d)).ExecuteScript("arguments[0].click();", element);
        }

        private static bool TryParseDate(string input, out DateTime dt)
        {
            return DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)
                || DateTime.TryParse(input, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out dt)
                || DateTime.TryParse(input, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out dt);
        }

        private static void AcceptCookies(IWebDriver driver)
        {
            try
            {
                var cookieBtn = driver.FindElements(By.XPath("//input[@value='I accept all cookies.']"));
                if (cookieBtn.Count > 0)
                {
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", cookieBtn[0]);
                    Thread.Sleep(1500);
                }

                var modalBtn = driver.FindElements(By.CssSelector("#popup button.close"));
                if (modalBtn.Count > 0)
                {
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", modalBtn[0]);
                    Thread.Sleep(1500);
                }

                ((IJavaScriptExecutor)driver).ExecuteScript(@"
                    document.body.classList.remove('modal-open');
                    document.querySelectorAll('.modal-backdrop').forEach(e => e.remove());
                ");
            }
            catch
            {
                // ignore
            }
        }
    }

    public sealed class EexOptions
    {
        public string Url { get; set; } = "";
        public bool Headless { get; set; } = true;
        public string? ChromeDriverPath { get; set; }

        public EexMarketOptions Power { get; set; } = new();
        public EexMarketOptions Gas { get; set; } = new();
    }

    public sealed class EexMarketOptions
    {
        public bool Enabled { get; set; } = true;
        public string[] Areas { get; set; } = Array.Empty<string>();
        public string[] Products { get; set; } = Array.Empty<string>();
        public string[] Deliveries { get; set; } = Array.Empty<string>();
    }

    public sealed record EexScrapeQuery(
        string Market,
        string Area,
        string? Product,
        string Delivery
    );
}

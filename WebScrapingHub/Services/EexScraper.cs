using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;
using System.Globalization;

namespace WebScrapingHub.Services
{
    public sealed class EexScraper
    {
        public IReadOnlyList<EexPriceRow> FetchPrices(
            string url,
            string[] areas,
            string[] products,
            string[] deliveries)
        {
            var service = ChromeDriverService.CreateDefaultService(@"C:\Selenuim\chromedriver-win64\");
            service.HideCommandPromptWindow = true;

            var options = new ChromeOptions();
            options.AddArgument("--start-maximized");

            using var driver = new ChromeDriver(service, options);
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));

            Console.WriteLine("🌐 Ouverture du site...");
            driver.Navigate().GoToUrl(url);

            Thread.Sleep(15000); // comme Python

            AcceptCookies(driver);

            var results = new List<EexPriceRow>();

            foreach (var product in products)
            {
                foreach (var delivery in deliveries)
                {
                    Console.WriteLine($"🔽 FR / {product} / {delivery}");

                    var areaSelect = new SelectElement(
                        wait.Until(ExpectedConditions.ElementToBeClickable(
                            By.Id("tableGraph_areaSelect"))));
                    areaSelect.SelectByValue("FR");
                    Thread.Sleep(1000);

                    new SelectElement(driver.FindElement(By.Id("tableGraph_productSelect")))
                        .SelectByValue(product);
                    Thread.Sleep(1000);

                    new SelectElement(driver.FindElement(By.Id("tableGraph_deliverySelect")))
                        .SelectByValue(delivery);
                    Thread.Sleep(1000);

                    var goButton = wait.Until(
                        ExpectedConditions.ElementToBeClickable(
                            By.Id("tableGraph_submitBtn")));

                    ((IJavaScriptExecutor)driver)
                        .ExecuteScript("arguments[0].click();", goButton);

                    Thread.Sleep(5000);

                    var tableButton = wait.Until(
                        ExpectedConditions.ElementToBeClickable(
                            By.Id("tableGraph_showTableRadioBtn")));

                    ((IJavaScriptExecutor)driver)
                        .ExecuteScript("arguments[0].click();", tableButton);

                    Thread.Sleep(3000);

                    wait.Until(ExpectedConditions.PresenceOfAllElementsLocatedBy(
                        By.XPath("//table[@id='tableGraph_dataTable']/tbody/tr")));

                    Thread.Sleep(2000);

                    var rows = driver.FindElements(
                        By.XPath("//table[@id='tableGraph_dataTable']/tbody/tr"));

                    foreach (var row in rows)
                    {
                        var cols = row.FindElements(By.TagName("td"));

                        if (cols.Count != 4)
                            continue;

                        if (!DateTime.TryParse(
                            cols[0].Text.Trim(),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out var dt))
                            continue;

                        var priceStr = cols[3].Text.Replace(" ", "").Replace(",", ".");

                        if (!decimal.TryParse(
                            priceStr,
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out var price))
                            continue;

                        results.Add(new EexPriceRow(
                            DateOnly.FromDateTime(dt),
                            "FR",
                            product,
                            delivery,
                            price));
                    }

                    driver.Navigate().Refresh();
                    Thread.Sleep(5000);
                }
            }

            driver.Quit();
            return results;
        }

        private static void AcceptCookies(IWebDriver driver)
        {
            try
            {
                var cookieBtn = driver.FindElements(
                    By.XPath("//input[@value='I accept all cookies.']"));

                if (cookieBtn.Count > 0)
                {
                    ((IJavaScriptExecutor)driver)
                        .ExecuteScript("arguments[0].click();", cookieBtn[0]);

                    Thread.Sleep(2000);
                }

                var modalBtn = driver.FindElements(
                    By.CssSelector("#popup button.close"));

                if (modalBtn.Count > 0)
                {
                    ((IJavaScriptExecutor)driver)
                        .ExecuteScript("arguments[0].click();", modalBtn[0]);

                    Thread.Sleep(2000);
                }

                ((IJavaScriptExecutor)driver).ExecuteScript(@"
                    document.body.classList.remove('modal-open');
                    document.querySelectorAll('.modal-backdrop').forEach(e => e.remove());
                ");
            }
            catch { }
        }
    }
}
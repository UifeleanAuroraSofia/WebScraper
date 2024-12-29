using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.IO.Compression;
using System.Net.Http.Headers;
using WebScraper.Logging;

namespace WebScraper.Scrapers
{
    public class AltexScraper
    {
        private readonly string _baseUrl = "https://altex.ro";
        private readonly decimal _procentMinimReducere;
        private readonly ChromeDriver _driver;
        private Dictionary<string, Produs> _produseAnteriorare;
        private NotifyIcon? _notifyIcon;
        private int _totalProduseGasite = 0;
        private int _totalNotificariTrimise = 0;

        public AltexScraper(decimal procentMinimReducere)
        {
            try
            {
                Logger.Info("Inițializare AltexScraper cu Selenium...");
                _procentMinimReducere = procentMinimReducere;
                _produseAnteriorare = new Dictionary<string, Produs>();

                var options = new ChromeOptions();
                options.AddArgument("--headless");
                options.AddArgument("--disable-gpu");
                options.AddArgument("--no-sandbox");
                _driver = new ChromeDriver(options);

                InitializeazaNotificari();
                Logger.Success("AltexScraper inițializat cu succes!");
            }
            catch (Exception ex)
            {
                Logger.Error("Eroare la inițializarea AltexScraper", ex);
                throw;
            }
        }

        private void InitializeazaNotificari()
        {
            try
            {
                _notifyIcon = new NotifyIcon();
                _notifyIcon.Icon = SystemIcons.Information;
                _notifyIcon.Visible = true;
                _notifyIcon.Text = "Altex Price Monitor";
            }
            catch (Exception ex)
            {
                Logger.Error("Eroare la inițializarea sistemului de notificări", ex);
            }
        }

        public async Task MonitorizeazaReduceri(string categorie, int intervalMinute)
        {
            Logger.Info($"Începe monitorizarea pentru categoria: {categorie}", ConsoleColor.Cyan);
            Logger.Info($"Interval de verificare: {intervalMinute} minute", ConsoleColor.Cyan);
            Logger.Info($"Procent minim reducere: {_procentMinimReducere}%", ConsoleColor.Cyan);

            while (true)
            {
                try
                {
                    Logger.Info($"Începe scanarea la {DateTime.Now}", ConsoleColor.Yellow);
                    var produseNoi = await ExtrageProduseReduse(categorie);
                    Logger.Info($"S-au găsit {produseNoi.Count} produse în total");

                    VerificaReduceriNoi(produseNoi);

                    Logger.Success($"Scanare completă: {_totalProduseGasite} produse procesate în total");
                    Logger.Success($"Total notificări trimise: {_totalNotificariTrimise}");

                    Logger.Info($"Următoarea verificare va fi la: {DateTime.Now.AddMinutes(intervalMinute)}", ConsoleColor.Yellow);
                    await Task.Delay(TimeSpan.FromMinutes(intervalMinute));
                }
                catch (Exception ex)
                {
                    Logger.Error("Eroare în timpul monitorizării", ex);
                    await Task.Delay(TimeSpan.FromMinutes(1));
                }
            }
        }

        private async Task<List<Produs>> ExtrageProduseReduse(string categorie)
        {
            var produse = new List<Produs>();
            int pagina = 1;
            bool endOfPages = false;

            do
            {
                try
                {
                    string url = pagina == 1
                        ? $"{_baseUrl}/{categorie}/cpl/"
                        : $"{_baseUrl}/{categorie}/cpl/filtru/p/{pagina}/";

                    Logger.Info($"Navighez către: {url}");
                    _driver.Navigate().GoToUrl(url);
                    var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(20));
                    wait.Until(driver => driver.FindElement(By.CssSelector("li.Products-item")));
                    var produseCarduri = _driver.FindElements(By.CssSelector("li.Products-item"));

                    if (produseCarduri == null || !produseCarduri.Any())
                    {
                        Logger.Info($"Nu s-au găsit produse pe pagina {pagina}");
                        endOfPages = true;
                        continue;
                    }

                    foreach (var card in produseCarduri)
                    {
                        try
                        {
                            var produs = ExtrageInformatiiProdus(card);
                            if (produs != null && produs.ProcentReducere > 0)
                            {
                                produse.Add(produs);
                                _totalProduseGasite++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Eroare la procesarea unui produs", ex);
                        }
                    }

                    pagina++;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Eroare la procesarea paginii {pagina}", ex);
                    endOfPages = true;
                }
            } while (!endOfPages);

            return produse;
        }

        private Produs? ExtrageInformatiiProdus(IWebElement cardProdus)
        {
            try
            {
                var nume = cardProdus.FindElement(By.CssSelector("a.Product-name > span")).Text.Trim();
                var linkProdus = cardProdus.FindElement(By.CssSelector("a.Product-name")).GetAttribute("href");

                var pretRedusText = cardProdus.FindElement(By.CssSelector("span.Price-int")).Text.Trim();
                var pretOriginalText = cardProdus.FindElement(By.CssSelector("div.has-line-through span")).Text.Trim();

                pretRedusText = CurataTextPret(pretRedusText);
                pretOriginalText = CurataTextPret(pretOriginalText);

                if (decimal.TryParse(pretRedusText, out decimal pretRedus) &&
                    decimal.TryParse(pretOriginalText, out decimal pretOriginal))
                {
                    var procentReducere = (pretOriginal - pretRedus) / pretOriginal * 100;

                    return new Produs
                    {
                        Nume = nume,
                        PretOriginal = pretOriginal,
                        PretRedus = pretRedus,
                        Link = linkProdus.StartsWith("http") ? linkProdus : $"{_baseUrl}{linkProdus}",
                        ProcentReducere = Math.Round(procentReducere, 2)
                    };
                }
            }
            catch (NoSuchElementException ex)
            {
                Logger.Error("Element lipsă în structura HTML a produsului", ex);
            }
            catch (Exception ex)
            {
                Logger.Error("Eroare la extragerea informațiilor despre produs", ex);
            }

            return null;
        }


        private void VerificaReduceriNoi(List<Produs> produseNoi)
        {
            var produseActualizate = new Dictionary<string, Produs>();
            var notificariInRunda = 0;

            foreach (var produs in produseNoi)
            {
                if (produs.Link == null) continue;

                string id = produs.Link;
                produseActualizate[id] = produs;

                if (!_produseAnteriorare.ContainsKey(id))
                {
                    if (produs.ProcentReducere >= _procentMinimReducere)
                    {
                        AfiseazaNotificare(produs, "Reducere nouă!");
                        notificariInRunda++;
                    }
                }
                else
                {
                    var produsAnterior = _produseAnteriorare[id];
                    if (produs.PretRedus < produsAnterior.PretRedus)
                    {
                        AfiseazaNotificare(produs, "Preț redus și mai mult!");
                        notificariInRunda++;
                    }
                }
            }

            Logger.Info($"S-au trimis {notificariInRunda} notificări în această rundă");
            _totalNotificariTrimise += notificariInRunda;
            _produseAnteriorare = produseActualizate;
        }

        private void AfiseazaNotificare(Produs produs, string tipNotificare)
        {
            try
            {
                _notifyIcon?.ShowBalloonTip(
                    5000,
                    tipNotificare,
                    $"{produs.Nume}\nPreț: {produs.PretRedus} Lei (-{produs.ProcentReducere}%)\nClick pentru a deschide link-ul",
                    ToolTipIcon.Info
                );
            }
            catch (Exception ex)
            {
                Logger.Error("Eroare la afișarea notificării", ex);
            }
        }

        private string CurataTextPret(string text)
        {
            return text
                .Replace("Lei", "")
                .Replace("lei", "")
                .Replace("RON", "")
                .Replace("de la", "")
                .Replace("&nbsp;", "")
                .Replace(" ", "")
                .Replace(".", "")
                .Replace(",", ".")
                .Replace("&#46;", "")
                .Replace("&#44;", ".")
                .Trim();
        }
    }
}
using HtmlAgilityPack;
using WebScraper.Logging;

namespace WebScraper.Scrapers
{
    public class EmagScraper
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "https://www.emag.ro";
        private Dictionary<string, Produs> _produseAnteriorare;
        private readonly decimal _procentMinimReducere;
        private NotifyIcon? _notifyIcon;
        private int _totalProduseGasite = 0;
        private int _totalNotificariTrimise = 0;

        public EmagScraper(decimal procentMinimReducere)
        {
            try
            {
                Logger.Info("Inițializare EmagScraper...");
                _httpClient = new HttpClient();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                _produseAnteriorare = new Dictionary<string, Produs>();
                _procentMinimReducere = procentMinimReducere;
                InitializeazaNotificari();
                Logger.Success("EmagScraper inițializat cu succes!");
            }
            catch (Exception ex)
            {
                Logger.Error("Eroare la inițializarea EmagScraper", ex);
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
                _notifyIcon.Text = "eMag Price Monitor";
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
                pagina++;
                string url = $"{_baseUrl}/{categorie}/p{pagina}/c";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Error($"Eroare HTTP: {response.StatusCode} pentru {url}");
                    continue;
                }
                var baseUrl = response.RequestMessage.RequestUri;
                if (!baseUrl.Segments.Contains($"p{pagina}/"))
                {
                    endOfPages = true;
                }

                var html = await response.Content.ReadAsStringAsync();
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);

                var carduriProduse = doc.DocumentNode.SelectNodes("//div[contains(@class, 'card-item')]");

                foreach (var card in carduriProduse)
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
            }
            while(!endOfPages);

            return produse;
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

        private Produs? ExtrageInformatiiProdus(HtmlNode cardProdus)
        {
            try
            {
                var nume = cardProdus.SelectSingleNode(".//a[contains(@class, 'card-v2-title')]")?.InnerText?.Trim() ??
                          cardProdus.SelectSingleNode(".//div[contains(@class, 'pad-hrz-xs')]//a")?.InnerText?.Trim();

                var linkProdus = cardProdus.SelectSingleNode(".//a[contains(@class, 'card-v2-title')]")?.GetAttributeValue("href", "") ??
                                cardProdus.SelectSingleNode(".//div[contains(@class, 'pad-hrz-xs')]//a")?.GetAttributeValue("href", "");

                var pretRedusText = cardProdus.SelectSingleNode(".//p[contains(@class, 'product-new-price')]")?.InnerText?.Trim() ??
                                   cardProdus.SelectSingleNode(".//div[contains(@class, 'product-new-price')]//p")?.InnerText?.Trim();

                var pretOriginalText = cardProdus.SelectSingleNode(".//s[contains(@class, 'rrp-lp30d-content')]")?.InnerText?.Trim() ??
                                      cardProdus.SelectSingleNode(".//span[contains(@class, 'rrp-lp30d-content')]//s")?.InnerText?.Trim();

                if (!string.IsNullOrEmpty(pretRedusText) && !string.IsNullOrEmpty(pretOriginalText))
                {
                    pretRedusText = CurataTextPret(pretRedusText);
                    pretOriginalText = CurataTextPret(pretOriginalText);

                    if (decimal.TryParse(pretRedusText, out decimal pretRedus) &&
                        decimal.TryParse(pretOriginalText, out decimal pretOriginal))
                    {
                        var procentReducere = (pretOriginal - pretRedus) / pretOriginal * 100;

                        var produs = new Produs
                        {
                            Nume = nume,
                            PretOriginal = pretOriginal,
                            PretRedus = pretRedus,
                            Link = linkProdus.StartsWith("http") ? linkProdus : $"https://www.emag.ro{linkProdus}",
                            ProcentReducere = Math.Round(procentReducere, 2)
                        };

                        return produs;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Eroare la extragerea informațiilor despre produs", ex);
            }

            return null;
        }

        #region HELPERS
        private string CurataTextPret(string text)
        {
            return text
                .Replace("Lei", "")
                .Replace("lei", "")
                .Replace("de la", "")
                .Replace("&nbsp;", "")
                .Replace(" ", "")
                .Replace(".", "")
                .Replace(",", ".")
                .Replace("&#46;", "")
                .Replace("&#44;", ".")
                .Trim();
        }
        #endregion
    }
}
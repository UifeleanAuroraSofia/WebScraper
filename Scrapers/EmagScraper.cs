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

        public EmagScraper(decimal procentMinimReducere = 20)
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
                Logger.Info("Start inițializare notificări...");

                _notifyIcon = new NotifyIcon();
                Logger.Info("NotifyIcon creat");

                _notifyIcon.Icon = SystemIcons.Information;
                _notifyIcon.Visible = true;
                _notifyIcon.Text = "eMag Price Monitor";
                Logger.Info("Proprietăți NotifyIcon setate");
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

        private async Task<List<Produs>> ExtrageProduseReduse(string categorie, int numaPagini = 2)
        {
            var produse = new List<Produs>();

            for (int pagina = 2; pagina <= numaPagini; pagina++)
            {
                try
                {
                    string url = $"{_baseUrl}/{categorie}/p{pagina}/c";
                    Logger.Info($"Se scanează pagina {pagina}: {url}");

                    var response = await _httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.Error($"Eroare HTTP: {response.StatusCode} pentru {url}");
                        continue;
                    }

                    var html = await response.Content.ReadAsStringAsync();
                    var doc = new HtmlAgilityPack.HtmlDocument();
                    doc.LoadHtml(html);

                    var carduriProduse = doc.DocumentNode.SelectNodes("//div[contains(@class, 'card-item')]");
                    if (carduriProduse == null)
                    {
                        Logger.Info($"Nu s-au găsit produse pe pagina {pagina}");
                        continue;
                    }

                    Logger.Info($"S-au găsit {carduriProduse.Count} produse pe pagina {pagina}");
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
                catch (Exception ex)
                {
                    Logger.Error($"Eroare la procesarea paginii {pagina}", ex);
                }

                await Task.Delay(1000); // Pauză între request-uri
            }

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

                // Afișăm în consolă
                Logger.Success($"\n{tipNotificare}");
                Logger.Info($"Produs: {produs.Nume}");
                Logger.Info($"Preț nou: {produs.PretRedus} Lei (redus de la {produs.PretOriginal} Lei)");
                Logger.Info($"Reducere: {produs.ProcentReducere}%");
                Logger.Info($"Link: {produs.Link}\n");

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
                Logger.Info("Începe extragerea informațiilor pentru un nou produs");

                // Selectoare actualizate pentru structura curentă eMag
                var nume = cardProdus.SelectSingleNode(".//a[contains(@class, 'card-v2-title')]")?.InnerText?.Trim() ??
                          cardProdus.SelectSingleNode(".//div[contains(@class, 'pad-hrz-xs')]//a")?.InnerText?.Trim();

                var linkProdus = cardProdus.SelectSingleNode(".//a[contains(@class, 'card-v2-title')]")?.GetAttributeValue("href", "") ??
                                cardProdus.SelectSingleNode(".//div[contains(@class, 'pad-hrz-xs')]//a")?.GetAttributeValue("href", "");

                // Extragere prețuri cu selectoare actualizate
                var pretRedusText = cardProdus.SelectSingleNode(".//p[contains(@class, 'product-new-price')]")?.InnerText?.Trim() ??
                                   cardProdus.SelectSingleNode(".//div[contains(@class, 'product-new-price')]//p")?.InnerText?.Trim();

                var pretOriginalText = cardProdus.SelectSingleNode(".//s[contains(@class, 'rrp-lp30d-content')]")?.InnerText?.Trim() ??
                                      cardProdus.SelectSingleNode(".//span[contains(@class, 'rrp-lp30d-content')]//s")?.InnerText?.Trim();

                Logger.Info($"Nume extras: {nume ?? "null"}");

                if (!string.IsNullOrEmpty(pretRedusText) && !string.IsNullOrEmpty(pretOriginalText))
                {
                    // Procesare text prețuri
                    pretRedusText = CurataTextPret(pretRedusText);
                    pretOriginalText = CurataTextPret(pretOriginalText);

                    Logger.Info($"Preț redus procesat: {pretRedusText}");
                    Logger.Info($"Preț original procesat: {pretOriginalText}");

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

                        Logger.Success($"Produs extras cu succes: {produs.Nume} - {produs.PretRedus:N2} Lei");
                        return produs;
                    }
                    else
                    {
                        Logger.Error($"Nu s-au putut converti prețurile: {pretRedusText} / {pretOriginalText}");
                    }
                }
                else
                {
                    Logger.Error("Nu s-au găsit prețurile pentru produs");
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

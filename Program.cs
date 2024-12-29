using WebScraper.Scrapers;
using WebScraper.Logging;

namespace WebScraper
{

    public class Produs
    {
        public string? Nume { get; set; }
        public decimal PretOriginal { get; set; }
        public decimal PretRedus { get; set; }
        public string? Link { get; set; }
        public decimal ProcentReducere { get; set; }
    }

    class Program 
    {
        static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("=== eMag Price Monitor ===\n");

                Console.Write("Introduceți categoria dorită (ex: laptopuri, telefoane-mobile): ");
                var categorie = Console.ReadLine() ?? "laptopuri";

                Console.Write("Introduceți procentul minim de reducere pentru notificări (ex: 20): ");
                if (!decimal.TryParse(Console.ReadLine(), out decimal procentMinim))
                {
                    procentMinim = 20;
                }

                Console.Write("Introduceți intervalul de verificare în minute (ex: 30): ");
                if (!int.TryParse(Console.ReadLine(), out int intervalMinute))
                {
                    intervalMinute = 30;
                }

                var scraper = new AltexScraper(procentMinim);
                await scraper.MonitorizeazaReduceri(categorie, intervalMinute);
            }
            catch (Exception ex)
            {
                Logger.Error("Eroare fatală în aplicație", ex);
                Console.WriteLine("\nApăsați orice tastă pentru a închide aplicația...");
                Console.ReadKey();
            }
        }
    }
}
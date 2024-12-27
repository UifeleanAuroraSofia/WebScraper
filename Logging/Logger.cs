namespace WebScraper.Logging
{
    public static class Logger
    {
        public static void Info(string message, ConsoleColor color = ConsoleColor.White)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] INFO: {message}");
            Console.ForegroundColor = originalColor;
        }

        public static void Error(string message, Exception? ex = null)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR: {message}");
            if (ex != null)
            {
                Console.WriteLine($"Detalii eroare: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
            Console.ForegroundColor = originalColor;
        }

        public static void Success(string message)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] SUCCESS: {message}");
            Console.ForegroundColor = originalColor;
        }
    }
}

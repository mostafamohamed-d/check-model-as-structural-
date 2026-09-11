using System;

namespace ETABSModelDefinitionValidator.Core.Logging
{
    public sealed class ConsoleValidationLogger : IValidationLogger
    {
        public void Debug(string message) => Write("DEBUG", message, ConsoleColor.DarkGray);
        public void Info(string message) => Write("INFO", message, ConsoleColor.Gray);
        public void Warning(string message) => Write("WARN", message, ConsoleColor.Yellow);
        public void Error(string message) => Write("ERROR", message, ConsoleColor.Red);

        private static void Write(string level, string message, ConsoleColor color)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine($"[{level,-5}] {message}");
            Console.ForegroundColor = previous;
        }
    }
}

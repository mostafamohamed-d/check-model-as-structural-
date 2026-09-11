namespace ETABSModelDefinitionValidator.Core.Logging
{
    /// <summary>
    /// Structured logging abstraction so import/detection/parsing/rule-execution diagnostics are
    /// available regardless of host (console runner today, WPF/desktop UI later). Deliberately
    /// small - this is not a general logging framework.
    /// </summary>
    public interface IValidationLogger
    {
        void Debug(string message);
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }
}

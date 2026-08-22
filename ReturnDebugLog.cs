using OWML.Common;

namespace Return
{
    /// <summary>
    /// Build120v4: all mod console debug output is disabled to reduce
    /// resource usage. Every former ModHelper.Console.WriteLine call now
    /// routes through this placeholder, so nothing reaches the OWML
    /// console or the mod log file. Flip Enabled to true and rebuild to
    /// restore console logging for future debugging.
    /// </summary>
    internal static class ReturnDebugLog
    {
        internal static bool Enabled = false;

        internal static void Write(string message)
        {
            if (Enabled)
            {
                WriteCore(message, MessageType.Info);
            }
        }

        internal static void Write(string message, MessageType type)
        {
            if (Enabled)
            {
                WriteCore(message, type);
            }
        }

        private static void WriteCore(string message, MessageType type)
        {
            ReturnMod.Instance?.ModHelper.Console.WriteLine(message, type);
        }
    }
}
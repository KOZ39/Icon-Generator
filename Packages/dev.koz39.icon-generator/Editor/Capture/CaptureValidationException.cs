using System;

namespace KOZ39.IconGenerator
{
    internal sealed class CaptureValidationException : Exception
    {
        internal readonly string Key;
        internal readonly object[] Arguments;
        internal (string Key, string Arguments) Identity => (Key, string.Join("\n", Arguments));

        internal CaptureValidationException(string key, params object[] arguments)
            : base(key)
        {
            Key = key;
            Arguments = arguments;
        }
    }
}

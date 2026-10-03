using System.Windows.Input;

namespace LibmpvIptvClient.Helpers
{
    public static class WpfKeyInput
    {
        /// <summary>
        /// Unwraps the key WPF reports while an IME is composing (or Alt is held). Without this the
        /// shortcut system receives Key.ImeProcessed and stops working whenever a Chinese/Japanese
        /// input method is active -- shortcuts only appeared to work with the English layout.
        /// </summary>
        public static Key NormalizeKey(System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.ImeProcessed) return e.ImeProcessedKey;
            if (e.Key == Key.System) return e.SystemKey;
            return e.Key;
        }
    }
}

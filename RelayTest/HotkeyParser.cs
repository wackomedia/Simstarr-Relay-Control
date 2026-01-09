using System;
using System.Windows.Forms;

namespace RelayTest
{
    /// <summary>
    /// Utility class to parse hotkey strings (e.g., "Shift+D1", "Ctrl+Alt+D2") into modifier and virtual key codes.
    /// </summary>
    public static class HotkeyParser
    {
        /// <summary>
        /// Parses a hotkey string like "Shift+D1" or "Ctrl+Alt+D2" into modifiers and virtual key.
        /// </summary>
        public static bool TryParseHotkey(string hotkeyString, out uint modifiers, out uint virtualKey)
        {
            modifiers = 0;
            virtualKey = 0;

            if (string.IsNullOrWhiteSpace(hotkeyString))
                return false;

            var parts = hotkeyString.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            // Parse modifiers (all parts except the last one)
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var mod = parts[i].Trim().ToLowerInvariant();
                if (mod == "shift")
                    modifiers |= 0x0004; // MOD_SHIFT
                else if (mod == "ctrl" || mod == "control")
                    modifiers |= 0x0002; // MOD_CONTROL
                else if (mod == "alt")
                    modifiers |= 0x0001; // MOD_ALT
                else if (mod == "win" || mod == "windows")
                    modifiers |= 0x0008; // MOD_WIN
                else
                    return false; // Unknown modifier
            }

            // Parse the key (last part)
            var keyPart = parts[parts.Length - 1].Trim();
            if (!Enum.TryParse<Keys>(keyPart, ignoreCase: true, out var key))
                return false;

            virtualKey = (uint)key;
            return true;
        }

        /// <summary>
        /// Converts a Keys enum to a friendly string representation.
        /// </summary>
        public static string KeysToString(Keys key)
        {
            return key switch
            {
                Keys.D0 => "D0",
                Keys.D1 => "D1",
                Keys.D2 => "D2",
                Keys.D3 => "D3",
                Keys.D4 => "D4",
                Keys.D5 => "D5",
                Keys.D6 => "D6",
                Keys.D7 => "D7",
                Keys.D8 => "D8",
                Keys.D9 => "D9",
                _ => key.ToString()
            };
        }
    }
}
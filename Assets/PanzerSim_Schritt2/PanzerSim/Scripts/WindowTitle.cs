using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Spielname im Fenstertitel. Der Produktname in den Projekteinstellungen bleibt absichtlich gleich
    /// ("Ballistik Studios - Panzer"): an ihm haengen die gespeicherten Einstellungen (PlayerPrefs) aller Spieler.
    /// </summary>
    public static class WindowTitle
    {
        public const string GameName = "Ballistik Tank Fronts";

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern System.IntPtr GetActiveWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool SetWindowTextW(System.IntPtr hWnd, string text);
#endif

        public static void Apply()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                var h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (h == System.IntPtr.Zero) h = GetActiveWindow();
                if (h != System.IntPtr.Zero) SetWindowTextW(h, GameName);
            }
            catch (System.Exception e) { Debug.LogWarning("Fenstertitel: " + e.Message); }
#endif
        }
    }
}

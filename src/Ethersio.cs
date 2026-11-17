// Ethersio - point d'entree
// Outil de zone de notification pour BTS SIO SISR : DHCP / IP fixe, diagnostic, outils reseau.
// Compile avec le SDK .NET pour .NET Framework 4.8 : voir Ethersio.csproj et publier.sh.

using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Ethersio
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            // Ethersio.exe --update : telecharge et installe la derniere version signee (tache planifiee, sans interface)
            if (args.Length == 1 && args[0] == "--update")
            {
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { } // TLS 1.2
                return Updater.Run();
            }

            // Ethersio.exe --export <dossier> : genere Ethersio.ico + apercu-icones.png (utilise par build.bat)
            if (args.Length == 2 && args[0] == "--export")
            {
                IconFactory.SaveIco(System.IO.Path.Combine(args[1], "Ethersio.ico"), NetState.Dhcp);
                IconFactory.SavePreview(System.IO.Path.Combine(args[1], "apercu-icones.png"));
                return 0;
            }

            // Ethersio.exe --capture <dossier> [dark|light] : captures PNG des 3 onglets (pour verifier le design)
            if (args.Length >= 2 && args[0] == "--capture")
            {
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                UI.Init();
                if (args.Length >= 3) { UI.ForceTheme(args[2] == "dark"); }
                var app = new TrayApp(null);
                app.CaptureAndExit(args[1]);
                Application.Run(app);
                return 0;
            }

            // une instance par session Windows ; relancer l'exe ouvre la popup de l'instance existante
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Ethersio_Show");
            bool created;
            using (var mutex = new Mutex(true, "Ethersio_SingleInstance", out created))
            {
                if (!created) { try { showEvent.Set(); } catch { } return 0; }
                try { SetProcessDPIAware(); } catch { }
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { } // TLS 1.2
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                UI.Init();
                Application.Run(new TrayApp(showEvent));
            }
            return 0;
        }
    }
}

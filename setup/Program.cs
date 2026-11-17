// Ethersio Setup - point d'entree : Ethersio-Setup.exe (installation) ou Uninstall.exe /uninstall (desinstallation)

using System;
using System.Windows.Forms;

namespace EthersioSetup
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length == 1 && args[0].Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) Uninstall();
            else Application.Run(new SetupForm());
        }

        static void Uninstall()
        {
            if (Installer.RunsFromInstallDir) { Installer.RelaunchFromTemp(); return; }

            var confirm = MessageBox.Show("Désinstaller Ethersio de cet ordinateur ?", "Ethersio",
                                          MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm == DialogResult.Yes)
            {
                try
                {
                    Installer.Uninstall();
                    MessageBox.Show("Ethersio est désinstallé.\n\nVos profils IP et votre journal restent dans %APPDATA%\\Ethersio.",
                                    "Ethersio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("La désinstallation a échoué : " + ex.Message, "Ethersio", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            Installer.DeleteSelfLater();
        }
    }
}

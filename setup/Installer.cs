// Ethersio Setup - installation et desinstallation pour tous les comptes du PC (equivalent de l'ancien installer.bat)

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace EthersioSetup
{
    static class Installer
    {
        const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Ethersio";
        const string LegacyUninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\IpCheckSIO";
        const string HourlyTask = @"Ethersio\Mise a jour";
        const string BootTask = @"Ethersio\Mise a jour au demarrage";

        static readonly string ProgramFiles =
            Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        public static readonly string Dir = Path.Combine(ProgramFiles, "Ethersio");
        static readonly string ExePath = Path.Combine(Dir, "Ethersio.exe");
        static readonly string UninstallerPath = Path.Combine(Dir, "Uninstall.exe");

        public static Version Version
        {
            get { var v = Assembly.GetExecutingAssembly().GetName().Version; return new Version(v.Major, v.Minor, v.Build); }
        }

        public static string LicenseText
        {
            get { return Encoding.UTF8.GetString(Resource("Informations.txt")) + "\n" + Encoding.UTF8.GetString(Resource("LICENSE.txt")); }
        }

        public static bool RunsFromInstallDir
        {
            get { return string.Equals(Path.GetDirectoryName(Application.ExecutablePath), Dir, StringComparison.OrdinalIgnoreCase); }
        }

        static byte[] Resource(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        static string ShortcutPath(string name)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), name + ".lnk");
        }

        static void KillAll(string processName)
        {
            foreach (var p in Process.GetProcessesByName(processName))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch { }
                p.Dispose();
            }
        }

        static void DeleteRunValue(RegistryKey hive, string name)
        {
            using (var k = hive.OpenSubKey(RunKey, true))
                if (k != null) k.DeleteValue(name, false);
        }

        static int Schtasks(string args)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        static void CreateTask(string name, string schedule)
        {
            string run = "\\\"" + ExePath + "\\\" --update";
            int code = Schtasks("/create /tn \"" + name + "\" /tr \"" + run + "\" " + schedule + " /ru SYSTEM /rl HIGHEST /f");
            if (code != 0) throw new InvalidOperationException("Impossible de créer la tâche de mise à jour « " + name + " » (code " + code + ").");
        }

        // l'ancienne version (IP Check SIO) est retiree avant d'installer Ethersio
        static void RemoveLegacy()
        {
            KillAll("IpCheckSIO");
            DeleteRunValue(Registry.LocalMachine, "IpCheckSIO");
            DeleteRunValue(Registry.CurrentUser, "IpCheckSIO");
            Registry.LocalMachine.DeleteSubKeyTree(LegacyUninstallKey, false);
            File.Delete(ShortcutPath("IP Check SIO"));
            string legacyDir = Path.Combine(ProgramFiles, "IpCheckSIO");
            if (Directory.Exists(legacyDir)) Directory.Delete(legacyDir, true);
        }

        static void CreateShortcut()
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic link = shell.CreateShortcut(ShortcutPath("Ethersio"));
            link.TargetPath = ExePath;
            link.Description = "État réseau : DHCP / IP fixe";
            link.Save();
        }

        static void RegisterUninstall()
        {
            using (var k = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Ethersio");
                k.SetValue("DisplayVersion", Version.ToString());
                k.SetValue("Publisher", "Yann Barthaux-Vallee");
                k.SetValue("DisplayIcon", ExePath);
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", "\"" + UninstallerPath + "\" /uninstall");
                k.SetValue("NoModify", 1);
                k.SetValue("NoRepair", 1);
            }
        }

        public static void Install(Action<string> step)
        {
            step("Fermeture des instances en cours");
            RemoveLegacy();
            KillAll("Ethersio");

            step("Copie des fichiers");
            Directory.CreateDirectory(Dir);
            foreach (string leftover in Directory.GetFiles(Dir, "Ethersio.exe.*"))
                try { File.Delete(leftover); } catch { }
            File.WriteAllBytes(ExePath, Resource("Ethersio.exe"));
            File.Copy(Application.ExecutablePath, UninstallerPath, true);

            step("Démarrage automatique pour tous les comptes");
            using (var k = Registry.LocalMachine.CreateSubKey(RunKey))
                k.SetValue("Ethersio", "\"" + ExePath + "\"");

            step("Mises à jour automatiques");
            CreateTask(HourlyTask, "/sc hourly /mo 4");
            CreateTask(BootTask, "/sc onstart /delay 0002:00");

            step("Menu Démarrer et Applications installées");
            CreateShortcut();
            RegisterUninstall();
        }

        // explorer.exe relance l'app sans les droits administrateur de l'installeur
        public static void Launch()
        {
            Process.Start("explorer.exe", "\"" + ExePath + "\"");
        }

        // Uninstall.exe est dans le dossier a supprimer : il se relance depuis TEMP
        public static void RelaunchFromTemp()
        {
            string copy = Path.Combine(Path.GetTempPath(), "Ethersio-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Application.ExecutablePath, copy);
            Process.Start(copy, "/uninstall");
        }

        public static void Uninstall()
        {
            Schtasks("/delete /tn \"" + HourlyTask + "\" /f");
            Schtasks("/delete /tn \"" + BootTask + "\" /f");
            KillAll("Ethersio");
            DeleteRunValue(Registry.LocalMachine, "Ethersio");
            Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);
            File.Delete(ShortcutPath("Ethersio"));
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
        }

        public static void DeleteSelfLater()
        {
            if (!Application.ExecutablePath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) return;
            var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + Application.ExecutablePath + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true
            };
            Process.Start(psi);
        }
    }
}

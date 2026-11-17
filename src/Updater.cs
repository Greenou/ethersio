// Ethersio - mises a jour : telechargement HTTPS, verification de la signature RSA, remplacement de l'exe.
// Sans signature valide (cle privee gardee sur le PC de l'auteur), aucune mise a jour n'est installee.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Ethersio
{
    class UpdateInfo
    {
        public Version Version;
        public string VersionText, File, Signature;
    }

    static class Updater
    {
        const string BaseUrl = "https://ethersio.barthaux.fr/";
        const int MaxExeBytes = 20 * 1024 * 1024;
        static readonly Regex VersionPattern = new Regex(@"^\d+\.\d+\.\d+$");
        static readonly Regex FilePattern = new Regex(@"^[A-Za-z0-9._-]+\.exe$");

        public static string LogPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ethersio", "update.log");
            }
        }

        static byte[] Get(string url, int maxBytes)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 15000;
            req.ReadWriteTimeout = 30000;
            req.UserAgent = "Ethersio/" + AppInfo.Version;
            req.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            using (var resp = req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[16384];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > maxBytes) throw new InvalidDataException("Fichier trop volumineux.");
                }
                return ms.ToArray();
            }
        }

        static string Field(string json, string name)
        {
            var m = Regex.Match(json, "\"" + name + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        // Lit version.json : {"version":"2.1.0","file":"Ethersio-2.1.0.exe","sig":"<base64>"}
        public static UpdateInfo Fetch()
        {
            string json = Encoding.UTF8.GetString(Get(BaseUrl + "version.json", 4096));
            string v = Field(json, "version"), f = Field(json, "file"), sig = Field(json, "sig");
            if (v == null || !VersionPattern.IsMatch(v) || f == null || !FilePattern.IsMatch(f) || string.IsNullOrEmpty(sig))
                throw new InvalidDataException("version.json invalide.");
            return new UpdateInfo { Version = new Version(v), VersionText = v, File = f, Signature = sig };
        }

        // La signature couvre la version ET l'exe : impossible de rejouer un ancien exe signe sous un numero plus recent.
        static bool Verify(UpdateInfo info, byte[] exe)
        {
            if (string.IsNullOrEmpty(UpdateKey.PublicKeyXml)) return false;
            byte[] head = Encoding.UTF8.GetBytes("ethersio:" + info.VersionText + ":");
            var payload = new byte[head.Length + exe.Length];
            Buffer.BlockCopy(head, 0, payload, 0, head.Length);
            Buffer.BlockCopy(exe, 0, payload, head.Length, exe.Length);
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(UpdateKey.PublicKeyXml);
                return rsa.VerifyData(payload, "SHA256", Convert.FromBase64String(info.Signature));
            }
        }

        // L'exe en cours d'execution ne peut pas etre ecrase, mais il peut etre renomme.
        static void Swap(string exe, byte[] data)
        {
            string fresh = exe + ".new", old = exe + "." + AppInfo.Version + ".old";
            File.WriteAllBytes(fresh, data);
            File.Move(exe, old);
            try { File.Move(fresh, exe); }
            catch { File.Move(old, exe); throw; }
        }

        static void DeleteOldCopies(string exe)
        {
            foreach (string f in Directory.GetFiles(Path.GetDirectoryName(exe), Path.GetFileName(exe) + ".*.old"))
                try { File.Delete(f); } catch { }
        }

        static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + "\r\n");
            }
            catch { }
        }

        // Ethersio.exe --update : lance par la tache planifiee (SYSTEM) ou en admin depuis le menu. Code de sortie 0 = OK.
        public static int Run()
        {
            try
            {
                string exe = Application.ExecutablePath;
                DeleteOldCopies(exe);
                var info = Fetch();
                if (info.Version <= new Version(AppInfo.Version)) return 0;

                byte[] data = Get(BaseUrl + info.File, MaxExeBytes);
                if (!Verify(info, data))
                {
                    Log("Signature invalide pour " + info.File + " : mise à jour refusée.");
                    return 2;
                }
                Swap(exe, data);
                Log("Mise à jour v" + AppInfo.Version + " -> v" + info.VersionText + " installée.");
                return 0;
            }
            catch (Exception ex)
            {
                Log("Échec : " + ex.Message);
                return 1;
            }
        }

        // Vrai quand l'exe sur le disque est plus recent que la version en cours (mise a jour faite par la tache SYSTEM).
        public static bool NewerOnDisk()
        {
            try
            {
                var f = FileVersionInfo.GetVersionInfo(Application.ExecutablePath);
                return new Version(f.FileMajorPart, f.FileMinorPart, f.FileBuildPart) > new Version(AppInfo.Version);
            }
            catch { return false; }
        }
    }
}

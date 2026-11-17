// Ethersio - application de la zone de notification (logique, tests, actions, mises a jour)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Ethersio
{
    class LogEntry
    {
        public DateTime T;
        public string Text;
        public int Level; // 0 info, 1 attention, 2 erreur
    }

    class TrayApp : ApplicationContext
    {
        const string AppName = "Ethersio";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly Flyout flyout;
        readonly System.Windows.Forms.Timer uiTimer;
        readonly System.Threading.Timer checkTimer, updateTimer;
        readonly SynchronizationContext ui;
        ProfilesForm profForm;
        Action balloonAction;

        // ---- etat lu par la popup
        public string PreferredId;
        public NetSnapshot Snap = new NetSnapshot();
        public List<NetSnapshot> AllCards = new List<NetSnapshot>();
        public double RxBps, TxBps;
        public readonly double[] RxHist = new double[60], TxHist = new double[60];
        public volatile NetCheck PingState = NetCheck.Unknown, HttpState = NetCheck.Unknown, DnsState = NetCheck.Unknown, GwState = NetCheck.Unknown;
        public volatile int PingMs = -1, GwMs = -1, DnsMs = -1;
        public volatile WifiInfo Wifi;
        public string Proxy = "aucun";
        public volatile string PublicIp;
        public readonly string HostName = Environment.MachineName;
        public readonly string Domain = "";
        public List<IpProfile> Profiles = new List<IpProfile>();
        public readonly List<LogEntry> Journal = new List<LogEntry>();
        public int JournalVersion;
        public Version UpdateAvailable;

        // ---- interne
        bool firstTick = true;
        NetState prevState;
        string prevId = "", prevIp = "";
        NetCheck prevInternet = NetCheck.Unknown;
        long lastRx = -1, lastTx = -1;
        string lastRateId = "";
        DateTime lastRateTime;
        int tickNo;
        volatile string gatewayToPing = "";
        int checking;

        public static string DataDir { get { return ProfileStore.Dir; } }
        public string JournalPath { get { return Path.Combine(DataDir, "journal.log"); } }

        public TrayApp(EventWaitHandle showEvent)
        {
            try { Domain = IPGlobalProperties.GetIPGlobalProperties().DomainName ?? ""; } catch { }
            try { Profiles = ProfileStore.Load(); } catch { }

            flyout = new Flyout(this);
            flyout.Icon = IconFactory.Get(NetState.Dhcp, false);
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            menu = new ContextMenuStrip();
            menu.Opening += delegate { BuildMenu(); };

            tray = new NotifyIcon
            {
                Icon = IconFactory.Get(NetState.NoNetwork, false),
                Text = "Ethersio",
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleFlyout(); };
            tray.BalloonTipClicked += delegate
            {
                var a = balloonAction;
                balloonAction = null;
                if (a != null) a(); else ShowFlyout();
            };

            NetworkChange.NetworkAddressChanged += delegate { TriggerCheckSoon(); };
            NetworkChange.NetworkAvailabilityChanged += delegate { TriggerCheckSoon(); };

            // une 2e instance lancee = ouvrir la popup de la 1re
            if (showEvent != null)
                ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { ui.Post(delegate { ShowFlyout(); }, null); }, null, -1, false);

            uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            uiTimer.Tick += delegate { Tick(); };
            uiTimer.Start();

            checkTimer = new System.Threading.Timer(CheckNetwork, null, 300, 8000);
            updateTimer = new System.Threading.Timer(CheckUpdate, null, 20000, 30 * 60 * 1000);
            Proxy = Extra.ProxyText();
            Tick();
            Log("Démarrage (v" + AppInfo.Version + ") : " + (Snap.Name == "" ? "aucune carte" : Snap.Name + " en " + StateShort(Snap.State) +
                (Snap.IPv4 != "" ? ", " + Snap.IPv4 : "")), Snap.State == NetState.Static ? 1 : 0);
        }

        void TriggerCheckSoon()
        {
            try { checkTimer.Change(1500, 8000); } catch { }
        }

        public void RecheckNow()
        {
            PingState = HttpState = DnsState = GwState = NetCheck.Unknown;
            PingMs = GwMs = DnsMs = -1;
            if (flyout.Visible) flyout.RefreshData();
            try { checkTimer.Change(50, 8000); } catch { }
        }

        // ============================================================ boucle principale (1 s)
        void Tick()
        {
            tickNo++;
            Snap = Net.Read(Net.PickBest(PreferredId));
            gatewayToPing = Snap.Gateway.Split(',')[0].Trim();

            var now = DateTime.UtcNow;
            if (Snap.Id == lastRateId && lastRx >= 0)
            {
                double sec = (now - lastRateTime).TotalSeconds;
                if (sec > 0.2)
                {
                    RxBps = Math.Max(0, (Snap.Rx - lastRx) * 8 / sec);
                    TxBps = Math.Max(0, (Snap.Tx - lastTx) * 8 / sec);
                }
            }
            else { RxBps = 0; TxBps = 0; }
            lastRx = Snap.Rx; lastTx = Snap.Tx; lastRateTime = now; lastRateId = Snap.Id;
            Array.Copy(RxHist, 1, RxHist, 0, RxHist.Length - 1); RxHist[RxHist.Length - 1] = RxBps;
            Array.Copy(TxHist, 1, TxHist, 0, TxHist.Length - 1); TxHist[TxHist.Length - 1] = TxBps;

            if (flyout.Visible && (tickNo % 3 == 1 || AllCards.Count == 0))
                AllCards = Net.Candidates().Select(Net.Read).ToList();
            if (tickNo % 5 == 0) Proxy = Extra.ProxyText();

            NetCheck internet = InternetState();
            bool fail = internet == NetCheck.Fail;
            tray.Icon = IconFactory.Get(Snap.State, fail);
            string tip = "Ethersio - " + StateShort(Snap.State) +
                         (Snap.IPv4 != "" ? "\n" + Snap.IPv4 : "") + (fail ? "\nPas d'internet" : "");
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            Notify(internet);
            if (flyout.Visible) flyout.RefreshData();
        }

        public NetCheck InternetState()
        {
            if (Snap.State == NetState.NoNetwork) return NetCheck.Fail;
            if (HttpState == NetCheck.Ok || PingState == NetCheck.Ok) return NetCheck.Ok;
            if (HttpState == NetCheck.Unknown && PingState == NetCheck.Unknown) return NetCheck.Unknown;
            return NetCheck.Fail;
        }

        static string StateShort(NetState s)
        {
            switch (s)
            {
                case NetState.Dhcp: return "DHCP (auto)";
                case NetState.Static: return "IP FIXE (manuel)";
                case NetState.Apipa: return "APIPA 169.254 !";
                default: return "Pas de réseau";
            }
        }

        // ============================================================ journal
        public void Log(string text, int level)
        {
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext) && ui != null)
            {
                ui.Post(delegate { Log(text, level); }, null);
                return;
            }
            Journal.Insert(0, new LogEntry { T = DateTime.Now, Text = text, Level = level });
            if (Journal.Count > 100) Journal.RemoveAt(Journal.Count - 1);
            JournalVersion++;
            try
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(JournalPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " +
                    (level == 2 ? "[ERREUR] " : level == 1 ? "[ATTENTION] " : "") + text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public void OpenJournal()
        {
            try
            {
                if (!File.Exists(JournalPath)) { Directory.CreateDirectory(DataDir); File.WriteAllText(JournalPath, ""); }
                Process.Start("notepad.exe", "\"" + JournalPath + "\"");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Ethersio"); }
        }

        // ============================================================ notifications
        void Notify(NetCheck internet)
        {
            bool cardChanged = Snap.Id != prevId;
            if (firstTick || cardChanged)
            {
                if (Snap.State == NetState.Static)
                    Balloon("Tu es en IP FIXE", "La carte « " + Snap.Name + " » est en configuration manuelle (" + Snap.IPv4 +
                            ").\nSi tu n'as pas internet, clique ici pour repasser en DHCP.", ToolTipIcon.Warning, ForceDhcp);
                else if (Snap.State == NetState.Apipa)
                    Balloon("Pas d'IP du DHCP", "Adresse 169.254.x.x : aucun serveur DHCP n'a répondu.", ToolTipIcon.Error, null);
                if (!firstTick) Log("Carte surveillée : " + (Snap.Name == "" ? "aucune" : Snap.Name), 0);
            }
            else if (Snap.State != prevState)
            {
                switch (Snap.State)
                {
                    case NetState.Static:
                        Balloon("Passage en IP FIXE", "« " + Snap.Name + " » est maintenant en manuel (" + Snap.IPv4 +
                                "). Pense à repasser en DHCP à la fin du TP.", ToolTipIcon.Warning, null);
                        Log("Passage en IP fixe : " + Snap.IPv4 + (Snap.Cidr >= 0 ? "/" + Snap.Cidr : ""), 1); break;
                    case NetState.Dhcp:
                        Balloon("Retour en DHCP", "« " + Snap.Name + " » est en automatique (" + Snap.IPv4 + ").", ToolTipIcon.Info, null);
                        Log("Retour en DHCP : " + Snap.IPv4, 0); break;
                    case NetState.Apipa:
                        Balloon("Pas d'IP du DHCP", "Adresse 169.254.x.x : aucun serveur DHCP n'a répondu.", ToolTipIcon.Error, null);
                        Log("APIPA : aucun serveur DHCP n'a répondu (" + Snap.IPv4 + ")", 2); break;
                    case NetState.NoNetwork:
                        Balloon("Réseau déconnecté", "Plus de connexion sur « " + Snap.Name + " ». Vérifie le câble ou le Wi-Fi.", ToolTipIcon.Warning, null);
                        Log("Réseau déconnecté (" + Snap.Name + ")", 2); break;
                }
            }
            else if (Snap.IPv4 != prevIp && Snap.IPv4 != "" && prevIp != "")
                Log("Nouvelle adresse IPv4 : " + Snap.IPv4 + " (avant : " + prevIp + ")", 0);

            if (internet != NetCheck.Unknown && prevInternet != NetCheck.Unknown && internet != prevInternet)
            {
                if (internet == NetCheck.Fail && Snap.State != NetState.NoNetwork)
                {
                    bool st = Snap.State == NetState.Static;
                    Balloon("Plus d'accès à internet",
                            st ? "Tu es en IP fixe : c'est sûrement la cause. Clique ici pour repasser en DHCP." : "Clique ici pour voir le diagnostic.",
                            ToolTipIcon.Warning, st ? (Action)ForceDhcp : null);
                    Log("Perte de l'accès à internet", 2);
                }
                else if (internet == NetCheck.Ok) Log("Accès à internet rétabli", 0);
            }

            if (internet != NetCheck.Unknown) prevInternet = internet;
            prevState = Snap.State;
            prevId = Snap.Id;
            prevIp = Snap.IPv4;
            firstTick = false;
        }

        void Balloon(string title, string text, ToolTipIcon icon, Action onClick)
        {
            balloonAction = onClick;
            tray.ShowBalloonTip(6000, title, text, icon);
        }

        // ============================================================ tests reseau (thread de fond, toutes les 8 s)
        void CheckNetwork(object _)
        {
            if (Interlocked.Exchange(ref checking, 1) == 1) return;
            try
            {
                try
                {
                    using (var p = new Ping())
                    {
                        var r = p.Send("1.1.1.1", 1500);
                        if (r.Status == IPStatus.Success) { PingMs = (int)r.RoundtripTime; PingState = NetCheck.Ok; }
                        else { PingMs = -1; PingState = NetCheck.Fail; }
                    }
                }
                catch { PingMs = -1; PingState = NetCheck.Fail; }

                // meme test que Windows : marche meme si le ping est bloque par le pare-feu du lycee
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("http://www.msftconnecttest.com/connecttest.txt");
                    req.Timeout = 3000;
                    req.ReadWriteTimeout = 3000;
                    using (var resp = req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream()))
                        HttpState = sr.ReadToEnd().Contains("Microsoft Connect Test") ? NetCheck.Ok : NetCheck.Fail;
                }
                catch { HttpState = NetCheck.Fail; }

                try
                {
                    var sw = Stopwatch.StartNew();
                    var a = Dns.GetHostAddresses("www.google.com");
                    DnsMs = (int)sw.ElapsedMilliseconds;
                    DnsState = a.Length > 0 ? NetCheck.Ok : NetCheck.Fail;
                }
                catch { DnsState = NetCheck.Fail; DnsMs = -1; }

                string gw = gatewayToPing;
                if (string.IsNullOrEmpty(gw)) { GwState = NetCheck.Unknown; GwMs = -1; }
                else
                {
                    try
                    {
                        using (var p = new Ping())
                        {
                            var r = p.Send(gw, 1000);
                            if (r.Status == IPStatus.Success) { GwMs = (int)r.RoundtripTime; GwState = NetCheck.Ok; }
                            else { GwMs = -1; GwState = NetCheck.Fail; }
                        }
                    }
                    catch { GwMs = -1; GwState = NetCheck.Fail; }
                }

                if (Snap.Wireless) Wifi = Extra.ReadWifi();
            }
            finally { Interlocked.Exchange(ref checking, 0); }
        }

        bool fetchingIp;
        public void FetchPublicIp()
        {
            if (fetchingIp) return;
            fetchingIp = true;
            PublicIp = "… recherche";
            if (flyout.Visible) flyout.RefreshData();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string r;
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("https://api.ipify.org");
                    req.Timeout = 5000;
                    using (var resp = req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream()))
                        r = sr.ReadToEnd().Trim();
                }
                catch { r = "échec (pas d'internet ?)"; }
                PublicIp = r;
                fetchingIp = false;
            });
        }

        // ============================================================ diagnostic en clair
        public void Diagnose(out string text, out int level)
        {
            var s = Snap;
            var inet = InternetState();
            var sb = new StringBuilder();
            level = 0;

            if (s.Name == "" || !s.Up)
            {
                text = "Aucune carte réseau connectée : vérifie le câble RJ45 ou le Wi-Fi.";
                level = 2; return;
            }
            if (s.State == NetState.Apipa)
            {
                text = "Adresse en 169.254.x.x (APIPA) : Windows n'a reçu aucune réponse d'un serveur DHCP.\n" +
                       "→ Vérifie le câble, le port du switch, le VLAN, ou que ton serveur DHCP tourne et a encore des adresses libres.";
                level = 2; return;
            }
            if (s.State == NetState.Static)
            {
                level = 1;
                if (inet == NetCheck.Fail)
                {
                    sb.AppendLine("Tu es en IP FIXE et tu n'as pas internet.");
                    sb.AppendLine("→ Si tu n'es plus en TP, clique sur « Repasser DHCP ».");
                    level = 2;
                }
                else sb.AppendLine("Tu es en IP fixe (manuel). Pense à repasser en DHCP à la fin du TP.");
                if (s.Gateway == "") sb.AppendLine("• Aucune passerelle : impossible de sortir du réseau local.");
                if (s.Dns == "") sb.AppendLine("• Aucun DNS : les noms de sites ne seront pas résolus.");
            }
            if (GwState == NetCheck.Fail && s.Gateway != "")
                sb.AppendLine("• La passerelle " + gatewayToPing + " ne répond pas au ping (mauvaise adresse, autre réseau, ou ping bloqué).");
            if (inet == NetCheck.Ok && DnsState == NetCheck.Fail)
            {
                sb.AppendLine("• Internet répond mais le DNS échoue : vérifie les serveurs DNS.");
                level = Math.Max(level, 1);
            }
            if (inet == NetCheck.Fail && s.State == NetState.Dhcp)
            {
                sb.AppendLine("DHCP OK mais pas d'internet : le problème vient de plus loin (passerelle, proxy, box, portail captif).");
                level = 2;
            }
            if (Proxy != "aucun" && inet == NetCheck.Fail)
                sb.AppendLine("• Un proxy est configuré : s'il ne répond pas, le navigateur n'aura pas internet.");
            if (s.DnsManual && s.State == NetState.Dhcp)
                sb.AppendLine("• Note : l'IP est automatique mais les DNS sont saisis à la main.");
            if (s.Wireless && Wifi != null && Wifi.Signal != "")
            {
                int sig;
                if (int.TryParse(new string(Wifi.Signal.Where(char.IsDigit).ToArray()), out sig) && sig < 40)
                    sb.AppendLine("• Signal Wi-Fi faible (" + Wifi.Signal + ") : rapproche-toi de la borne.");
            }
            if (sb.Length == 0) sb.Append("Tout est bon : configuration automatique et internet accessible. 👍");
            text = sb.ToString().TrimEnd();
        }

        // ============================================================ popup
        void ToggleFlyout()
        {
            if (flyout.Visible) flyout.HideFlyout();
            else if (unchecked(Environment.TickCount - flyout.HiddenAt) > 300) ShowFlyout();
        }

        void ShowFlyout()
        {
            AllCards = Net.Candidates().Select(Net.Read).ToList();
            flyout.ShowAt();
        }

        // mode --capture : ouvre la popup, photographie les 3 onglets puis quitte
        public void CaptureAndExit(string dir)
        {
            int step = 0;
            var t = new System.Windows.Forms.Timer { Interval = 7000 };
            t.Tick += delegate
            {
                t.Interval = 1200;
                if (step == 0) { ShowFlyout(); flyout.Pinned = true; flyout.Height = UI.S(1250); flyout.SelectTab(0); }
                else if (step <= 3)
                {
                    flyout.Save(Path.Combine(dir, "capture-" + step + ".png"));
                    if (step < 3) flyout.SelectTab(step);
                }
                else { t.Stop(); Quit(); }
                step++;
            };
            t.Start();
        }

        public void SelectCard(string id)
        {
            PreferredId = id;
            lastRx = -1; lastTx = -1; lastRateId = "";
            Array.Clear(RxHist, 0, RxHist.Length);
            Array.Clear(TxHist, 0, TxHist.Length);
            Tick();
            RecheckNow();
        }

        // ============================================================ actions
        public void OpenNcpa()
        {
            try { Process.Start("ncpa.cpl"); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Ethersio"); }
        }

        // Lance une commande en admin (UAC), attend la fin en arriere-plan puis affiche le resultat
        void RunElevated(string cmdArgs, string okTitle, string okText, Action onOk = null,
                         string failHint = "Vérifie les adresses et que la carte existe.")
        {
            Process proc;
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c " + cmdArgs)
                {
                    Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                proc = Process.Start(psi);
            }
            catch (Win32Exception) { return; }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Ethersio"); return; }
            if (proc == null) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                int code = -1;
                try { proc.WaitForExit(60000); code = proc.ExitCode; } catch { }
                finally { proc.Dispose(); }
                ui.Post(delegate
                {
                    TriggerCheckSoon();
                    if (okTitle == null) return;
                    if (code == 0)
                    {
                        Balloon(okTitle, okText, ToolTipIcon.Info, null);
                        Log(okTitle + " : " + okText, 0);
                        if (onOk != null) onOk();
                    }
                    else
                    {
                        Balloon("Échec de la commande", "Erreur (code " + code + "). " + failHint, ToolTipIcon.Error, null);
                        Log("Échec : " + okTitle + " (code " + code + ")", 2);
                    }
                }, null);
            });
        }

        public void ForceDhcp()
        {
            ApplyProfile(new IpProfile { Name = "DHCP", Dhcp = true });
        }

        public void ApplyProfile(IpProfile p)
        {
            if (Snap.Name == "") { MessageBox.Show("Aucune carte réseau trouvée.", "Ethersio"); return; }
            string detail = p.Dhcp
                ? "IPv4 : DHCP (automatique)\nDNS : automatique"
                : "IP : " + p.Ip + "\nMasque : " + IpTools.NormalizeMask(p.Mask) +
                  "\nPasserelle : " + (p.Gateway.Trim() == "" ? "aucune" : p.Gateway) +
                  "\nDNS : " + (p.Dns1.Trim() == "" ? "aucun" : p.Dns1 + (p.Dns2.Trim() != "" ? ", " + p.Dns2 : ""));
            var r = MessageBox.Show("Appliquer « " + p.Name + " » sur la carte « " + Snap.Name + " » ?\n\n" + detail +
                                    "\n\n(Windows va demander les droits administrateur)",
                                    "Ethersio", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            RunElevated(IpTools.NetshCommand(p, Snap.Name), "Profil appliqué", "« " + p.Name + " » est actif sur " + Snap.Name + ".");
        }

        public void OpenProfiles()
        {
            flyout.HideFlyout();
            if (profForm != null && !profForm.IsDisposed) { profForm.Activate(); return; }
            profForm = new ProfilesForm(
                delegate { return Snap; },
                delegate (IpProfile p) { ApplyProfile(p); },
                delegate { try { Profiles = ProfileStore.Load(); } catch { } });
            profForm.Icon = IconFactory.Get(NetState.Dhcp, false);
            profForm.FormClosed += delegate { profForm = null; };
            profForm.Show();
            profForm.Activate();
        }

        public void RenewIp()
        {
            if (Snap.Name == "") return;
            string n = Snap.Name.Replace("\"", "");
            RunElevated("ipconfig /release \"" + n + "\" & ipconfig /renew \"" + n + "\" & exit /b 0",
                        "IP renouvelée", "Nouvelle demande DHCP envoyée pour " + Snap.Name + ".");
        }

        public void FlushDns()
        {
            try
            {
                Extra.RunCapture("ipconfig", "/flushdns", 5000);
                Balloon("Cache DNS vidé", "ipconfig /flushdns exécuté.", ToolTipIcon.Info, null);
                Log("Cache DNS vidé", 0);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Ethersio"); }
        }

        public void CopyInfos()
        {
            var s = Snap;
            var sb = new StringBuilder();
            sb.AppendLine("=== Ethersio - " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + " - " + HostName + " ===");
            sb.AppendLine("Carte        : " + s.Name + " (" + s.Description + ")");
            sb.AppendLine("Mode IPv4    : " + (s.Dhcp ? "DHCP" : "STATIQUE"));
            sb.AppendLine("IPv4         : " + s.IPv4 + (s.Cidr >= 0 ? "/" + s.Cidr : "") + "  masque " + s.Mask);
            sb.AppendLine("Passerelle   : " + s.Gateway);
            sb.AppendLine("DNS          : " + s.Dns + (s.DnsManual ? " (manuel)" : " (auto)"));
            if (s.Dhcp)
            {
                sb.AppendLine("Serveur DHCP : " + s.DhcpServer);
                if (s.LeaseRemaining > 0) sb.AppendLine("Bail         : expire dans " + Net.FormatDuration(s.LeaseRemaining));
            }
            sb.AppendLine("IPv6         : " + (s.IPv6.Count == 0 ? "aucune" : string.Join(" | ", s.IPv6.ToArray())));
            sb.AppendLine("MAC          : " + s.Mac);
            sb.AppendLine("Lien         : " + Net.FormatBits(s.SpeedBps));
            if (s.Wireless && Wifi != null && Wifi.Ssid != "")
                sb.AppendLine("Wi-Fi        : " + Wifi.Ssid + " · signal " + Wifi.Signal + " · canal " + Wifi.Channel + " · " + Wifi.Band);
            sb.AppendLine("Proxy        : " + Proxy.Replace("\n", " | "));
            if (PublicIp != null && !PublicIp.StartsWith("…")) sb.AppendLine("IP publique  : " + PublicIp);
            sb.AppendLine("Internet     : " + InternetState() + (PingMs >= 0 ? " (ping " + PingMs + " ms)" : ""));
            sb.AppendLine("Domaine      : " + (Domain == "" ? "groupe de travail" : Domain));
            try
            {
                Clipboard.SetText(sb.ToString());
                Balloon("Infos copiées", "Colle-les dans ton compte rendu ou ton ticket (Ctrl+V).", ToolTipIcon.Info, null);
            }
            catch { }
        }

        // ============================================================ mises a jour (voir Updater.cs)
        // La tache planifiee SYSTEM installe la nouvelle version ; ici on la detecte sur le disque puis on redemarre.
        void CheckUpdate(object _)
        {
            try
            {
                if (Updater.NewerOnDisk()) { ui.Post(delegate { RestartSelf(); }, null); return; }
                Version v = Updater.Fetch().Version;
                if (v <= new Version(AppInfo.Version) || UpdateAvailable == v) return;
                ui.Post(delegate
                {
                    UpdateAvailable = v;
                    Log("Mise à jour disponible : v" + v, 1);
                    if (!IsInstalledForAll())
                        Balloon("Mise à jour disponible", "Ethersio v" + v + " est disponible. Clique ici pour l'installer.", ToolTipIcon.Info, DoUpdate);
                }, null);
            }
            catch { }
        }

        public void DoUpdate()
        {
            if (UpdateAvailable == null) return;
            var r = MessageBox.Show("Installer Ethersio v" + UpdateAvailable + " ?\n\nL'application va redémarrer (droits administrateur demandés).",
                                    "Ethersio", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            RunElevated("start \"\" /wait \"" + Application.ExecutablePath + "\" --update", "Mise à jour installée",
                        "Ethersio va redémarrer.", delegate { ThreadPool.QueueUserWorkItem(CheckUpdate); },
                        "Voir " + Updater.LogPath + ".");
        }

        void RestartSelf()
        {
            if (profForm != null) return; // ne pas perdre des profils en cours d'edition
            Log("Redémarrage après mise à jour", 0);
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & start \"\" \"" + Application.ExecutablePath + "\"")
            {
                CreateNoWindow = true, UseShellExecute = false
            });
            Quit();
        }

        // ============================================================ menu clic droit
        void BuildMenu()
        {
            menu.Items.Clear();
            var title = new ToolStripMenuItem(StateShort(Snap.State) + (Snap.IPv4 != "" ? "  -  " + Snap.IPv4 : "")) { Enabled = false };
            menu.Items.Add(title);
            if (UpdateAvailable != null)
                menu.Items.Add(new ToolStripMenuItem("⬆ Installer la v" + UpdateAvailable, null, delegate { DoUpdate(); }) { Font = new Font(menu.Font, FontStyle.Bold) });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Ouvrir Ethersio", null, delegate { ShowFlyout(); });
            menu.Items.Add("Ouvrir ncpa.cpl", null, delegate { OpenNcpa(); });
            var dhcp = new ToolStripMenuItem("Repasser en DHCP (admin)", null, delegate { ForceDhcp(); });
            dhcp.Enabled = Snap.Name != "" && (!Snap.Dhcp || Snap.DnsManual);
            menu.Items.Add(dhcp);

            var profMenu = new ToolStripMenuItem("Profils IP");
            try { Profiles = ProfileStore.Load(); } catch { }
            foreach (var p in Profiles)
            {
                var prof = p;
                var it = new ToolStripMenuItem(p.ToString(), null, delegate { ApplyProfile(prof.Clone()); });
                it.Checked = IpTools.Matches(p, Snap);
                profMenu.DropDownItems.Add(it);
            }
            if (Profiles.Count > 0) profMenu.DropDownItems.Add(new ToolStripSeparator());
            profMenu.DropDownItems.Add("Gérer les profils…", null, delegate { OpenProfiles(); });
            menu.Items.Add(profMenu);
            menu.Items.Add("Copier les infos", null, delegate { CopyInfos(); });

            var cards = new ToolStripMenuItem("Carte surveillée");
            var auto = new ToolStripMenuItem("Automatique (recommandé)", null, delegate { SelectCard(null); });
            auto.Checked = PreferredId == null;
            cards.DropDownItems.Add(auto);
            cards.DropDownItems.Add(new ToolStripSeparator());
            foreach (var n in Net.Candidates())
            {
                string id = n.Id;
                string label = n.Name + (n.OperationalStatus == OperationalStatus.Up ? "" : "  (déconnectée)") +
                               (Net.IsVirtual(n) ? "  [virtuelle]" : "");
                var it = new ToolStripMenuItem(label, null, delegate { SelectCard(id); });
                it.Checked = PreferredId == id;
                cards.DropDownItems.Add(it);
            }
            menu.Items.Add(cards);

            if (IsInstalledForAll())
                menu.Items.Add(new ToolStripMenuItem("Démarrage auto : activé pour tous (installé)") { Checked = true, Enabled = false });
            else
            {
                var startup = new ToolStripMenuItem("Lancer au démarrage (mon compte)", null, delegate { ToggleStartup(); });
                startup.Checked = IsStartupEnabled();
                menu.Items.Add(startup);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Version " + AppInfo.Version) { Enabled = false });
            menu.Items.Add("Quitter", null, delegate { Quit(); });
        }

        static bool IsInstalledForAll()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(RunKey))
                    return k != null && k.GetValue(AppName) != null;
            }
            catch { return false; }
        }

        bool IsStartupEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(AppName) != null;
            }
            catch { return false; }
        }

        void ToggleStartup()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (k.GetValue(AppName) != null) k.DeleteValue(AppName, false);
                    else k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Ethersio"); }
        }

        void Quit()
        {
            uiTimer.Stop();
            checkTimer.Dispose();
            updateTimer.Dispose();
            tray.Visible = false;
            tray.Dispose();
            flyout.Close();
            flyout.Dispose();
            ExitThread();
        }
    }
}

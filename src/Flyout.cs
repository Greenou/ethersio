// Ethersio - la popup (facon panneau Windows 11) : Infos / Diagnostic / Outils
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Ethersio
{
    class HeaderPanel : Panel
    {
        public string Title = "", Subtitle = "";
        public NetState State = NetState.NoNetwork;
        public bool Fail;
        Bitmap icon; string iconKey = "";

        public HeaderPanel() { DoubleBuffered = true; BackColor = UI.Bg; }

        public void Set(string title, string sub, NetState st, bool fail)
        {
            if (title == Title && sub == Subtitle && st == State && fail == Fail) return;
            Title = title; Subtitle = sub; State = st; Fail = fail;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(UI.Blend(IconFactory.ColorOf(State), UI.Bg, UI.Dark ? 0.22f : 0.13f));
            string key = State + "|" + Fail;
            if (icon == null || key != iconKey) { if (icon != null) icon.Dispose(); icon = IconFactory.Render(UI.S(40), State, Fail); iconKey = key; }
            g.DrawImage(icon, UI.S(16), (Height - UI.S(40)) / 2);
            var bg = UI.Blend(IconFactory.ColorOf(State), UI.Bg, UI.Dark ? 0.22f : 0.13f);
            TextRenderer.DrawText(g, Title, UI.Semi(13f), new Rectangle(UI.S(68), UI.S(15), Width - UI.S(150), UI.S(28)), UI.Text, bg,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Subtitle, UI.F(9f), new Rectangle(UI.S(68), UI.S(43), Width - UI.S(80), UI.S(20)), UI.Sub, bg,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    class Flyout : Form
    {
        readonly TrayApp app;
        public bool Pinned;
        public int HiddenAt;
        readonly int W, CW;

        readonly HeaderPanel header;
        readonly FlatBtn bPin, bClose;
        readonly Segmented tabs;
        readonly FlowLayoutPanel[] pages = new FlowLayoutPanel[3];

        // Infos
        Card cUpdate, cWifi, cCards;
        Label lUpdate, lRx, lTx;
        HeroIp hero;
        KvRow rProfile, rGw, rDns, rDhcp, rLease, rV6, rMac, rLink, rSsid, rSignal, rChan, rBand, rHost, rDomain, rProxy, rPublic;
        Sparkline spark;
        FlowLayoutPanel cardsList;
        string cardsSig = "";

        // Diagnostic
        ChainView chain;
        Card cAdvice;
        Label lAdvice, lSpeed;
        FlatBtn bSpeed;
        FlowLayoutPanel journalList;
        int journalVersion = -1;
        bool speedRunning;

        // Outils
        TextBox tTarget, tOut, tHost, tPort, tCalc, tSplit, tCalcOut, tArp;
        Label lPort;
        Process consoleProc;
        bool toolsPrefilled;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;  // ombre
                cp.ExStyle |= 0x80;        // pas dans Alt+Tab
                return cp;
            }
        }

        public Flyout(TrayApp app)
        {
            this.app = app;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = UI.Bg;
            ForeColor = UI.Text;
            Font = UI.F(9f);
            KeyPreview = true;
            Text = "Ethersio";

            W = UI.S(410);
            CW = W - UI.S(24) - SystemInformation.VerticalScrollBarWidth - UI.S(2);
            Size = new Size(W, UI.S(660));

            // --- pages
            var content = new Panel { Dock = DockStyle.Fill, BackColor = UI.Bg };
            for (int i = 0; i < 3; i++)
            {
                var p = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
                    BackColor = UI.Bg, Padding = new Padding(UI.S(12), UI.S(2), 0, UI.S(4)), Visible = i == 0
                };
                p.HandleCreated += delegate { UI.DarkScroll(p); };
                pages[i] = p;
                content.Controls.Add(p);
            }

            // --- onglets
            var tabBar = new Panel { Dock = DockStyle.Top, Height = UI.S(48), Padding = new Padding(UI.S(12), UI.S(8), UI.S(12), UI.S(6)), BackColor = UI.Bg };
            tabs = new Segmented("Infos", "Diagnostic", "Outils") { Dock = DockStyle.Fill };
            tabs.Changed += delegate
            {
                for (int i = 0; i < 3; i++) pages[i].Visible = i == tabs.Selected;
                RefreshData();
            };
            tabBar.Controls.Add(tabs);

            // --- en-tete
            header = new HeaderPanel { Dock = DockStyle.Top, Height = UI.S(78) };
            bClose = new FlatBtn(UI.GClose, "") { Width = UI.S(32), Height = UI.S(32) };
            bPin = new FlatBtn(UI.GPin, "") { Width = UI.S(32), Height = UI.S(32) };
            bClose.Click += delegate { HideFlyout(); };
            bPin.Click += delegate
            {
                Pinned = !Pinned;
                bPin.Glyph = Pinned ? UI.GUnpin : UI.GPin;
                bPin.Primary = Pinned;
                bPin.Invalidate();
            };
            header.Controls.Add(bClose);
            header.Controls.Add(bPin);
            header.Resize += delegate
            {
                bClose.Location = new Point(header.Width - UI.S(44), UI.S(12));
                bPin.Location = new Point(header.Width - UI.S(80), UI.S(12));
            };
            var tip = new ToolTip();
            tip.SetToolTip(bPin, "Garder la fenêtre ouverte");
            tip.SetToolTip(bClose, "Fermer (Échap)");

            // --- actions en bas
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom, ColumnCount = 3, RowCount = 2, Height = UI.S(90),
                Padding = new Padding(UI.S(9), UI.S(9), UI.S(9), UI.S(9)), BackColor = UI.Bg
            };
            for (int i = 0; i < 3; i++) footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            for (int i = 0; i < 2; i++) footer.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            footer.Paint += (s, e) => { using (var p = new Pen(UI.Border)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };
            AddFoot(footer, UI.GEdit, "Profils IP", delegate { app.OpenProfiles(); });
            AddFoot(footer, UI.GSettings, "ncpa.cpl", delegate { app.OpenNcpa(); });
            AddFoot(footer, UI.GSync, "Repasser DHCP", delegate { app.ForceDhcp(); });
            AddFoot(footer, UI.GRefresh, "Renouveler IP", delegate { app.RenewIp(); });
            AddFoot(footer, UI.GDelete, "Vider DNS", delegate { app.FlushDns(); });
            AddFoot(footer, UI.GCopy, "Copier infos", delegate { app.CopyInfos(); });

            Controls.Add(content);
            Controls.Add(tabBar);
            Controls.Add(header);
            Controls.Add(footer);

            BuildInfo();
            BuildDiag();
            BuildTools();
        }

        void AddFoot(TableLayoutPanel t, string glyph, string text, EventHandler click)
        {
            var b = new FlatBtn(glyph, text) { Dock = DockStyle.Fill, Margin = new Padding(UI.S(3)) };
            b.Click += click;
            t.Controls.Add(b);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UI.RoundCorners(this);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!Pinned) HideFlyout();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) HideFlyout();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideFlyout(); return; }
            StopConsole();
            base.OnFormClosing(e);
        }

        public void HideFlyout()
        {
            if (!Visible) return;
            Hide();
            HiddenAt = Environment.TickCount;
        }

        public void ShowAt()
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Height = Math.Min(UI.S(660), wa.Height - UI.S(24));
            Location = new Point(wa.Right - Width - UI.S(12), wa.Bottom - Height - UI.S(12));
            RefreshData();
            Show();
            Activate();
        }

        void UiDo(Action a)
        {
            if (IsDisposed) return;
            try { if (IsHandleCreated) BeginInvoke(a); } catch { }
        }

        static KvRow Kv(Card c, string key)
        {
            var r = new KvRow(key, c.Inner);
            c.Controls.Add(r);
            return r;
        }

        // ================================================================== INFOS
        void BuildInfo()
        {
            var p = pages[0];

            cUpdate = new Card(CW) { Stripe = UI.Accent, Visible = false };
            lUpdate = UI.Lbl("", UI.Semi(9.5f), UI.Text, cUpdate.Inner);
            cUpdate.Controls.Add(lUpdate);
            var bU = new FlatBtn(UI.GDownload, "Installer la mise à jour") { Primary = true };
            bU.Click += delegate { app.DoUpdate(); };
            cUpdate.Controls.Add(bU);
            p.Controls.Add(cUpdate);

            var c = new Card(CW);
            hero = new HeroIp(c.Inner);
            c.Controls.Add(hero);
            rProfile = Kv(c, "Profil");
            rGw = Kv(c, "Passerelle");
            rDns = Kv(c, "DNS");
            rDhcp = Kv(c, "Serveur DHCP");
            rLease = Kv(c, "Bail DHCP");
            rV6 = Kv(c, "IPv6");
            rMac = Kv(c, "Adresse MAC");
            rLink = Kv(c, "Carte");
            p.Controls.Add(c);

            cWifi = new Card(CW);
            cWifi.Title("Wi-Fi");
            rSsid = Kv(cWifi, "Réseau (SSID)");
            rSignal = Kv(cWifi, "Signal");
            rChan = Kv(cWifi, "Canal");
            rBand = Kv(cWifi, "Bande / norme");
            p.Controls.Add(cWifi);

            var cr = new Card(CW);
            cr.Title("Débit en direct");
            var rates = UI.Row(cr.Inner);
            lRx = UI.Lbl("↓ 0 b/s", UI.Semi(11f), UI.Fg(UI.Accent), cr.Inner);
            lTx = UI.Lbl("↑ 0 b/s", UI.Semi(11f), UI.Fg(UI.Purple), cr.Inner);
            lRx.Margin = new Padding(0, 0, UI.S(18), 0);
            rates.Controls.Add(lRx);
            rates.Controls.Add(lTx);
            rates.Controls.Add(UI.Lbl("réception / envoi", UI.F(8f), UI.Sub, cr.Inner));
            cr.Controls.Add(rates);
            spark = new Sparkline(cr.Inner, UI.S(70));
            cr.Controls.Add(spark);
            p.Controls.Add(cr);

            var cs = new Card(CW);
            cs.Title("Système");
            rHost = Kv(cs, "Nom du PC");
            rDomain = Kv(cs, "Domaine");
            rProxy = Kv(cs, "Proxy");
            rPublic = Kv(cs, "IP publique");
            rPublic.Action += delegate { app.FetchPublicIp(); };
            p.Controls.Add(cs);

            cCards = new Card(CW);
            cCards.Title("Toutes les cartes réseau");
            cCards.Controls.Add(UI.Lbl("Clique sur une carte pour la surveiller. En automatique, les cartes virtuelles (VMware, VPN…) sont ignorées.",
                                       UI.F(8.5f), UI.Sub, cCards.Inner));
            cardsList = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = UI.Card, Margin = new Padding(0, UI.S(6), 0, 0)
            };
            cCards.Controls.Add(cardsList);
            p.Controls.Add(cCards);
        }

        void RefreshInfo()
        {
            var s = app.Snap;

            cUpdate.Visible = app.UpdateAvailable != null;
            if (app.UpdateAvailable != null)
                lUpdate.Text = "Nouvelle version " + app.UpdateAvailable + " disponible (tu as la " + AppInfo.Version + ").";

            string chip;
            switch (s.State)
            {
                case NetState.Dhcp: chip = "DHCP · AUTO"; break;
                case NetState.Static: chip = "MANUEL"; break;
                case NetState.Apipa: chip = "APIPA"; break;
                default: chip = "HORS LIGNE"; break;
            }
            hero.Set(s.IPv4 == "" ? "Pas d'adresse IPv4" : s.IPv4 + (s.Cidr >= 0 ? " /" + s.Cidr : ""),
                     s.Mask != "" ? "Masque " + s.Mask : (s.Name == "" ? "Aucune carte réseau" : s.Name + " déconnectée"),
                     chip, IconFactory.ColorOf(s.State));

            var prof = app.Profiles.FirstOrDefault(x => IpTools.Matches(x, s));
            rProfile.Set(prof != null ? prof.Name : "aucun profil enregistré", prof != null ? (Color?)null : UI.Sub, null);
            rGw.Set(s.Gateway == "" ? "aucune" : s.Gateway, s.Gateway == "" && s.IPv4 != "" ? (Color?)UI.Fg(IconFactory.Red) : null,
                    s.Gateway == "" ? null : s.Gateway);
            rDns.Set(s.Dns == "" ? "aucun" : s.Dns.Replace(", ", "\n") + (s.DnsManual ? "   · saisis à la main" : "   · automatiques"),
                     s.Dns == "" && s.IPv4 != "" ? (Color?)UI.Fg(IconFactory.Red) : null, s.Dns == "" ? null : s.Dns);
            rDhcp.Set(s.Dhcp ? (s.DhcpServer == "" ? "—" : s.DhcpServer) : "— (IP fixe)", null, s.Dhcp && s.DhcpServer != "" ? s.DhcpServer : null);
            if (s.LeaseRemaining > 0)
                rLease.Set("expire dans " + Net.FormatDuration(s.LeaseRemaining) + " (le " +
                           DateTime.Now.AddSeconds(s.LeaseRemaining).ToString("dd/MM à HH:mm") + ")" +
                           (s.LeaseTotal > 0 ? "\ndurée du bail : " + Net.FormatDuration(s.LeaseTotal) : ""), null, null);
            else rLease.Set(s.Dhcp ? "—" : "— (IP fixe)", null, null);
            string v6copy = s.IPv6.Where(x => !x.Contains("(")).FirstOrDefault();
            rV6.Set(s.IPv6.Count == 0 ? "désactivé / aucune adresse" : string.Join("\n", s.IPv6.ToArray()), null, v6copy);
            rMac.Set(s.Mac == "" ? "—" : s.Mac, null, s.Mac == "" ? null : s.Mac);
            rLink.Set(s.Name == "" ? "—" : s.Name + (s.Type != s.Name ? " · " + s.Type : "") + (s.Up && s.SpeedBps > 0 ? " · " + Net.FormatBits(s.SpeedBps) : "") +
                      "\n" + s.Description, null, null);

            cWifi.Visible = s.Wireless;
            if (s.Wireless)
            {
                var w = app.Wifi;
                if (w == null) { rSsid.Set("lecture…", UI.Sub, null); rSignal.Set("", null, null); rChan.Set("", null, null); rBand.Set("", null, null); }
                else if (w.Error != "") { rSsid.Set(w.Error, UI.Fg(IconFactory.Orange), null); rSignal.Set("—", null, null); rChan.Set("—", null, null); rBand.Set("—", null, null); }
                else
                {
                    int sig = 0;
                    int.TryParse(new string(w.Signal.Where(char.IsDigit).ToArray()), out sig);
                    string q = sig >= 80 ? "excellent" : sig >= 60 ? "bon" : sig >= 40 ? "moyen" : "faible";
                    Color qc = sig >= 60 ? IconFactory.Green : sig >= 40 ? IconFactory.Orange : IconFactory.Red;
                    rSsid.Set(w.Ssid, null, w.Ssid);
                    rSignal.Set(w.Signal + "  · " + q, UI.Fg(qc), null);
                    rChan.Set(w.Channel == "" ? "—" : w.Channel, null, null);
                    rBand.Set((w.Band == "" ? "" : w.Band + "  · ") + w.Radio, null, null);
                }
            }

            lRx.Text = "↓ " + Net.FormatBits(app.RxBps);
            lTx.Text = "↑ " + Net.FormatBits(app.TxBps);
            spark.SetData(app.RxHist, app.TxHist);

            rHost.Set(app.HostName, null, app.HostName);
            rDomain.Set(app.Domain == "" ? "aucun (groupe de travail)" : app.Domain, null, app.Domain == "" ? null : app.Domain);
            rProxy.Set(app.Proxy, app.Proxy != "aucun" ? (Color?)UI.Fg(IconFactory.Orange) : null, null);
            string pub = app.PublicIp;
            if (pub == null) rPublic.Set("cliquer pour afficher", UI.Fg(UI.Accent), null);
            else if (pub.StartsWith("…") || pub.StartsWith("échec")) rPublic.Set(pub, UI.Sub, null);
            else rPublic.Set(pub, null, pub);

            RefreshCards();
        }

        void RefreshCards()
        {
            var cards = app.AllCards;
            string sig = string.Join("|", cards.Select(c => c.Id + c.State + c.IPv4 + c.Up + c.Gateway).ToArray()) + "#" + app.PreferredId + "#" + app.Snap.Id;
            if (sig == cardsSig) return;
            cardsSig = sig;
            cardsList.SuspendLayout();
            foreach (Control old in cardsList.Controls.Cast<Control>().ToList()) old.Dispose();
            cardsList.Controls.Clear();

            int w = cCards.Inner;
            var auto = new NicRow(w)
            {
                Id = "", Title = "Automatique (recommandé)",
                Line = "en ce moment : " + (app.PreferredId == null ? (app.Snap.Name == "" ? "aucune carte" : app.Snap.Name) : "désactivé"),
                Dot = UI.Accent, Selected = app.PreferredId == null
            };
            auto.Click += delegate { app.SelectCard(null); };
            cardsList.Controls.Add(auto);
            foreach (var c in cards)
            {
                string id = c.Id;
                string line = !c.Up ? "déconnectée"
                    : (c.IPv4 == "" ? "pas d'IPv4" : c.IPv4 + (c.Cidr >= 0 ? "/" + c.Cidr : "")) + " · " + (c.Dhcp ? "DHCP" : "IP fixe") +
                      (c.Gateway != "" ? " · passerelle " + c.Gateway : "");
                var r = new NicRow(w)
                {
                    Id = id, Title = c.Name + (c.Virtual ? "   · virtuelle" : ""), Line = line,
                    Dot = c.Up ? IconFactory.ColorOf(c.State) : UI.Track, Selected = app.PreferredId == id
                };
                r.Click += delegate { app.SelectCard(id); };
                cardsList.Controls.Add(r);
            }
            cardsList.ResumeLayout();
        }

        // ================================================================== DIAGNOSTIC
        void BuildDiag()
        {
            var p = pages[1];
            var c = new Card(CW);
            c.Title("Chaîne de connexion");
            chain = new ChainView(c.Inner);
            c.Controls.Add(chain);
            var b = new FlatBtn(UI.GRefresh, "Relancer le test");
            b.Click += delegate { app.RecheckNow(); };
            c.Controls.Add(b);
            p.Controls.Add(c);

            cAdvice = new Card(CW);
            cAdvice.Title("Ce qu'il faut retenir");
            lAdvice = UI.Lbl("", UI.F(9.5f), UI.Text, cAdvice.Inner);
            cAdvice.Controls.Add(lAdvice);
            p.Controls.Add(cAdvice);

            var cs = new Card(CW);
            cs.Title("Test de débit");
            lSpeed = UI.Lbl("Télécharge un fichier de test pendant 10 s (serveur Cloudflare) pour mesurer ton vrai débit internet.",
                            UI.F(9f), UI.Sub, cs.Inner);
            cs.Controls.Add(lSpeed);
            bSpeed = new FlatBtn(UI.GDownload, "Lancer le test") { Primary = true };
            bSpeed.Click += delegate { StartSpeedTest(); };
            cs.Controls.Add(bSpeed);
            p.Controls.Add(cs);

            var cj = new Card(CW);
            cj.Title("Journal des événements");
            journalList = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = UI.Card, Margin = new Padding(0)
            };
            cj.Controls.Add(journalList);
            var bj = new FlatBtn(UI.GFolder, "Ouvrir le fichier journal");
            bj.Click += delegate { app.OpenJournal(); };
            cj.Controls.Add(bj);
            p.Controls.Add(cj);
        }

        void RefreshDiag()
        {
            var s = app.Snap;
            bool cardOk = s.State == NetState.Dhcp || s.State == NetState.Static;
            chain.States[0] = cardOk ? NetCheck.Ok : NetCheck.Fail;
            chain.Details[0] = s.IPv4 == "" ? "pas d'IP" : (s.Apipa ? "APIPA" : s.IPv4);

            if (s.Gateway == "") { chain.States[1] = NetCheck.Fail; chain.Details[1] = "aucune"; }
            else
            {
                chain.States[1] = app.GwState;
                chain.Details[1] = app.GwMs >= 0 ? app.GwMs + " ms" : (app.GwState == NetCheck.Fail ? "ne répond pas" : "…");
            }
            chain.States[2] = app.DnsState;
            chain.Details[2] = app.DnsState == NetCheck.Ok ? (app.DnsMs <= 0 ? "OK (cache)" : app.DnsMs + " ms") : (app.DnsState == NetCheck.Fail ? "échec" : "…");
            var inet = app.InternetState();
            chain.States[3] = inet;
            chain.Details[3] = inet == NetCheck.Ok ? (app.PingMs >= 0 ? "ping " + app.PingMs + " ms" : "HTTP OK")
                             : (inet == NetCheck.Fail ? "inaccessible" : "…");
            chain.Invalidate();

            string text; int level;
            app.Diagnose(out text, out level);
            lAdvice.Text = text;
            cAdvice.Stripe = level == 0 ? IconFactory.Green : (level == 1 ? IconFactory.Orange : IconFactory.Red);

            if (journalVersion != app.JournalVersion)
            {
                journalVersion = app.JournalVersion;
                journalList.SuspendLayout();
                foreach (Control old in journalList.Controls.Cast<Control>().ToList()) old.Dispose();
                journalList.Controls.Clear();
                var entries = app.Journal.Take(25).ToList();
                if (entries.Count == 0) journalList.Controls.Add(UI.Lbl("Rien pour l'instant.", UI.F(9f), UI.Sub, CW));
                foreach (var en in entries)
                {
                    Color c = en.Level == 2 ? UI.Fg(IconFactory.Red) : (en.Level == 1 ? UI.Fg(IconFactory.Orange) : UI.Text);
                    var l = UI.Lbl(en.T.ToString("dd/MM HH:mm") + "   " + en.Text, UI.F(8.5f), c, CW - UI.S(30));
                    journalList.Controls.Add(l);
                }
                journalList.ResumeLayout();
            }
        }

        void StartSpeedTest()
        {
            if (speedRunning) return;
            speedRunning = true;
            bSpeed.Enabled = false;
            lSpeed.ForeColor = UI.Text;
            lSpeed.Text = "Connexion au serveur…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result;
                long total = 0;
                double sec = 0;
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("https://speed.cloudflare.com/__down?bytes=250000000");
                    req.Timeout = 8000;
                    req.ReadWriteTimeout = 8000;
                    req.UserAgent = "Ethersio/" + AppInfo.Version;
                    var resp = req.GetResponse();
                    var st = resp.GetResponseStream();
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var buf = new byte[1 << 16];
                        long lastUi = 0;
                        int n;
                        while ((n = st.Read(buf, 0, buf.Length)) > 0)
                        {
                            total += n;
                            long ms = sw.ElapsedMilliseconds;
                            if (ms - lastUi > 300 && ms > 0)
                            {
                                lastUi = ms;
                                string txt = "Mesure en cours…  " + Net.FormatBits(total * 8000.0 / ms) + "   (" + (ms / 1000) + " s / 10 s)";
                                UiDo(delegate { lSpeed.Text = txt; });
                            }
                            if (ms >= 10000) break;
                        }
                    }
                    finally
                    {
                        sec = sw.Elapsed.TotalSeconds;
                        try { req.Abort(); } catch { }
                        try { st.Dispose(); resp.Dispose(); } catch { }
                    }
                    double bps = total * 8 / Math.Max(0.1, sec);
                    result = "↓ " + Net.FormatBits(bps) + "     (" + (total / 1048576.0).ToString("0.#") + " Mo reçus en " + sec.ToString("0.0") + " s)";
                    app.Log("Test de débit : " + Net.FormatBits(bps) + " en réception", 0);
                }
                catch (Exception ex)
                {
                    result = total > 0 ? "Test interrompu : " + Net.FormatBits(total * 8 / Math.Max(0.1, sec)) : "Échec du test : " + ex.Message;
                }
                UiDo(delegate
                {
                    lSpeed.Text = result;
                    lSpeed.Font = UI.Semi(11f);
                    bSpeed.Enabled = true;
                    bSpeed.Text = "Relancer le test";
                    bSpeed.FitWidth();
                    speedRunning = false;
                });
            });
        }

        // ================================================================== OUTILS
        void BuildTools()
        {
            var p = pages[2];

            // --- console
            var c = new Card(CW);
            c.Title("Console réseau");
            c.Controls.Add(UI.Lbl("Cible : une IP ou un nom (8.8.8.8, google.fr, ta passerelle…)", UI.F(8.5f), UI.Sub, c.Inner));
            tTarget = UI.Box(c.Inner, "8.8.8.8");
            tTarget.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunTool("ping"); } };
            c.Controls.Add(tTarget);
            var row = UI.Row(c.Inner);
            foreach (var tool in new[] { "ping", "tracert", "nslookup" })
            {
                string t = tool;
                var b = new FlatBtn(t == "ping" ? UI.GPlay : UI.GCmd, t) { Primary = t == "ping" };
                b.Click += delegate { RunTool(t); };
                row.Controls.Add(b);
            }
            var bStop = new FlatBtn(UI.GStop, "Stop");
            bStop.Click += delegate { StopConsole(); AppendOut("\r\n[arrêté]\r\n"); };
            row.Controls.Add(bStop);
            c.Controls.Add(row);
            tOut = UI.Output(c.Inner, UI.S(170));
            c.Controls.Add(tOut);
            p.Controls.Add(c);

            // --- test de port
            var cp = new Card(CW);
            cp.Title("Test de port TCP");
            var r1 = UI.Row(cp.Inner);
            tHost = UI.Box(UI.S(140));
            tPort = UI.Box(UI.S(58), "22");
            var colon = UI.Lbl(":", UI.Semi(11f), UI.Sub, 40);
            colon.Margin = new Padding(0, UI.S(4), UI.S(4), 0);
            var bTest = new FlatBtn(UI.GPlay, "Tester") { Primary = true };
            bTest.Click += delegate { TestPort(); };
            tPort.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; TestPort(); } };
            r1.Controls.Add(tHost); r1.Controls.Add(colon); r1.Controls.Add(tPort); r1.Controls.Add(bTest);
            cp.Controls.Add(r1);
            var chips = UI.Row(cp.Inner);
            foreach (var pp in new[] { "22 SSH", "53 DNS", "80 HTTP", "443 HTTPS", "445 SMB", "3389 RDP", "3306 MySQL" })
            {
                string port = pp.Split(' ')[0];
                var b = new FlatBtn(null, pp) { Height = UI.S(26), Font = UI.F(8.5f) };
                b.FitWidth();
                b.Click += delegate { tPort.Text = port; TestPort(); };
                chips.Controls.Add(b);
            }
            cp.Controls.Add(chips);
            lPort = UI.Lbl("Vérifie si un service répond (SSH de ta VM Debian, RDP, site web…).", UI.F(9f), UI.Sub, cp.Inner);
            cp.Controls.Add(lPort);
            p.Controls.Add(cp);

            // --- calculateur
            var cc = new Card(CW);
            cc.Title("Calculateur IP / sous-réseaux");
            var r2 = UI.Row(cc.Inner);
            tCalc = UI.Box(UI.S(200), "192.168.10.50/24");
            var bCalc = new FlatBtn(UI.GPlay, "Calculer") { Primary = true };
            bCalc.Click += delegate { Calculate(); };
            tCalc.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Calculate(); } };
            r2.Controls.Add(tCalc); r2.Controls.Add(bCalc);
            cc.Controls.Add(r2);
            var r3 = UI.Row(cc.Inner);
            var l1 = UI.Lbl("Découper en", UI.F(9f), UI.Sub, 200);
            l1.Margin = new Padding(0, UI.S(6), UI.S(6), 0);
            tSplit = UI.Box(UI.S(46), "4");
            var l2 = UI.Lbl("sous-réseaux (laisser 1 pour ne pas découper)", UI.F(9f), UI.Sub, cc.Inner - UI.S(160));
            l2.Margin = new Padding(0, UI.S(6), 0, 0);
            r3.Controls.Add(l1); r3.Controls.Add(tSplit); r3.Controls.Add(l2);
            cc.Controls.Add(r3);
            tCalcOut = UI.Output(cc.Inner, UI.S(200));
            cc.Controls.Add(tCalcOut);
            p.Controls.Add(cc);

            // --- ARP
            var ca = new Card(CW);
            ca.Title("Appareils vus (table ARP)");
            ca.Controls.Add(UI.Lbl("Lecture du cache ARP de Windows : les machines avec qui ton PC a parlé récemment. Aucun scan du réseau.",
                                   UI.F(8.5f), UI.Sub, ca.Inner));
            var bArp = new FlatBtn(UI.GRefresh, "Actualiser");
            bArp.Click += delegate { LoadArp(); };
            ca.Controls.Add(bArp);
            tArp = UI.Output(ca.Inner, UI.S(150));
            ca.Controls.Add(tArp);
            p.Controls.Add(ca);
        }

        void PrefillTools()
        {
            if (toolsPrefilled) return;
            toolsPrefilled = true;
            var s = app.Snap;
            string gw = s.Gateway.Split(',')[0].Trim();
            if (gw != "") tHost.Text = gw;
            if (s.IPv4 != "" && s.Cidr >= 0) tCalc.Text = s.IPv4 + "/" + s.Cidr;
            Calculate();
            LoadArp();
        }

        static readonly Regex SafeTarget = new Regex(@"^[A-Za-z0-9][A-Za-z0-9.\-:]{0,252}$");

        void RunTool(string tool)
        {
            string target = tTarget.Text.Trim();
            if (!SafeTarget.IsMatch(target)) { tOut.Text = "Cible invalide : mets une adresse IP ou un nom de domaine."; return; }
            string args = tool == "ping" ? "-n 4 " + target : (tool == "tracert" ? "-d -h 20 -w 800 " + target : target);
            StopConsole();
            tOut.Clear();
            AppendOut("> " + tool + " " + args + "\r\n");
            try
            {
                var psi = new ProcessStartInfo(tool, args)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                    StandardOutputEncoding = Extra.Oem, StandardErrorEncoding = Extra.Oem
                };
                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) AppendOut(e.Data + "\r\n"); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendOut(e.Data + "\r\n"); };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                consoleProc = proc;
            }
            catch (Exception ex) { AppendOut(ex.Message); }
        }

        void AppendOut(string s)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { UiDo(delegate { AppendOut(s); }); return; }
            tOut.AppendText(s);
        }

        void StopConsole()
        {
            var p = consoleProc;
            consoleProc = null;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch { }
        }

        void TestPort()
        {
            string host = tHost.Text.Trim();
            int port;
            if (!SafeTarget.IsMatch(host)) { SetPort("Indique une IP ou un nom de machine.", IconFactory.Orange); return; }
            if (!int.TryParse(tPort.Text.Trim(), out port) || port < 1 || port > 65535) { SetPort("Port invalide (1 à 65535).", IconFactory.Orange); return; }
            SetPort("Test de " + host + ":" + port + "…", Color.Empty);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string res; Color col;
                var sw = Stopwatch.StartNew();
                try
                {
                    using (var cl = new TcpClient())
                    {
                        var ar = cl.BeginConnect(host, port, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(2500))
                        {
                            res = "⏱  Pas de réponse en 2,5 s sur le port " + port + " : port filtré par un pare-feu, ou machine éteinte.";
                            col = IconFactory.Orange;
                        }
                        else
                        {
                            cl.EndConnect(ar);
                            res = "✔  Port " + port + " OUVERT sur " + host + "  (" + sw.ElapsedMilliseconds + " ms) : un service écoute.";
                            col = IconFactory.Green;
                        }
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.ConnectionRefused)
                    { res = "✖  Port " + port + " FERMÉ : la machine répond, mais aucun service n'écoute sur ce port."; col = IconFactory.Red; }
                    else if (ex.SocketErrorCode == SocketError.HostNotFound || ex.SocketErrorCode == SocketError.NoData)
                    { res = "✖  Nom introuvable : le DNS ne connaît pas « " + host + " »."; col = IconFactory.Red; }
                    else { res = "✖  " + ex.Message; col = IconFactory.Red; }
                }
                catch (Exception ex) { res = "✖  " + ex.Message; col = IconFactory.Red; }
                UiDo(delegate { SetPort(res, col); });
            });
        }

        void SetPort(string text, Color c)
        {
            lPort.Text = text;
            lPort.ForeColor = c == Color.Empty ? UI.Sub : UI.Fg(c);
        }

        void Calculate()
        {
            string input = tCalc.Text.Trim();
            var parts = input.Split(new[] { '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            uint ip, mask;
            if (parts.Length < 2 || !IpTools.TryV4(parts[0], out ip))
            { tCalcOut.Text = "Format attendu : 192.168.10.50/24  ou  192.168.10.50 255.255.255.0"; return; }
            string m = IpTools.NormalizeMask(parts[1]);
            if (m == null || !IpTools.TryV4(m, out mask)) { tCalcOut.Text = "Masque invalide."; return; }

            int cidr = IpTools.MaskToCidr(mask);
            uint net = ip & mask, bc = net | ~mask;
            long hosts = cidr >= 31 ? (cidr == 31 ? 2 : 1) : ((long)1 << (32 - cidr)) - 2;
            var sb = new StringBuilder();
            sb.AppendLine("Adresse      " + IpTools.ToV4(ip));
            sb.AppendLine("Masque       " + m + "  (/" + cidr + ")");
            sb.AppendLine("Wildcard     " + IpTools.ToV4(~mask));
            sb.AppendLine("Réseau       " + IpTools.ToV4(net));
            sb.AppendLine("Broadcast    " + IpTools.ToV4(bc));
            sb.AppendLine("1er hôte     " + (cidr >= 31 ? IpTools.ToV4(net) : IpTools.ToV4(net + 1)));
            sb.AppendLine("Dernier hôte " + (cidr >= 31 ? IpTools.ToV4(bc) : IpTools.ToV4(bc - 1)));
            sb.AppendLine("Nb d'hôtes   " + hosts.ToString("N0"));
            sb.AppendLine("Type         " + Kind(ip) + " · classe " + ClassOf(ip));

            int n;
            if (int.TryParse(tSplit.Text.Trim(), out n) && n > 1)
            {
                int bits = 0;
                while ((1 << bits) < n) bits++;
                int nc = cidr + bits;
                sb.AppendLine();
                if (nc > 30) sb.AppendLine("Impossible : il faudrait un /" + nc + " (trop petit pour des machines).");
                else
                {
                    uint size = 1u << (32 - nc);
                    int count = 1 << bits;
                    sb.AppendLine("Découpage en " + count + " sous-réseaux /" + nc + "  (masque " + IpTools.ToV4(IpTools.CidrToMask(nc)) +
                                  ", " + (size - 2) + " hôtes chacun)");
                    if (count != n) sb.AppendLine("(" + n + " demandés → arrondi à " + count + ", une puissance de 2)");
                    sb.AppendLine("n°  réseau             plage des hôtes   (broadcast = dernière + 1)");
                    for (int i = 0; i < Math.Min(count, 64); i++)
                    {
                        uint sn = net + (uint)i * size, sbc = sn + size - 1;
                        sb.AppendLine(string.Format("#{0,-3}{1,-19}{2} - {3}", i + 1, IpTools.ToV4(sn) + "/" + nc,
                                      IpTools.ToV4(sn + 1), IpTools.ToV4(sbc - 1)));
                    }
                    if (count > 64) sb.AppendLine("… (" + (count - 64) + " autres)");
                }
            }
            tCalcOut.Text = sb.ToString().TrimEnd();
        }

        static string Kind(uint ip)
        {
            uint a = ip >> 24;
            if (a == 10 || (ip & 0xFFF00000u) == 0xAC100000u || (ip & 0xFFFF0000u) == 0xC0A80000u) return "privée (RFC 1918)";
            if ((ip & 0xFFC00000u) == 0x64400000u) return "CGNAT (100.64.0.0/10)";
            if ((ip & 0xFFFF0000u) == 0xA9FE0000u) return "APIPA / lien local";
            if (a == 127) return "loopback";
            if (a >= 224 && a < 240) return "multicast";
            if (a >= 240 || a == 0) return "réservée";
            return "publique";
        }

        static string ClassOf(uint ip)
        {
            uint a = ip >> 24;
            return a < 128 ? "A" : a < 192 ? "B" : a < 224 ? "C" : a < 240 ? "D" : "E";
        }

        void LoadArp()
        {
            tArp.Text = "Lecture…";
            string iface = app.Snap.IPv4;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var list = Extra.ReadArp(iface);
                var sb = new StringBuilder();
                sb.AppendLine(string.Format("{0,-17}{1,-19}{2}", "Adresse IP", "Adresse MAC", "Type"));
                foreach (var e in list) sb.AppendLine(string.Format("{0,-17}{1,-19}{2}", e[0], e[1], e[2]));
                sb.AppendLine();
                sb.Append(list.Count + " entrée(s) sur la carte " + (iface == "" ? "?" : iface));
                string txt = sb.ToString();
                UiDo(delegate { tArp.Text = txt; });
            });
        }

        public void SelectTab(int i) { tabs.Selected = i; }

        public void Save(string path)
        {
            using (var bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        // ================================================================== rafraichissement
        public void RefreshData()
        {
            var s = app.Snap;
            string title;
            switch (s.State)
            {
                case NetState.Dhcp: title = "Automatique (DHCP)"; break;
                case NetState.Static: title = "IP fixe (manuel)"; break;
                case NetState.Apipa: title = "Pas de réponse DHCP"; break;
                default: title = "Pas de réseau"; break;
            }
            var inet = app.InternetState();
            string sub = (s.Name == "" ? "aucune carte" : s.Name) + "  ·  " +
                         (inet == NetCheck.Ok ? "internet OK" : inet == NetCheck.Fail ? "pas d'internet" : "test en cours…");
            header.Set(title, sub, s.State, inet == NetCheck.Fail);

            if (tabs.Selected == 0) RefreshInfo();
            else if (tabs.Selected == 1) RefreshDiag();
            else PrefillTools();
        }
    }
}

// Ethersio - profils IP enregistres (Maison / Lycee / TP...)
// Stockes dans %APPDATA%\Ethersio\profils.ini (chaque compte Windows a ses propres profils).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace Ethersio
{
    class IpProfile
    {
        public string Name = "", Ip = "", Mask = "", Gateway = "", Dns1 = "", Dns2 = "";
        public bool Dhcp = true;

        public IpProfile Clone() { return (IpProfile)MemberwiseClone(); }

        public override string ToString()
        {
            return (Name == "" ? "(sans nom)" : Name) + (Dhcp ? "   DHCP" : "   " + Ip);
        }
    }

    // ------------------------------------------------------------------ outils IPv4
    static class IpTools
    {
        public static bool TryV4(string s, out uint value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.Split('.').Length != 4) return false; // IPAddress.TryParse accepte "10" sinon...
            IPAddress a;
            if (!IPAddress.TryParse(s, out a) || a.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = a.GetAddressBytes();
            value = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
            return true;
        }

        public static string ToV4(uint v)
        {
            return ((v >> 24) & 255) + "." + ((v >> 16) & 255) + "." + ((v >> 8) & 255) + "." + (v & 255);
        }

        public static uint CidrToMask(int cidr)
        {
            return cidr <= 0 ? 0u : (cidr >= 32 ? 0xFFFFFFFFu : 0xFFFFFFFFu << (32 - cidr));
        }

        // Accepte "255.255.255.0", "24" ou "/24". Renvoie le masque en notation pointee, ou null si invalide.
        public static string NormalizeMask(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().TrimStart('/');
            int cidr;
            if (!s.Contains(".") && int.TryParse(s, out cidr))
                return cidr >= 1 && cidr <= 32 ? ToV4(CidrToMask(cidr)) : null;
            uint m;
            if (!TryV4(s, out m)) return null;
            uint inv = ~m;
            if ((inv & (inv + 1)) != 0 || m == 0) return null; // les 1 doivent etre contigus
            return ToV4(m);
        }

        public static int MaskToCidr(uint m)
        {
            int c = 0;
            while ((m & 0x80000000u) != 0) { c++; m <<= 1; }
            return c;
        }

        // Analyse pedagogique d'un profil : erreurs bloquantes, avertissements, infos.
        public static void Analyze(IpProfile p, out List<string> errors, out List<string> warnings, out string info)
        {
            errors = new List<string>();
            warnings = new List<string>();
            info = "";
            if (p.Name.Trim() == "") errors.Add("Donne un nom au profil.");
            if (p.Dhcp)
            {
                info = "IP, masque, passerelle et DNS seront fournis automatiquement par le serveur DHCP.";
                return;
            }

            uint ip, mask = 0, gw, d;
            bool ipOk = TryV4(p.Ip, out ip);
            string m = NormalizeMask(p.Mask);
            bool maskOk = m != null && TryV4(m, out mask);

            if (!ipOk) errors.Add("Adresse IP invalide (ex : 192.168.10.50).");
            if (!maskOk) errors.Add("Masque invalide (ex : 255.255.255.0 ou 24).");

            if (ipOk)
            {
                if ((ip >> 24) == 127) errors.Add("127.x.x.x est réservé au loopback.");
                if ((ip >> 24) >= 224) errors.Add("Adresse multicast / réservée, pas utilisable pour une machine.");
                if ((ip >> 16) == 0xA9FE) warnings.Add("169.254.x.x est la plage APIPA : à éviter en IP fixe.");
            }

            if (ipOk && maskOk)
            {
                int cidr = MaskToCidr(mask);
                uint net = ip & mask, bc = net | ~mask;
                long hosts = cidr >= 31 ? (cidr == 31 ? 2 : 1) : ((long)1 << (32 - cidr)) - 2;
                info = "Réseau " + ToV4(net) + "/" + cidr + "  ·  broadcast " + ToV4(bc) + "\n" +
                       "Plage : " + (cidr >= 31 ? ToV4(net) : ToV4(net + 1)) + " → " + (cidr >= 31 ? ToV4(bc) : ToV4(bc - 1)) +
                       "  (" + hosts.ToString("N0") + " hôtes)";
                if (cidr < 31 && ip == net) errors.Add("C'est l'adresse du RÉSEAU, pas une adresse de machine.");
                if (cidr < 31 && ip == bc) errors.Add("C'est l'adresse de BROADCAST, pas une adresse de machine.");

                if (p.Gateway.Trim() == "") warnings.Add("Sans passerelle : pas d'accès en dehors du réseau local (ni internet).");
                else if (!TryV4(p.Gateway, out gw)) errors.Add("Passerelle invalide.");
                else
                {
                    if ((gw & mask) != net) errors.Add("La passerelle " + ToV4(gw) + " n'est pas dans le réseau " + ToV4(net) + "/" + cidr + ".");
                    else if (gw == ip) errors.Add("La passerelle ne peut pas être ta propre IP.");
                    else if (cidr < 31 && (gw == net || gw == bc)) errors.Add("La passerelle ne peut pas être l'adresse réseau ou broadcast.");
                }
            }

            if (p.Dns1.Trim() == "") warnings.Add("Sans DNS : les noms (google.fr...) ne seront pas résolus.");
            else if (!TryV4(p.Dns1, out d)) errors.Add("DNS préféré invalide.");
            if (p.Dns2.Trim() != "" && !TryV4(p.Dns2, out d)) errors.Add("DNS auxiliaire invalide.");
        }

        // Commande netsh complete pour appliquer un profil sur une carte
        public static string NetshCommand(IpProfile p, string card)
        {
            string n = "name=\"" + card.Replace("\"", "") + "\"";
            if (p.Dhcp)
                return "netsh interface ipv4 set address " + n + " source=dhcp & " +
                       "netsh interface ipv4 set dnsservers " + n + " source=dhcp & exit /b 0";

            string gw = p.Gateway.Trim() == "" ? "none" : p.Gateway.Trim();
            string cmd = "netsh interface ipv4 set address " + n + " source=static address=" + p.Ip.Trim() +
                         " mask=" + NormalizeMask(p.Mask) + " gateway=" + gw;
            cmd += " && netsh interface ipv4 set dnsservers " + n + " source=static address=" +
                   (p.Dns1.Trim() == "" ? "none" : p.Dns1.Trim()) + " register=primary validate=no";
            if (p.Dns1.Trim() != "" && p.Dns2.Trim() != "")
                cmd += " && netsh interface ipv4 add dnsservers " + n + " address=" + p.Dns2.Trim() + " index=2 validate=no";
            return cmd;
        }

        public static bool Matches(IpProfile p, NetSnapshot s)
        {
            if (s.Name == "") return false;
            if (p.Dhcp) return s.Dhcp && !s.DnsManual;
            return !s.Dhcp && p.Ip.Trim() == s.IPv4 && NormalizeMask(p.Mask) == s.Mask;
        }
    }

    // ------------------------------------------------------------------ fichier profils.ini
    static class ProfileStore
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ethersio");
        public static readonly string FilePath = Path.Combine(Dir, "profils.ini");
        // profils crees par l'ancien nom du projet (IpCheckSIO), repris au premier lancement
        static readonly string LegacyFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IpCheckSIO", "profils.ini");

        public static List<IpProfile> Load()
        {
            var list = new List<IpProfile>();
            if (!File.Exists(FilePath) && File.Exists(LegacyFilePath))
            {
                try { Directory.CreateDirectory(Dir); File.Copy(LegacyFilePath, FilePath); } catch { }
            }
            if (!File.Exists(FilePath))
            {
                list.Add(new IpProfile { Name = "Maison / Lycée", Dhcp = true });
                list.Add(new IpProfile
                {
                    Name = "TP - exemple", Dhcp = false, Ip = "192.168.10.50", Mask = "255.255.255.0",
                    Gateway = "192.168.10.254", Dns1 = "192.168.10.1", Dns2 = "1.1.1.1"
                });
                try { Save(list); } catch { }
                return list;
            }

            IpProfile cur = null;
            foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line == "" || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    cur = new IpProfile { Name = line.Substring(1, line.Length - 2).Trim() };
                    list.Add(cur);
                    continue;
                }
                if (cur == null) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "mode": cur.Dhcp = v.ToLowerInvariant() != "manuel" && v.ToLowerInvariant() != "static"; break;
                    case "ip": cur.Ip = v; break;
                    case "masque": case "mask": cur.Mask = NormalizeOr(v); break;
                    case "passerelle": case "gateway": cur.Gateway = v; break;
                    case "dns1": cur.Dns1 = v; break;
                    case "dns2": cur.Dns2 = v; break;
                }
            }
            return list;
        }

        static string NormalizeOr(string v)
        {
            string m = IpTools.NormalizeMask(v);
            return m ?? v;
        }

        public static void Save(List<IpProfile> list)
        {
            Directory.CreateDirectory(Dir);
            var sb = new StringBuilder();
            sb.AppendLine("; Profils IP d'Ethersio - modifiable a la main");
            sb.AppendLine("; mode = dhcp ou manuel   |   masque = 255.255.255.0 ou 24");
            sb.AppendLine();
            foreach (var p in list)
            {
                sb.AppendLine("[" + p.Name.Replace("[", "(").Replace("]", ")") + "]");
                sb.AppendLine("mode=" + (p.Dhcp ? "dhcp" : "manuel"));
                if (!p.Dhcp)
                {
                    sb.AppendLine("ip=" + p.Ip.Trim());
                    sb.AppendLine("masque=" + NormalizeOr(p.Mask.Trim()));
                    sb.AppendLine("passerelle=" + p.Gateway.Trim());
                    sb.AppendLine("dns1=" + p.Dns1.Trim());
                    sb.AppendLine("dns2=" + p.Dns2.Trim());
                }
                sb.AppendLine();
            }
            File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
        }
    }

    // ------------------------------------------------------------------ editeur de profils
    class ProfilesForm : Form
    {
        readonly List<IpProfile> profiles;
        readonly Func<NetSnapshot> getSnapshot;
        readonly Action<IpProfile> applyRequested;
        readonly Action saved;

        readonly ListBox list;
        readonly TextBox tName, tIp, tMask, tGw, tDns1, tDns2;
        readonly RadioButton rDhcp, rStatic;
        readonly Label lInfo;
        readonly Button bApply;
        bool loading, dirty;
        readonly float k;

        int S(int v) { return (int)(v * k); }

        public ProfilesForm(Func<NetSnapshot> getSnapshot, Action<IpProfile> applyRequested, Action saved)
        {
            this.getSnapshot = getSnapshot;
            this.applyRequested = applyRequested;
            this.saved = saved;
            profiles = ProfileStore.Load();

            using (var g = CreateGraphics()) k = g.DpiX / 96f;

            Text = "Profils IP - Ethersio";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            BackColor = UI.Bg;
            ForeColor = UI.Text;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ShowIcon = false;

            var root = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(10) };

            // --- colonne gauche : liste
            var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
            left.Controls.Add(new Label { Text = "Mes profils", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
            list = new ListBox
            {
                Width = S(220), Height = S(250), IntegralHeight = false, BackColor = UI.Card, ForeColor = UI.Text,
                BorderStyle = BorderStyle.FixedSingle, Font = UI.F(9.5f)
            };
            list.HandleCreated += delegate { UI.DarkScroll(list); };
            left.Controls.Add(list);
            var lb = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var bNew = Btn("Nouveau");
            var bDel = Btn("Supprimer");
            lb.Controls.Add(bNew);
            lb.Controls.Add(bDel);
            left.Controls.Add(lb);
            var bCurrent = Btn("Copier la config actuelle");
            left.Controls.Add(bCurrent);

            // --- colonne droite : champs
            var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(12, 0, 0, 0) };
            var fields = new TableLayoutPanel { ColumnCount = 2, AutoSize = true };
            tName = AddField(fields, "Nom");
            var modes = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            rDhcp = new RadioButton { Text = "Automatique (DHCP)", AutoSize = true };
            rStatic = new RadioButton { Text = "Manuel (IP fixe)", AutoSize = true };
            modes.Controls.Add(rDhcp);
            modes.Controls.Add(rStatic);
            AddRow(fields, "Mode", modes);
            tIp = AddField(fields, "Adresse IP");
            tMask = AddField(fields, "Masque");
            tGw = AddField(fields, "Passerelle");
            tDns1 = AddField(fields, "DNS préféré");
            tDns2 = AddField(fields, "DNS auxiliaire");
            right.Controls.Add(fields);

            lInfo = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(S(360), 0),
                MinimumSize = new Size(S(360), S(70)),
                Padding = new Padding(8),
                Margin = new Padding(3, 8, 3, 8)
            };
            right.Controls.Add(lInfo);

            var rb = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var bSave = Btn("Enregistrer");
            var snap = getSnapshot();
            bApply = Btn("Appliquer à « " + (snap.Name == "" ? "?" : snap.Name) + " » 🛡");
            bApply.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            bApply.BackColor = UI.Accent;
            bApply.ForeColor = UI.AccentText;
            bApply.FlatAppearance.BorderColor = UI.Accent;
            rb.Controls.Add(bApply);
            rb.Controls.Add(bSave);
            right.Controls.Add(rb);

            var file = new LinkLabel
            {
                Text = "Ouvrir profils.ini dans le Bloc-notes", AutoSize = true, Margin = new Padding(3, 8, 3, 0),
                LinkColor = UI.Accent, ActiveLinkColor = UI.Accent
            };
            file.LinkClicked += delegate
            {
                try { if (dirty) DoSave(); Process.Start("notepad.exe", "\"" + ProfileStore.FilePath + "\""); } catch { }
            };
            right.Controls.Add(file);

            root.Controls.Add(left, 0, 0);
            root.Controls.Add(right, 1, 0);
            Controls.Add(root);

            // --- evenements
            list.SelectedIndexChanged += delegate { if (!loading) LoadSelected(); };
            foreach (var t in new[] { tName, tIp, tMask, tGw, tDns1, tDns2 }) t.TextChanged += delegate { FieldsChanged(); };
            rDhcp.CheckedChanged += delegate { FieldsChanged(); };
            rStatic.CheckedChanged += delegate { FieldsChanged(); };

            bNew.Click += delegate
            {
                AddProfile(new IpProfile { Name = "Nouveau profil", Dhcp = false, Mask = "255.255.255.0" });
                tName.Focus();
                tName.SelectAll();
            };
            bDel.Click += delegate
            {
                int i = list.SelectedIndex;
                if (i < 0) return;
                if (MessageBox.Show("Supprimer le profil « " + profiles[i].Name + " » ?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                profiles.RemoveAt(i);
                list.Items.RemoveAt(i);
                dirty = true;
                if (list.Items.Count > 0) list.SelectedIndex = Math.Min(i, list.Items.Count - 1);
                else LoadSelected();
            };
            bCurrent.Click += delegate { AddProfile(FromSnapshot(getSnapshot())); };
            bSave.Click += delegate { DoSave(); };
            bApply.Click += delegate { Apply(); };

            foreach (var p in profiles) list.Items.Add(p);
            if (list.Items.Count > 0) list.SelectedIndex = 0; else LoadSelected();
        }

        Button Btn(string text)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, Margin = new Padding(3), Padding = new Padding(6, 3, 6, 3),
                FlatStyle = FlatStyle.Flat, BackColor = UI.Card, ForeColor = UI.Text, Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = UI.Border;
            b.FlatAppearance.MouseOverBackColor = UI.CardHover;
            return b;
        }

        TextBox AddField(TableLayoutPanel t, string label)
        {
            var tb = new TextBox { Width = S(200), BackColor = UI.InputBg, ForeColor = UI.Text, BorderStyle = BorderStyle.FixedSingle, Font = UI.F(9.5f) };
            AddRow(t, label, tb);
            return tb;
        }

        void AddRow(TableLayoutPanel t, string label, Control c)
        {
            int i = t.RowCount++;
            t.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = UI.Sub, Margin = new Padding(0, 7, 8, 0) }, 0, i);
            t.Controls.Add(c, 1, i);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UI.DarkTitle(this);
        }

        static IpProfile FromSnapshot(NetSnapshot s)
        {
            var dns = s.Dns.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            return new IpProfile
            {
                Name = "Config actuelle " + DateTime.Now.ToString("dd-MM HH'h'mm"),
                Dhcp = s.Dhcp,
                Ip = s.IPv4,
                Mask = s.Mask,
                Gateway = s.Gateway.Split(',')[0].Trim(),
                Dns1 = dns.Length > 0 ? dns[0] : "",
                Dns2 = dns.Length > 1 ? dns[1] : ""
            };
        }

        void AddProfile(IpProfile p)
        {
            profiles.Add(p);
            list.Items.Add(p);
            dirty = true;
            list.SelectedIndex = list.Items.Count - 1;
        }

        IpProfile Current
        {
            get { int i = list.SelectedIndex; return i >= 0 && i < profiles.Count ? profiles[i] : null; }
        }

        void LoadSelected()
        {
            var p = Current;
            loading = true;
            tName.Text = p == null ? "" : p.Name;
            rDhcp.Checked = p != null && p.Dhcp;
            rStatic.Checked = p != null && !p.Dhcp;
            tIp.Text = p == null ? "" : p.Ip;
            tMask.Text = p == null ? "" : p.Mask;
            tGw.Text = p == null ? "" : p.Gateway;
            tDns1.Text = p == null ? "" : p.Dns1;
            tDns2.Text = p == null ? "" : p.Dns2;
            loading = false;
            UpdateState();
        }

        void FieldsChanged()
        {
            if (loading) return;
            var p = Current;
            if (p == null) return;
            p.Name = tName.Text;
            p.Dhcp = rDhcp.Checked;
            p.Ip = tIp.Text;
            p.Mask = tMask.Text;
            p.Gateway = tGw.Text;
            p.Dns1 = tDns1.Text;
            p.Dns2 = tDns2.Text;
            dirty = true;
            loading = true;
            list.Items[list.SelectedIndex] = p; // rafraichit le texte de la liste
            loading = false;
            UpdateState();
        }

        void UpdateState()
        {
            var p = Current;
            bool has = p != null;
            foreach (Control c in new Control[] { tName, rDhcp, rStatic, bApply }) c.Enabled = has;
            bool st = has && !p.Dhcp;
            foreach (var t in new[] { tIp, tMask, tGw, tDns1, tDns2 }) t.Enabled = st;
            if (!has) { lInfo.Text = "Crée un profil avec « Nouveau » ou « Copier la config actuelle »."; lInfo.BackColor = UI.Card; return; }

            List<string> errors, warnings;
            string info;
            IpTools.Analyze(p, out errors, out warnings, out info);
            var sb = new StringBuilder();
            if (info != "") sb.AppendLine(info);
            foreach (var e in errors) sb.AppendLine("✖ " + e);
            foreach (var w in warnings) sb.AppendLine("⚠ " + w);
            if (errors.Count == 0 && warnings.Count == 0 && !p.Dhcp) sb.AppendLine("✔ Configuration cohérente.");
            lInfo.Text = sb.ToString().TrimEnd();
            lInfo.BackColor = errors.Count > 0 ? UI.Soft(IconFactory.Red)
                            : warnings.Count > 0 ? UI.Soft(IconFactory.Orange)
                            : UI.Soft(IconFactory.Green);
        }

        bool DoSave()
        {
            try
            {
                ProfileStore.Save(profiles);
                dirty = false;
                if (saved != null) saved();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible d'enregistrer : " + ex.Message, Text);
                return false;
            }
        }

        void Apply()
        {
            var p = Current;
            if (p == null) return;
            List<string> errors, warnings;
            string info;
            IpTools.Analyze(p, out errors, out warnings, out info);
            if (errors.Count > 0)
            {
                MessageBox.Show("Corrige d'abord :\n\n• " + string.Join("\n• ", errors.ToArray()), Text,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!DoSave()) return;
            if (applyRequested != null) applyRequested(p.Clone());
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (dirty && e.CloseReason == CloseReason.UserClosing)
            {
                var r = MessageBox.Show("Enregistrer les modifications des profils ?", Text,
                                        MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes && !DoSave()) { e.Cancel = true; return; }
            }
            base.OnFormClosing(e);
        }
    }
}

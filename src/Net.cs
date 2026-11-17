// Ethersio - lecture des infos reseau, outils systeme et icones
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ethersio
{
    enum NetState { NoNetwork, Dhcp, Static, Apipa }
    enum NetCheck { Unknown, Ok, Fail }

    class NetSnapshot
    {
        public string Id = "", Name = "", Description = "", Type = "", Mac = "";
        public bool Up, Dhcp, Apipa, DnsManual, Virtual, Wireless;
        public string IPv4 = "", Mask = "", Gateway = "", Dns = "", DhcpServer = "";
        public int Cidr = -1;
        public List<string> IPv6 = new List<string>();
        public bool IPv6Global;
        public long SpeedBps;
        public long Rx, Tx;
        public long LeaseRemaining = -1, LeaseTotal = -1; // secondes
        public NetState State;
    }

    // ------------------------------------------------------------------ lecture reseau
    static class Net
    {
        static readonly string[] VirtualHints = {
            "virtual", "vmware", "virtualbox", "hyper-v", "vethernet", "loopback", "tap-", "wan miniport",
            "bluetooth", "wsl", "docker", "npcap", "tailscale", "zerotier", "wireguard", "openvpn", "vpn"
        };

        public static bool IsVirtual(NetworkInterface n)
        {
            string s = (n.Description + " " + n.Name).ToLowerInvariant();
            return VirtualHints.Any(h => s.Contains(h));
        }

        public static List<NetworkInterface> Candidates()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                             && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .OrderBy(n => n.Name)
                    .ToList();
            }
            catch { return new List<NetworkInterface>(); }
        }

        // Choisit la "vraie" carte : celle qui a une passerelle et qui n'est pas virtuelle (VMware, VPN...)
        public static NetworkInterface PickBest(string preferredId)
        {
            var all = Candidates();
            if (!string.IsNullOrEmpty(preferredId))
            {
                var p = all.FirstOrDefault(n => n.Id == preferredId);
                if (p != null) return p;
            }
            NetworkInterface best = null;
            int bestScore = int.MinValue;
            foreach (var n in all)
            {
                int score = 0;
                IPInterfaceProperties props;
                try { props = n.GetIPProperties(); } catch { continue; }
                if (n.OperationalStatus == OperationalStatus.Up) score += 40;
                if (props.UnicastAddresses.Any(u => u.Address.AddressFamily == AddressFamily.InterNetwork)) score += 5;
                if (props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                 && !g.Address.Equals(IPAddress.Any))) score += 20;
                if (!IsVirtual(n)) score += 30; else score -= 30;
                if (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 3;
                if (n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 2;
                if (score > bestScore) { bestScore = score; best = n; }
            }
            return best;
        }

        public static NetSnapshot Read(NetworkInterface n)
        {
            var s = new NetSnapshot();
            if (n == null) { s.State = NetState.NoNetwork; return s; }

            s.Id = n.Id;
            s.Name = n.Name;
            s.Description = n.Description;
            s.Type = TypeName(n.NetworkInterfaceType);
            s.Wireless = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            s.Virtual = IsVirtual(n);
            s.Up = n.OperationalStatus == OperationalStatus.Up;
            try { s.Mac = FormatMac(n.GetPhysicalAddress()); } catch { }
            try { s.SpeedBps = n.Speed; } catch { }
            try
            {
                var st = n.GetIPv4Statistics();
                s.Rx = st.BytesReceived;
                s.Tx = st.BytesSent;
            }
            catch { }

            IPInterfaceProperties p = null;
            try { p = n.GetIPProperties(); } catch { }
            if (p != null)
            {
                try { s.Dhcp = p.GetIPv4Properties().IsDhcpEnabled; } catch { }

                var v4 = p.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                if (v4 != null)
                {
                    s.IPv4 = v4.Address.ToString();
                    try
                    {
                        if (v4.IPv4Mask != null)
                        {
                            s.Mask = v4.IPv4Mask.ToString();
                            s.Cidr = MaskToCidr(v4.IPv4Mask);
                        }
                    }
                    catch { }
                    s.Apipa = s.IPv4.StartsWith("169.254.");
                    if (s.Dhcp && !s.Apipa)
                    {
                        try
                        {
                            long rem = v4.AddressValidLifetime, tot = v4.DhcpLeaseLifetime;
                            if (rem > 0 && rem < uint.MaxValue) s.LeaseRemaining = rem;
                            if (tot > 0 && tot < uint.MaxValue) s.LeaseTotal = tot;
                        }
                        catch { }
                    }
                }

                s.Gateway = string.Join(", ", p.GatewayAddresses
                    .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))
                    .Select(g => g.Address.ToString()).ToArray());
                s.Dns = string.Join(", ", p.DnsAddresses
                    .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                    .Select(d => d.ToString()).ToArray());
                try
                {
                    s.DhcpServer = string.Join(", ", p.DhcpServerAddresses.Select(d => d.ToString()).ToArray());
                }
                catch { }

                foreach (var u in p.UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetworkV6))
                {
                    var a = u.Address;
                    string txt = a.ToString();
                    int pct = txt.IndexOf('%');
                    if (pct >= 0) txt = txt.Substring(0, pct);
                    if (a.IsIPv6LinkLocal) s.IPv6.Add(txt + "  (lien local)");
                    else
                    {
                        s.IPv6.Add(txt);
                        if (!a.IsIPv6SiteLocal) s.IPv6Global = true;
                    }
                }
            }

            s.DnsManual = IsDnsManual(n.Id);

            if (!s.Up || s.IPv4 == "") s.State = NetState.NoNetwork;
            else if (s.Apipa) s.State = NetState.Apipa;
            else if (s.Dhcp) s.State = NetState.Dhcp;
            else s.State = NetState.Static;
            return s;
        }

        // DNS saisis a la main = valeur "NameServer" non vide dans le registre (lisible sans droits admin)
        static bool IsDnsManual(string id)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + id))
                {
                    if (k == null) return false;
                    var v = k.GetValue("NameServer") as string;
                    return !string.IsNullOrEmpty(v);
                }
            }
            catch { return false; }
        }

        static int MaskToCidr(IPAddress mask)
        {
            int bits = 0;
            foreach (byte b in mask.GetAddressBytes())
                for (int i = 7; i >= 0; i--) if ((b & (1 << i)) != 0) bits++;
            return bits;
        }

        static string FormatMac(PhysicalAddress a)
        {
            var b = a.GetAddressBytes();
            if (b.Length == 0) return "";
            return string.Join("-", b.Select(x => x.ToString("X2")).ToArray());
        }

        static string TypeName(NetworkInterfaceType t)
        {
            switch (t)
            {
                case NetworkInterfaceType.Ethernet: return "Ethernet";
                case NetworkInterfaceType.Wireless80211: return "Wi-Fi";
                default: return t.ToString();
            }
        }

        public static string FormatBits(double bitsPerSecond)
        {
            if (bitsPerSecond < 0) bitsPerSecond = 0;
            if (bitsPerSecond >= 1e9) return (bitsPerSecond / 1e9).ToString("0.##") + " Gb/s";
            if (bitsPerSecond >= 1e6) return (bitsPerSecond / 1e6).ToString("0.##") + " Mb/s";
            if (bitsPerSecond >= 1e3) return (bitsPerSecond / 1e3).ToString("0.#") + " kb/s";
            return bitsPerSecond.ToString("0") + " b/s";
        }

        public static string FormatDuration(long seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalDays >= 1) return (int)t.TotalDays + " j " + t.Hours + " h";
            if (t.TotalHours >= 1) return (int)t.TotalHours + " h " + t.Minutes.ToString("00") + " min";
            return t.Minutes + " min";
        }
    }

    // ------------------------------------------------------------------ Wi-Fi, proxy, ARP, commandes
    class WifiInfo
    {
        public string Ssid = "", Signal = "", Channel = "", Band = "", Radio = "", Error = "";
    }

    static class Extra
    {
        public static Encoding Oem
        {
            get
            {
                try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch { return Encoding.Default; }
            }
        }

        public static string RunCapture(string exe, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Oem,
                    StandardErrorEncoding = Oem
                };
                using (var p = Process.Start(psi))
                {
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
                    return outTask.Result + errTask.Result;
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        // netsh wlan show interfaces (FR ou EN). Depuis Windows 11 24H2 il faut que la localisation soit activee.
        public static WifiInfo ReadWifi()
        {
            var w = new WifiInfo();
            string txt = RunCapture("netsh", "wlan show interfaces", 4000);
            foreach (var raw in txt.Split('\n'))
            {
                int i = raw.IndexOf(" : ");
                if (i < 0) continue;
                string k = raw.Substring(0, i).Trim(), v = raw.Substring(i + 3).Trim();
                string kl = k.ToLowerInvariant();
                if (kl == "ssid") w.Ssid = v;
                else if (kl.StartsWith("signal")) w.Signal = v;
                else if (kl == "canal" || kl == "channel") w.Channel = v;
                else if (kl == "bande" || kl == "band") w.Band = v;
                else if (kl.StartsWith("type de radio") || kl.StartsWith("radio type")) w.Radio = v;
            }
            if (w.Ssid == "")
            {
                string l = txt.ToLowerInvariant();
                if (l.Contains("localisation") || l.Contains("location"))
                    w.Error = "Active la localisation Windows pour voir le Wi-Fi";
                else w.Error = "non connecté";
            }
            return w;
        }

        public static string ProxyText()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    if (k == null) return "aucun";
                    var parts = new List<string>();
                    object en = k.GetValue("ProxyEnable");
                    var srv = k.GetValue("ProxyServer") as string;
                    var pac = k.GetValue("AutoConfigURL") as string;
                    if (en is int && (int)en == 1 && !string.IsNullOrEmpty(srv)) parts.Add(srv);
                    if (!string.IsNullOrEmpty(pac)) parts.Add("script PAC : " + pac);
                    return parts.Count == 0 ? "aucun" : string.Join("\n", parts.ToArray());
                }
            }
            catch { return "?"; }
        }

        static readonly Regex ArpLine = new Regex(@"^\s*(\d{1,3}(?:\.\d{1,3}){3})\s+([0-9a-fA-F]{2}(?:-[0-9a-fA-F]{2}){5})\s+(\S+)");

        public static List<string[]> ReadArp(string ifaceIp)
        {
            var list = new List<string[]>();
            string txt = RunCapture("arp", string.IsNullOrEmpty(ifaceIp) ? "-a" : "-a -N " + ifaceIp, 5000);
            foreach (var line in txt.Split('\n'))
            {
                var m = ArpLine.Match(line);
                if (!m.Success) continue;
                string mac = m.Groups[2].Value.ToLowerInvariant();
                if (mac == "ff-ff-ff-ff-ff-ff" || mac.StartsWith("01-00-5e") || mac.StartsWith("33-33")) continue;
                list.Add(new[] { m.Groups[1].Value, mac.ToUpperInvariant(), m.Groups[3].Value });
            }
            return list;
        }
    }

    // ------------------------------------------------------------------ icones
    static class IconFactory
    {
        static readonly Dictionary<string, Icon> Cache = new Dictionary<string, Icon>();

        public static readonly Color Green = Color.FromArgb(34, 154, 72);
        public static readonly Color Orange = Color.FromArgb(232, 126, 4);
        public static readonly Color Red = Color.FromArgb(205, 38, 38);
        public static readonly Color Grey = Color.FromArgb(120, 124, 130);

        public static Icon Get(NetState st, bool internetFail)
        {
            string key = st + "|" + internetFail;
            Icon ic;
            if (Cache.TryGetValue(key, out ic)) return ic;
            using (var bmp = Render(32, st, internetFail))
                ic = Icon.FromHandle(bmp.GetHicon()); // en cache : 8 icones max, jamais detruites
            Cache[key] = ic;
            return ic;
        }

        public static Color ColorOf(NetState st)
        {
            switch (st)
            {
                case NetState.Dhcp: return Green;
                case NetState.Static: return Orange;
                case NetState.Apipa: return Red;
                default: return Grey;
            }
        }

        // Tuile arrondie de couleur + prise RJ45 blanche. Dessin dans un repere 32x32 mis a l'echelle.
        public static Bitmap Render(int size, NetState st, bool internetFail)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                g.ScaleTransform(size / 32f, size / 32f);
                Color bg = ColorOf(st);

                using (var tile = RoundedRect(new RectangleF(1, 1, 30, 30), 7))
                using (var b = new SolidBrush(bg))
                    g.FillPath(b, tile);

                var port = new PointF[] {
                    new PointF(7, 8), new PointF(25, 8), new PointF(25, 20), new PointF(20.5f, 20),
                    new PointF(20.5f, 24.5f), new PointF(11.5f, 24.5f), new PointF(11.5f, 20), new PointF(7, 20)
                };
                g.FillPolygon(Brushes.White, port);
                using (var pin = new SolidBrush(bg))
                    for (int i = 0; i < 4; i++)
                        g.FillRectangle(pin, 9.6f + i * 3.6f, 8, 1.8f, 5.5f);

                if (st == NetState.NoNetwork)
                    using (var slash = new Pen(bg, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(slash, 6, 27, 27, 5);
                        using (var w = new Pen(Color.White, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(w, 6, 27, 27, 5);
                    }

                if (internetFail && st != NetState.NoNetwork)
                {
                    g.FillEllipse(Brushes.White, 18.5f, 18.5f, 13.5f, 13.5f);
                    using (var r = new SolidBrush(st == NetState.Apipa ? Color.FromArgb(60, 60, 60) : Red))
                        g.FillEllipse(r, 20.5f, 20.5f, 9.5f, 9.5f);
                }
            }
            return bmp;
        }

        public static GraphicsPath RoundedRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // .ico multi-tailles (PNG embarques) pour l'icone de l'exe
        public static void SaveIco(string path, NetState st)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 256 };
            var pngs = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Render(s, st, false))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }

        public static void SavePreview(string path)
        {
            var states = new[] { NetState.Dhcp, NetState.Static, NetState.Apipa, NetState.NoNetwork };
            using (var sheet = new Bitmap(4 * 90 + 20, 230))
            using (var g = Graphics.FromImage(sheet))
            using (var f = new Font("Segoe UI", 9f))
            {
                g.Clear(Color.FromArgb(32, 32, 32));
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                string[] names = { "DHCP", "Manuel", "APIPA", "Pas de réseau" };
                for (int i = 0; i < 4; i++)
                {
                    int x = 20 + i * 90;
                    using (var b = Render(64, states[i], false)) g.DrawImage(b, x, 10);
                    using (var b = Render(64, states[i], true)) g.DrawImage(b, x, 85);
                    using (var b = Render(16, states[i], false)) g.DrawImage(b, x, 165);
                    using (var b = Render(16, states[i], true)) g.DrawImage(b, x + 24, 165);
                    g.DrawString(names[i], f, Brushes.White, x - 4, 195);
                }
                sheet.Save(path, ImageFormat.Png);
            }
        }
    }
}

// Ethersio - theme (clair/sombre comme Windows) et controles dessines a la main
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Ethersio
{
    static class UI
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);

        public static float K = 1f;
        public static bool Dark;
        public static Color Bg, Card, CardHover, Border, Text, Sub, InputBg, Track, Accent, AccentText;
        public static readonly Color Purple = Color.FromArgb(140, 100, 230);
        public static string IconFontName = "Segoe MDL2 Assets";
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        // glyphes Segoe MDL2 / Fluent Icons
        public const string GCopy = "", GRefresh = "", GSettings = "", GGlobe = "",
            GCheck = "", GClose = "", GDownload = "", GPlay = "", GStop = "",
            GEdit = "", GSync = "", GDelete = "", GPin = "", GUnpin = "",
            GFolder = "", GInfo = "", GCmd = "";

        public static void Init()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) K = g.DpiX / 96f;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    Dark = v is int && (int)v == 0;
                }
            }
            catch { }
            ApplyPalette();
            try
            {
                using (var f = new Font("Segoe Fluent Icons", 10f))
                    if (f.Name == "Segoe Fluent Icons") IconFontName = f.Name;
            }
            catch { }
        }

        public static void ForceTheme(bool dark) { Dark = dark; ApplyPalette(); }

        static void ApplyPalette()
        {
            if (Dark)
            {
                Bg = Color.FromArgb(32, 32, 32); Card = Color.FromArgb(43, 43, 43); CardHover = Color.FromArgb(54, 54, 54);
                Border = Color.FromArgb(60, 60, 60); Text = Color.FromArgb(242, 242, 242); Sub = Color.FromArgb(160, 160, 160);
                InputBg = Color.FromArgb(30, 30, 30); Track = Color.FromArgb(62, 62, 62);
                Accent = Color.FromArgb(76, 194, 255); AccentText = Color.FromArgb(0, 0, 0);
            }
            else
            {
                Bg = Color.FromArgb(243, 243, 243); Card = Color.White; CardHover = Color.FromArgb(245, 245, 245);
                Border = Color.FromArgb(226, 226, 226); Text = Color.FromArgb(26, 26, 26); Sub = Color.FromArgb(100, 100, 100);
                InputBg = Color.White; Track = Color.FromArgb(222, 222, 222);
                Accent = Color.FromArgb(0, 95, 184); AccentText = Color.White;
            }
        }

        public static int S(float v) { return (int)Math.Round(v * K); }

        public static Font F(float size, FontStyle st = FontStyle.Regular, string family = "Segoe UI")
        {
            string key = family + size + st;
            Font f;
            if (!fonts.TryGetValue(key, out f)) { f = new Font(family, size, st); fonts[key] = f; }
            return f;
        }
        public static Font Semi(float size) { return F(size, FontStyle.Regular, "Segoe UI Semibold"); }
        public static Font Mono(float size) { return F(size, FontStyle.Regular, "Consolas"); }
        public static Font Icon(float size) { return F(size, FontStyle.Regular, IconFontName); }

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R * t + b.R * (1 - t)), (int)(a.G * t + b.G * (1 - t)), (int)(a.B * t + b.B * (1 - t)));
        }
        public static Color Soft(Color c) { return Blend(c, Card, Dark ? 0.25f : 0.12f); }
        // couleur d'etat lisible en texte (un peu plus claire en mode sombre)
        public static Color Fg(Color c) { return Dark ? Blend(c, Color.White, 0.75f) : c; }

        public static GraphicsPath Round(RectangleF r, float rad) { return IconFactory.RoundedRect(r, rad); }

        public static void DarkScroll(Control c)
        {
            if (!Dark) return;
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
        }
        public static void RoundCorners(Form f)
        {
            int v = 2; // DWMWCP_ROUND (Windows 11)
            try { DwmSetWindowAttribute(f.Handle, 33, ref v, 4); } catch { }
        }
        public static void DarkTitle(Form f)
        {
            if (!Dark) return;
            int v = 1;
            try { DwmSetWindowAttribute(f.Handle, 20, ref v, 4); } catch { }
        }

        public static Label Lbl(string text, Font f, Color fore, int maxWidth)
        {
            return new Label
            {
                Text = text, Font = f, ForeColor = fore, BackColor = Card, AutoSize = true,
                MaximumSize = new Size(maxWidth, 0), Margin = new Padding(0, S(2), 0, S(2))
            };
        }

        public static TextBox Box(int width, string text = "")
        {
            return new TextBox
            {
                Width = width, Text = text, BackColor = InputBg, ForeColor = Text,
                BorderStyle = BorderStyle.FixedSingle, Font = F(9.5f), Margin = new Padding(0, S(3), S(6), S(3))
            };
        }

        public static TextBox Output(int width, int height)
        {
            var t = new TextBox
            {
                Width = width, Height = height, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                BackColor = InputBg, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, Font = Mono(8.5f),
                WordWrap = false, Margin = new Padding(0, S(6), 0, 0)
            };
            t.HandleCreated += delegate { DarkScroll(t); };
            return t;
        }

        public static FlowLayoutPanel Row(int width)
        {
            return new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                MaximumSize = new Size(width, 0), BackColor = Card, Margin = new Padding(0, S(2), 0, S(2))
            };
        }
    }

    // ------------------------------------------------------------------ carte arrondie
    class Card : FlowLayoutPanel
    {
        Color? stripe;
        public Color? Stripe { get { return stripe; } set { stripe = value; Invalidate(); } }

        public Card(int width)
        {
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(width, 0);
            MaximumSize = new Size(width, 0);
            Padding = new Padding(UI.S(14), UI.S(10), UI.S(14), UI.S(12));
            Margin = new Padding(0, 0, 0, UI.S(10));
            BackColor = UI.Bg;
            DoubleBuffered = true;
        }

        public int Inner { get { return MaximumSize.Width - Padding.Horizontal; } }

        public Label Title(string text)
        {
            var l = UI.Lbl(text.ToUpperInvariant(), UI.Semi(8f), UI.Sub, Inner);
            l.Margin = new Padding(0, 0, 0, UI.S(4));
            Controls.Add(l);
            return l;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(UI.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = UI.Round(r, UI.S(8)))
            {
                using (var b = new SolidBrush(UI.Card)) g.FillPath(b, path);
                using (var p = new Pen(UI.Border)) g.DrawPath(p, path);
            }
            if (stripe.HasValue)
                using (var b = new SolidBrush(stripe.Value))
                using (var bar = UI.Round(new RectangleF(UI.S(5), UI.S(12), UI.S(3), Height - UI.S(24)), UI.S(1.5f)))
                    g.FillPath(b, bar);
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Invalidate(); }
    }

    // ------------------------------------------------------------------ bouton plat
    class FlatBtn : Control
    {
        public string Glyph;
        public bool Primary;
        bool hover, down;

        public FlatBtn(string glyph, string text)
        {
            Glyph = glyph ?? "";
            Text = text ?? "";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = UI.F(9f);
            Height = UI.S(30);
            Margin = new Padding(0, UI.S(3), UI.S(6), UI.S(3));
            FitWidth();
        }

        public void FitWidth()
        {
            int w = UI.S(20);
            if (Glyph != "") w += TextRenderer.MeasureText(Glyph, UI.Icon(10f)).Width;
            if (Text != "") w += TextRenderer.MeasureText(Text, Font).Width;
            if (Glyph != "" && Text != "") w -= UI.S(4);
            Width = w;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill, fore, border;
            if (Primary)
            {
                fill = hover ? UI.Blend(UI.Accent, Color.White, UI.Dark ? 0.85f : 0.9f) : UI.Accent;
                if (down) fill = UI.Blend(UI.Accent, Color.Black, 0.85f);
                fore = UI.AccentText; border = fill;
            }
            else
            {
                fill = down ? UI.Track : (hover ? UI.CardHover : UI.Blend(UI.Card, UI.Bg, UI.Dark ? 0.7f : 1f));
                if (UI.Dark && !hover && !down) fill = UI.Blend(Color.White, UI.Card, 0.06f);
                fore = UI.Text; border = UI.Border;
            }
            if (!Enabled) { fill = UI.Blend(fill, UI.Card, 0.5f); fore = UI.Sub; }

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = UI.Round(r, UI.S(5)))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(border)) g.DrawPath(p, path);
            }

            var gf = UI.Icon(10f);
            int gw = Glyph == "" ? 0 : TextRenderer.MeasureText(g, Glyph, gf, Size.Empty, TextFormatFlags.NoPadding).Width;
            int tw = Text == "" ? 0 : TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            int gap = (gw > 0 && tw > 0) ? UI.S(7) : 0;
            int x = (Width - (gw + gap + tw)) / 2;
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            if (gw > 0) TextRenderer.DrawText(g, Glyph, gf, new Rectangle(x, 0, gw + 2, Height), fore, flags);
            if (tw > 0) TextRenderer.DrawText(g, Text, Font, new Rectangle(x + gw + gap, 0, tw + 4, Height), fore, flags);
        }
    }

    // ------------------------------------------------------------------ onglets segmentes
    class Segmented : Control
    {
        readonly string[] items;
        int sel, hover = -1;
        public event EventHandler Changed;
        public int Selected { get { return sel; } set { sel = value; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); } }

        public Segmented(params string[] items)
        {
            this.items = items;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = UI.Bg;
            Font = UI.F(9.5f);
            Cursor = Cursors.Hand;
        }

        int IndexAt(int x) { return Math.Max(0, Math.Min(items.Length - 1, x * items.Length / Math.Max(1, Width))); }
        protected override void OnMouseMove(MouseEventArgs e) { int h = IndexAt(e.X); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e) { Selected = IndexAt(e.X); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(UI.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = UI.Round(outer, UI.S(7)))
            using (var b = new SolidBrush(UI.Dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(230, 230, 230)))
                g.FillPath(b, p);

            float w = (Width - UI.S(6)) / (float)items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                var r = new RectangleF(UI.S(3) + i * w, UI.S(3), w, Height - UI.S(6) - 1);
                if (i == sel)
                {
                    using (var p = UI.Round(r, UI.S(5)))
                    {
                        using (var b = new SolidBrush(UI.Card)) g.FillPath(b, p);
                        using (var pen = new Pen(UI.Border)) g.DrawPath(pen, p);
                    }
                    using (var b = new SolidBrush(UI.Accent))
                    using (var bar = UI.Round(new RectangleF(r.X + r.Width / 2 - UI.S(9), r.Bottom - UI.S(3.5f), UI.S(18), UI.S(3)), UI.S(1.5f)))
                        g.FillPath(b, bar);
                }
                var col = i == sel ? UI.Text : (i == hover ? UI.Text : UI.Sub);
                TextRenderer.DrawText(g, items[i], i == sel ? UI.Semi(9.5f) : Font, Rectangle.Round(r), col,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // ------------------------------------------------------------------ ligne cle / valeur (clic = copier)
    class KvRow : Control
    {
        readonly string key;
        string val = "";
        Color? valColor;
        public string CopyText;
        public event EventHandler Action;
        bool hover;
        int copiedUntil;
        readonly int keyW;

        public KvRow(string key, int width)
        {
            this.key = key;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Font = UI.F(9f);
            BackColor = UI.Card;
            Margin = new Padding(0);
            Width = width;
            keyW = UI.S(104);
            Recalc();
        }

        bool Clickable { get { return Action != null || !string.IsNullOrEmpty(CopyText); } }

        public void Set(string value, Color? color, string copy)
        {
            if (value == val && color == valColor && copy == CopyText) return;
            val = value ?? ""; valColor = color; CopyText = copy;
            Cursor = Clickable ? Cursors.Hand : Cursors.Default;
            Recalc();
            Invalidate();
        }

        void Recalc()
        {
            var sz = TextRenderer.MeasureText(val == "" ? " " : val, Font, new Size(Width - keyW - UI.S(22), 0),
                                              TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            int h = Math.Max(sz.Height, Font.Height) + UI.S(10);
            if (Height != h) Height = h;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Action != null && string.IsNullOrEmpty(CopyText)) { Action(this, e); return; }
            if (string.IsNullOrEmpty(CopyText)) return;
            try { Clipboard.SetText(CopyText); } catch { }
            copiedUntil = Environment.TickCount + 1200;
            Invalidate();
            var t = new Timer { Interval = 1300 };
            t.Tick += delegate { t.Stop(); t.Dispose(); Invalidate(); };
            t.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UI.Card);
            if (hover && Clickable)
                using (var p = UI.Round(new RectangleF(-UI.S(6), 0, Width + UI.S(12) - 1, Height - 1), UI.S(4)))
                using (var b = new SolidBrush(UI.CardHover)) g.FillPath(b, p);
            Color bgNow = hover && Clickable ? UI.CardHover : UI.Card;
            int y = UI.S(5);
            TextRenderer.DrawText(g, key, Font, new Rectangle(0, y, keyW, Height), UI.Sub, bgNow, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, val, Font, new Rectangle(keyW, y, Width - keyW - UI.S(22), Height),
                                  valColor ?? UI.Text, bgNow, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            var flagsR = TextFormatFlags.NoPadding | TextFormatFlags.Right;
            if (Environment.TickCount < copiedUntil)
                TextRenderer.DrawText(g, UI.GCheck, UI.Icon(9f), new Rectangle(0, y + UI.S(1), Width, Height), UI.Fg(IconFactory.Green), bgNow, flagsR);
            else if (hover && !string.IsNullOrEmpty(CopyText))
                TextRenderer.DrawText(g, UI.GCopy, UI.Icon(9f), new Rectangle(0, y + UI.S(1), Width, Height), UI.Sub, bgNow, flagsR);
        }
    }

    // ------------------------------------------------------------------ grande IP en haut
    class HeroIp : Control
    {
        string ip = "", sub = "", chip = "";
        Color chipColor = Color.Gray;
        bool hover;
        int copiedUntil;

        public HeroIp(int width)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Width = width;
            Height = UI.S(66);
            BackColor = UI.Card;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, 0, UI.S(6));
        }

        public void Set(string ip, string sub, string chip, Color chipColor)
        {
            if (ip == this.ip && sub == this.sub && chip == this.chip && chipColor == this.chipColor) return;
            this.ip = ip; this.sub = sub; this.chip = chip; this.chipColor = chipColor;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnClick(EventArgs e)
        {
            string copy = ip.Split(' ')[0];
            if (copy == "" || !char.IsDigit(copy[0])) return;
            try { Clipboard.SetText(copy.Split('/')[0]); } catch { }
            copiedUntil = Environment.TickCount + 1200;
            Invalidate();
            var t = new Timer { Interval = 1300 };
            t.Tick += delegate { t.Stop(); t.Dispose(); Invalidate(); };
            t.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(UI.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var big = UI.Semi(20f);
            TextRenderer.DrawText(g, ip, big, new Point(-UI.S(2), 0), UI.Text, UI.Card, TextFormatFlags.NoPadding);
            var ipSize = TextRenderer.MeasureText(g, ip, big, Size.Empty, TextFormatFlags.NoPadding);
            string hint = Environment.TickCount < copiedUntil ? "copiée ✓" : (hover ? "cliquer pour copier" : "");
            TextRenderer.DrawText(g, sub + (hint != "" ? "   ·   " + hint : ""), UI.F(9f), new Point(0, ipSize.Height + UI.S(4)),
                                  UI.Sub, UI.Card, TextFormatFlags.NoPadding);

            if (chip != "")
            {
                var cf = UI.F(8.5f, FontStyle.Bold);
                var cs = TextRenderer.MeasureText(g, chip, cf, Size.Empty, TextFormatFlags.NoPadding);
                var r = new RectangleF(Width - cs.Width - UI.S(20), UI.S(6), cs.Width + UI.S(18), UI.S(24));
                using (var p = UI.Round(r, r.Height / 2))
                using (var b = new SolidBrush(UI.Soft(chipColor))) g.FillPath(b, p);
                TextRenderer.DrawText(g, chip, cf, Rectangle.Round(r), UI.Fg(chipColor), UI.Soft(chipColor),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // ------------------------------------------------------------------ mini graphique de debit
    class Sparkline : Control
    {
        double[] rx = new double[0], tx = new double[0];

        public Sparkline(int width, int height)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Width = width; Height = height;
            BackColor = UI.Card;
            Margin = new Padding(0, UI.S(6), 0, 0);
        }

        public void SetData(double[] rx, double[] tx) { this.rx = (double[])rx.Clone(); this.tx = (double[])tx.Clone(); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(UI.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double max = 8000;
            foreach (var v in rx) max = Math.Max(max, v);
            foreach (var v in tx) max = Math.Max(max, v);
            max *= 1.15;
            int top = UI.S(14), bottom = Height - 2;
            using (var pen = new Pen(UI.Track) { DashStyle = DashStyle.Dot })
            {
                g.DrawLine(pen, 0, top, Width, top);
                g.DrawLine(pen, 0, (top + bottom) / 2, Width, (top + bottom) / 2);
            }
            using (var pen = new Pen(UI.Track)) g.DrawLine(pen, 0, bottom, Width, bottom);
            Draw(g, rx, max, top, bottom, UI.Accent, true);
            Draw(g, tx, max, top, bottom, UI.Purple, false);
            TextRenderer.DrawText(g, "max " + Net.FormatBits(max / 1.15), UI.F(7.5f), new Point(0, 0), UI.Sub, UI.Card, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "60 s", UI.F(7.5f), new Rectangle(0, 0, Width, top), UI.Sub, UI.Card, TextFormatFlags.NoPadding | TextFormatFlags.Right);
        }

        void Draw(Graphics g, double[] data, double max, int top, int bottom, Color c, bool fill)
        {
            if (data.Length < 2) return;
            var pts = new PointF[data.Length];
            for (int i = 0; i < data.Length; i++)
                pts[i] = new PointF(i * (Width - 1f) / (data.Length - 1), (float)(bottom - (bottom - top) * Math.Min(1, data[i] / max)));
            if (fill)
            {
                var poly = new List<PointF>(pts);
                poly.Add(new PointF(Width - 1, bottom));
                poly.Add(new PointF(0, bottom));
                using (var b = new SolidBrush(Color.FromArgb(UI.Dark ? 55 : 40, c))) g.FillPolygon(b, poly.ToArray());
            }
            using (var p = new Pen(c, UI.S(1.6f)) { LineJoin = LineJoin.Round }) g.DrawLines(p, pts);
        }
    }

    // ------------------------------------------------------------------ chaine PC -> passerelle -> DNS -> internet
    class ChainView : Control
    {
        public readonly string[] Names = { "Carte", "Passerelle", "DNS", "Internet" };
        public readonly NetCheck[] States = new NetCheck[4];
        public readonly string[] Details = { "", "", "", "" };

        public ChainView(int width)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Width = width; Height = UI.S(86);
            BackColor = UI.Card;
            Margin = new Padding(0, UI.S(4), 0, UI.S(4));
        }

        static Color Col(NetCheck c) { return c == NetCheck.Ok ? IconFactory.Green : (c == NetCheck.Fail ? IconFactory.Red : UI.Track); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(UI.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int n = Names.Length, r = UI.S(15), cy = UI.S(20);
            float m = Width / (2f * n);
            for (int i = 0; i < n - 1; i++)
            {
                float x1 = m + i * 2 * m + r + UI.S(3), x2 = m + (i + 1) * 2 * m - r - UI.S(3);
                Color c = States[i + 1] == NetCheck.Fail && States[i] == NetCheck.Ok ? IconFactory.Red
                        : (States[i] == NetCheck.Ok && States[i + 1] == NetCheck.Ok ? IconFactory.Green : UI.Track);
                using (var p = new Pen(c, UI.S(2.5f)) { DashStyle = c == IconFactory.Red ? DashStyle.Dash : DashStyle.Solid })
                    g.DrawLine(p, x1, cy, x2, cy);
            }
            for (int i = 0; i < n; i++)
            {
                float cx = m + i * 2 * m;
                Color c = Col(States[i]);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);
                string glyph = States[i] == NetCheck.Ok ? UI.GCheck : (States[i] == NetCheck.Fail ? UI.GClose : "…");
                var gfont = States[i] == NetCheck.Unknown ? UI.F(10f, FontStyle.Bold) : UI.Icon(11f);
                TextRenderer.DrawText(g, glyph, gfont, new Rectangle((int)(cx - r), cy - r, 2 * r, 2 * r),
                    States[i] == NetCheck.Unknown ? UI.Sub : Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                var rect = new Rectangle((int)(cx - m), cy + r + UI.S(6), (int)(2 * m), UI.S(20));
                TextRenderer.DrawText(g, Names[i], UI.Semi(8.5f), rect, UI.Text, UI.Card, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                rect.Y += UI.S(17);
                TextRenderer.DrawText(g, Details[i], UI.F(7.5f), rect, UI.Sub, UI.Card,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // ------------------------------------------------------------------ ligne "carte reseau" dans la liste
    class NicRow : Control
    {
        public string Id = "", Title = "", Line = "";
        public Color Dot;
        public bool Selected;
        bool hover;

        public NicRow(int width)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Width = width; Height = UI.S(42);
            BackColor = UI.Card;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, 0, UI.S(2));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(UI.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg = Selected ? UI.Soft(UI.Accent) : (hover ? UI.CardHover : UI.Card);
            using (var p = UI.Round(new RectangleF(0, 0, Width - 1, Height - 1), UI.S(5)))
            using (var b = new SolidBrush(bg)) g.FillPath(b, p);
            using (var b = new SolidBrush(Dot)) g.FillEllipse(b, UI.S(8), UI.S(9), UI.S(9), UI.S(9));
            TextRenderer.DrawText(g, Title, UI.Semi(9f), new Rectangle(UI.S(24), UI.S(4), Width - UI.S(110), UI.S(20)), UI.Text, bg,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Line, UI.F(8.5f), new Rectangle(UI.S(24), UI.S(22), Width - UI.S(30), UI.S(18)), UI.Sub, bg,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            if (Selected)
                TextRenderer.DrawText(g, "surveillée", UI.F(8f), new Rectangle(0, UI.S(5), Width - UI.S(8), UI.S(18)), UI.Fg(UI.Accent), bg,
                    TextFormatFlags.NoPadding | TextFormatFlags.Right);
        }
    }
}

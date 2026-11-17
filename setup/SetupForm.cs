// Ethersio Setup - fenetre d'installation : licence, progression, fin

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace EthersioSetup
{
    class SetupForm : Form
    {
        static readonly Color Accent = Color.FromArgb(15, 118, 110);
        static readonly Color Muted = Color.FromArgb(82, 82, 91);

        readonly Label title, subtitle, status;
        readonly TextBox license;
        readonly CheckBox accept, launch;
        readonly Button primary, cancel;
        readonly ProgressBar bar;
        Action onPrimary;
        bool installing;

        public SetupForm()
        {
            Text = "Ethersio";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(560, 480);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            title = Label(new Rectangle(32, 28, 496, 40), new Font("Segoe UI", 20f, FontStyle.Bold), Color.Black);
            subtitle = Label(new Rectangle(32, 72, 496, 24), Font, Muted);
            status = Label(new Rectangle(32, 120, 496, 96), new Font("Segoe UI", 10.5f), Color.Black);
            license = new TextBox
            {
                Bounds = new Rectangle(32, 112, 496, 240), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White, Text = Installer.LicenseText.Replace("\n", "\r\n"), TabStop = false
            };
            accept = new CheckBox { Bounds = new Rectangle(32, 364, 496, 24), Text = "J'ai lu les informations et j'accepte la licence" };
            launch = new CheckBox { Bounds = new Rectangle(32, 232, 496, 24), Text = "Lancer Ethersio", Checked = true };
            bar = new ProgressBar { Bounds = new Rectangle(32, 232, 496, 8), Style = ProgressBarStyle.Marquee };
            primary = Button(new Rectangle(408, 424, 120, 36), true);
            cancel = Button(new Rectangle(272, 424, 120, 36), false);
            cancel.Text = "Annuler";
            cancel.Click += delegate { Close(); };
            primary.Click += delegate { onPrimary(); };
            accept.CheckedChanged += delegate { primary.Enabled = accept.Checked; };
            AcceptButton = primary;

            Controls.AddRange(new Control[] { title, subtitle, status, license, accept, launch, bar, primary, cancel });
            ShowWelcome();
        }

        Label Label(Rectangle bounds, Font font, Color color)
        {
            return new Label { Bounds = bounds, Font = font, ForeColor = color, AutoSize = false };
        }

        Button Button(Rectangle bounds, bool isPrimary)
        {
            var b = new Button { Bounds = bounds, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = isPrimary ? Accent : Color.FromArgb(212, 212, 216);
            b.BackColor = isPrimary ? Accent : Color.White;
            b.ForeColor = isPrimary ? Color.White : Color.Black;
            return b;
        }

        void Page(string heading, string sub, bool licenseVisible, bool progressVisible, bool doneVisible)
        {
            title.Text = heading;
            subtitle.Text = sub;
            subtitle.Visible = sub != "";
            license.Visible = accept.Visible = licenseVisible;
            status.Visible = !licenseVisible;
            bar.Visible = progressVisible;
            launch.Visible = doneVisible;
        }

        void ShowWelcome()
        {
            Page("Installer Ethersio", "Version " + Installer.Version + "  ·  pour tous les comptes de cet ordinateur", true, false, false);
            primary.Text = "Installer";
            primary.Enabled = accept.Checked;
            onPrimary = Install;
        }

        void Install()
        {
            installing = true;
            Page("Installation en cours", "", false, true, false);
            status.Text = "Préparation";
            primary.Enabled = cancel.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Installer.Install(step => BeginInvoke((Action)delegate { status.Text = step; }));
                    BeginInvoke((Action)ShowDone);
                }
                catch (Exception ex)
                {
                    BeginInvoke((Action)delegate { ShowFailure(ex); });
                }
            });
        }

        void ShowDone()
        {
            installing = false;
            Page("Ethersio est installé", "", false, false, true);
            status.Text = "Il démarre avec Windows et se met à jour tout seul.\r\nRetrouvez son icône près de l'horloge.";
            primary.Text = "Terminer";
            primary.Enabled = true;
            cancel.Visible = false;
            onPrimary = delegate
            {
                if (launch.Checked) Installer.Launch();
                Close();
            };
        }

        void ShowFailure(Exception ex)
        {
            installing = false;
            Page("L'installation a échoué", "", false, false, false);
            status.Text = ex.Message;
            primary.Text = "Fermer";
            primary.Enabled = true;
            cancel.Visible = false;
            onPrimary = Close;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (installing) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}

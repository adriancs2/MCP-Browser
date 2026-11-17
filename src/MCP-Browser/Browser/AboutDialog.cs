using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MCP_Browser.Browser
{
    /// <summary>The "About" pop-up: product name, licence line and project link.</summary>
    internal class AboutDialog : Form
    {
        public AboutDialog()
        {
            Text = "About " + AppInfo.ProductName;
            Icon = AppInfo.Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = SystemColors.Window;
            Font = new Font("Segoe UI", 9.75F);
            ClientSize = new Size(440, 190);

            var logo = new PictureBox
            {
                Location = new Point(24, 24),
                Size = new Size(64, 64),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = new Icon(AppInfo.Icon, 64, 64).ToBitmap()
            };

            var name = new Label
            {
                Text = AppInfo.ProductName,
                Font = new Font("Segoe UI Semibold", 16F),
                AutoSize = true,
                Location = new Point(106, 20)
            };

            var tagline = new Label
            {
                Text = AppInfo.Tagline + "  ·  version " + AppInfo.Version,
                ForeColor = SystemColors.GrayText,
                AutoSize = true,
                Location = new Point(109, 56)
            };

            var link = new LinkLabel
            {
                Text = AppInfo.ProjectUrl,
                AutoSize = true,
                Location = new Point(109, 84)
            };
            link.LinkClicked += (s, e) => OpenProjectPage();

            var ok = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Size = new Size(88, 28),
                Location = new Point(440 - 24 - 88, 190 - 20 - 28),
                FlatStyle = FlatStyle.System
            };

            Controls.AddRange(new Control[] { logo, name, tagline, link, ok });
            AcceptButton = ok;
            CancelButton = ok;
        }

        private void OpenProjectPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo(AppInfo.ProjectUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open the link:\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}

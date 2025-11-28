using System;
using System.Windows.Forms;

namespace MSP_ES
{
    public partial class SettingsForm : Form
    {
        private Config config;

        public SettingsForm()
        {
            InitializeComponent();
            Text = "Edit Configuration - C:\\MSP-ES\\settings.ini";
            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                config = new Config();
                txtServer.Text           = config.Get("Server");
                txtUser.Text             = config.Get("UserId");
                txtPassword.Text         = config.Get("Password");
                txtDatabase.Text         = config.Get("Database");
                txtAttachmentFolder.Text = config.AttachmentFolder;
                txtSmtpUser.Text         = config.SmtpUser;
                txtSmtpPass.Text         = config.SmtpPass;
                txtProcedure.Text        = config.Procedure;
                txtSmtpHost.Text         = config.SmtpHost;
                txtSmtpPort.Text         = config.SmtpPort.ToString();
                chkEnableSsl.Checked     = config.EnableSsl;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot load config file!\n\n" + ex.Message, "Error",
                              MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }

        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            try
            {
                config.Set("Server", txtServer.Text.Trim());
                config.Set("UserId", txtUser.Text.Trim());
                config.Set("Password", txtPassword.Text);
                config.Set("Database", txtDatabase.Text.Trim());
                config.Set("AttachmentFolder", txtAttachmentFolder.Text.Trim());
                config.Set("SmtpUser", txtSmtpUser.Text.Trim());
                config.Set("SmtpPass", txtSmtpPass.Text);
                config.Set("Procedure", txtProcedure.Text.Trim());
                config.Set("SmtpHost", txtSmtpHost.Text.Trim());
                config.Set("SmtpPort", txtSmtpPort.Text.Trim());
                config.Set("EnableSsl", chkEnableSsl.Checked.ToString());
                config.Save();

                MessageBox.Show("Settings saved successfully!\n\nRestart the app to apply changes.",
                              "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save config!\n\n" + ex.Message, "Error",
                              MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

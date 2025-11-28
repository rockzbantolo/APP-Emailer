using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Windows.Forms;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MSP_ES
{
    public partial class EmailSenderForm : Form
    {
        private readonly Config config;
        private bool _lastLogWasError = false;

        public EmailSenderForm()
        {
            InitializeComponent();
            config = new Config();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        private string connString => config.ConnString;
        private string attachmentFolder => config.AttachmentFolder;
        private string strProcedure => config.Procedure;
        private string smtpUser => config.SmtpUser;
        private string smtpPass => config.SmtpPass;
        private string smtpHost => config.SmtpHost;
        private int smtpPort => config.SmtpPort;
        private bool smtpSSL => config.EnableSsl;

        private async void btnStart_Click(object sender, EventArgs e)
        {
            switch (btnStart.Text)
            {
                case "Start":
                    Log("Application Started");
                    await SendEmailAsync();
                    tmScheduler.Interval = 2 * 60 * 60 * 1000;
                    tmScheduler.Start();
                    btnStart.Text = "Stop";
                    break;

                case "Stop":
                    Log("Application Stopped");
                    tmScheduler.Stop();
                    btnStart.Text = "Start";
                    break;
            }

            /* 
             * DEBUGGER
            try
            {
                // Test with attachment (optional)
                string[] attachments = null;
                string testFile = @"C:\MSP_Attachments\1_TestFile.txt";

                if (File.Exists(testFile))
                    attachments = new[] { testFile };

                SendEmail(
                    to: "x@gmail.com",
                    subject: "TEST EMAIL FROM MSP_ES",
                    html: "<h2 style='color: green;'>This is a TEST email!</h2><p>Sent at: " + DateTime.Now + "</p>",
                    attachments: attachments
                );

                Log("TEST EMAIL SENT SUCCESSFULLY!", false);

            }
            catch (Exception ex)
            {
                Log("TEST EMAIL FAILED: " + ex.Message, true);

            }

            */
        }

        private async Task SendEmailAsync()
        {
            var emails = GetScheduledEmails();
            if (emails.Count == 0) return;

            using (var conn = new MySqlConnection(connString))
            {
                await conn.OpenAsync();

                foreach (var r in emails)
                {
                    try
                    {
                        string subject, htmlBody;

                        switch (r.SendMode)
                        {
                            case 0:
                                subject = "Daily Report";
                                htmlBody = "<h3>Daily Report</h3>";
                                break;
                            case 1:
                                subject = "Monthly Billing Statement";
                                htmlBody = "<h3>Monthly Billing Statement</h3>";
                                break;
                            case 2:
                                subject = "Reminder: POS Terminal Rental Due in 2 Months";
                                htmlBody = SetEmailFormat(r.ClientName, r.Tid.ToString(), r.InstallDate, r.DueDate, r.AmountDue, 60);
                                break;
                            case 3:
                                subject = "Statement of Account (SOA) – Due in 45 Days";
                                htmlBody = SetEmailFormat(r.ClientName, r.Tid.ToString(), r.InstallDate, r.DueDate, r.AmountDue, 45);
                                break;
                            default:
                                continue;
                        }

                        string[] files = r.SendMode != 2 ? GetAttachments(r.Tid) : null;

                        await ProcessEmailAsync(r.Email, subject, htmlBody, files, r.Cc);
                        MarkAsSent(conn, r.Id);

                        Log($"Sent → {r.Email} | TID {r.Tid} | " +
                            (r.SendMode == 0 ? "Daily Report" :
                            r.SendMode == 1 ? "Monthly Billing" :
                            r.SendMode == 2 ? "60-day Reminder" : "45-day SOA") +
                            $" | Files: {files?.Length ?? 0}");
                    }
                    catch (SmtpException ex)
                    { MarkAsFailed(conn, r.Id, ex.Message, r.FailedCount, r.SendMode); }
                    catch (Exception ex)
                    { MarkAsFailed(conn, r.Id, ex.Message, r.FailedCount, r.SendMode); }
                }
            }

        }

        private async Task ProcessEmailAsync(string to, string subject, string html, string[] attachments, string cc = null)
        {
            using (var client = new SmtpClient(smtpHost, smtpPort))
            {
                client.Credentials = new NetworkCredential(smtpUser, smtpPass);
                client.EnableSsl = smtpSSL;

                using (var msg = new MailMessage())
                {
                    msg.From = new MailAddress(smtpUser);
                    msg.To.Add(to);
                    if (!string.IsNullOrWhiteSpace(cc))
                    {
                        foreach (var email in cc.Split(',').Select(e => e.Trim()))
                            msg.CC.Add(email);
                    }

                    msg.Subject = subject;
                    msg.Body = html;
                    msg.IsBodyHtml = true;

                    if (attachments != null)
                        foreach (var f in attachments)
                            if (File.Exists(f))
                                msg.Attachments.Add(new Attachment(f));

                    await client.SendMailAsync(msg); // Async send
                }
            }
        }

        private List<ScheduledEmail> GetScheduledEmails()
        {
            var list = new List<ScheduledEmail>();

            using (var conn = new MySqlConnection(connString))
            {
                try
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand(strProcedure, conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@p_Action", "View");
                        cmd.Parameters.AddWithValue("@p_Search", "Billing-Schedule");

                        var pValue = cmd.Parameters.Add("@p_Value", MySqlDbType.JSON);
                        pValue.Value = DBNull.Value;
                        pValue.Direction = ParameterDirection.Input;

                        using (var rd = cmd.ExecuteReader())
                        {
                            var columns = new HashSet<string>();
                            for (int i = 0; i < rd.FieldCount; i++)
                                columns.Add(rd.GetName(i).ToLower());

                            while (rd.Read())
                            {
                                var details = new ScheduledEmail
                                {
                                    Id = columns.Contains("id") && !rd.IsDBNull(rd.GetOrdinal("id"))
                                        ? rd.GetInt32("id") : 0,

                                    Tid = columns.Contains("tid") && !rd.IsDBNull(rd.GetOrdinal("tid"))
                                        ? rd.GetInt32("tid") : 0,

                                    SendMode = columns.Contains("send_mode") && !rd.IsDBNull(rd.GetOrdinal("send_mode"))
                                        ? rd.GetInt32("send_mode") : 0,

                                    Email = columns.Contains("email") && !rd.IsDBNull(rd.GetOrdinal("email"))
                                        ? rd.GetString("email") : null,

                                    ClientName = columns.Contains("merchant_name") && !rd.IsDBNull(rd.GetOrdinal("merchant_name"))
                                        ? rd.GetString("merchant_name") : null,

                                    Frequency = columns.Contains("frequency") && !rd.IsDBNull(rd.GetOrdinal("frequency"))
                                        ? rd.GetInt32("frequency") : 0,

                                    AmountDue = columns.Contains("amount_due") && !rd.IsDBNull(rd.GetOrdinal("amount_due"))
                                        ? rd.GetDecimal("amount_due") : 0m,

                                    InstallDate = columns.Contains("install_date") && !rd.IsDBNull(rd.GetOrdinal("install_date"))
                                        ? rd.GetDateTime("install_date") : DateTime.MinValue,

                                    DueDate = columns.Contains("due_date") && !rd.IsDBNull(rd.GetOrdinal("due_date"))
                                        ? rd.GetDateTime("due_date") : DateTime.MinValue,

                                    SentDate = columns.Contains("sent_date") && !rd.IsDBNull(rd.GetOrdinal("sent_date"))
                                        ? rd.GetDateTime("sent_date") : (DateTime?)null,

                                    Cc = columns.Contains("cc") && !rd.IsDBNull(rd.GetOrdinal("cc"))
                                        ? rd.GetString("cc") : null,

                                    FailedCount = columns.Contains("failed_count") && !rd.IsDBNull(rd.GetOrdinal("failed_count"))
                                        ? rd.GetInt32("failed_count") : 0,

                                    PermanentFail = columns.Contains("permanent_fail") && !rd.IsDBNull(rd.GetOrdinal("permanent_fail"))
                                        ? rd.GetBoolean("permanent_fail") : false,

                                    ErrorMessage = columns.Contains("error_msg") && !rd.IsDBNull(rd.GetOrdinal("error_msg"))
                                        ? rd.GetString("error_msg") : null,

                                    LastAttempt = columns.Contains("last_attempt") && !rd.IsDBNull(rd.GetOrdinal("last_attempt"))
                                        ? rd.GetDateTime("last_attempt") : (DateTime?)null,

                                    IsSentToday = columns.Contains("is_sent") && !rd.IsDBNull(rd.GetOrdinal("is_sent"))
                                        ? rd.GetBoolean("is_sent") : false
                                };

                                list.Add(details);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Load data error: {ex.Message}\n{ex.StackTrace}", true);
                }
            }

            return list;
        }

        private List<ScheduledEmail> GetEmailReports()
        {
            var list = new List<ScheduledEmail>();

            using (var conn = new MySqlConnection(connString))
            {
                try
                {
                    conn.Open();
                    using (var cmd = new MySqlCommand(strProcedure, conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@p_Action", "Report");
                        cmd.Parameters.AddWithValue("@p_Search", "Billing-Schedule");

                        var pValue = cmd.Parameters.Add("@p_Value", MySqlDbType.JSON);
                        pValue.Value = DBNull.Value;
                        pValue.Direction = ParameterDirection.Input;

                        using (var rd = cmd.ExecuteReader())
                        {
                            while (rd.Read())
                            {
                                var details = new ScheduledEmail
                                {
                                    Id = rd.GetInt32("id"),
                                    Tid = rd.GetInt32("tid"),
                                    SendMode = rd.GetInt32("send_mode"),
                                    Email = rd.GetString("email"),
                                    ClientName = rd.GetString("merchant_name"),
                                    Frequency = rd.GetInt32("frequency")
                                };

                                list.Add(details);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Load data error: {ex.Message}\n{ex.StackTrace}", true);
                }
            }

            return list;
        }

        private string[] GetAttachments(int tid)
        {
            if (!Directory.Exists(attachmentFolder))
            {
                Log($"Folder missing: {attachmentFolder}", true);
                return new string[0];
            }

            string tidStr = tid.ToString();
            var files = Directory.GetFiles(attachmentFolder)
                        .Where(f =>
                        {
                            var name = Path.GetFileName(f);
                            var match = Regex.Match(name, @"\d+");
                            return match.Success && match.Value == tidStr;
                        })
                        .ToArray();

            return files;
        }

        private string SetEmailFormat(
        string clientName,
        string tid,
        DateTime installDate,
        DateTime dueDate,
        decimal amountDue,
        int reminderType = 60)
        {
            string title = reminderType == 60
                ? "Upcoming Reminder: POS Terminal Rental Due in 2 Months"
                : "Upcoming Reminder: POS Terminal Rental Due Soon (45-Day Notice)";

            string dueInText = reminderType == 60 ? "two months" : "45 days";

            // Main HTML Template
            string html = @"
                    <!DOCTYPE html>
                    <html lang='en'>
                    <head>
                        <meta charset='UTF-8'>
                        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                        <title>Castles Technology - Rental Reminder</title>
                        <style>
                            body { 
                                margin: 0; 
                                padding: 0; 
                                background: #f0f2f5; 
                                font-family: 'Segoe UI', Arial, sans-serif; 
                            }
                            table { 
                                border-collapse: collapse; 
                                width: 100%; 
                            }
                            .container { 
                                max-width: 1200px !important;   /* WIDER */
                                width: 100% !important; 
                                margin: 20px auto; 
                                background: #ffffff; 
                                border-radius: 16px; 
                                overflow: hidden; 
                                box-shadow: 0 15px 40px rgba(0,0,0,0.12); 
                                border: 1px solid #ddd;
                            }
                            .header { 
                                background: White; 
                                padding: 10px 50px; 
                                text-align: left; 
                            }
                            .header img { 
                                height: 160px;          /* BIGGER LOGO */
                                width: auto; 
                            }
                            .title { 
                                background: #f0f2f5; 
                                color: black; 
                                padding: 20px; 
                                text-align: center; 
                                font-size: 26px;        /* BIGGER TITLE */
                                font-weight: bold; 
                            }
                            .content { 
                                padding: 35px 50px;     /* MORE SPACING */
                                font-size: 19px;        /* BIGGER TEXT */
                                line-height: 1.8; 
                            }
                            .highlight { 
                                color: #e74c3c; 
                                font-weight: bold; 
                                font-size: 20px; 
                            }
                            table.info { 
                                width: 65%; 
                                margin: 45px 0; 
                                font-size: 18px;        /* BIGGER TABLE TEXT */
                                border: 2px solid #003087; 
                                border-radius: 12px; 
                                overflow: hidden; 
                            }
                            th { 
                                background: #003087; 
                                color: white; 
                                padding: 25px; 
                                text-align: left; 
                                font-weight: bold; 
                            }
                            td { 
                                padding: 20px; 
                                background: #f8fbff; 
                                border-bottom: 1px solid #ddd; 
                            }
                            .amount { 
                                color: #e74c3c; 
                                font-weight: bold; 
                                font-size: 20px !important;   /* HUGE AMOUNT */
                            }
                            .due { 
                                color: #e74c3c; 
                                font-weight: bold; 
                                font-size: 20px; 
                            }
                            .footer { 
                                background: #2c3e50; 
                                color: #bdc3c7; 
                                padding: 45px; 
                                text-align: center; 
                                font-size: 18px; 
                            }
                            .footer a { color: #3498db; text-decoration: none; }

                            /* RESPONSIVE */
                            @media only screen and (max-width: 820px) {
                                .container { max-width: 95% !important; }
                                .content { padding: 40px 30px !important; }
                                table.info, th, td { font-size: 18px !important; }
                                .amount { font-size: 24px !important; }
                            }
                            @media only screen and (max-width: 480px) {
                                .title { font-size: 22px !important; }
                                .content { padding: 30px 20px !important; font-size: 17px !important; }
                                table.info { font-size: 16px !important; }
                            }
                        </style>
                    </head>
                    <body>
                        <center>
                            <table role='presentation' class='container'>
                                <tr>
                                    <td class='header'>
						     <img src='https://citas.com.ph/msp/msp/msp/assets/img/castlestechnology_hd.png' alt='CASTLES TECHNOLOGY'/>
                                    </td>
                                </tr>
                                <tr>
                                    <td class='title'>" + title + @"</td>
                                </tr>
                                <tr>
                                    <td class='content'>
                                        <p>Dear <strong>" + clientName + @"</strong>,</p>
                            
                                        <p>We hope you are doing well.</p>
                            
                                        <p>This is a friendly reminder that your <span class='highlight'>12-month advance payment
                                        </span>for your POS terminal is approaching it's renewal period.  
					    Your next rental payment will be due in <strong>" + dueInText + @"</strong>.</p>
                            
                                        <table class='info'>
                                            <tr><th>Acquiring Bank</th>        <td>Metrobank</td></tr>
                                            <tr><th>Merchant TID</th>          <td><strong>" + tid + @"</strong></td></tr>
                                            <tr><th>Date of Installation</th>   <td>" + installDate.ToString("MMMM dd, yyyy") + @"</td></tr>
                                            <tr><th>Due Date</th>              <td class='due'>" + dueDate.ToString("MMMM dd, yyyy") + @"</td></tr>
                                            <tr><th>Amount Due</th>            <td class='amount'>₱" + amountDue.ToString("N2") + @"</td></tr>
                                        </table>
                            
                                        <p>To avoid any service interruption, please settle the amount on or before the due date. 
                                            If you need an updated invoice or clarification regarding your rental schedule, feel free to contact us</p>
                            
                                        <p>For billing concerns:<br>
                                        <a href='mailto:CITASBilling@castles.asia'>CITASBilling@castles.asia</a></p>
                           
                            
                                        <p>Warm regards,<br>
                                        <strong>CASTLES INTL TECHNOLOGY AND SERVICES PHILS., INC.</strong></p>
                                    </td>
                                </tr>
                                <tr>
                                    <td class='footer'>
                                        © 2025 Castles Technology Philippines. All rights reserved.<br>
                                        This is an automated reminder • Please do not reply to this email
                                    </td>
                                </tr>
                            </table>
                        </center>
                    </body>
                    </html>";

            //Debug.WriteLine(html);

            return html;
        }

        private void UpdateSendingStatus(MySqlConnection conn, string payload)
        {
            using (var cmd = new MySqlCommand(config.Procedure, conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@p_Action", "Update");
                cmd.Parameters.AddWithValue("@p_Search", "Billing-Schedule");
                cmd.Parameters.AddWithValue("@p_Value", payload);
                cmd.ExecuteNonQuery();
            }
        }

        private void MarkAsSent(MySqlConnection conn, int id)
        {
            var payload = new
            {
                Id = id,
                is_sent = 1,
                send_mode = (string)null,
                sent_date = DateTime.Today.ToString("yyyy-MM-dd"),
                failed_count = 0,
                permanent_fail = 0,
                error_message = (string)null
            };

            UpdateSendingStatus(conn, JsonConvert.SerializeObject(payload));

            //Log($"Sent → ID {id}");
        }

        private void MarkAsFailed(MySqlConnection conn, int id, string error, int currentFailedCount,  int mode)
        {
            var payload = new
            {
                Id = id,
                is_sent = 0,
                sent_mode = mode,
                failed_count = currentFailedCount,
                permanent_fail = (currentFailedCount + 1) >= 3 ? 1 : 0,
                error_msg = error != null && error.Length > 500 ? error.Substring(0, 500) : error
            };

            string payloadJson = JsonConvert.SerializeObject(payload);

            if ((currentFailedCount + 1) >= 3)
                Log($"Permanent Fail → ID: {id} | After 3 attempts | {error}", true);
            else
                Log($"Sending Failed → ID: {id} | Attempt {currentFailedCount + 1}/3 | Will retry | {error}", true);

            UpdateSendingStatus(conn, payloadJson);
        }

        private void listBox1_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            string s = lbLogs.Items[e.Index].ToString();
            bool isError = s.StartsWith("[ERROR]");

            e.DrawBackground();
            e.Graphics.DrawString(
                s,
                e.Font,
                new SolidBrush(isError ? Color.Red : Color.Navy),
                e.Bounds.X + 2,
                e.Bounds.Y);
            e.DrawFocusRectangle();
        }

        private void Log(string message, bool isError = false)
        {
            string timestamp = DateTime.Now.ToString("MM-dd-yyyy HH:mm:ss");
            string prefix = isError ? "[ERROR] " : "[INFO] ";
            string fullMessage = $"{prefix}{message} ─ {timestamp}";

            const int maxLineLength = 88;
            var lines = new List<string>();

            string remaining = fullMessage;
            while (remaining.Length > maxLineLength)
            {
                int breakIndex = remaining.LastIndexOf(' ', maxLineLength);
                if (breakIndex <= 0) breakIndex = maxLineLength;

                lines.Add(remaining.Substring(0, breakIndex).Trim());
                remaining = "    " + remaining.Substring(breakIndex).Trim();
            }
            lines.Add(remaining.Trim());

            _lastLogWasError = isError;

            Action addLog = () =>
            {
                foreach (string line in lines)
                {
                    int index = lbLogs.Items.Add(line);
                    lbLogs.Tag = isError;
                }
                lbLogs.Items.Add("");
                lbLogs.TopIndex = lbLogs.Items.Count - 1;
            };

            if (lbLogs.InvokeRequired)
                lbLogs.Invoke(addLog);
            else
                addLog();

            // Write to file
            try
            {
                string logFile = Path.Combine(config.DefaultPath, "App.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logFile));
                File.AppendAllText(logFile, fullMessage + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }

        private async void tmScheduler_Tick(object sender, EventArgs e)
        {
            await SendEmailAsync();
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            lbLogs.Items.Clear();
            Log("Logs cleared");
        }

        private void btnConfig_Click(object sender, EventArgs e)
        {
             new SettingsForm().ShowDialog();
        }
    }
}
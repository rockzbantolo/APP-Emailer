using System;

namespace MSP_ES
{
    public class ScheduledEmail
    {
        public int Id { get; set; }
        public int Tid { get; set; }
        public string Email { get; set; }
        public int Frequency { get; set; }
        public int SendMode { get; set; }
        public DateTime? SentDate { get; set; }
        public string ClientName { get; set; }
        public DateTime InstallDate { get; set; }
        public decimal AmountDue { get; set; }
        public string Cc { get; set; }
        public DateTime DueDate { get; set; }

        // ADDED: Error tracking & retry system (SAFE & CLEAN)
        public int FailedCount { get; set; } = 0;
        public bool PermanentFail { get; set; } = false;
        public string ErrorMessage { get; set; } = null;
        public DateTime? LastAttempt { get; set; } = null;
        public bool IsSentToday { get; set; } = false;

        // OPTIONAL: For cleaner code (you can use DueDate or this)
        public DateTime NextDueDate { get; set; }  // You can assign DueDate here if you want
    }
}

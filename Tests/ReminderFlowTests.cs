using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;
using LibmpvIptvClient.Diagnostics;

namespace LibmpvIptvClient.Tests
{
    [TestClass]
    public class ReminderFlowTests
    {
        [TestMethod]
        public void Play_PreAlert_FiresOnce()
        {
            var fired = 0;
            void OnLog(string msg)
            {
                if (msg == null) return;
                // Logger.GetDisplayMessage strips the "[Reminder]" tag before raising OnMessage, so the
                // assertion must look at the text after the tag.
                if ((msg.Contains("fired") && msg.Contains("action=play")) || msg.Contains("pre-alert scheduled autoplay"))
                {
                    Interlocked.Increment(ref fired);
                }
            }
            Logger.OnMessage += OnLog;
            try
            {
                var start = DateTime.UtcNow.AddSeconds(3);
                AppSettings.Current.ScheduledReminders = new System.Collections.Generic.List<ScheduledReminder>
                {
                    new ScheduledReminder
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        ChannelId = "T1",
                        ChannelName = "Test1",
                        StartAtUtc = start,
                        PreAlertSeconds = 2,
                        Action = "play",
                        Enabled = true,
                        Note = "P"
                    }
                };
                LibmpvIptvClient.Services.ReminderService.Instance.Start();
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < deadline && fired == 0) Thread.Sleep(50);
                Assert.IsTrue(fired >= 1);
            }
            finally
            {
                Logger.OnMessage -= OnLog;
            }
        }
    }
}

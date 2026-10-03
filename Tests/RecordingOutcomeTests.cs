using LibmpvIptvClient.Models;
using LibmpvIptvClient.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// A user requested stop must not be recorded as a failure (issue found in the code review: stopping a
    /// background recording marked it failed and popped a failure toast).
    /// </summary>
    [TestClass]
    public class RecordingOutcomeTests
    {
        [TestMethod]
        public void Resolve_KeepsTheStopStatusForUserCancellations()
        {
            Assert.AreEqual(ScheduledRecordingStatus.Cancelled,
                RecordingOutcome.Resolve(false, "Cancelled", ScheduledRecordingStatus.Recording));

            Assert.AreEqual(ScheduledRecordingStatus.Stopped,
                RecordingOutcome.Resolve(false, "Cancelled", ScheduledRecordingStatus.Stopped));

            Assert.AreEqual(ScheduledRecordingStatus.Stopped,
                RecordingOutcome.Resolve(false, null, ScheduledRecordingStatus.Stopped),
                "停止状态不应被后到的完成事件覆盖成失败");
        }

        [TestMethod]
        public void Resolve_ReportsRealFailures()
        {
            Assert.AreEqual(ScheduledRecordingStatus.Failed,
                RecordingOutcome.Resolve(false, "connection reset", ScheduledRecordingStatus.Recording));

            Assert.AreEqual(ScheduledRecordingStatus.Failed,
                RecordingOutcome.Resolve(false, null, ScheduledRecordingStatus.Recording));
        }

        [TestMethod]
        public void Resolve_ReportsSuccess()
        {
            Assert.AreEqual(ScheduledRecordingStatus.Completed,
                RecordingOutcome.Resolve(true, null, ScheduledRecordingStatus.Recording));

            Assert.AreEqual(ScheduledRecordingStatus.Completed,
                RecordingOutcome.Resolve(true, "some warning", ScheduledRecordingStatus.Recording));
        }

        [TestMethod]
        public void IsUserStopped_CoversBothStopStatuses()
        {
            Assert.IsTrue(RecordingOutcome.IsUserStopped(ScheduledRecordingStatus.Stopped));
            Assert.IsTrue(RecordingOutcome.IsUserStopped(ScheduledRecordingStatus.Cancelled));
            Assert.IsFalse(RecordingOutcome.IsUserStopped(ScheduledRecordingStatus.Failed));
            Assert.IsFalse(RecordingOutcome.IsUserStopped(ScheduledRecordingStatus.Completed));
            Assert.IsFalse(RecordingOutcome.IsUserStopped(ScheduledRecordingStatus.Recording));
        }
    }
}

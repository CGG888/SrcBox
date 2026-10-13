using LibmpvIptvClient.Services.WebRemote;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// A phone that talks normally reports its whole recording flow (start, record, upload, result...),
    /// and those reports used to be written at Warn level - about 180 lines a day that made the log look
    /// broken. Only the stages that describe a failure stay warnings.
    /// </summary>
    [TestClass]
    public class WebRemoteVoiceDiagTests
    {
        [DataTestMethod]
        [DataRow("{\"stage\":\"start\",\"protocol\":\"https:\"}")]
        [DataRow("{\"stage\":\"started\"}")]
        [DataRow("{\"stage\":\"record\",\"detail\":\"start\"}")]
        [DataRow("{\"stage\":\"upload\",\"detail\":\"4.2s 135000 bytes\"}")]
        [DataRow("{\"stage\":\"result\",\"detail\":\"换到3台\"}")]
        [DataRow("{\"stage\":\"resample\"}")]
        [DataRow("{\"stage\":\"end\"}")]
        public void RoutineStagesAreNotProblems(string message)
            => Assert.IsFalse(WebRemoteServer.IsVoiceProblem(message), message);

        [DataTestMethod]
        [DataRow("{\"stage\":\"stall\",\"detail\":\"12s\"}")]
        [DataRow("{\"stage\":\"start-error\",\"detail\":\"InvalidStateError\"}")]
        [DataRow("{\"stage\":\"error\",\"detail\":\"not-allowed\"}")]
        [DataRow("{\"stage\":\"mic\",\"detail\":\"granted\"}")]
        [DataRow("{\"stage\":\"max\"}")]
        public void FailureStagesStayWarnings(string message)
            => Assert.IsTrue(WebRemoteServer.IsVoiceProblem(message), message);

        [TestMethod]
        public void AnEmptyReportIsNotAProblem()
        {
            Assert.IsFalse(WebRemoteServer.IsVoiceProblem(null));
            Assert.IsFalse(WebRemoteServer.IsVoiceProblem(""));
            Assert.IsFalse(WebRemoteServer.IsVoiceProblem("{}"));
        }
    }
}

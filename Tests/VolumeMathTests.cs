using LibmpvIptvClient.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibmpvIptvClient.Tests
{
    /// <summary>
    /// mpv keeps "volume" at 100 = unity, so the loudness ceiling configured in the settings has to become
    /// extra gain. These tests pin that mapping (and its cap) down.
    /// </summary>
    [TestClass]
    public class VolumeMathTests
    {
        const double Tolerance = 0.01;

        [TestMethod]
        public void CeilingGain_AtUnityCeiling_HasNoBoost()
        {
            Assert.AreEqual(0.0, VolumeMath.CeilingGainDb(100), Tolerance, "100% is the source level");
        }

        [TestMethod]
        public void CeilingGain_TracksTheConfiguredCeiling()
        {
            Assert.AreEqual(2.28, VolumeMath.CeilingGainDb(130), Tolerance);
            Assert.AreEqual(6.02, VolumeMath.CeilingGainDb(200), Tolerance);
        }

        [TestMethod]
        public void CeilingGain_IsCappedAtTwelveDb()
        {
            Assert.AreEqual(VolumeMath.MaxHeadroomDb, VolumeMath.CeilingGainDb(400), Tolerance);
            Assert.AreEqual(VolumeMath.MaxHeadroomDb, VolumeMath.CeilingGainDb(1000), Tolerance);
        }

        [TestMethod]
        public void CeilingGain_IgnoresCeilingsBelowUnity()
        {
            Assert.AreEqual(0.0, VolumeMath.CeilingGainDb(0), Tolerance);
            Assert.AreEqual(0.0, VolumeMath.CeilingGainDb(50), Tolerance);
        }

        [TestMethod]
        public void Gain_AddsTheUserGainToTheCeiling()
        {
            Assert.AreEqual(6.0, VolumeMath.GainDb(6, 100), Tolerance);
            Assert.AreEqual(6.02, VolumeMath.GainDb(0, 200), Tolerance);
            Assert.AreEqual(-3.72, VolumeMath.GainDb(-6, 130), Tolerance);
        }

        [TestMethod]
        public void Gain_StaysInsideTheRangeMpvAccepts()
        {
            Assert.AreEqual(60.0, VolumeMath.GainDb(60, 400), Tolerance, "mpv rejects volume-gain above +60 dB");
            Assert.AreEqual(-193.98, VolumeMath.GainDb(-250, 200), Tolerance, "the user gain is floored first, then the ceiling boost is added (still inside -200..60)");
        }
    }
}

using System;
using System.IO;

namespace LibmpvIptvClient.Services
{
    // 仅用于 .TS 文件的快速时长估计：读取文件头与尾部窗口，解析 PCR 与 PES PTS（PCR 优先）
    // 只扫描小窗口，避免卡顿；未解析到 PTS 则返回 null
    static class TsDurationEstimator
    {
        const int TsPacket = 188;
        const byte Sync = 0x47;
        const long PtsWrap = 1L << 33; // 33-bit
        const double PtsClock = 90000.0; // 90 kHz
        const double PcrBaseClock = 90000.0; // PCR base part
        const double PcrExtClock = 27000000.0; // PCR extension clock

        public static TimeSpan? Estimate(string path, int windowBytes = 8 * 1024 * 1024)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < TsPacket * 3) return null;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long firstPts = -1, lastPts = -1;
                double minPcr = double.MaxValue, maxPcr = double.MinValue;
                // 扫描头部窗口（用 long 计算：录像 >2GB 时 (int)fi.Length 会溢出为负数，导致时长为空）
                ScanWindow(fs, 0, (int)Math.Min((long)windowBytes, fi.Length / 2), ref firstPts, ref lastPts, ref minPcr, ref maxPcr, stopAfterFirst:true);
                // 扫描尾部窗口
                long tailStart = Math.Max(0, fi.Length - windowBytes);
                ScanWindow(fs, tailStart, (int)Math.Min((long)windowBytes, fi.Length - tailStart), ref firstPts, ref lastPts, ref minPcr, ref maxPcr, stopAfterFirst:false);
                // 优先 PCR（更稳），否则回退 PTS
                if (maxPcr > minPcr && minPcr < double.MaxValue)
                {
                    var secs = maxPcr - minPcr;
                    if (secs > 0.2) return TimeSpan.FromSeconds(secs);
                }
                if (firstPts >= 0 && lastPts >= 0)
                {
                    long diff = lastPts >= firstPts ? (lastPts - firstPts) : (lastPts + PtsWrap - firstPts);
                    if (diff > 0) return TimeSpan.FromSeconds(diff / PtsClock);
                }
                return null;
            }
            catch { return null; }
        }

        static void ScanWindow(FileStream fs, long start, int length, ref long firstPts, ref long lastPts, ref double minPcr, ref double maxPcr, bool stopAfterFirst)
        {
            fs.Position = start;
            byte[] buf = new byte[length];
            int read = fs.Read(buf, 0, length);
            if (read <= 0) return;
            // 同步扫描
            int i = 0;
            while (i + TsPacket <= read)
            {
                if (buf[i] != Sync)
                {
                    i++;
                    continue;
                }
                // 粗略校验：后续若存在若干个 188 步长的 sync，则认为对齐
                if (!LikelyAligned(buf, i, read)) { i++; continue; }

                // 解析 PCR（若有）
                TryExtractPcr(buf, i, TsPacket, ref minPcr, ref maxPcr);
                // 解析单个包的 PTS（若有）
                TryExtractPts(buf, i, TsPacket, ref firstPts, ref lastPts, stopAfterFirst);
                i += TsPacket;
            }
        }

        static bool LikelyAligned(byte[] buf, int pos, int read)
        {
            int ok = 0, checks = 3;
            for (int k = 1; k <= checks; k++)
            {
                int p = pos + k * TsPacket;
                if (p < read && buf[p] == Sync) ok++;
            }
            return ok >= 2; // 至少 2 次命中
        }

        static void TryExtractPcr(byte[] buf, int pos, int size, ref double minPcr, ref double maxPcr)
        {
            if (pos + 4 > buf.Length) return;
            byte b3 = buf[pos + 3];
            int afc = (b3 >> 4) & 0x3;
            if (afc == 2 || afc == 3)
            {
                int idx = pos + 4;
                if (idx >= buf.Length) return;
                int adLen = buf[idx];
                if (adLen <= 0 || idx + 1 + adLen > pos + size || idx + 1 + adLen > buf.Length) return;
                byte flags = buf[idx + 1];
                bool hasPcr = (flags & 0x10) != 0;
                if (!hasPcr) return;
                int pcrPos = idx + 2;
                if (pcrPos + 5 >= buf.Length) return;
                // PCR 基础 + 扩展
                ulong b0 = buf[pcrPos];
                ulong b1 = buf[pcrPos + 1];
                ulong b2 = buf[pcrPos + 2];
                ulong b3v = buf[pcrPos + 3];
                ulong b4 = buf[pcrPos + 4];
                ulong b5 = buf[pcrPos + 5];
                ulong base33 = (b0 << 25) | (b1 << 17) | (b2 << 9) | (b3v << 1) | (b4 >> 7);
                ulong ext9 = ((b4 & 0x01) << 8) | b5;
                double secs = base33 / PcrBaseClock + ext9 / PcrExtClock;
                if (secs < minPcr) minPcr = secs;
                if (secs > maxPcr) maxPcr = secs;
            }
        }

        static void TryExtractPts(byte[] buf, int pos, int size, ref long firstPts, ref long lastPts, bool stopAfterFirst)
        {
            // 头部
            if (pos + 4 > buf.Length) return;
            byte b1 = buf[pos + 1];
            byte b3 = buf[pos + 3];
            int payloadStart = (b1 & 0x40) != 0 ? 1 : 0; // PUSI
            int afc = (b3 >> 4) & 0x3;
            int idx = pos + 4;
            if (afc == 2) return; // 只有自适应，无负载
            if (afc == 3)
            {
                if (idx >= buf.Length) return;
                int adLen = buf[idx];
                idx += 1 + adLen;
                if (idx >= pos + size) return;
            }
            if (payloadStart == 0) return;
            // PES 起始必须以 00 00 01
            if (idx + 9 >= buf.Length) return;
            if (!(buf[idx] == 0x00 && buf[idx + 1] == 0x00 && buf[idx + 2] == 0x01)) return;
            int pesHdrLen = buf[idx + 8];
            byte flags = buf[idx + 7];
            bool hasPts = (flags & 0x80) != 0;
            if (!hasPts) return;
            int p = idx + 9; // PTS 字段起始
            if (p + 4 >= buf.Length) return;
            long pts = DecodePts(buf, p);
            if (pts < 0) return;
            if (firstPts < 0) firstPts = pts;
            if (pts > lastPts) lastPts = pts;
        }

        static long DecodePts(byte[] d, int p)
        {
            try
            {
                long v = 0;
                v |= (long)((d[p] >> 1) & 0x07) << 30;
                v |= (long)(d[p + 1]) << 22;
                v |= (long)((d[p + 2] >> 1) & 0x7F) << 15;
                v |= (long)(d[p + 3]) << 7;
                v |= (long)(d[p + 4] >> 1);
                return v;
            }
            catch { return -1; }
        }
    }
}

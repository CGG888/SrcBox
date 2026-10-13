using System;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>
    /// Similarity between what the user said and a channel/stream/playlist name.
    ///
    /// The scale is deliberately narrow and its numbers are load bearing: the router auto-executes at
    /// <see cref="AutoExecuteScore"/> or above and offers candidates from <see cref="CandidateScore"/> up,
    /// so a name prefix ("湖南" vs "湖南卫视") must land in the middle band instead of silently winning.
    /// </summary>
    public static class VoiceFuzzy
    {
        /// <summary>At or above this the best candidate is taken without asking.</summary>
        public const double AutoExecuteScore = 0.85;

        /// <summary>At or above this a candidate is worth showing.</summary>
        public const double CandidateScore = 0.55;

        /// <summary>Best score of <paramref name="spoken"/> against any of the names.</summary>
        public static double Best(string spoken, params string?[] names)
        {
            var best = 0d;
            if (names == null) return best;
            foreach (var name in names)
            {
                var score = Score(spoken, name);
                if (score > best) best = score;
            }
            return best;
        }

        public static double Score(string? spoken, string? name)
        {
            var left = VoiceText.Compact(VoiceText.Normalize(spoken));
            var right = VoiceText.Compact(VoiceText.Normalize(name));
            if (left.Length == 0 || right.Length == 0) return 0;
            if (left == right) return 1.0;

            if (right.StartsWith(left, StringComparison.Ordinal))
                return left.Length >= 2 ? 0.90 : 0.76;
            if (left.StartsWith(right, StringComparison.Ordinal))
                return right.Length >= 2 ? 0.88 : 0.70;
            if (right.Contains(left, StringComparison.Ordinal))
                return left.Length >= 2 ? 0.80 : 0.62;
            if (left.Contains(right, StringComparison.Ordinal))
                return 0.72;

            // "cctv5" said against "CCTV-5 体育": the spoken short form is one of the name's tokens.
            if (left.Length >= 2 && right.Contains(left, StringComparison.Ordinal))
                return 0.78;

            var score = Math.Max(Dice(Bigrams(left), Bigrams(right)), Dice(Chars(left), Chars(right)));
            return score switch
            {
                >= 0.80 => 0.82,
                >= 0.65 => 0.72,
                >= 0.50 => 0.62,
                >= 0.40 => 0.55,
                _ => score * 0.6
            };
        }

        private static string[] Bigrams(string value)
        {
            if (value.Length == 1) return new[] { value };
            var result = new string[value.Length - 1];
            for (var i = 0; i < value.Length - 1; i++) result[i] = value.Substring(i, 2);
            return result;
        }

        private static string[] Chars(string value)
        {
            var result = new string[value.Length];
            for (var i = 0; i < value.Length; i++) result[i] = value[i].ToString();
            return result;
        }

        /// <summary>Sørensen–Dice coefficient, integer counts to avoid building multisets.</summary>
        private static double Dice(string[] a, string[] b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            var hit = 0;
            var used = new bool[b.Length];
            foreach (var token in a)
            {
                for (var j = 0; j < b.Length; j++)
                {
                    if (used[j] || b[j] != token) continue;
                    used[j] = true;
                    hit++;
                    break;
                }
            }
            return 2.0 * hit / (a.Length + b.Length);
        }
    }
}

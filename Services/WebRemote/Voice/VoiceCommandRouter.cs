using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>
    /// Turns one recognised sentence into a player action. The router owns the semantics and the matching
    /// but never touches mpv: every side effect goes through a callback on
    /// <see cref="VoiceCommandContext"/>, which is what lets the page, a user macro and the ColorOS
    /// shortcut share one implementation.
    ///
    /// Strategy per intent:
    ///   1. a callback executes the change (works for both the page and the http endpoint),
    ///   2. when the target is ambiguous the reply carries candidates and the page taps one,
    ///   3. when no player callback is attached (http endpoint, dry run) the reply carries the action
    ///      payload so the caller can still run it.
    /// </summary>
    public static class VoiceCommandRouter
    {
        private const int MaxOptions = 12;
        private const int MaxStepSeconds = 600;

        /// <summary>Words a channel name may not absorb when a sentence is split into name + number.</summary>
        private static readonly string[] NoiseWords =
        {
            "回看", "回放", "重播", "今天", "今日", "昨天", "昨日", "前天", "明天", "后天", "本日", "节目",
            "有什么", "什么", "看一下", "看看", "看", "帮我", "请", "我要", "我想", "在播", "正在把",
            "现在", "正在", "播", "今晚", "昨天晚上", "晚上", "上午", "下午", "中午", "早上", "凌晨"
        };

        public static VoiceCommandReply Route(VoiceCommandContext? context)
        {
            if (context == null) return Fail("", "unknown", "voice_not_understood");
            var text = context.Text ?? "";
            var reply = new VoiceCommandReply { Verb = "unknown", Text = text };
            var normalized = VoiceText.Normalize(text);
            if (string.IsNullOrEmpty(normalized))
            {
                reply.Spoken = VoiceAnswer.NotUnderstood(VoiceAnswer.Strings(context.Lang), context.Lang);
                return reply;
            }

            var command = MatchUserRules(context, normalized) ?? MatchBuiltIn(context, normalized);
            if (command == null)
            {
                reply.Spoken = VoiceAnswer.NotUnderstood(VoiceAnswer.Strings(context.Lang), context.Lang);
                return reply;
            }

            reply.Verb = VerbName(command.Verb);
            reply.Confidence = command.Confidence;
            reply.Rule = command.Rule;
            reply.Options = command.Options;
            return Dispatch(command, context, reply);
        }

        /// <summary>
        /// Runs an already-resolved action, which is how the page replays a tapped candidate and how a
        /// phone-side script (ColorOS shortcut) drives the endpoint without any natural language.
        /// </summary>
        public static VoiceCommandReply Execute(VoiceCommandContext? context, string? action, string? target)
        {
            if (context == null) return new VoiceCommandReply { Spoken = "no context" };
            var verb = VerbFromAction(action);
            if (verb == VoiceVerb.Unknown)
                return Fail(context.Text, "unknown", "voice_not_understood");
            var command = new VoiceCommand
            {
                Verb = verb,
                Arg = target ?? "",
                Confidence = 1.0,
                Rule = $"action:{action}",
                Options = ResolveOptions(verb, target ?? "", context)
            };
            var reply = new VoiceCommandReply { Verb = VerbName(verb), Text = context.Text ?? "" };
            return Dispatch(command, context, reply);
        }

        // ---------------------------------------------------------------- matching

        private static VoiceCommand? MatchUserRules(VoiceCommandContext context, string normalized)
        {
            var rules = context.UserRules?.Invoke();
            if (rules == null) return null;
            foreach (var rule in rules)
            {
                // A rule may list several phrasings; the first hit wins, so the order in the settings
                // file is the precedence the user asked for.
                var target = "";
                if (rule.Phrases is { Count: > 0 })
                {
                    foreach (var phrase in rule.Phrases)
                    {
                        var single = new VoiceRule
                        {
                            Kind = rule.Kind,
                            Phrases = new List<string> { phrase },
                            Action = rule.Action,
                            Target = rule.Target
                        };
                        if (VoiceAliases.TryRule(single, normalized, out target)) break;
                    }
                }
                else if (!VoiceAliases.TryRule(rule, normalized, out target))
                {
                    continue;
                }

                var action = (rule.Action ?? "").Trim();
                var verb = VerbFromAction(action);
                if (verb != VoiceVerb.Unknown)
                {
                    var effective = string.IsNullOrWhiteSpace(target) ? (rule.Target ?? "") : target;
                    return new VoiceCommand
                    {
                        Verb = verb,
                        Arg = effective,
                        Confidence = 1.0,
                        Rule = $"user:{action}",
                        Options = ResolveOptions(verb, effective, context)
                    };
                }

                // Convenience: a rule may also quote a channel name directly instead of naming a verb.
                var direct = string.IsNullOrWhiteSpace(target) ? (rule.Target ?? "") : target;
                if (!string.IsNullOrWhiteSpace(direct))
                    return new VoiceCommand
                    {
                        Verb = VoiceVerb.SwitchChannel,
                        Arg = direct,
                        Confidence = 0.9,
                        Rule = "user:channel",
                        Options = ResolveOptions(VoiceVerb.SwitchChannel, direct, context)
                    };
            }
            return null;
        }

        private static VoiceCommand? MatchBuiltIn(VoiceCommandContext context, string normalized)
        {
            if (!VoiceAliases.TryBuiltIn(normalized, out var verb, out var arg, out var ruleName)) return null;
            var options = ResolveOptions(verb, arg, context);

            // "换到线路二" names one of the current channel's streams, not a channel. When no channel
            // answers but a stream does, the stream list takes the sentence: the user should not have
            // to spell out "源" every time.
            if (verb == VoiceVerb.SwitchChannel && options.Count == 0)
            {
                var streams = BuildStreamCandidates(arg, context);
                if (streams.Count > 0)
                {
                    verb = VoiceVerb.SwitchStream;
                    options = streams;
                }
            }

            return new VoiceCommand
            {
                Verb = verb,
                Arg = arg,
                Confidence = 0.95,
                Rule = ruleName,
                Options = options
            };
        }

        private static List<VoiceOption> ResolveOptions(VoiceVerb verb, string arg, VoiceCommandContext context)
        {
            switch (verb)
            {
                case VoiceVerb.SwitchChannel:
                case VoiceVerb.ChannelStep:
                    return BuildChannelCandidates(arg, context);
                case VoiceVerb.SwitchStream:
                    return BuildStreamCandidates(arg, context);
                case VoiceVerb.LoadPlaylist:
                    return BuildPlaylistCandidates(arg, context);
                case VoiceVerb.EpgReplay:
                    return BuildReplayCandidates(arg, context);
                default:
                    return new List<VoiceOption>();
            }
        }

        // ---------------------------------------------------------------- dispatch

        private static VoiceCommandReply Dispatch(VoiceCommand command, VoiceCommandContext context, VoiceCommandReply reply)
        {
            switch (command.Verb)
            {
                case VoiceVerb.SwitchChannel: return HandleChannel(command, context, reply);
                case VoiceVerb.ChannelStep: return HandleChannelStep(command, context, reply);
                case VoiceVerb.SwitchStream: return HandleStream(command, context, reply);
                case VoiceVerb.LoadPlaylist: return HandlePlaylist(command, context, reply);
                case VoiceVerb.Play: return HandlePlayer("play", "voice_playing", "正在播放", command, context, reply);
                case VoiceVerb.Pause: return HandlePlayer("pause", "voice_paused", "已暂停", command, context, reply);
                case VoiceVerb.Stop: return HandlePlayer("stop", "voice_stopped", "已停止", command, context, reply);
                case VoiceVerb.Fullscreen: return HandlePlayer("fullscreen", "voice_fullscreen", "已切换全屏", command, context, reply);
                case VoiceVerb.Exit: return HandlePlayer("exit", "voice_exit", "正在退出播放器", command, context, reply);
                case VoiceVerb.ToggleMute: return HandleMute(command, context, reply);
                case VoiceVerb.VolumeSet: return HandleVolumeSet(command, context, reply);
                case VoiceVerb.VolumeStep: return HandleVolumeStep(command, context, reply);
                case VoiceVerb.Seek: return HandleSeek(command, context, reply);
                case VoiceVerb.EpgNow: return HandleEpgNow(command, context, reply);
                case VoiceVerb.EpgReplay: return HandleReplay(command, context, reply);
                default: return Help(context, reply);
            }
        }

        private static VoiceCommandReply Help(VoiceCommandContext context, VoiceCommandReply reply)
        {
            reply.Spoken = VoiceAnswer.NotUnderstood(VoiceAnswer.Strings(context.Lang), context.Lang);
            return reply;
        }

        private static VoiceCommandReply HandlePlayer(string action, string textKey, string fallback, VoiceCommand command,
            VoiceCommandContext context, VoiceCommandReply reply)
        {
            var strings = VoiceAnswer.Strings(context.Lang);
            reply.Success = true;
            reply.Spoken = VoiceAnswer.Text(strings, textKey, fallback);
            if (context.DryRun)
            {
                reply.Options = new List<VoiceOption> { PlainOption("player", action, new Dictionary<string, string>()) };
                return reply;
            }

            switch (action)
            {
                case "play": context.OnPlay?.Invoke(); break;
                case "pause": context.OnPause?.Invoke(); break;
                case "stop": context.OnStop?.Invoke(); break;
                case "fullscreen": context.OnFullscreen?.Invoke(); break;
                case "exit": context.OnExit?.Invoke(); break;
            }
            reply.Executed = true;
            return reply;
        }

        // ---------------------------------------------------------------- channel

        private static VoiceCommandReply HandleChannel(VoiceCommand command, VoiceCommandContext context, VoiceCommandReply reply)
        {
            var strings = VoiceAnswer.Strings(context.Lang);
            var channels = SafeList(context.Channels);
            if (channels.Count == 0) return Fail(context.Text, VerbName(command.Verb), "voice_channels_empty");

            // A plain number is a channel number: "换到21台" / "换到二十一台" / "1频道".
            if (TryNumberArg(command.Arg, out var number) && FindByNumber(number, channels) is { } byNumber)
                return ReplyChannel(byNumber, strings, context, reply, command);

            var options = command.Options.Count > 0 ? command.Options : BuildChannelCandidates(command.Arg, context);
            if (options.Count == 0) return Fail(context.Text, VerbName(command.Verb), "voice_channel_not_found", command.Arg);

            var winner = PickOption(options);
            if (winner != null) return ReplyChannel(ToChannel(winner), strings, context, reply, command);

            reply.Success = true;
            reply.Options = options;
            reply.Spoken = VoiceAnswer.Format(strings, "voice_choose_channel", "找到 {0} 个候选，请选择", options.Count);
            return reply;
        }

        private static VoiceCommandReply HandleChannelStep(VoiceCommand command, VoiceCommandContext context, VoiceCommandReply reply)
        {
            var strings = VoiceAnswer.Strings(context.Lang);
            var channels = SafeList(context.Channels);
            if (channels.Count == 0) return Fail(context.Text, VerbName(command.Verb), "voice_channels_empty");

            var currentId = context.CurrentChannel?.Invoke()?.Id ?? "";
            var index = -1;
            for (var i = 0; i < channels.Count; i++)
                if (channels[i].Id == currentId) { index = i; break; }

            VoiceChannel? target = command.Arg switch
            {
                "first" => channels[0],
                "last" => channels[channels.Count - 1],
                _ => Step(channels, index, command.Arg)
            };

            if (target == null) return Fail(context.Text, VerbName(command.Verb), "voice_no_more_channel");
            return ReplyChannel(target, strings, context, reply, command);
        }

        private static VoiceChannel? Step(IReadOnlyList<VoiceChannel> channels, int index, string arg)
        {
            var step = VoiceText.FirstNumber(arg) ?? 1;
            if (arg.StartsWith('-')) step = -step;
            if (step == 0) step = 1;
            if (index < 0) return step > 0 ? channels[0] : channels[channels.Count - 1];
            var next = index + step;
            if (next < 0 || next >= channels.Count) return null;
            return channels[next];
        }

        private static VoiceCommandReply ReplyChannel(VoiceChannel channel, Dictionary<string, string> strings,
            VoiceCommandContext context, VoiceCommandReply reply, VoiceCommand command)
        {
            var option = ChannelOption(channel, 1.0);
            reply.Success = true;
            reply.Spoken = channel.Number > 0
                ? VoiceAnswer.Format(strings, "voice_switched_number", "已切换到 {0}，{1}台", channel.Name, channel.Number)
                : VoiceAnswer.Format(strings, "voice_switched", "已切换到 {0}", channel.Name);

            if (context.DryRun)
            {
                reply.Options = new List<VoiceOption> { option };
                return reply;
            }

            if (context.OnSwitchChannel != null)
            {
                context.OnSwitchChannel(channel.Id);
                reply.Executed = true;
                reply.Options = new List<VoiceOption> { option };
                return reply;
            }

            reply.Options = new List<VoiceOption> { option };
            return reply;
        }

        private static bool TryNumberArg(string arg, out int number)
        {
            number = 0;
            if (string.IsNullOrEmpty(arg)) return false;
            var value = VoiceText.HanToArabicDigits(arg);
            return value.Length is > 0 and <= 4 && value.All(char.IsDigit)
                   && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }

        /// <summary>Resolves a spoken number to a channel: the numbering map first, then prefixed names.</summary>
        private static VoiceChannel? FindByNumber(int number, IReadOnlyList<VoiceChannel> channels)
        {
            foreach (var channel in channels)
                if (channel.Number == number) return channel;

            var prefix = number.ToString(CultureInfo.InvariantCulture);
            foreach (var channel in channels)
            {
                var normalized = VoiceText.Normalize(channel.Name);
                if (normalized.StartsWith(prefix, StringComparison.Ordinal) && normalized.Length > prefix.Length)
                    return channel;
            }
            return null;
        }

        private static List<VoiceOption> BuildChannelCandidates(string arg, VoiceCommandContext context)
        {
            var channels = SafeList(context.Channels);
            if (channels.Count == 0 || string.IsNullOrEmpty(arg)) return new List<VoiceOption>();

            // Keep the whole sentence for scoring, but also try the noise-stripped name.
            var cleaned = VoiceAliases.StripFiller(arg, NoiseWords);
            var scored = new List<(VoiceChannel Channel, double Score)>();
            foreach (var channel in channels)
            {
                var score = Math.Max(VoiceFuzzy.Score(arg, channel.Name), VoiceFuzzy.Score(cleaned, channel.Name));
                if (score < VoiceFuzzy.CandidateScore) continue;
                scored.Add((channel, score));
            }

            return scored
                .OrderByDescending(pair => pair.Score)
                .ThenBy(pair => pair.Channel.Number == 0 ? int.MaxValue : pair.Channel.Number)
                .Take(MaxOptions)
                .Select(pair => ChannelOption(pair.Channel, Math.Round(pair.Score, 3)))
                .ToList();
        }

        private static VoiceChannel ToChannel(VoiceOption option) => new()
        {
            Id = option.Id,
            Name = option.Title,
            Number = option.Number
        };

        private static VoiceOption ChannelOption(VoiceChannel channel, double score) => new()
        {
            Kind = VoiceKind.Channel,
            Id = channel.Id,
            Title = channel.Name,
            Subtitle = channel.Number > 0
                ? (string.IsNullOrEmpty(channel.Group) ? $"{channel.Number}台" : $"{channel.Number}台 · {channel.Group}")
                : channel.Group,
            Number = channel.Number,
            Score = score,
            Action = "channel",
            Payload = new Dictionary<string, string> { ["channelId"] = channel.Id }
        };

        /// <summary>The single clear winner of a candidate list, or null when the user has to choose.</summary>
        private static VoiceOption? PickOption(IReadOnlyList<VoiceOption> options)
        {
            if (options.Count == 0) return null;
            var top = options[0];
            if (top.Score < VoiceFuzzy.AutoExecuteScore) return null;
            if (options.Count > 1 && top.Score - options[1].Score < 0.08) return null;
            return top;
        }

        // ---------------------------------------------------------------- stream ("源" = stream variant)

        private static VoiceCommandReply HandleStream(VoiceCommand command, VoiceCommandContext context, VoiceCommandReply reply)
        {
            var strings = VoiceAnswer.Strings(context.Lang);
            var arg = command.Arg ?? "";
            var streams = SafeList(context.CurrentStreams);
            reply.Success = true;

            // "换个源" / "换下一路" / "上一路" always mean: walk the stream list of the current channel.
            if (string.IsNullOrEmpty(arg) || arg is "next" or "prev")
            {
                var action = arg == "prev" ? "prevSource" : "nextSource";
                reply.Spoken = VoiceAnswer.Text(strings, "voice_switching_source", "正在切换源");
                if (context.DryRun)
                {
                    reply.Options = new List<VoiceOption> { PlainOption("stream", action, new Dictionary<string, string>()) };
                    return reply;
                }
                context.OnNextStream?.Invoke(arg == "prev" ? -1 : 1);
                reply.Executed = true;
                return reply;
            }

            if (TryNumberArg(arg, out var index) && index >= 1 && index <= streams.Count)
            {
                var stream = streams[index - 1];
                return ReplyStream(stream, strings, context, reply);
            }

            var options = command.Options.Count > 0 ? command.Options : BuildStreamCandidates(arg, context);
            var winner = options.Count == 1 ? options[0] : PickOption(options);
            if (winner == null)
            {
                reply.Options = options;
                reply.Success = options.Count > 0;
                reply.Spoken = options.Count > 0
                    ? VoiceAnswer.Format(strings, "voice_choose_source", "找到 {0} 个源，请选择", options.Count)
                    : VoiceAnswer.NotUnderstood(strings, context.Lang);
                return reply;
            }

            return ReplyStream(new VoiceStream { Id = winner.Id, Name = winner.Title, Extra = winner.Subtitle }, strings, context, reply);
        }

        private static VoiceCommandReply ReplyStream(VoiceStream stream, Dictionary<string, string> strings,
            VoiceCommandContext context, VoiceCommandReply reply)
        {
            reply.Success = true;
            reply.Spoken = VoiceAnswer.Format(strings, "voice_source_switched", "已切换到源 {0}", stream.Name);
            var option = new VoiceOption
            {
                Kind = VoiceKind.Stream,
                Id = stream.Id,
                Title = stream.Name,
                Subtitle = stream.Extra,
                Action = "selectSource",
                Payload = new Dictionary<string, string> { ["name"] = stream.Name }
            };
            reply.Options = new List<VoiceOption> { option };
            if (context.DryRun) return reply;
            if (context.OnSelectedStream != null)
            {
                // The name is what both the page payload and the manager's stream lookup agree on.
                context.OnSelectedStream(stream.Name);
                reply.Executed = true;
            }
            return reply;
        }

        private static List<VoiceOption> BuildStreamCandidates(string arg, VoiceCommandContext context)
        {
            var result = new List<VoiceOption>();
            foreach (var stream in SafeList(context.CurrentStreams))
            {
                var score = string.IsNullOrEmpty(arg) ? 0.7 : VoiceFuzzy.Best(arg, new[] { stream.Name, stream.Extra });
                if (score < VoiceFuzzy.CandidateScore) continue;
                result.Add(new VoiceOption
                {
                    Kind = VoiceKind.Stream,
                    Id = stream.Id,
                    Title = stream.Name,
                    Subtitle = stream.Extra,
                    Score = Math.Round(score, 3),
                    Action = "selectSource",
                    Payload = new Dictionary<string, string> { ["name"] = stream.Name }
                });
            }
            return result.OrderByDescending(o => o.Score).Take(MaxOptions).ToList();
        }

        // ---------------------------------------------------------------- channel data source (playlist)

        private static VoiceCommandReply HandlePlaylist(VoiceCommand command, VoiceCommandContext context, VoiceCommandReply reply)
        {
            var strings = VoiceAnswer.Strings(context.Lang);
            var options = command.Options.Count > 0 ? command.Options : BuildPlaylistCandidates(command.Arg, context);
            var winner = options.Count == 1 ? options[0] : PickOption(options);
            if (winner == null)
            {
                reply.Options = options;
                reply.Success = options.Count > 0;
                reply.Spoken = options.Count > 0
                    ? VoiceAnswer.Format(strings, "voice_choose_playlist", "找到 {0} 个频道数据源，请选择", options.Count)
                    : VoiceAnswer.NotUnderstood(strings, context.Lang);
                return reply;
            }

            reply.Success = true;
            reply.Spoken = VoiceAnswer.Format(strings, "voice_playlist_switched", "已切换到 {0}", winner.Title);
            reply.Options = new List<VoiceOption> { winner };
            if (context.DryRun) return reply;
            context.OnLoadPlaylist?.Invoke(winner.Id);
            reply.Executed = true;
            return reply;
        }

        private static List<VoiceOption> BuildPlaylistCandidates(string arg, VoiceCommandContext context)
        {
            var options = new List<VoiceOption>();
            foreach (var playlist in SafeList(context.Playlists))
            {
                var score = string.IsNullOrEmpty(arg) ? 0.7 : VoiceFuzzy.Best(arg, new[] { playlist.Name });
                if (score < VoiceFuzzy.CandidateScore) continue;
                options.Add(new VoiceOption
                {
                    Kind = VoiceKind.Playlist,
                    Id = playlist.Id,
                    Title = playlist.Name,
                    Subtitle = playlist.Extra,
                    Score = Math.Round(score, 3),
                    // "selectSource" is the action the page already uses for this list, so a tapped row
                    // goes through the existing handler instead of a new one.
                    Action = "selectSource",
                    Payload = new Dictionary<string, string> { ["name"] = playlist.Name, ["url"] = playlist.Id }
                });
            }
            return options.OrderByDescending(o => o.Score).Take(MaxOptions).ToList();
        }

        // ---------------------------------------------------------------- volume / mute / seek

        private static VoiceCommandReply HandleMute(VoiceCommand command, VoiceCommandContext context, VoiceCommandReply reply)
        {
            var strings = VoiceAnswer.Strings(context.Lang);
            var wantUnmute = command.Rule is "unmute" or "mute_off";
            reply.Success = true;
            if (wantUnmute)
            {
                reply.Spoken = VoiceAnswer.Text(strings, "voice_unmuted", "已打开声音");
                if (!context.DryRun)
                {
                    if (context.IsMuted?.Invoke() ?? false) context.OnVolume?.Invoke(context.Volume?.Invoke() ?? 70);
                    reply.Executed = true;
                }
                return reply;
            }

            if (context.DryRun)
            {
                reply.Options = new List<VoiceOption> { PlainOption("player", "mute", new Dictionary<string, string>()) };
                reply.Spoken = VoiceAnswer.Text(strings, "voice_muted", "已静音");
                return reply;
            }

            // A muted player unmutes again, which is what "静音" means on a remote with one button.
            if (context.IsMuted?.Invoke() ?? false)
            {
                context.OnToggleMute?.Invoke();
                reply.Spoken = VoiceAnswer.Text(strings, "voice_unmuted", "已打开声音");
            }
            else
            {
                context.OnToggleMute?.Invoke();
                reply.Spoken = VoiceAnswer.Text(strings, "voice_muted", "已静音");
            }
            reply.Executed = true;
            return reply;
        }

        private static VoiceCommandReply HandleVolumeSet(VoiceCommand command, VoiceCommandContext context, VoiceCommandReply reply)
        {
            var strings = VoiceAnswer.Strings(context.Lang);
            var value = Math.Clamp(VoiceText.FirstNumber(command.Arg) ?? 50, 0, 100);
            reply.Success = true;
            reply.Spoken = VoiceAnswer.Format(strings, "voice_volume_set", "音量已调到 {0}", value);
            if (context.DryRun)
            {
                reply.Options = new List<VoiceOption>
                {
                    PlainOption("player", "volume", new Dictionary<string, string> { ["value"] = value.ToString(CultureInfo.InvariantCulture) })
                };
                return reply;
            }
            context.OnVolume?.Invoke(value);
            reply.Executed = true;
            return reply;
        }

        /// <summary>Words that ask for a quieter volume, and therefore for a negative step.</summary>
        private static readonly string[] VolumeDownWords =
            { "小点", "小声", "小一点", "降低", "减小", "调小", "down", "decrease", "уменьши", "убавь", "тише", "quieter" };

        private static bool MentionsVolumeDown(string text)
            => !string.IsNullOrEmpty(text) && VolumeDownWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

        private static VoiceCommandReply HandleVolumeStep(VoiceCommand command, VoiceCommandContext context, VoiceCommandReply reply)
        {
            var strings = VoiceAnswer.Strings(context.Lang);
            var arg = command.Arg ?? "";
            var step = Math.Abs(VoiceText.FirstNumber(arg) ?? (int)Math.Round(context.VolumeStep));
            if (step == 0) step = 10;

            // "小点声" carries no number, so the direction comes from the wording, not from the sign.
            var down = command.Rule == "volume_down" || arg.StartsWith('-') || MentionsVolumeDown(context.Text ?? "");
            if (down) step = -step;

            var current = context.Volume?.Invoke() ?? 50;
            var value = Math.Clamp(current + step, 0, 100);
            var option = PlainOption("player", step > 0 ? "volumeUp" : "volumeDown",
                new Dictionary<string, string> { ["step"] = Math.Abs(step).ToString(CultureInfo.InvariantCulture) });
            reply.Success = true;
            reply.Spoken = VoiceAnswer.Format(strings, "voice_volume_set", "音量已调到 {0}", (int)Math.Round(value));
            reply.Options = new List<VoiceOption> { option };
            if (context.DryRun) return reply;
            context.OnVolume?.Invoke(value);
            reply.Executed = true;
            return reply;
        }

        private static VoiceCommandReply HandleSeek(VoiceCommand command, VoiceCommandContext context, VoiceCommandReply reply)
        {
            var strings = VoiceAnswer.Strings(context.Lang);
            var arg = command.Arg ?? "";
            var seconds = Math.Abs(VoiceText.FirstNumber(arg) ?? 10);
            if (seconds == 0) seconds = 10;

            // "后退 30 秒" carries the direction in the rule, not in the number.
            if (command.Rule == "seek_back" || arg.StartsWith('-')) seconds = -seconds;
            seconds = Math.Clamp(seconds, -MaxStepSeconds, MaxStepSeconds);

            reply.Success = true;
            reply.Spoken = seconds < 0
                ? VoiceAnswer.Format(strings, "voice_seek_back", "已后退 {0} 秒", Math.Abs(seconds))
                : VoiceAnswer.Format(strings, "voice_seek_forward", "已快进 {0} 秒", seconds);
            reply.Options = new List<VoiceOption>
            {
                PlainOption("player", seconds < 0 ? "seekBack" : "seekForward",
                    new Dictionary<string, string> { ["seconds"] = Math.Abs(seconds).ToString(CultureInfo.InvariantCulture) })
            };
            if (context.DryRun) return reply;
            context.OnSeek?.Invoke(seconds);
            reply.Executed = true;
            return reply;
        }

        // ---------------------------------------------------------------- EPG

        private static VoiceCommandReply HandleEpgNow(VoiceCommand command, VoiceCommandContext context, VoiceCommandReply reply)
        {
            var strings = VoiceAnswer.Strings(context.Lang);
            var channel = ResolveChannelForEpg(command.Arg, context);
            if (channel == null) return Fail(context.Text, VerbName(command.Verb), "voice_no_channel", context.Lang);

            var program = context.EpgNow?.Invoke(channel.Id);
            if (program == null) return Fail(context.Text, VerbName(command.Verb), "voice_no_epg", context.Lang, channel.Name);

            reply.Success = true;
            reply.Spoken = VoiceAnswer.Format(strings, "voice_epg_now", "{0} 正在播出 {1}（{2}-{3}）",
                channel.Name, program.Title, program.Start.ToString("HH:mm"), program.End.ToString("HH:mm"));
            reply.Options = new List<VoiceOption>
            {
                new()
                {
                    Kind = VoiceKind.Program,
                    Id = channel.Id,
                    Title = program.Title,
                    Subtitle = VoiceAnswer.TimeRange(program.Start, program.End),
                    Score = 1.0,
                    Action = "replayProgram",
                    Payload = ReplayPayload(channel, program)
                }
            };
            return reply;
        }

        private static VoiceCommandReply HandleReplay(VoiceCommand command, VoiceCommandContext context, VoiceCommandReply reply)
        {
            var strings = VoiceAnswer.Strings(context.Lang);
            var window = VoiceTimeParser.Parse(VoiceText.Normalize(context.Text), context.Lang, DateTime.Now);

            // "回看今天有什么节目" names no channel on purpose — that is a question about every channel,
            // so the search only narrows down when the sentence (or the play state) really picked one.
            var named = TryResolveNamedChannel(command.Arg, context);
            var title = TitleFromArg(command.Arg);

            // "在 CCTV-1 回看今天的节目" leaves the channel name behind: that is not a programme title,
            // so anything the channel itself is called is cut out of the filter before it is used.
            if (named != null && title.Length > 0) title = StripChannelMention(title, named);

            var hits = new List<(VoiceChannel Channel, VoiceProgram Program)>();
            var shown = new HashSet<string>();

            if (named != null)
            {
                var before = hits.Count;
                Collect(hits, named, context, window, title.Length > 0 ? title : null);

                // A wrong title guess must not answer with nothing: when the filter killed every
                // programme, fall back to the channel's own window.
                if (hits.Count == before && title.Length > 0)
                {
                    title = "";
                    Collect(hits, named, context, window, null);
                }
            }
            else
            {
                foreach (var channel in SafeList(context.Channels))
                {
                    var added = Collect(hits, channel, context, window, title);
                    if (added == null) continue;
                    for (var i = added.Count - 1; i >= 0; i--)
                    {
                        var key = $"{channel.Id}|{added[i].Start.Ticks}|{added[i].Title}";
                        if (shown.Add(key)) continue;
                        hits.RemoveAt(hits.Count - added.Count + i);
                    }
                }
            }

            var options = hits
                .OrderByDescending(hit => hit.Program.Start)
                .Take(MaxOptions)
                .Select(hit => new VoiceOption
                {
                    Kind = VoiceKind.Program,
                    Id = hit.Channel.Id,
                    ChannelId = hit.Channel.Id,
                    Title = hit.Program.Title,
                    Subtitle = string.IsNullOrEmpty(hit.Program.ChannelName)
                        ? VoiceAnswer.TimeRange(hit.Program.Start, hit.Program.End)
                        : $"{hit.Program.ChannelName} · {VoiceAnswer.TimeRange(hit.Program.Start, hit.Program.End)}",
                    Score = 0.9,
                    Action = "replayProgram",
                    Payload = ReplayPayload(hit.Channel, hit.Program)
                })
                .ToList();

            if (options.Count == 0)
            {
                if (title.Length > 0)
                    return Fail(context.Text, VerbName(command.Verb), "voice_replay_no_title", context.Lang, title);

                // Nothing in the window: offer the channels so the turn still ends with a choice.
                reply.Success = true;
                reply.Spoken = $"{window.Label}：{VoiceAnswer.Text(strings, "voice_replay_ask_channel", "请说频道名或节目名")}";
                reply.Options = AskChannelOptions(context);
                if (reply.Options.Count > 0) return reply;
                return Fail(context.Text, VerbName(command.Verb), "voice_replay_empty", context.Lang, window.Label);
            }

            reply.Options = options;

            // One hit is not a choice: start it right away unless the user asked to confirm first.
            if (options.Count == 1 && context.AutoReplaySingle && context.OnReplayProgram != null && !context.DryRun)
            {
                var only = options[0];
                context.OnReplayProgram(only.ChannelId, only.Title, only.Payload["start"], only.Payload["end"]);
                reply.Success = true;
                reply.Executed = true;
                reply.Spoken = VoiceAnswer.Format(strings, "voice_replay_playing", "正在回看 {0}", only.Title);
                return reply;
            }

            reply.Success = true;
            reply.Spoken = title.Length > 0
                ? VoiceAnswer.Format(strings, "voice_replay_found", "找到 {0} 个和 {1} 有关的节目，请选择", options.Count, title)
                : $"{window.Label}：{VoiceAnswer.ReplaySummary(strings, window.Label, options.Count)}";
            return reply;
        }

        /// <summary>
        /// Adds the programmes of one channel inside the window to <paramref name="hits"/>. A title
        /// filter that matches nothing keeps the channel only when the window itself has programmes,
        /// so a wrong title guess does not silently answer with everything.
        /// </summary>
        private static List<VoiceProgram>? Collect(List<(VoiceChannel Channel, VoiceProgram Program)> hits,
            VoiceChannel channel, VoiceCommandContext context, VoiceTimeWindow window, string? title)
        {
            var programs = SafePrograms(context.CatchupPrograms, channel.Id, window.Start, window.End);
            if (programs.Count == 0) return null;

            if (!string.IsNullOrEmpty(title))
            {
                var matched = programs
                    .Where(p => VoiceFuzzy.Score(title, p.Title) >= VoiceFuzzy.CandidateScore || p.Title.Contains(title, StringComparison.Ordinal))
                    .Select(p => string.IsNullOrEmpty(p.ChannelName) ? WithChannel(p, channel.Name) : p)
                    .ToList();
                if (matched.Count == 0) return null;
                foreach (var program in matched) hits.Add((channel, program));
                return matched;
            }

            foreach (var program in programs)
                hits.Add((channel, string.IsNullOrEmpty(program.ChannelName) ? WithChannel(program, channel.Name) : program));
            return programs;
        }

        private static VoiceProgram WithChannel(VoiceProgram program, string channelName) => new()
        {
            Title = program.Title,
            Start = program.Start,
            End = program.End,
            ChannelName = program.ChannelName.Length > 0 ? program.ChannelName : channelName
        };

        /// <summary>
        /// Removes the words that only named the channel from a leftover title, so "在 CCTV-1 回看今天的
        /// 节目" filters on nothing instead of filtering on "cctv1".
        /// </summary>
        private static string StripChannelMention(string title, VoiceChannel channel)
        {
            if (string.IsNullOrEmpty(title) || channel == null) return title;

            var value = VoiceText.Compact(VoiceText.Normalize(title));
            foreach (var mention in ChannelMentions(channel).OrderByDescending(m => m.Length))
                value = value.Replace(mention, "", StringComparison.Ordinal);

            // Whatever is left over from "在 … 台/频道" is scaffolding, not a title.
            value = System.Text.RegularExpressions.Regex.Replace(value, "[在从用把给的台频道]", " ");
            return VoiceText.Compact(value);
        }

        /// <summary>Every spelling a channel answers to: full name, each word of it and its number.</summary>
        private static IEnumerable<string> ChannelMentions(VoiceChannel channel)
        {
            var full = VoiceText.Compact(VoiceText.Normalize(channel.Name));
            if (full.Length > 0) yield return full;

            var parts = full.Replace("频道", "", StringComparison.Ordinal).Replace("台", "", StringComparison.Ordinal);
            if (parts.Length > 0 && parts != full) yield return parts;

            foreach (var token in System.Text.RegularExpressions.Regex.Split(channel.Name ?? "", "[^0-9A-Za-z\\u4e00-\\u9fff]+"))
            {
                var compact = VoiceText.Compact(VoiceText.Normalize(token));
                if (compact.Length > 0 && compact != full) yield return compact;
            }

            if (channel.Number > 0) yield return channel.Number.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Channel rows the page can show when the sentence did not say which channel.</summary>
        private static List<VoiceOption> AskChannelOptions(VoiceCommandContext context) => SafeList(context.Channels)
            .Where(c => c.Number > 0)
            .OrderBy(c => c.Number)
            .Take(MaxOptions)
            .Select(c => new VoiceOption
            {
                Kind = VoiceKind.Channel,
                Id = c.Id,
                Title = c.Name,
                Subtitle = c.Number > 0 ? c.Number.ToString(CultureInfo.InvariantCulture) : "",
                Number = c.Number,
                Score = 0.5,
                Action = "channel",
                Payload = new Dictionary<string, string> { ["channelId"] = c.Id }
            })
            .ToList();

        /// <summary>What is left of the sentence once the "回看 / 今天" scaffolding is removed.</summary>
        private static string TitleFromArg(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg)) return "";
            var cleaned = VoiceText.Normalize(arg);
            foreach (var word in NoiseWords)
                cleaned = cleaned.Replace(word, " ", StringComparison.Ordinal);
            foreach (var word in TimeWords.Day) cleaned = cleaned.Replace(word, " ", StringComparison.Ordinal);
            foreach (var word in TimeWords.Parts) cleaned = cleaned.Replace(word, " ", StringComparison.Ordinal);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "[\\?？。！!，,、的了吗呢啊有没有什么哪些可以能]", " ");
            return cleaned.Trim();
        }

        private static List<VoiceOption> BuildReplayCandidates(string arg, VoiceCommandContext context)
        {
            var channel = ResolveChannelForEpg(arg, context);
            if (channel == null) return new List<VoiceOption>();
            var window = VoiceTimeParser.Parse(arg, context.Lang, DateTime.Now);
            return SafePrograms(context.CatchupPrograms, channel.Id, window.Start, window.End)
                .OrderByDescending(p => p.Start)
                .Take(MaxOptions)
                .Select(p => new VoiceOption
                {
                    Kind = VoiceKind.Program,
                    Id = channel.Id,
                    ChannelId = channel.Id,
                    Title = p.Title,
                    Subtitle = VoiceAnswer.TimeRange(p.Start, p.End),
                    Score = 0.9,
                    Action = "replayProgram",
                    Payload = ReplayPayload(channel, p)
                })
                .ToList();
        }

        private static Dictionary<string, string> ReplayPayload(VoiceChannel channel, VoiceProgram program) => new()
        {
            ["channelId"] = channel.Id,
            ["programTitle"] = program.Title,
            ["start"] = program.Start.ToString("o", CultureInfo.InvariantCulture),
            ["end"] = program.End.ToString("o", CultureInfo.InvariantCulture)
        };

        /// <summary>The channel an EPG question is about: one named in the sentence, else the playing one.</summary>
        private static VoiceChannel? ResolveChannelForEpg(string arg, VoiceCommandContext context)
            => TryResolveNamedChannel(arg, context) ?? CurrentChannel(context);

        /// <summary>
        /// The channel named in the sentence, or null when the user named none. Callers that want a
        /// sensible default use <see cref="ResolveChannelForEpg"/>; callers that must scan everything
        /// ("回看今天有什么节目") branch on null instead.
        /// </summary>
        private static VoiceChannel? TryResolveNamedChannel(string arg, VoiceCommandContext context)
        {
            if (string.IsNullOrWhiteSpace(arg)) return null;

            var channels = SafeList(context.Channels);
            if (channels.Count == 0) return null;

            var cleaned = VoiceAliases.StripFiller(arg, NoiseWords)
                .Replace("频道", " ").Replace('台', ' ').Trim();
            if (TryNumberArg(cleaned, out var number) && FindByNumber(number, channels) is { } byNumber)
                return byNumber;

            var best = channels
                .Select(c => (Channel: c, Score: Math.Max(VoiceFuzzy.Score(cleaned, c.Name), VoiceFuzzy.Score(arg, c.Name))))
                .OrderByDescending(pair => pair.Score)
                .FirstOrDefault();
            return best.Channel != null && best.Score >= VoiceFuzzy.CandidateScore ? best.Channel : null;
        }

        /// <summary>The channel to fall back to when a sentence names none.</summary>
        private static VoiceChannel? CurrentChannel(VoiceCommandContext context)
        {
            var channels = SafeList(context.Channels);
            if (channels.Count == 0) return context.CurrentChannel?.Invoke();

            var current = context.CurrentChannel?.Invoke();
            if (current != null)
            {
                var match = channels.FirstOrDefault(c => c.Id == current.Id);
                return match ?? current;
            }
            return channels[0];
        }

        // ---------------------------------------------------------------- shared helpers

        private static VoiceOption PlainOption(string kind, string action, Dictionary<string, string> payload) => new()
        {
            Kind = kind == "player" ? VoiceKind.Player : VoiceKind.Stream,
            Title = action,
            Action = action,
            Payload = payload
        };

        private static List<VoiceProgram> SafePrograms(Func<string, DateTime, DateTime, IReadOnlyList<VoiceProgram>>? provider,
            string channelId, DateTime start, DateTime end)
        {
            if (provider == null) return new List<VoiceProgram>();
            try { return provider(channelId, start, end)?.ToList() ?? new List<VoiceProgram>(); }
            catch { return new List<VoiceProgram>(); }
        }

        private static VoiceCommandReply Fail(string text, string verb, string textKey, string? lang = null, params object[] args)
        {
            var strings = WebRemoteStrings.Get(string.IsNullOrWhiteSpace(lang) ? "zh-CN" : lang);
            var reply = new VoiceCommandReply { Success = false, Verb = verb, Text = text };
            reply.Spoken = args.Length > 0
                ? VoiceAnswer.Format(strings, textKey, textKey, args)
                : VoiceAnswer.Text(strings, textKey, textKey);
            return reply;
        }

        private static string VerbName(VoiceVerb verb) => verb switch
        {
            VoiceVerb.SwitchChannel => "channel",
            VoiceVerb.ChannelStep => "channel_step",
            VoiceVerb.SwitchStream => "switch_stream",
            VoiceVerb.LoadPlaylist => "playlist",
            VoiceVerb.Play => "play",
            VoiceVerb.Pause => "pause",
            VoiceVerb.Stop => "stop",
            VoiceVerb.ToggleMute => "mute",
            VoiceVerb.VolumeSet => "volume_set",
            VoiceVerb.VolumeStep => "volume_step",
            VoiceVerb.Fullscreen => "fullscreen",
            VoiceVerb.Exit => "exit",
            VoiceVerb.Seek => "seek",
            VoiceVerb.EpgNow => "epg_now",
            VoiceVerb.EpgReplay => "epg_replay",
            _ => "unknown"
        };

        private static VoiceVerb VerbFromAction(string? action) => (action ?? "").Trim().ToLowerInvariant() switch
        {
            "channel" or "switch_channel" or "channel_number" => VoiceVerb.SwitchChannel,
            "channel_step" => VoiceVerb.ChannelStep,
            "switch_stream" or "source" or "next_source" => VoiceVerb.SwitchStream,
            "playlist" or "load_m3u" or "m3u" => VoiceVerb.LoadPlaylist,
            "play" or "resume" => VoiceVerb.Play,
            "pause" => VoiceVerb.Pause,
            "stop" => VoiceVerb.Stop,
            "mute" or "toggle_mute" => VoiceVerb.ToggleMute,
            "volume_set" or "volume" => VoiceVerb.VolumeSet,
            "volume_step" or "volume_up" or "volume_down" => VoiceVerb.VolumeStep,
            "fullscreen" => VoiceVerb.Fullscreen,
            "exit" => VoiceVerb.Exit,
            "seek" => VoiceVerb.Seek,
            "epg_now" or "epg" => VoiceVerb.EpgNow,
            "epg_replay" or "replay" => VoiceVerb.EpgReplay,
            _ => VoiceVerb.Unknown
        };

        private static IReadOnlyList<T> SafeList<T>(Func<IReadOnlyList<T>>? provider)
        {
            if (provider == null) return Array.Empty<T>();
            try { return provider() ?? Array.Empty<T>(); }
            catch { return Array.Empty<T>(); }
        }
    }
}

using System;
using System.Collections.Generic;

namespace LibmpvIptvClient.Services.WebRemote.Voice
{
    /// <summary>What the user asked for. Adding a verb means a case in both the alias table and the router.</summary>
    public enum VoiceVerb
    {
        Unknown = 0,
        SwitchChannel,
        ChannelStep,
        SwitchStream,
        LoadPlaylist,
        Play,
        Pause,
        Stop,
        ToggleMute,
        VolumeSet,
        VolumeStep,
        Fullscreen,
        Exit,
        Seek,
        EpgNow,
        EpgReplay
    }

    /// <summary>Candidate category, mirrored to the page so it can pick an icon and the right action.</summary>
    public enum VoiceKind
    {
        Channel = 0,
        Stream,
        Playlist,
        Program,
        Player
    }

    /// <summary>Channel projected for voice matching; numbering comes from the caller (BuildChannelNumbers).</summary>
    public sealed class VoiceChannel
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Number { get; set; }
        public string Group { get; set; } = "";
    }

    /// <summary>One stream variant of the current channel: what "换个源" walks through.</summary>
    public sealed class VoiceStream
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Extra { get; set; } = "";
    }

    /// <summary>One channel data source (m3u), i.e. the "频道列表" the web remote can load.</summary>
    public sealed class VoicePlaylist
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Extra { get; set; } = "";
    }

    /// <summary>One programme inside a catch-up window.</summary>
    public sealed class VoiceProgram
    {
        public string Title { get; set; } = "";
        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        /// <summary>Which channel the programme belongs to, used when a search spans every channel.</summary>
        public string ChannelName { get; set; } = "";
    }

    /// <summary>A possible interpretation shown as a tappable row when the sentence is ambiguous.</summary>
    public sealed class VoiceOption
    {
        public VoiceKind Kind { get; set; }
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public int Number { get; set; }
        public double Score { get; set; }

        /// <summary>Set when the row belongs to a channel other than the current one.</summary>
        public string ChannelId { get; set; } = "";

        /// <summary>Remote action the page sends back when this row is tapped, e.g. "channel".</summary>
        public string Action { get; set; } = "";

        /// <summary>Payload for <see cref="Action"/>; keys literally match the action handler's names.</summary>
        public Dictionary<string, string> Payload { get; set; } = new();
    }

    /// <summary>One parse of one sentence.</summary>
    public sealed class VoiceCommand
    {
        public VoiceVerb Verb { get; set; }
        public string Arg { get; set; } = "";
        public double Confidence { get; set; }
        public string Rule { get; set; } = "";
        public List<VoiceOption> Options { get; set; } = new();
    }

    /// <summary>What the page renders and reads out. Serialised as the ws payload for "voiceCommand".</summary>
    public sealed class VoiceCommandReply
    {
        /// <summary>Transport marker: the page routes a message to the voice box only when this says "voice".</summary>
        public string Type { get; set; } = "voice";

        public string Verb { get; set; } = "unknown";
        public string Rule { get; set; } = "";
        public double Confidence { get; set; }
        public bool Success { get; set; }
        public bool Executed { get; set; }
        public string Text { get; set; } = "";

        /// <summary>Sentence shown on the page and spoken by speech synthesis.</summary>
        public string Spoken { get; set; } = "";

        public List<VoiceOption> Options { get; set; } = new();
    }

    /// <summary>
    /// Everything the router may need to read (data) or write (callbacks). The callbacks are the seam that
    /// keeps the router free of mpv/WPF types, and that lets tests run whole sentences without a player.
    /// </summary>
    public sealed class VoiceCommandContext
    {
        public string Text { get; set; } = "";
        public string Lang { get; set; } = "zh-CN";

        /// <summary>Endpoint/script mode: produce the action payload instead of touching the player.</summary>
        public bool DryRun { get; set; }

        /// <summary>Percent one bare "大声点" moves the volume when the sentence names no amount.</summary>
        public double VolumeStep { get; set; } = 10;

        /// <summary>Start a catch-up programme straight away when the sentence matches exactly one.</summary>
        public bool AutoReplaySingle { get; set; } = true;

        public Func<IReadOnlyList<VoiceChannel>>? Channels { get; set; }
        public Func<IReadOnlyList<VoiceStream>>? CurrentStreams { get; set; }
        public Func<IReadOnlyList<VoicePlaylist>>? Playlists { get; set; }
        public Func<VoiceChannel?>? CurrentChannel { get; set; }
        public Func<bool>? IsMuted { get; set; }
        public Func<double>? Volume { get; set; }
        /// <summary>What <paramref name="channelId"/> is showing right now, or null when the EPG has nothing.</summary>
        public Func<string, VoiceProgram?>? EpgNow { get; set; }

        /// <summary>Programmes of <c>channelId</c> inside [start, end); the EPG service answers from memory.</summary>
        public Func<string, DateTime, DateTime, IReadOnlyList<VoiceProgram>>? CatchupPrograms { get; set; }
        public Func<IReadOnlyList<VoiceRule>>? UserRules { get; set; }

        public Action<string>? OnSwitchChannel { get; set; }
        public Action<string>? OnSelectedStream { get; set; }

        /// <summary>+1 next stream, -1 previous stream of the current channel.</summary>
        public Action<int>? OnNextStream { get; set; }
        public Action<string>? OnLoadPlaylist { get; set; }
        public Action<string, string, string, string>? OnReplayProgram { get; set; }
        public Action<double>? OnVolume { get; set; }
        public Action? OnToggleMute { get; set; }
        public Action<double>? OnSeek { get; set; }
        public Action? OnPlay { get; set; }
        public Action? OnPause { get; set; }
        public Action? OnStop { get; set; }
        public Action? OnFullscreen { get; set; }
        public Action? OnExit { get; set; }
    }
}

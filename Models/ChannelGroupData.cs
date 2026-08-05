using System.Collections.Generic;
using LibmpvIptvClient.Models;

namespace LibmpvIptvClient
{
    public sealed class ChannelGroupData
    {
        public string Name { get; set; } = "";
        public List<Channel> Channels { get; set; } = new List<Channel>();
    }
}
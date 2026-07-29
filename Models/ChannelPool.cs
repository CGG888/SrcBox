using System;
using System.Collections.Generic;
using System.Threading;

namespace LibmpvIptvClient.Models
{
    public static class ChannelPool
    {
        private static readonly Stack<Channel> _pool = new();
        private static int _count;
        private const int MaxPoolSize = 2000;

        public static Channel Rent()
        {
            lock (_pool)
            {
                if (_pool.Count > 0)
                {
                    _count--;
                    return _pool.Pop();
                }
            }
            return new Channel();
        }

        public static void Return(Channel channel)
        {
            if (channel == null) return;
            Reset(channel);
            lock (_pool)
            {
                if (_pool.Count < MaxPoolSize)
                {
                    _pool.Push(channel);
                    _count++;
                }
            }
        }

        public static void Reset(Channel channel)
        {
            channel.Reset();
        }

        public static int PoolCount => _count;
    }
}

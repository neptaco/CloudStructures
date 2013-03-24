﻿using BookSleeve;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloudStructures.Redis
{
    public class RedisString<T>
    {
        public string Key { get; private set; }
        readonly RedisSettings settings;
        readonly Func<T> valueFactory;
        readonly int? expirySeconds;

        public RedisString(RedisSettings settings, string stringKey, Func<T> valueFactoryIfNotExists = null, int? expirySeconds = null)
        {
            this.settings = settings;
            this.Key = stringKey;
            this.valueFactory = valueFactoryIfNotExists;
            this.expirySeconds = expirySeconds;
        }

        public RedisString(RedisGroup connectionGroup, string stringKey, Func<T> valueFactoryIfNotExists = null, int? expirySeconds = null)
            : this(connectionGroup.GetSettings(stringKey), stringKey, valueFactoryIfNotExists, expirySeconds)
        {
        }

        protected RedisConnection Connection
        {
            get
            {
                return settings.GetConnection();
            }
        }

        protected IStringCommands Command
        {
            get
            {
                return Connection.Strings;
            }
        }

        public virtual async Task<Tuple<bool, T>> TryGet(bool queueJump = false)
        {
            var value = await Command.Get(settings.Db, Key, queueJump);
            if (value == null)
            {
                if (valueFactory != null)
                {
                    var v = valueFactory();
                    await Set(v, expirySeconds, queueJump);
                    return Tuple.Create(true, v);
                }
                else
                {
                    return Tuple.Create(false, default(T));
                }
            }

            return Tuple.Create(true, settings.ValueConverter.Deserialize<T>(value));
        }

        public virtual Task Set(T value, long? expirySeconds = null, bool queueJump = false)
        {
            var v = settings.ValueConverter.Serialize(value);
            if (expirySeconds == null)
            {
                return Command.Set(settings.Db, Key, v, queueJump: queueJump);
            }
            else
            {
                return Command.Set(settings.Db, Key, v, expirySeconds.Value, queueJump: queueJump);
            }
        }

        public virtual Task<long> Increment(long value = 1, bool queueJump = false)
        {
            return Command.Increment(settings.Db, Key, value, queueJump);
        }

        public virtual Task<long> Decrement(long value = 1, bool queueJump = false)
        {
            return Command.Decrement(settings.Db, Key, value, queueJump);
        }
    }

    public class MemoizedRedisString<T> : RedisString<T>
    {
        bool isCached;
        T cacheItem;

        public MemoizedRedisString(RedisSettings settings, string stringKey, Func<T> valueFactoryIfNotExists = null, int? expirySeconds = null)
            : base(settings, stringKey, valueFactoryIfNotExists, expirySeconds)
        {
        }

        public MemoizedRedisString(RedisGroup connectionGroup, string stringKey, Func<T> valueFactoryIfNotExists = null, int? expirySeconds = null)
            : base(connectionGroup, stringKey, valueFactoryIfNotExists, expirySeconds)
        {
        }

        public override async Task<Tuple<bool, T>> TryGet(bool queueJump = false)
        {
            if (isCached) return Tuple.Create(true, cacheItem);
            var value = await base.TryGet(queueJump);
            if (value.Item1)
            {
                isCached = true;
                cacheItem = value.Item2;
            }
            return value;
        }

        public override async Task Set(T value, long? expirySeconds = null, bool queueJump = false)
        {
            await base.Set(value, expirySeconds, queueJump);
            isCached = false;
            cacheItem = default(T);
        }
    }

    public static class RedisStringExtensions
    {
        public static async Task<T> GetValueOrDefault<T>(this RedisString<T> redis, T defaultValue = default(T), bool queueJump = false)
        {
            var result = await redis.TryGet(queueJump);
            return result.Item1 ? result.Item2 : defaultValue;
        }
    }
}
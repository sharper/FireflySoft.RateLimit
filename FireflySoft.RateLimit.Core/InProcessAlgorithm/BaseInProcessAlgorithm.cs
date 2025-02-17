using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Caching;
using System.Threading.Tasks;
using FireflySoft.RateLimit.Core.Rule;
using FireflySoft.RateLimit.Core.Time;

namespace FireflySoft.RateLimit.Core.InProcessAlgorithm
{
    /// <summary>
    /// The base class for in process algorithm
    /// </summary>
    public abstract class BaseInProcessAlgorithm : BaseAlgorithm
    {
        /// <summary>
        /// in-process memory cache
        /// </summary>
        protected readonly MemoryCache _cache;

        /// <summary>
        /// Create a new instance
        /// </summary>
        public BaseInProcessAlgorithm(IEnumerable<RateLimitRule> rules, ITimeProvider timeProvider = null, bool updatable = false)
        : base(rules, timeProvider, updatable)
        {
            _cache = new MemoryCache("IPMS-" + Guid.NewGuid().ToString());
            _cache.Add("IPMS", 1, DateTimeOffset.MaxValue);
        }

        /// <summary>
        /// Lock the rate limit target until the expiration time, when triggering the rate limit rule.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="currentTime"></param>
        /// <param name="expireTimeSpan"></param>
        protected bool TryLock(string target, DateTimeOffset currentTime, TimeSpan expireTimeSpan)
        {
            var expireTime = currentTime.Add(expireTimeSpan);
            return _cache.Add($"{target}-lock", 1, expireTime);
        }

        /// <summary>
        /// Check whether the rate limit target is locked
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        protected bool CheckLocked(string target)
        {
            return _cache.Get($"{target}-lock") == null ? false : true;
        }

        /// <summary>
        /// Defines in-process count value
        /// </summary>
        protected class CountValue
        {
            /// <summary>
            /// Create a new instance
            /// </summary>
            /// <param name="value"></param>
            public CountValue(long value)
            {
                Value = value;
            }

            /// <summary>
            /// The Count Value
            /// </summary>
            /// <value></value>
            public long Value { get; set; }

            /// <summary>
            /// The last flow-out or flow-in time for leaky bucket and token bucket
            /// </summary>
            /// <value></value>
            public DateTimeOffset LastFlowTime { get; set; }
        }
    }
}
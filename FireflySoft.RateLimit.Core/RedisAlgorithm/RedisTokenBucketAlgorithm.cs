using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FireflySoft.RateLimit.Core.Rule;
using FireflySoft.RateLimit.Core.Time;
using StackExchange.Redis;

namespace FireflySoft.RateLimit.Core.RedisAlgorithm
{
    /// <summary>
    /// Redis Token Bucket Algorithm
    /// </summary>
    public class RedisTokenBucketAlgorithm : BaseRedisAlgorithm
    {
        private readonly RedisLuaScript _tokenBucketDecrementLuaScript;

        /// <summary>
        /// create a new instance
        /// </summary>
        /// <param name="rules">The rate limit rules</param>
        /// <param name="redisClient">The redis client</param>
        /// <param name="timeProvider">The time provider</param>
        /// <param name="updatable">If rules can be updated</param>
        public RedisTokenBucketAlgorithm(IEnumerable<TokenBucketRule> rules, ConnectionMultiplexer redisClient = null, ITimeProvider timeProvider = null, bool updatable = false)
        : base(rules, redisClient, timeProvider, updatable)
        {
            _tokenBucketDecrementLuaScript = new RedisLuaScript(_redisClient, "Src-DecrWithTokenBucket",
                @"local ret={}
                local cl_key = '{' .. KEYS[1] .. '}'
                local lock_key = cl_key .. '-lock'
                local lock_val = redis.call('get',lock_key)
                if lock_val == '1' then
                    ret[1]=1
                    ret[2]=-1
                    return ret;
                end
                ret[1]=0
                local st_key= cl_key .. '-st'
                local amount=tonumber(ARGV[1])
                local capacity=tonumber(ARGV[2])
                local inflow_unit=tonumber(ARGV[3])
                local inflow_quantity_per_unit=tonumber(ARGV[4])
                local current_time=tonumber(ARGV[5])
                local start_time=tonumber(ARGV[6])
                local lock_seconds=tonumber(ARGV[7])
                local key_expire_time=math.ceil((capacity/inflow_quantity_per_unit)*inflow_unit)+10
                local bucket_amount=0
                local last_time=redis.call('get',st_key)
                if(last_time==false)
                then
                    bucket_amount = capacity - amount;
                    redis.call('set',KEYS[1],bucket_amount,'PX',key_expire_time)
                    redis.call('set',st_key,start_time,'PX',key_expire_time)
                    ret[2]=bucket_amount
                    return ret
                end
                
                local current_value = redis.call('get',KEYS[1])
                current_value = tonumber(current_value)
                last_time=tonumber(last_time)
                local last_time_changed=0
                local past_time=current_time-last_time
                if(past_time<inflow_unit)
                then
                    bucket_amount=current_value-amount
                else
                    local past_inflow_unit_quantity = past_time/inflow_unit
                    past_inflow_unit_quantity=math.floor(past_inflow_unit_quantity)
                    last_time=last_time+past_inflow_unit_quantity*inflow_unit
                    last_time_changed=1
                    local past_inflow_quantity=past_inflow_unit_quantity*inflow_quantity_per_unit
                    bucket_amount=current_value+past_inflow_quantity-amount
                end

                if(bucket_amount>=capacity)
                then
                    bucket_amount=capacity-amount
                end
                ret[2]=bucket_amount

                if(bucket_amount<0)
                then
                    if lock_seconds>0 then
                        redis.call('set',lock_key,'1','EX',lock_seconds,'NX')
                    end
                    ret[1]=1
                    return ret
                end

                if last_time_changed==1 then
                    redis.call('set',KEYS[1],bucket_amount,'PX',key_expire_time)
                    redis.call('set',st_key,last_time,'PX',key_expire_time)
                else
                    redis.call('set',KEYS[1],bucket_amount,'PX',key_expire_time)
                end
                return ret");
        }

        /// <summary>
        /// Take a peek at the result of the last processing of the specified target in the specified rule
        /// </summary>
        /// <param name="target"></param>
        /// <param name="rule"></param>
        /// <returns></returns>
        protected override RuleCheckResult PeekSingleRule(string target, RateLimitRule rule)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// check single rule for target
        /// </summary>
        /// <param name="target"></param>
        /// <param name="rule"></param>
        /// <returns></returns>
        protected override RuleCheckResult CheckSingleRule(string target, RateLimitRule rule)
        {
            var currentRule = rule as TokenBucketRule;
            var amount = 1;

            var inflowUnit = currentRule.InflowUnit.TotalMilliseconds;
            var currentTime = _timeProvider.GetCurrentUtcMilliseconds();
            var startTime = AlgorithmStartTime.ToSpecifiedTypeTime(currentTime, TimeSpan.FromMilliseconds(inflowUnit), currentRule.StartTimeType);

            var ret = (long[])EvaluateScript(_tokenBucketDecrementLuaScript, new RedisKey[] { target },
                new RedisValue[] { amount, currentRule.Capacity, inflowUnit, currentRule.InflowQuantityPerUnit, currentTime, startTime, currentRule.LockSeconds });
            return new RuleCheckResult()
            {
                IsLimit = ret[0] == 0 ? false : true,
                Target = target,
                Count = ret[1],
                Rule = rule
            };
        }

        /// <summary>
        /// async check single rule for target
        /// </summary>
        /// <param name="target"></param>
        /// <param name="rule"></param>
        /// <returns></returns>
        protected override async Task<RuleCheckResult> CheckSingleRuleAsync(string target, RateLimitRule rule)
        {
            var currentRule = rule as TokenBucketRule;
            var amount = 1;

            var inflowUnit = currentRule.InflowUnit.TotalMilliseconds;
            var currentTime = await _timeProvider.GetCurrentUtcMillisecondsAsync();
            var startTime = AlgorithmStartTime.ToSpecifiedTypeTime(currentTime, TimeSpan.FromMilliseconds(inflowUnit), currentRule.StartTimeType);

            var ret = (long[])await EvaluateScriptAsync(_tokenBucketDecrementLuaScript, new RedisKey[] { target },
                new RedisValue[] { amount, currentRule.Capacity, inflowUnit, currentRule.InflowQuantityPerUnit, currentTime, startTime, currentRule.LockSeconds });
            var result = new Tuple<bool, long>(ret[0] == 0 ? false : true, ret[1]);
            return new RuleCheckResult()
            {
                IsLimit = ret[0] == 0 ? false : true,
                Target = target,
                Count = ret[1],
                Rule = rule
            };
        }
    }
}
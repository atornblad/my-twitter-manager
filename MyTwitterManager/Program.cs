using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Tweetinvi;
using Tweetinvi.Exceptions;
using Tweetinvi.Models;
using Tweetinvi.Parameters;

[assembly: CLSCompliant(true)]

namespace MyTwitterManager
{
    [SuppressMessage("Reliability", "CA2007", Justification = "It is generally appropriate to suppress the warning entirely for projects that represent application code rather than library code")]
    [SuppressMessage("Reliability", "CA1303", Justification = "It is generally appropriate to suppress the warning entirely for projects that represent application code rather than library code")]
    public static class Program
    {
        private const string TWITTER_API_KEY = nameof(TWITTER_API_KEY);
        private const string TWITTER_API_SECRET = nameof(TWITTER_API_SECRET);
        private const string TWITTER_ACCESS_TOKEN = nameof(TWITTER_ACCESS_TOKEN);
        private const string TWITTER_ACCESS_TOKEN_SECRET = nameof(TWITTER_ACCESS_TOKEN_SECRET);
        private const string TWITTER_SCREEN_NAME = nameof(TWITTER_SCREEN_NAME);

        static async Task Main()
        {
            Console.WriteLine("=== My Twitter Manager ===");
            string apiKey = Environment.GetEnvironmentVariable(TWITTER_API_KEY);
            string apiSecret = Environment.GetEnvironmentVariable(TWITTER_API_SECRET);
            string accessToken = Environment.GetEnvironmentVariable(TWITTER_ACCESS_TOKEN);
            string accessSecret = Environment.GetEnvironmentVariable(TWITTER_ACCESS_TOKEN_SECRET);
            string screenName = Environment.GetEnvironmentVariable(TWITTER_SCREEN_NAME);

            var client = new TwitterClient(apiKey, apiSecret, accessToken, accessSecret);
            client.Config.RateLimitTrackerMode = RateLimitTrackerMode.TrackAndAwait;
            await DeleteOldTweets(client, screenName);
            await DeleteOldLikes(client, screenName);
        }

        private static async Task DeleteOldTweets(ITwitterClient client, string screenName)
        {
            var tweetsToDelete = new HashSet<long>();
            int totalCount = 0;

            await client.ForEachUserTimelineTweet(screenName, (tweet) =>
            {
                int retweets = tweet.RetweetCount;
                string text = tweet.FullText;
                if (tweet.IsRetweet)
                {
                    retweets -= tweet.RetweetedTweet.RetweetCount;
                    text = $"(RETWEET) {tweet.RetweetedTweet.FullText}";
                }

                bool anyInteraction = (tweet.FavoriteCount + retweets + (tweet.QuoteCount ?? 0)) > 0 || tweet.InReplyToStatusId.HasValue;

                int maxDaysOld = 14 + (anyInteraction ? 7 : 0) + tweet.FavoriteCount * 3 + retweets * 7 + (tweet.QuoteCount ?? 0) * 14;
                if (maxDaysOld > 180) maxDaysOld = 365 * 5;

                var daysOld = DateTimeOffset.Now - tweet.CreatedAt;
                if (daysOld > TimeSpan.FromDays(maxDaysOld))
                {
                    tweetsToDelete.Add(tweet.Id);
                    Console.WriteLine($"https://twitter.com/{tweet.CreatedBy.ScreenName}/status/{tweet.Id} : {text} ({tweet.FavoriteCount} likes, {retweets} retweets, {tweet.QuoteCount ?? 0} quotes)");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"^^^ OLD TWEET ({daysOld}, allowed: {maxDaysOld} days) ^^^");
                    Console.ResetColor();
                }

                ++totalCount;
            });

            var destroyers = tweetsToDelete.Select(id => client.Tweets.DestroyTweetAsync(id)).ToArray();
            Console.WriteLine($"Destroying {destroyers.Length} tweets of {totalCount}, please wait...");
            Task.WaitAll(destroyers);
            Console.WriteLine("Done!");
        }

        private static async Task DeleteOldLikes(ITwitterClient client, string screenName)
        {
            var tweetsToUnlike = new HashSet<long>();
            int totalCount = 0;

            await client.ForEachUserFavoriteTweet(screenName, tweet =>
            {
                string text = tweet.FullText;
                if (tweet.IsRetweet)
                {
                    text = $"(RETWEET) {tweet.RetweetedTweet.FullText}";
                }
                int maxDaysOld = (int)(3 + 4 * Math.Atan((tweet.FavoriteCount + tweet.RetweetCount) / 50.0));

                var daysOld = DateTimeOffset.Now - tweet.CreatedAt;
                if (daysOld > TimeSpan.FromDays(maxDaysOld))
                {
                    tweetsToUnlike.Add(tweet.Id);
                    Console.WriteLine($"https://twitter.com/{tweet.CreatedBy.ScreenName}/status/{tweet.Id} : {text} ({tweet.FavoriteCount} likes, {tweet.RetweetCount} retweets, {tweet.QuoteCount ?? 0} quotes)");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"^^^ OLD LIKE ({daysOld}, allowed: {maxDaysOld} days) ^^^");
                    Console.ResetColor();
                }

                ++totalCount;
            });

            var destroyers = tweetsToUnlike.Select(id => RetryAsync(() => client.Tweets.UnfavoriteTweetAsync(id), $"Deleting like {id}", TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), 5)).ToArray();
            Console.WriteLine($"Destroying {destroyers.Length} likes of {totalCount}, please wait...");
            Task.WaitAll(destroyers);
            Console.WriteLine("Done!");
        }

        private static async Task ForEachUserFavoriteTweet(this ITwitterClient client, string screenName, [NotNull] Action<ITweet> tweetAction, int batchSize = 10)
        {
            await ForEachTweet(client, (client, untilId) => client.Tweets.GetUserFavoriteTweetsAsync(new FavoritesParams
            {
                User = new UserIdentifier { ScreenName = screenName },
                IncludeEntities = true,
                PageSize = batchSize,
                MaxId = untilId
            }), tweetAction);
        }

        private static async Task ForEachUserTimelineTweet(this ITwitterClient client, string screenName, [NotNull] Action<ITweet> tweetAction, int batchSize = 10, bool includeRetweets = true)
        {
            await ForEachTweet(client, (client, untilId) => client.Timelines.GetUserTimelineAsync(new TimelineParams
            {
                User = new UserIdentifier { ScreenName = screenName },
                IncludeRetweets = includeRetweets,
                IncludeEntities = true,
                PageSize = batchSize,
                MaxId = untilId
            }), tweetAction);
        }

        private static async Task ForEachTweet(ITwitterClient client, Func<ITwitterClient, long?, Task<ITweet[]>> tweetGetter, Action<ITweet> tweetAction)
        {
            bool running = true;
            long? untilId = null;
            while (running)
            {
                var tweets = await RetryAsync(() => tweetGetter(client, untilId), "Get Tweets", TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), 5);
                if (tweets == null) return;
                int count = 0;
                long lastId = long.MaxValue;
                foreach (var tweet in tweets)
                {
                    tweetAction(tweet);
                    lastId = tweet.Id;
                    ++count;
                }
                if (count == 0)
                {
                    running = false;
                }
                else
                {
                    untilId = lastId - 1;
                }
            }
        }

        private static async Task RetryAsync(Func<Task> func, string title, TimeSpan minDelay, TimeSpan maxDelay, int maxTries)
        {
            for (int i = 1; i <= maxTries - 1; ++i)
            {
                try
                {
                    await func();
                }
                catch (Exception e)
                {
                    if (e is TwitterException te)
                    {
                        if (e.Message.Contains("Code : 404"))
                        {
                            Console.Error.WriteLine($"Caught a 404 Not Found error. Aborting!");
                            return;
                        }
                        double logMin = Math.Log(minDelay.TotalSeconds);
                        double logMax = Math.Log(maxDelay.TotalSeconds);
                        double logThis = logMin + (logMax - logMin) * (i - 1) / (maxTries - 1);
                        double seconds = Math.Exp(logThis);
                        TimeSpan sleep = TimeSpan.FromSeconds(Math.Round(seconds));
                        Console.Error.WriteLine($"Error in task {title}, try {i} of {maxTries}");
                        Console.Error.WriteLine($"Caught exception {e.GetType().Name}: \"{e.Message}\"");
                        Console.Error.WriteLine($"Sleeping for {sleep}");
                        Thread.Sleep(sleep);
                        Console.Error.WriteLine($"Retrying {i + 1} of {maxTries}");
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            await func();
        }

        private static async Task<T> RetryAsync<T>(Func<Task<T>> func, string title, TimeSpan minDelay, TimeSpan maxDelay, int maxTries)
        {
            for (int i = 1; i <= maxTries - 1; ++i)
            {
                try
                {
                    return await func();
                }
                catch (Exception e)
                {
                    if (e is TwitterException te)
                    {
                        if (e.Message.Contains("Code : 404"))
                        {
                            Console.Error.WriteLine($"Caught a 404 Not Found error. Aborting!");
                            return default(T);
                        }
                        double logMin = Math.Log(minDelay.TotalSeconds);
                        double logMax = Math.Log(maxDelay.TotalSeconds);
                        double logThis = logMin + (logMax - logMin) * (i - 1) / (maxTries - 1);
                        double seconds = Math.Exp(logThis);
                        TimeSpan sleep = TimeSpan.FromSeconds(Math.Round(seconds));
                        Console.Error.WriteLine($"Error in task {title}, try {i} of {maxTries}");
                        Console.Error.WriteLine($"Caught exception {e.GetType().Name}: \"{e.Message}\"");
                        Console.Error.WriteLine($"Sleeping for {sleep}");
                        Thread.Sleep(sleep);
                        Console.Error.WriteLine($"Retrying {i + 1} of {maxTries}");
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            return await func();
        }
    }

    class UserIdentifier : IUserIdentifier
    {
        public string ScreenName { get; init; }

        public long Id { get; set; }
        public string IdStr { get; set; }
    }

    class FavoritesParams : IGetUserFavoriteTweetsParameters
    {
        public IUserIdentifier User { get; set; }
        public bool? IncludeEntities { get; set; }
        public int PageSize { get; set; }
        public long? SinceId { get; set; }
        public long? MaxId { get; set; }
        public ContinueMinMaxCursor ContinueMinMaxCursor { get; set; }

        public List<Tuple<string, string>> CustomQueryParameters { get; private init; } = new List<Tuple<string, string>>();

        public string FormattedCustomQueryParameters { get; private init; }

        public TweetMode? TweetMode { get; set; }

        public void AddCustomQueryParameter(string name, string value)
        {
            CustomQueryParameters.Add(new Tuple<string, string>(name, value));
        }

        public void ClearCustomQueryParameters()
        {
            CustomQueryParameters.Clear();
        }
    }

    class TimelineParams : IGetUserTimelineParameters
    {
        public IUserIdentifier User { get; set; }
        public bool IncludeRetweets { get; set; }
        public bool ExcludeReplies { get; set; }
        public bool? TrimUser { get; set; }
        public bool? IncludeEntities { get; set; }
        public int PageSize { get; set; }
        public long? SinceId { get; set; }
        public long? MaxId { get; set; }
        public ContinueMinMaxCursor ContinueMinMaxCursor { get; set; }

        public List<Tuple<string, string>> CustomQueryParameters { get; private set; } = new List<Tuple<string, string>>();

        public string FormattedCustomQueryParameters { get; private set; }

        public TweetMode? TweetMode { get; set; }

        public void AddCustomQueryParameter(string name, string value)
        {
            CustomQueryParameters.Add(new Tuple<string, string>(name, value));
        }

        public void ClearCustomQueryParameters()
        {
            CustomQueryParameters.Clear();
        }
    }
}

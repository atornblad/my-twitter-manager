using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tweetinvi;
using Tweetinvi.Models;
using Tweetinvi.Parameters;
using Tweetinvi.Parameters.V2;

namespace MyTwitterManager
{
    public static class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== My Twitter Manager ===");
            string apiKey = Environment.GetEnvironmentVariable("TWITTER_API_KEY");
            string apiSecret = Environment.GetEnvironmentVariable("TWITTER_API_SECRET");
            string accessToken = Environment.GetEnvironmentVariable("TWITTER_ACCESS_TOKEN");
            string accessSecret = Environment.GetEnvironmentVariable("TWITTER_ACCESS_TOKEN_SECRET");
            string screenName = Environment.GetEnvironmentVariable("TWITTER_SCREEN_NAME");

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
                    Console.WriteLine($"{tweet.Id}: {tweet.CreatedAt} {text} ({tweet.FavoriteCount} likes, {retweets} retweets, {tweet.QuoteCount ?? 0} quotes)");
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
                    Console.WriteLine($"{tweet.Id}: {tweet.CreatedAt} {tweet.CreatedBy.ScreenName} tweeted {text} ({tweet.FavoriteCount} likes, {tweet.RetweetCount} retweets, {tweet.QuoteCount ?? 0} quotes)");
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

        public static async Task ForEachUserFavoriteTweet(this ITwitterClient client, string screenName, Action<ITweet> tweetAction, int batchSize = 10)
        {
            await ForEachTweet(client, (client, untilId) => client.Tweets.GetUserFavoriteTweetsAsync(new FavoritesParams
            {
                User = new UserIdentifier { ScreenName = screenName },
                IncludeEntities = true,
                PageSize = batchSize,
                MaxId = untilId
            }), tweetAction);
        }

        public static async Task ForEachUserTimelineTweet(this ITwitterClient client, string screenName, Action<ITweet> tweetAction, int batchSize = 10, bool includeRetweets = true)
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

    class SearchParams : ISearchTweetsV2Parameters
    {
        public DateTime? EndTime { get; set; }
        public string Query { get; set; }
        public int? PageSize { get; set; }
        public string NextToken { get; set; }
        public string SinceId { get; set; }
        public DateTime? StartTime { get; set; }
        public string UntilId { get; set; }
        public HashSet<string> Expansions { get; set; }
        public HashSet<string> MediaFields { get; set; }
        public HashSet<string> PlaceFields { get; set; }
        public HashSet<string> PollFields { get; set; }
        public HashSet<string> TweetFields { get; set; }
        public HashSet<string> UserFields { get; set; }

        public List<Tuple<string, string>> CustomQueryParameters { get; private set; } = new List<Tuple<string, string>>();

        public string FormattedCustomQueryParameters { get; private set; }

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

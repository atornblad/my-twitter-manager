using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ATornblad.Conphig;
using Tweetinvi;
using Tweetinvi.Exceptions;
using Tweetinvi.Models;
using Tweetinvi.Parameters;
using Tweetinvi.Parameters.V2;

[assembly: CLSCompliant(true)]

namespace MyTwitterManager
{
    [SuppressMessage("Reliability", "CA2007", Justification = "It is generally appropriate to suppress the warning entirely for projects that represent application code rather than library code")]
    [SuppressMessage("Reliability", "CA2008", Justification = "It is generally appropriate to suppress the warning entirely for projects that represent application code rather than library code")]
    [SuppressMessage("Globalization", "CA1303", Justification = "I don't care about globalization for now.")]
    public static class Program
    {
        static async Task Main()
        {
            Console.WriteLine("=== My Twitter Manager ===");
            var settings = Config.Load<Settings>();

            var client = new TwitterClient(settings.ApiKey, settings.ApiSecret, settings.AccessToken, settings.AccessTokenSecret);
            client.Config.RateLimitTrackerMode = RateLimitTrackerMode.TrackAndAwait;

            var users = await RetryAsync(
                () => client.UsersV2.GetUsersByNameAsync(settings.ScreenName),
                $"Get user {settings.ScreenName}"
            );

            string pinnedIdRaw = users.Users[0].PinnedTweetId;

            var permanent = new List<long>();
            if (!string.IsNullOrEmpty(pinnedIdRaw))
            {
                permanent.Add(long.Parse(pinnedIdRaw, CultureInfo.InvariantCulture));
            }
            if (settings.PermanentTweetIds != null)
            {
                permanent.AddRange(settings.PermanentTweetIds);
            }

            await DeleteOldTweets(client, settings.ScreenName, permanent.ToArray(), settings.MaxTweetAgeMultiplier);
            await DeleteOldLikes(client, settings.ScreenName);
            await IgnoreBlockedUsers(client);
        }

        private static async Task IgnoreBlockedUsers(ITwitterClient client)
        {
            long[] blockedIds = await RetryAsync(() => client.Users.GetBlockedUserIdsAsync(), "Getting blocked users");

            var muterUnblockers = blockedIds.Select(id =>
                RetryAsync(
                    () => client.Users.MuteUserAsync(id).ContinueWith(u => client.Users.UnblockUserAsync(id)),
                    $"Muting and unblocking user {id}"
                )
            ).Take(100).ToArray();
            Console.WriteLine($"Muting and unblocking {muterUnblockers.Length} users out of {blockedIds.Length}, please wait...");
            Task.WaitAll(muterUnblockers);
            Console.WriteLine("Done!");
        }

        private static async Task DeleteOldTweets(ITwitterClient client, string screenName, long[] permanentIds, double maxTweetAgeMultiplier)
        {
            var tweetsToDelete = new HashSet<long>();
            int totalCount = 0;

            await client.ForEachUserTimelineTweet(screenName, (tweet) =>
            {
                int retweets = tweet.RetweetCount;
                string text = tweet.FullText;

                if (permanentIds.Contains(tweet.Id))
                {
                    Console.WriteLine($"Permanent tweet {tweet.Id}: {tweet.FullText}");
                    ++totalCount;
                    return;
                }

                if (tweet.IsRetweet)
                {
                    retweets -= tweet.RetweetedTweet.RetweetCount;
                    text = $"(RETWEET) {tweet.RetweetedTweet.FullText}";
                }

                bool anyInteraction = (tweet.FavoriteCount + retweets + (tweet.QuoteCount ?? 0)) > 0 || tweet.InReplyToStatusId.HasValue;

                int maxDaysOld = 2 + (anyInteraction ? 1 : 0) + (tweet.FavoriteCount + 1) / 2 + retweets + (tweet.QuoteCount ?? 0) * 2;
                maxDaysOld = (int)(maxDaysOld * maxTweetAgeMultiplier);
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

            var destroyers = tweetsToDelete.Select(id =>
                RetryAsync(
                    () => client.Tweets.DestroyTweetAsync(id),
                    $"Destroying tweet {id}"
                )
            ).ToArray();
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
                bool mentionsMe = tweet.FullText.Contains($"@{screenName}", StringComparison.InvariantCultureIgnoreCase);
                int maxDaysOld = (int)(3 + 4 * Math.Atan((tweet.FavoriteCount + tweet.RetweetCount) / 50.0)) + (mentionsMe ? 7 : 0);

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

            var destroyers = tweetsToUnlike.Select(id =>
                RetryAsync(
                    () => client.Tweets.UnfavoriteTweetAsync(id),
                    $"Deleting like {id}"
                )
            ).ToArray();
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

        private static readonly TimeSpan MIN_RETRY_TIME = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MAX_RETRY_TIME = TimeSpan.FromMinutes(1);

        private static async Task RetryAsync(Func<Task> func, string title, TimeSpan? minDelay = null, TimeSpan? maxDelay = null, int maxTries = 5)
        {
            for (int i = 1; i <= maxTries - 1; ++i)
            {
                try
                {
                    await func();
                    return; // Need to return here, so that the func isn't tried twice upon initial success!
                }
                catch (TwitterException te)
                {
                    if (te.StatusCode == 404)
                    {
                        Console.Error.WriteLine($"Caught a 404 Not Found error. Aborting!");
                        return;
                    }
                    double logMin = Math.Log((minDelay ?? MIN_RETRY_TIME).TotalSeconds);
                    double logMax = Math.Log((maxDelay ?? MAX_RETRY_TIME).TotalSeconds);
                    double logThis = logMin + (logMax - logMin) * (i - 1) / (maxTries - 2);
                    double seconds = Math.Exp(logThis);
                    TimeSpan sleep = TimeSpan.FromSeconds(Math.Round(seconds));
                    Console.Error.WriteLine($"Error in task {title}, try {i} of {maxTries}");
                    Console.Error.WriteLine($"Caught exception {te.GetType().Name}: \"{te.Message}\"");
                    Console.Error.WriteLine($"Sleeping for {sleep}");
                    Thread.Sleep(sleep);
                    Console.Error.WriteLine($"Retrying {i + 1} of {maxTries}");
                }
            }
            await func();
        }

        private static async Task<T> RetryAsync<T>(Func<Task<T>> func, string title, TimeSpan? minDelay = null, TimeSpan? maxDelay = null, int maxTries = 5)
        {
            for (int i = 1; i <= maxTries - 1; ++i)
            {
                try
                {
                    return await func();
                }
                catch (TwitterException te)
                {
                    if (te.StatusCode == 404)
                    {
                        Console.Error.WriteLine($"Caught a 404 Not Found error. Aborting!");
                        return default;
                    }
                    double logMin = Math.Log((minDelay ?? MIN_RETRY_TIME).TotalSeconds);
                    double logMax = Math.Log((maxDelay ?? MAX_RETRY_TIME).TotalSeconds);
                    double logThis = logMin + (logMax - logMin) * (i - 1) / (maxTries - 2);
                    double seconds = Math.Exp(logThis);
                    TimeSpan sleep = TimeSpan.FromSeconds(Math.Round(seconds));
                    Console.Error.WriteLine($"Error in task {title}, try {i} of {maxTries}");
                    Console.Error.WriteLine($"Caught exception {te.GetType().Name}: \"{te.Message}\"");
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

    class ParamsBase : IMinMaxQueryParameters, ICustomRequestParameters
    {
        public int PageSize { get; set; }
        public long? SinceId { get; set; }
        public long? MaxId { get; set; }
        public ContinueMinMaxCursor ContinueMinMaxCursor { get; set; }

        public List<Tuple<string, string>> CustomQueryParameters { get; private set; } = new List<Tuple<string, string>>();

        public string FormattedCustomQueryParameters { get; set; }

        public void AddCustomQueryParameter(string name, string value)
        {
            CustomQueryParameters.Add(new Tuple<string, string>(name, value));
        }

        public void ClearCustomQueryParameters()
        {
            CustomQueryParameters.Clear();
        }
    }

    class FavoritesParams : ParamsBase, IGetUserFavoriteTweetsParameters
    {
        public IUserIdentifier User { get; set; }
        public bool? IncludeEntities { get; set; }
        public TweetMode? TweetMode { get; set; }
    }

    class TimelineParams : ParamsBase, IGetUserTimelineParameters
    {
        public IUserIdentifier User { get; set; }
        public bool IncludeRetweets { get; set; }
        public bool ExcludeReplies { get; set; }
        public bool? TrimUser { get; set; }
        public bool? IncludeEntities { get; set; }
        public TweetMode? TweetMode { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tweetinvi;
using Tweetinvi.Models;
using Tweetinvi.Parameters;
using Tweetinvi.Parameters.V2;

namespace MyTwitterManager
{
    class Program
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
            await DeleteOldTweets(client, screenName);
            await DeleteOldLikes(client, screenName);
        }

        private static async Task DeleteOldTweets(TwitterClient client, string screenName)
        {
            bool running = true;
            long? untilId = null;

            var tweetsToDelete = new HashSet<long>();
            int totalCount = 0;

            while (running)
            {
                var tweets = await client.Timelines.GetUserTimelineAsync(new TimelineParams
                {
                    User = new UserIdentifier { ScreenName = screenName },
                    IncludeRetweets = true,
                    IncludeEntities = true,
                    PageSize = 10,
                    MaxId = untilId
                });

                int count = 0;
                long lastId = long.MaxValue;
                foreach (var tweet in tweets)
                {
                    int retweets = tweet.RetweetCount;
                    string text = tweet.FullText;
                    if (tweet.IsRetweet)
                    {
                        retweets -= tweet.RetweetedTweet.RetweetCount;
                        text = $"(RETWEET) {tweet.RetweetedTweet.FullText}";
                    }

                    bool anyInteraction = (tweet.FavoriteCount + retweets + (tweet.QuoteCount ?? 0)) > 0 || tweet.InReplyToStatusId.HasValue;

                    int maxDaysOld = 14 + (anyInteraction ? 14 : 0) + tweet.FavoriteCount * 7 + retweets * 14 + (tweet.QuoteCount ?? 0) * 28;
                    if (maxDaysOld > 180) maxDaysOld = 365 * 5;

                    var daysOld = DateTimeOffset.Now - tweet.CreatedAt;
                    if (daysOld > TimeSpan.FromDays(maxDaysOld))
                    {
                        tweetsToDelete.Add(tweet.Id);
                        Console.WriteLine($"{tweet.Id}: {tweet.CreatedAt} {text} ({tweet.FavoriteCount} likes, {retweets} retweets, {tweet.QuoteCount ?? 0} quotes)");
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"^^^ TOO OLD ({daysOld}, allowed: {maxDaysOld} days) ^^^");
                        Console.ResetColor();
                    }

                    lastId = tweet.Id;
                    ++count;
                    ++totalCount;
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

            var destroyers = tweetsToDelete.Select(id => client.Tweets.DestroyTweetAsync(id)).ToArray();
            Console.WriteLine($"Destroying {destroyers.Length} tweets of {totalCount}, please wait...");
            Task.WaitAll(destroyers);
            Console.WriteLine("Done!");
        }

        private static async Task DeleteOldLikes(TwitterClient client, string screenName)
        {
            bool running = true;
            long? untilId = null;

            var tweetsToUnlike = new HashSet<long>();
            int totalCount = 0;

            while (running)
            {
                var tweets = await client.Tweets.GetUserFavoriteTweetsAsync(new FavoritesParams
                {
                    User = new UserIdentifier { ScreenName = screenName },
                    IncludeEntities = true,
                    PageSize = 10,
                    MaxId = untilId
                });
                int count = 0;

                long lastId = long.MaxValue;
                foreach (var tweet in tweets)
                {
                    string text = tweet.FullText;
                    if (tweet.IsRetweet)
                    {
                        text = $"(RETWEET) {tweet.RetweetedTweet.FullText}";
                    }
                    int maxDaysOld = (int)(5 + 9 * Math.Atan((tweet.FavoriteCount + tweet.RetweetCount) / 50.0));

                    var daysOld = DateTimeOffset.Now - tweet.CreatedAt;
                    if (daysOld > TimeSpan.FromDays(maxDaysOld))
                    {
                        tweetsToUnlike.Add(tweet.Id);
                        Console.WriteLine($"{tweet.Id}: {tweet.CreatedAt} {tweet.CreatedBy.ScreenName} tweeted {text} ({tweet.FavoriteCount} likes, {tweet.RetweetCount} retweets, {tweet.QuoteCount ?? 0} quotes)");
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"^^^ OLD LIKE ({daysOld}, allowed: {maxDaysOld} days) ^^^");
                        Console.ResetColor();
                    }

                    lastId = tweet.Id;
                    ++count;
                    ++totalCount;
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

            var destroyers = tweetsToUnlike.Select(id => client.Tweets.UnfavoriteTweetAsync(id)).ToArray();
            Console.WriteLine($"Destroying {destroyers.Length} likes of {totalCount}, please wait...");
            Task.WaitAll(destroyers);
            Console.WriteLine("Done!");
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

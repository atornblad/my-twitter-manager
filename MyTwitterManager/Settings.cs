using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using ATornblad.Conphig;

namespace MyTwitterManager
{
    public class Settings
    {
        private const string TWITTER_API_KEY = nameof(TWITTER_API_KEY);
        private const string TWITTER_API_SECRET = nameof(TWITTER_API_SECRET);
        private const string TWITTER_ACCESS_TOKEN = nameof(TWITTER_ACCESS_TOKEN);
        private const string TWITTER_ACCESS_TOKEN_SECRET = nameof(TWITTER_ACCESS_TOKEN_SECRET);
        private const string TWITTER_SCREEN_NAME = nameof(TWITTER_SCREEN_NAME);
        private const string MAX_TWEET_AGE_MULTIPLIER = nameof(MAX_TWEET_AGE_MULTIPLIER);

        [EnvironmentVariable(TWITTER_API_KEY)]
        public string ApiKey { get; set; }

        [EnvironmentVariable(TWITTER_API_SECRET)]
        public string ApiSecret { get; set; }

        [EnvironmentVariable(TWITTER_ACCESS_TOKEN)]
        public string AccessToken { get; set; }

        [EnvironmentVariable(TWITTER_ACCESS_TOKEN_SECRET)]
        public string AccessTokenSecret { get; set; }

        [EnvironmentVariable(TWITTER_SCREEN_NAME)]
        public string ScreenName { get; set; }

        [SuppressMessage("Performance", "CA1819", Justification = "I really don't care about performance for a configuration object!")]
        [JsonPropertyName("permanent")]
        public long[] PermanentTweetIds { get; set; }

        [EnvironmentVariable(MAX_TWEET_AGE_MULTIPLIER)]
        [JsonPropertyName("maxtweetagemultiplier")]
        public double MaxTweetAgeMultiplier { get; set; } = 7.0;

        [SuppressMessage("Performance", "CA1819", Justification = "I really don't care about performance for a configuration object!")]
        [JsonPropertyName("permanentregex")]
        public string[] PermanentRegexPatterns { get; set;}
    }
}

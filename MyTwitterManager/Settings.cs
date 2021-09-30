using System;
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
    }
}

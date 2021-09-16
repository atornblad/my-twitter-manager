# My Twitter Manager

[![Daily](https://github.com/atornblad/my-twitter-manager/actions/workflows/daily.yml/badge.svg)](https://github.com/atornblad/my-twitter-manager/actions/workflows/daily.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/atornblad/my-twitter-manager/blob/master/LICENSE)
[![Code of Conduct](https://img.shields.io/badge/code-of%20conduct-brightgreen.svg)](https://github.com/atornblad/my-twitter-manager/blob/master/CODE_OF_CONDUCT.md)

This is just a simple automated destroyer of old tweets and likes to keep my Twitter timeline trimmed.

## Tweet rules:

* Tweets are kept for at least 14 days.
* If a tweet has any interaction *(like, retweet or quote tweet)*, another 14 days are added.
* For each like, 7 more days are added.
* For each retweet, 14 more days are added.
* For each quote tweet, 28 more days are added.
* Any tweet with a limit above 180 days gets to stay for 5 years.

## Like rules:

* Likes are kept for at least 5 days, and at most 19 days.
* The number of days is = 5 + 9 \* *atan*((*likes*+*retweets*)/50)

## Frameworks used:

* [Tweetinvi](https://github.com/linvi/tweetinvi)


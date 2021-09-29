LANG=en_US git filter-branch --env-filter \
    'parts=($GIT_AUTHOR_DATE)
    unixtime=${parts[0]}
    year=`date -j -f "@%s" $unixtime "+%Y"`
    month=`date -j -f "@%s" $unixtime "+%m"`
    day=`date -j -f "@%s" $unixtime "+%d"`
    weekday=`date -j -f "@%s" $unixtime "+%u"`
    hour=`date -j -f "@%s" $unixtime "+%H"`
    minute=`date -j -f "@%s" $unixtime "+%M"`
    second=`date -j -f "@%s" $unixtime "+%S"`
    hour=$((10#$hour))
    minute=$((10#$minute))
    second=$((10#$second))

    if (( $weekday >= 1 && $weekday <= 5 )); then
        if (( $hour >= 8 && $hour <= 16)); then
            echo "Moving from $year-$month-$day $hour:$minute:$second"
            officetime=`echo "scale=9; ( $hour - 8.0 + $minute / 60.0 + $second / 3600.0 ) / 9.0" | bc`
            newhour=17
            newminute=`echo "scale=9; 30.0 * $officetime" | bc`
            wholeminute=`echo "scale=0; $newminute / 1" | bc`
            newsecond=`echo "scale=9; ( $newminute - $wholeminute ) * 60" | bc`
            newsecond=`echo "scale=0; $newsecond / 1" | bc`
            newminute=$wholeminute
        elif (( $hour == 17 )); then
            echo "Moving from $hour:$minute:$second"
            $newminute=`echo "scale=9; ( $minute + $second / 60.0 ) / 2.0 + 30.0" | bc`
            wholeminute=`echo "scale=0; $newminute / 1" | bc`
            newsecond=`echo "scale=9; ( $newminute - $wholeminute ) * 60" | bc`
            newsecond=`echo "scale=0; $newsecond / 1" | bc`
        fi
        if [[ -n $newhour ]]; then
            echo "Moving to $year-$month-$day $newhour:$newminute:$newsecond"
            GIT_AUTHOR_DATE=`date -j -f "%Y-%m-%d %H:%M:%S" "$year-$month-$day $newhour:$newminute:$newsecond"`
            GIT_COMMITTER_DATE="$GIT_AUTHOR_DATE"
        fi
    fi'

-module(main).
-export([main/0]).

main() ->
    Word = lists:flatten(["abc"]),
    io:format("~p~n", [Word + 1]).

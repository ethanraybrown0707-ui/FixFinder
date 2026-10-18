-module(main).
-export([main/0]).

main() ->
    Count = length([]),
    io:format("~p~n", [10 div Count]).

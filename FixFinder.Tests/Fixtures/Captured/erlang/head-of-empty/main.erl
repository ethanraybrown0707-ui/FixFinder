-module(main).
-export([main/0]).

main() ->
    Marks = lists:seq(1, 0),
    io:format("~p~n", [hd(Marks)]).

-module(main).
-export([main/0]).

main() ->
    io:format("~p~n", [lists:summ([70, 80])]).

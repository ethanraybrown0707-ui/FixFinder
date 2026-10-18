-module(main).
-export([main/0]).

main() ->
    io:format("~p~n", [total([70, 80])]).

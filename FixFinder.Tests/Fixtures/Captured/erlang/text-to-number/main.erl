-module(main).
-export([main/0]).

main() ->
    Number = list_to_integer("abc"),
    io:format("~p~n", [Number]).

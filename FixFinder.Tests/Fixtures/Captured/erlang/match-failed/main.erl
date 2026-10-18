-module(main).
-export([main/0]).

main() ->
    {ok, Mark} = list_to_tuple([error, 70]),
    io:format("~p~n", [Mark]).

-module(main).
-export([main/0]).

main() ->
    Marks = [70, 80, 0],
    lists:foreach(fun(Mark) ->
        io:format("Mark: ~p~n", [Mark]),
        io:format("Share: ~p~n", [100 div Mark])
    end, Marks).

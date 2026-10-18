-module(main).
-export([main/0]).

average(Marks) -> lists:sum(Marks) div length(Marks).

report(Marks) -> average(Marks).

main() ->
    io:format("~p~n", [report([])]).

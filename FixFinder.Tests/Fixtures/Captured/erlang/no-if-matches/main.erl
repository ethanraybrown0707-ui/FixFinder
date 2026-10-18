-module(main).
-export([main/0]).

main() ->
    Mark = length([a]),
    if
        Mark > 5 -> io:format("big~n")
    end.

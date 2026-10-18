-module(main).
-export([main/0]).

main() ->
    Mark = length([a, b, c]),
    case Mark of
        1 -> io:format("one~n")
    end.

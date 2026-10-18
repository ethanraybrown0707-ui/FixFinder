-module(main).
-export([main/0]).

grade(Mark) when Mark >= 0 -> Mark.

main() ->
    grade(-1).

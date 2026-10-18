defmodule Marks do
  def grade(mark) when mark >= 0, do: mark
end

IO.puts(Marks.grade(-1))

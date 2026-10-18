defmodule Marks do
  def average(marks), do: div(Enum.sum(marks), length(marks))
  def report(marks), do: average(marks)
end

IO.puts(Marks.report([]))

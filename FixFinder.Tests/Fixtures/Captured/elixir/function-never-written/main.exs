defmodule Marks do
  def total(marks), do: summ(marks)
end

IO.puts(Marks.total([70, 80]))

marks = [70, 80, 0]
for mark <- marks do
  IO.puts("Mark: #{mark}")
  IO.puts("Share: #{div(100, mark)}")
end

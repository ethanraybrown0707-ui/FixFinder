def average(marks)
  marks.sum / marks.length
end

def report(marks)
  average(marks)
end

puts report([])

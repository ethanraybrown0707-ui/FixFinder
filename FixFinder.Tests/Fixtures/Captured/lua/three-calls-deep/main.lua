local function first(marks)
  return marks[1].value
end

local function report(marks)
  return first(marks)
end

print(report({}))

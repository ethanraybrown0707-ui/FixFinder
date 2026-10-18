local function share(prize, winners)
  if winners == 0 then error("no winners") end
  return prize / winners
end

print(share(120, 0))

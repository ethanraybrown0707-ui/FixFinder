@main def run(): Unit =
  val mark = 120
  if mark > 100 then throw IllegalArgumentException("mark out of range")

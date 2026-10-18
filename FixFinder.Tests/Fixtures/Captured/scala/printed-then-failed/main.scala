@main def run(): Unit =
  val marks = List(70, 80, 0)
  for mark <- marks do
    println(s"Mark: $mark")
    println(s"Share: ${100 / mark}")

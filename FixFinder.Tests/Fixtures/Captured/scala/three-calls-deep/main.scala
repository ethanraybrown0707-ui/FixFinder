def average(marks: List[Int]): Int = marks.sum / marks.length

def report(marks: List[Int]): Int = average(marks)

@main def run(): Unit =
  println(report(Nil))

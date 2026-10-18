Console.WriteLine(Report(new int[0]));

static int Report(int[] marks) => Average(marks);

static int Average(int[] marks)
{
    return marks.Sum() / marks.Length;
}

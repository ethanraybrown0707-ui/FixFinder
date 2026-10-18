var marks = new List<int> { 70, 80, 90 };
foreach (var mark in marks)
{
    if (mark == 70) marks.Remove(mark);
}

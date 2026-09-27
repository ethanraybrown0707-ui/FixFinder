int[] readings = { 12, 15, 9, 21 };

int total = 0;

for (int i = 0; i < readings.Length; i++)
{
    total += readings[i];
}

Console.WriteLine($"Average reading: {total / readings.Length}");

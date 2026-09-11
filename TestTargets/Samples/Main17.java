// A Java file that does not compile.
//
// "cannot find symbol" is the single most common javac error, and it names the symbol, which
// makes it a usable search term.
public class Main17
{
    public static void main(String[] args)
    {
        int[] numbers = {1, 2, 3, 4};
        int total = 0;

        for (int number : numbers)
        {
            total += number;
        }

        // The bug: "avg" was never declared.
        System.out.println("total " + total + ", average " + avg);
    }
}

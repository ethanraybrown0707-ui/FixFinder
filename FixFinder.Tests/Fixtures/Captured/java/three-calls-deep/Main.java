public class Main {
    static int average(int[] marks) {
        int total = 0;
        for (int mark : marks) total += mark;
        return total / marks.length;
    }

    static int report(int[] marks) {
        return average(marks);
    }

    public static void main(String[] args) {
        System.out.println(report(new int[0]));
    }
}

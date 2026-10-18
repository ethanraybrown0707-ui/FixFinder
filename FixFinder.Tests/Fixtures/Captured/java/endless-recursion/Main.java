public class Main {
    static int countDown(int n) {
        return countDown(n - 1);
    }

    public static void main(String[] args) {
        countDown(3);
    }
}

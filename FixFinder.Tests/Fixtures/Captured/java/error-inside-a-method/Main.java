public class Main {
    static int share(int prize, int winners) {
        return prize / winners;
    }

    public static void main(String[] args) {
        System.out.println(share(120, 0));
    }
}

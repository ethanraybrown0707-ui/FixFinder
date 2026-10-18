public class Main {
    public static void main(String[] args) {
        String[] names = {"Ada", "Bob", null};
        for (String name : names) {
            System.out.println(name.toUpperCase());
        }
    }
}

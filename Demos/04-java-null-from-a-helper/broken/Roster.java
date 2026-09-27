public class Roster {

    /** Returns the name in that position, or null when there is nobody there. */
    static String nameAt(String[] names, int position) {
        if (position >= 0 && position < names.length) {
            return names[position];
        }
        return null;
    }

    public static void main(String[] args) {
        String[] names = {"Ada", "Alan"};

        System.out.println("Third name is " + nameAt(names, 2).toUpperCase());
    }
}

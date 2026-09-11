// A Java program that compiles and then throws.
//
// NullPointerException is the most written-about error in the language, and since Java 14 the
// message names exactly which expression was null - which makes it far more searchable than it
// used to be.
import java.util.HashMap;
import java.util.Map;

public class Main18
{
    static String lookup(Map<String, String> users, String id)
    {
        return users.get(id);          // the bug: returns null for an unknown id
    }

    public static void main(String[] args)
    {
        Map<String, String> users = new HashMap<>();
        users.put("1", "Ada");

        System.out.println("looking up users");
        System.out.println("user 1 is " + lookup(users, "1").toUpperCase());
        System.out.println("user 2 is " + lookup(users, "2").toUpperCase());
    }
}

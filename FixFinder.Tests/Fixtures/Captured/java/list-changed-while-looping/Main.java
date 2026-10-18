import java.util.*;

public class Main {
    public static void main(String[] args) {
        List<Integer> marks = new ArrayList<>(List.of(70, 80, 90));
        for (Integer mark : marks) {
            if (mark == 70) marks.remove(mark);
        }
    }
}

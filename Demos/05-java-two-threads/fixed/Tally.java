public class Tally implements Runnable {

    private int count = 0;

    public void run() {
        for (int i = 0; i < 100000; i++) {
            increment();
        }
    }

    private synchronized void increment() {
        count++;
    }

    public synchronized int getCount() {
        return count;
    }

    public static void main(String[] args) throws InterruptedException {
        Tally tally = new Tally();

        Thread first = new Thread(tally);
        Thread second = new Thread(tally);

        first.start();
        second.start();
        first.join();
        second.join();

        System.out.println("Counted to " + tally.getCount());
    }
}

fn main() {
    let marks = vec![70, 80, 90];
    let mark = marks.get(5).expect("no mark at 5");
    println!("{}", mark);
}

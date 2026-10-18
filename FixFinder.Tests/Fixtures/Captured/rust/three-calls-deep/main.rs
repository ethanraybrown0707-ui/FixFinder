fn first(marks: &[i32]) -> i32 {
    marks[0]
}

fn report(marks: &[i32]) -> i32 {
    first(marks)
}

fn main() {
    println!("{}", report(&[]));
}

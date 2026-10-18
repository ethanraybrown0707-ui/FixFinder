int first(List<int> marks) {
  return marks.first;
}

int report(List<int> marks) {
  return first(marks);
}

void main() {
  print(report([]));
}

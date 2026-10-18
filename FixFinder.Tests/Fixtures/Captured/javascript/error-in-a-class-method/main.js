class Student {
  constructor(name) {
    this.name = name;
  }
  shout() {
    return this.nmae.toUpperCase();
  }
}
console.log(new Student("Ada").shout());

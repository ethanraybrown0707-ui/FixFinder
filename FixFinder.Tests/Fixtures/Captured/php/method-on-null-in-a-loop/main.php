<?php
class Student {
    public function name() { return "Ada"; }
}
$students = [new Student(), null];
foreach ($students as $student) {
    echo $student->name(), "\n";
}

function Test-Mark($mark) {
    if ($mark -gt 100) { throw "mark out of range" }
    return $mark
}
Test-Mark 120

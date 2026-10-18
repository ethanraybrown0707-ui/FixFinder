<?php
function average($marks) {
    return array_sum($marks) / count($marks);
}
function report($marks) {
    return average($marks);
}
echo report([]);

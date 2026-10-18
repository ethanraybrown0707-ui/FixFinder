use strict;
use warnings;

sub average {
    my @marks = @_;
    my $total = 0;
    $total += $_ for @marks;
    return $total / scalar(@marks);
}

sub report {
    return average(@_);
}

print report(), "\n";

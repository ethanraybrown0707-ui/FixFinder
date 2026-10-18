use strict;
use warnings;

sub share {
    my ($prize, $winners) = @_;
    return $prize / $winners;
}

print share(120, 0);

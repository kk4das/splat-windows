/* P2 probe: a 3 GB static array, like dem[64] in SPLAT! HD 8x8 (Linux uses -mcmodel=medium).
   Expected on Windows: fails to link or fails to load. */
static char big[3000000000ULL];
int main(void) { big[2999999999ULL] = 1; return big[2999999999ULL] == 1 ? 0 : 1; }
